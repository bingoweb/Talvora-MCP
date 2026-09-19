using Talvora.Shared;

namespace Talvora.Tools;

public static partial class MobileTools
{
    private static string NormalizeWorkingDirectory(string? workingDirectory)
    {
        var full = string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetFullPath(Environment.CurrentDirectory)
            : Path.GetFullPath(workingDirectory);

        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException(
                $"Working directory was not found: {full}");
        }

        return full;
    }

    private static async Task<TalvoraCliCommandResponse> RunCliAsync(
        string executable,
        string? workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            executable,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);

        return new TalvoraCliCommandResponse(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.ProcessId,
            result.Executable,
            result.WorkingDirectory,
            result.Arguments,
            result.ElapsedMilliseconds);
    }

    private static async Task<TalvoraCliCommandResponse> RunCliCheckedAsync(
        string executable,
        string? workingDirectory,
        IEnumerable<string> arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunCheckedAsync(
            executable,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            timeoutSeconds: timeoutSeconds,
            cancellationToken: cancellationToken);

        return new TalvoraCliCommandResponse(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError,
            result.TimedOut,
            result.ProcessId,
            result.Executable,
            result.WorkingDirectory,
            result.Arguments,
            result.ElapsedMilliseconds);
    }

    private static string ResolveRequiredExecutable(
        string? explicitExecutable,
        string displayName,
        IEnumerable<string> commandNames,
        IEnumerable<string>? candidates = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitExecutable))
        {
            var resolvedExplicit = CommandResolver.Resolve([explicitExecutable]);
            if (resolvedExplicit is not null)
            {
                return resolvedExplicit;
            }

            var full = Path.GetFullPath(explicitExecutable);
            if (File.Exists(full))
            {
                return full;
            }

            throw new FileNotFoundException(
                $"{displayName} executable was not found.",
                full);
        }

        return CommandResolver.Resolve(commandNames, candidates)
            ?? throw new FileNotFoundException(
                $"{displayName} executable was not found.");
    }

    private static string? FirstNonEmptyLine(
        params string[] values)
    {
        foreach (var value in values)
        {
            var line = Talvora.Shared.TextLines
                .Split(value)
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));

            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return null;
    }
}
