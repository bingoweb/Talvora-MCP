using System.Security.Principal;
using Talvora.Shared;

namespace Talvora.Tools;

// UR1: developer tools find and run their programs in the signed-in user's own environment (PATH, JAVA_HOME,
// LOCALAPPDATA …) and as that user, so builds never leave SYSTEM-owned files or caches in the service profile.
// Only the installed service runs as LocalSystem and switches to the user's session; any other account (tests,
// CI, a developer run) already is that user, so it resolves and runs in its own environment.
internal static class SignedInUserToolchain
{
    private static readonly bool RunsAsService = IsLocalSystem();

    internal static string? Resolve(
        IEnumerable<string> commandNames,
        IEnumerable<string>? explicitCandidates = null) =>
        RunsAsService
            ? CommandResolver.Resolve(
                commandNames,
                explicitCandidates,
                environment: WindowsSessionLauncher.GetActiveInteractiveUser().Environment)
            : CommandResolver.Resolve(commandNames, explicitCandidates);

    internal static IReadOnlyList<string> ResolveAll(IEnumerable<string> commandNames) =>
        RunsAsService
            ? CommandResolver.ResolveAll(commandNames, environment: WindowsSessionLauncher.GetActiveInteractiveUser().Environment)
            : CommandResolver.ResolveAll(commandNames);

    internal static string? Variable(string name)
    {
        var value = RunsAsService
            ? WindowsSessionLauncher.GetActiveInteractiveUser().Environment.TryGetValue(name, out var userValue)
                ? userValue
                : null
            : Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // The profile folders come from the signed-in user's environment; machine folders are the same for every account.
    internal static string Folder(Environment.SpecialFolder folder) => !RunsAsService
        ? Environment.GetFolderPath(folder)
        : folder switch
        {
            Environment.SpecialFolder.UserProfile => Variable("USERPROFILE") ?? string.Empty,
            Environment.SpecialFolder.ApplicationData => Variable("APPDATA") ?? string.Empty,
            Environment.SpecialFolder.LocalApplicationData => Variable("LOCALAPPDATA") ?? string.Empty,
            _ => Environment.GetFolderPath(folder),
        };

    internal static Task<ProcessExecutionResult> RunAsync(
        string executable,
        string? workingDirectory,
        IEnumerable<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        int timeoutSeconds = 0,
        CancellationToken cancellationToken = default) =>
        RunsAsService
            ? InteractiveUserProcessRunner.RunAsync(
                executable,
                string.IsNullOrWhiteSpace(workingDirectory) ? Environment.SystemDirectory : workingDirectory,
                arguments,
                environment,
                timeoutSeconds,
                cancellationToken,
                ProcessRunner.DefaultMaximumCapturedCharacters,
                WindowsSessionLauncher.GetActiveInteractiveUser(),
                closeStandardInput: true)
            : ProcessRunner.RunAsync(
                executable,
                workingDirectory,
                arguments,
                environment,
                timeoutSeconds,
                cancellationToken);

    // The version probes of the *_info tools: the same failure rule as ProcessRunner.RunCheckedAsync.
    internal static async Task<ProcessExecutionResult> RunCheckedAsync(
        string executable,
        string? workingDirectory,
        IEnumerable<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        int timeoutSeconds = 0,
        CancellationToken cancellationToken = default)
    {
        if (!RunsAsService)
        {
            return await ProcessRunner.RunCheckedAsync(
                executable,
                workingDirectory,
                arguments,
                environment,
                timeoutSeconds,
                cancellationToken).ConfigureAwait(false);
        }

        var result = await RunAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken).ConfigureAwait(false);
        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process failed. Executable={executable}, ExitCode={result.ExitCode}, " +
                $"TimedOut={result.TimedOut}, OutputDrainTimedOut={result.OutputDrainTimedOut}, " +
                $"StderrTruncated={result.StandardErrorTruncated}, stderr={result.StandardError}");
        }

        return result;
    }

    private static bool IsLocalSystem()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.IsSystem;
    }
}
