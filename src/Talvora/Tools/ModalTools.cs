using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraModalInfoResponse(
    bool Found,
    string? Executable,
    string? Version,
    bool ProfileConfigured,
    string? ActiveProfile,
    int? ProfileExitCode,
    string? ProfileStatus);

[McpServerToolType]
public static class ModalTools
{
    [McpServerTool(
        Name = "talvora_modal_info",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraModalInfoResponse)),
     Description("Resolve the installed Modal CLI, report its version, and check whether the logged-on Windows user has an active Modal profile. Profile credentials are never returned.")]
    public static async Task<TalvoraModalInfoResponse> Info(
        CancellationToken cancellationToken = default)
    {
        var executable = ResolveModal();
        if (executable is null)
        {
            return new TalvoraModalInfoResponse(
                false,
                null,
                null,
                false,
                null,
                null,
                "Modal CLI was not found.");
        }

        var versionResult = await ProcessRunner.RunAsync(
            executable,
            Environment.CurrentDirectory,
            ["--version"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);

        var version = FirstNonEmptyLine(
            versionResult.StandardOutput,
            versionResult.StandardError);

        try
        {
            var workingDirectory = NormalizeWorkingDirectory(null);
            var profileResult =
                await InteractiveUserProcessRunner.RunAsync(
                    executable,
                    workingDirectory,
                    ["profile", "current"],
                    timeoutSeconds: 30,
                    cancellationToken: cancellationToken);

            var configured =
                profileResult.ExitCode == 0 &&
                !profileResult.TimedOut;
            var profile = configured
                ? FirstNonEmptyLine(
                    profileResult.StandardOutput,
                    profileResult.StandardError)
                : null;

            return new TalvoraModalInfoResponse(
                true,
                executable,
                version,
                configured,
                profile,
                profileResult.ExitCode,
                configured
                    ? null
                    : "Modal profile is not configured for the logged-on Windows user.");
        }
        catch (InvalidOperationException ex)
        {
            return new TalvoraModalInfoResponse(
                true,
                executable,
                version,
                false,
                null,
                null,
                ex.Message);
        }
    }

    [McpServerTool(
        Name = "talvora_modal_endpoint_list",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("List current Modal LLM Endpoints as JSON under the logged-on Windows user's Modal profile. Optional Modal environment and profile selectors are passed through to the official CLI.")]
    public static Task<TalvoraCliCommandResponse> EndpointList(
        [Description("Optional Modal environment name.")] string? modalEnvironment = null,
        [Description("Optional Modal profile name.")] string? profile = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "endpoint",
            "list",
            "--json",
        };

        if (!string.IsNullOrWhiteSpace(modalEnvironment))
        {
            arguments.Add("-e");
            arguments.Add(modalEnvironment);
        }

        if (!string.IsNullOrWhiteSpace(profile))
        {
            arguments.Add("--profile");
            arguments.Add(profile);
        }

        return RunModalAsync(
            arguments,
            workingDirectory: null,
            environment: null,
            modalExecutable: null,
            timeoutSeconds: 120,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_modal_run",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the official Modal CLI in the logged-on Windows user's session with a caller-supplied argument vector, working directory, environment overrides, and timeout. Prefer Modal profiles and Modal Secrets for credentials instead of passing secret values in command arguments.")]
    public static Task<TalvoraCliCommandResponse> Run(
        string[] arguments,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        string? modalExecutable = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length == 0)
        {
            throw new ArgumentException(
                "At least one Modal CLI argument is required.",
                nameof(arguments));
        }

        return RunModalAsync(
            arguments,
            workingDirectory,
            environment,
            modalExecutable,
            timeoutSeconds,
            cancellationToken);
    }

    private static async Task<TalvoraCliCommandResponse> RunModalAsync(
        IEnumerable<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        string? modalExecutable,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var executable =
            string.IsNullOrWhiteSpace(modalExecutable)
                ? ResolveModal()
                : ResolveExplicitModal(modalExecutable);
        if (executable is null)
        {
            throw new FileNotFoundException(
                "Modal CLI executable was not found.");
        }

        var result =
            await InteractiveUserProcessRunner.RunAsync(
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

    private static string? ResolveModal() =>
        CommandResolver.Resolve(
            ["modal.exe", "modal"],
            [
                @"C:\Python314\Scripts\modal.exe",
                @"C:\Python313\Scripts\modal.exe",
                @"C:\Python312\Scripts\modal.exe",
                @"C:\Python311\Scripts\modal.exe",
            ]);

    private static string ResolveExplicitModal(
        string executable)
    {
        var resolved =
            CommandResolver.Resolve([executable]);
        if (resolved is not null)
        {
            return resolved;
        }

        var full = Path.GetFullPath(executable);
        if (File.Exists(full))
        {
            return full;
        }

        throw new FileNotFoundException(
            "Modal CLI executable was not found.",
            full);
    }

    private static string NormalizeWorkingDirectory(
        string? workingDirectory)
    {
        string full;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            var context =
                WindowsSessionLauncher.GetDefaultInteractiveUser();
            full = context.UserProfile
                ?? throw new InvalidOperationException(
                    "Logged-on Windows user's profile directory could not be resolved.");
        }
        else
        {
            full = Path.GetFullPath(workingDirectory);
        }

        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException(
                $"Working directory was not found: {full}");
        }

        return full;
    }

    private static string? FirstNonEmptyLine(
        params string[] values)
    {
        foreach (var value in values)
        {
            var line =
                TextLines.Split(value)
                    .FirstOrDefault(item =>
                        !string.IsNullOrWhiteSpace(item));
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return null;
    }
}
