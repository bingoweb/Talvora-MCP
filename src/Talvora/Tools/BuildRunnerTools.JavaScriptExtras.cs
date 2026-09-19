using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraJavascriptToolInfoResponse(
    string Tool,
    bool Found,
    string? Executable,
    string? Version);

public static partial class BuildRunnerTools
{
    [McpServerTool(
        Name = "talvora_pnpm_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJavascriptToolInfoResponse)),
     Description("Resolve the modern pnpm CLI and report its executable path and version. Missing pnpm is reported structurally.")]
    public static Task<TalvoraJavascriptToolInfoResponse> PnpmInfo(
        CancellationToken cancellationToken = default) =>
        JavascriptToolInfo(
            "pnpm",
            ResolvePnpm(),
            cancellationToken);

    [McpServerTool(
        Name = "talvora_pnpm_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run pnpm with an arbitrary argument vector, working directory, environment overrides, and timeout. No package/workspace/script/registry/store/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> PnpmRun(
        string workingDirectory,
        string[] arguments,
        string? pnpmExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = ResolveRequestedExecutable(
            pnpmExecutable,
            ResolvePnpm,
            "pnpm");

        return RunCliAsync(
            executable,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_yarn_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJavascriptToolInfoResponse)),
     Description("Resolve Yarn Modern and report its executable path and version. Missing Yarn Modern is reported structurally.")]
    public static Task<TalvoraJavascriptToolInfoResponse> YarnInfo(
        CancellationToken cancellationToken = default) =>
        JavascriptToolInfo(
            "yarn",
            ResolveYarn(),
            cancellationToken);

    [McpServerTool(
        Name = "talvora_yarn_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run Yarn Modern with an arbitrary argument vector, working directory, environment overrides, and timeout. No package/workspace/script/plugin/registry/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> YarnRun(
        string workingDirectory,
        string[] arguments,
        string? yarnExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = ResolveRequestedExecutable(
            yarnExecutable,
            ResolveYarn,
            "Yarn Modern");

        return RunCliAsync(
            executable,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_bun_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJavascriptToolInfoResponse)),
     Description("Resolve the Bun runtime/toolkit and report its executable path and version. Missing Bun is reported structurally.")]
    public static Task<TalvoraJavascriptToolInfoResponse> BunInfo(
        CancellationToken cancellationToken = default) =>
        JavascriptToolInfo(
            "bun",
            ResolveBun(),
            cancellationToken);

    [McpServerTool(
        Name = "talvora_bun_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run Bun with an arbitrary argument vector for runtime, install, test, build, bunx, package management, and scripts. No command/package/script/registry/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> BunRun(
        string workingDirectory,
        string[] arguments,
        string? bunExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = ResolveRequestedExecutable(
            bunExecutable,
            ResolveBun,
            "Bun");

        return RunCliAsync(
            executable,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static async Task<TalvoraJavascriptToolInfoResponse>
        JavascriptToolInfo(
            string tool,
            string? executable,
            CancellationToken cancellationToken)
    {
        if (executable is null)
        {
            return new TalvoraJavascriptToolInfoResponse(
                tool,
                false,
                null,
                null);
        }

        var result = await RunCliCheckedAsync(
            executable,
            Environment.CurrentDirectory,
            ["--version"],
            timeoutSeconds: 60,
            cancellationToken: cancellationToken);

        return new TalvoraJavascriptToolInfoResponse(
            tool,
            true,
            executable,
            FirstNonEmptyLine(
                result.StandardOutput,
                result.StandardError));
    }

    private static string? ResolvePnpm() =>
        CommandResolver.Resolve(
            ["pnpm.exe", "pnpm.cmd", "pnpm"],
            [
                @"C:\ProgramData\chocolatey\bin\pnpm.exe",
                @"C:\ProgramData\chocolatey\lib\pnpm\tools\pnpm.exe",
            ]);

    private static string? ResolveYarn()
    {
        var candidates = new List<string>();

        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            candidates.Add(Path.Combine(appData, "npm", "yarn.cmd"));
            candidates.Add(Path.Combine(appData, "npm", "yarn"));
        }

        var systemProfile =
            Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrWhiteSpace(systemProfile))
        {
            candidates.Add(Path.Combine(
                systemProfile,
                "System32",
                "config",
                "systemprofile",
                "AppData",
                "Roaming",
                "npm",
                "yarn.cmd"));
        }

        return CommandResolver.Resolve(
            ["yarn.cmd", "yarn.exe", "yarn"],
            candidates);
    }

    private static string? ResolveBun()
    {
        var candidates = new List<string>
        {
            @"C:\ProgramData\chocolatey\bin\bun.exe",
            @"C:\ProgramData\chocolatey\lib\bun\tools\bun.exe",
        };

        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            candidates.Add(
                Path.Combine(
                    userProfile,
                    ".bun",
                    "bin",
                    "bun.exe"));
        }

        return CommandResolver.Resolve(
            ["bun.exe", "bun"],
            candidates);
    }
}