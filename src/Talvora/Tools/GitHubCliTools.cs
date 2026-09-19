using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraGitHubCliInfoResponse(
    bool Found,
    string? Executable,
    string? Version,
    bool Authenticated,
    int? AuthExitCode,
    string? AuthStatus);

[McpServerToolType]
public static class GitHubCliTools
{
    [McpServerTool(
        Name = "talvora_gh_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraGitHubCliInfoResponse)),
     Description("Resolve the current GitHub CLI and report its version plus authentication status for the Talvora service context. Missing or unauthenticated gh is reported structurally.")]
    public static async Task<TalvoraGitHubCliInfoResponse> Info(
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var executable = ResolveGh();
        if (executable is null)
        {
            return new TalvoraGitHubCliInfoResponse(
                false,
                null,
                null,
                false,
                null,
                null);
        }

        var cwd = NormalizeWorkingDirectory(workingDirectory);

        var version = await ProcessRunner.RunAsync(
            executable,
            cwd,
            ["--version"],
            environment,
            timeoutSeconds: 30,
            cancellationToken);

        var auth = await ProcessRunner.RunAsync(
            executable,
            cwd,
            ["auth", "status"],
            environment,
            timeoutSeconds: 60,
            cancellationToken);

        return new TalvoraGitHubCliInfoResponse(
            true,
            executable,
            FirstNonEmptyLine(
                version.StandardOutput,
                version.StandardError),
            auth.ExitCode == 0 && !auth.TimedOut,
            auth.ExitCode,
            JoinOutput(
                auth.StandardOutput,
                auth.StandardError));
    }

    [McpServerTool(
        Name = "talvora_gh_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run GitHub CLI with an arbitrary argument vector, working directory, environment overrides, and timeout. No repository/host/API/issue/pull-request/workflow/release/auth/extension/option allowlist or denylist is applied.")]
    public static async Task<TalvoraCliCommandResponse> Run(
        string workingDirectory,
        string[] arguments,
        string? ghExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = string.IsNullOrWhiteSpace(ghExecutable)
            ? ResolveGh()
            : ResolveExplicitGh(ghExecutable);

        if (executable is null)
        {
            throw new FileNotFoundException(
                "GitHub CLI executable was not found.");
        }

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

    private static string? ResolveGh() =>
        CommandResolver.Resolve(
            ["gh.exe", "gh"],
            [
                @"C:\Program Files\GitHub CLI\gh.exe",
                @"C:\Program Files (x86)\GitHub CLI\gh.exe",
            ]);

    private static string ResolveExplicitGh(string executable)
    {
        var resolved = CommandResolver.Resolve([executable]);
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
            "GitHub CLI executable was not found.",
            full);
    }

    private static string NormalizeWorkingDirectory(
        string? workingDirectory)
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

    private static string? FirstNonEmptyLine(
        params string[] values)
    {
        foreach (var value in values)
        {
            var line = TextLines
                .Split(value)
                .FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item));

            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return null;
    }

    private static string? JoinOutput(
        string standardOutput,
        string standardError)
    {
        var values = new[]
        {
            standardOutput.Trim(),
            standardError.Trim(),
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToArray();

        return values.Length == 0
            ? null
            : string.Join(
                Environment.NewLine,
                values);
    }
}
