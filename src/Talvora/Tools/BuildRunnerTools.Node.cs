using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class BuildRunnerTools
{
[McpServerTool(
        Name = "talvora_node_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraNodeInfoResponse)),
     Description("Resolve Node.js and npm from the Talvora service environment and return their paths and versions. Missing Node/npm is reported structurally instead of throwing.")]
    public static async Task<TalvoraNodeInfoResponse> NodeInfo(
        CancellationToken cancellationToken = default)
    {
        var node = ResolveNode();
        var npm = ResolveNpm();

        string? nodeVersion = null;
        string? npmVersion = null;

        if (node is not null)
        {
            var result = await RunCliCheckedAsync(
                node,
                Environment.CurrentDirectory,
                ["--version"],
                timeoutSeconds: 30,
                cancellationToken: cancellationToken);
            nodeVersion = result.StandardOutput.Trim();
        }

        if (npm is not null)
        {
            var result = await RunCliCheckedAsync(
                npm,
                Environment.CurrentDirectory,
                ["--version"],
                timeoutSeconds: 30,
                cancellationToken: cancellationToken);
            npmVersion = result.StandardOutput.Trim();
        }

        return new TalvoraNodeInfoResponse(
            node is not null,
            node,
            nodeVersion,
            npm is not null,
            npm,
            npmVersion);
    }

    [McpServerTool(
        Name = "talvora_npm_install",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run npm install in any accessible working directory. Optional package names and common save/dev/global/exact controls are modeled; arbitrary additional npm arguments remain available.")]
    public static Task<TalvoraCliCommandResponse> NpmInstall(
        string workingDirectory,
        string[]? packages = null,
        bool saveDev = false,
        bool saveExact = false,
        bool global = false,
        string[]? additionalArguments = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "install" };
        arguments.AddRange(packages ?? []);

        if (saveDev)
        {
            arguments.Add("--save-dev");
        }
        if (saveExact)
        {
            arguments.Add("--save-exact");
        }
        if (global)
        {
            arguments.Add("--global");
        }

        arguments.AddRange(additionalArguments ?? []);

        return RunNpmAsync(
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_npm_ci",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run npm ci in any accessible project with arbitrary additional arguments and environment overrides.")]
    public static Task<TalvoraCliCommandResponse> NpmCi(
        string workingDirectory,
        string[]? additionalArguments = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "ci" };
        arguments.AddRange(additionalArguments ?? []);

        return RunNpmAsync(
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_npm_run_script",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run any package.json script using npm run. Supports workspace/workspaces/if-present controls and passes script arguments after -- without restricting script names or arguments.")]
    public static Task<TalvoraCliCommandResponse> NpmRunScript(
        string workingDirectory,
        string script,
        string[]? scriptArguments = null,
        string[]? workspaces = null,
        bool allWorkspaces = false,
        bool ifPresent = false,
        string[]? additionalNpmArguments = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            throw new ArgumentException("npm script name is required.", nameof(script));
        }

        var arguments = new List<string> { "run", script };

        foreach (var workspace in workspaces ?? [])
        {
            arguments.Add("--workspace");
            arguments.Add(workspace);
        }
        if (allWorkspaces)
        {
            arguments.Add("--workspaces");
        }
        if (ifPresent)
        {
            arguments.Add("--if-present");
        }

        arguments.AddRange(additionalNpmArguments ?? []);

        var scriptArgs = scriptArguments ?? [];
        if (scriptArgs.Length > 0)
        {
            arguments.Add("--");
            arguments.AddRange(scriptArgs);
        }

        return RunNpmAsync(
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_npm_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run npm with an arbitrary argument vector, working directory, and environment overrides. This preserves the complete npm CLI surface with no command/package/script/workspace/option allowlist or denylist.")]
    public static Task<TalvoraCliCommandResponse> NpmRun(
        string workingDirectory,
        string[] arguments,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return RunNpmAsync(
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }
}
