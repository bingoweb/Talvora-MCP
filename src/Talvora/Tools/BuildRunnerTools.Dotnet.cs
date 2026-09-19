using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class BuildRunnerTools
{
[McpServerTool(
        Name = "talvora_dotnet_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDotnetInfoResponse)),
     Description("Return the resolved dotnet CLI path, version, installed SDKs/runtimes, and dotnet --info output. Missing dotnet is reported structurally instead of throwing.")]
    public static async Task<TalvoraDotnetInfoResponse> DotnetInfo(
        CancellationToken cancellationToken = default)
    {
        var dotnet = ResolveDotnet();
        if (dotnet is null)
        {
            return new TalvoraDotnetInfoResponse(
                false,
                null,
                null,
                [],
                [],
                null);
        }

        var version = await RunCliCheckedAsync(
            dotnet,
            Environment.CurrentDirectory,
            ["--version"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);

        var sdks = await RunCliCheckedAsync(
            dotnet,
            Environment.CurrentDirectory,
            ["--list-sdks"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);

        var runtimes = await RunCliCheckedAsync(
            dotnet,
            Environment.CurrentDirectory,
            ["--list-runtimes"],
            timeoutSeconds: 30,
            cancellationToken: cancellationToken);

        var info = await RunCliCheckedAsync(
            dotnet,
            Environment.CurrentDirectory,
            ["--info"],
            timeoutSeconds: 60,
            cancellationToken: cancellationToken);

        return new TalvoraDotnetInfoResponse(
            true,
            dotnet,
            version.StandardOutput.Trim(),
            TextLines.Split(sdks.StandardOutput),
            TextLines.Split(runtimes.StandardOutput),
            info.StandardOutput);
    }

    [McpServerTool(
        Name = "talvora_dotnet_restore",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run dotnet restore for any project/solution/file with arbitrary additional arguments and environment overrides. No project/source/runtime/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> DotnetRestore(
        string workingDirectory,
        string? target = null,
        string[]? additionalArguments = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1200,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "restore" };
        AddTarget(arguments, target);
        arguments.AddRange(additionalArguments ?? []);

        return RunDotnetAsync(
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_dotnet_build",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run dotnet build for any project/solution/file. Common configuration/no-restore options are modeled and arbitrary additional dotnet arguments remain available.")]
    public static Task<TalvoraCliCommandResponse> DotnetBuild(
        string workingDirectory,
        string? target = null,
        string? configuration = null,
        bool noRestore = false,
        string[]? additionalArguments = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1200,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "build" };
        AddTarget(arguments, target);

        if (!string.IsNullOrWhiteSpace(configuration))
        {
            arguments.Add("--configuration");
            arguments.Add(configuration);
        }
        if (noRestore)
        {
            arguments.Add("--no-restore");
        }

        arguments.AddRange(additionalArguments ?? []);

        return RunDotnetAsync(
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_dotnet_test",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run dotnet test for any project/solution/file. Supports common configuration/no-restore/no-build/filter controls plus arbitrary additional arguments.")]
    public static Task<TalvoraCliCommandResponse> DotnetTest(
        string workingDirectory,
        string? target = null,
        string? configuration = null,
        bool noRestore = false,
        bool noBuild = false,
        string? filter = null,
        string[]? additionalArguments = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "test" };
        AddTarget(arguments, target);

        if (!string.IsNullOrWhiteSpace(configuration))
        {
            arguments.Add("--configuration");
            arguments.Add(configuration);
        }
        if (noRestore)
        {
            arguments.Add("--no-restore");
        }
        if (noBuild)
        {
            arguments.Add("--no-build");
        }
        if (!string.IsNullOrWhiteSpace(filter))
        {
            arguments.Add("--filter");
            arguments.Add(filter);
        }

        arguments.AddRange(additionalArguments ?? []);

        return RunDotnetAsync(
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_dotnet_publish",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run dotnet publish for any project/solution/file. Supports configuration/runtime/self-contained/output controls plus arbitrary additional arguments.")]
    public static Task<TalvoraCliCommandResponse> DotnetPublish(
        string workingDirectory,
        string? target = null,
        string? configuration = null,
        string? runtime = null,
        bool? selfContained = null,
        string? output = null,
        string[]? additionalArguments = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "publish" };
        AddTarget(arguments, target);

        if (!string.IsNullOrWhiteSpace(configuration))
        {
            arguments.Add("--configuration");
            arguments.Add(configuration);
        }
        if (!string.IsNullOrWhiteSpace(runtime))
        {
            arguments.Add("--runtime");
            arguments.Add(runtime);
        }
        if (selfContained is bool selfContainedValue)
        {
            arguments.Add("--self-contained");
            arguments.Add(selfContainedValue ? "true" : "false");
        }
        if (!string.IsNullOrWhiteSpace(output))
        {
            arguments.Add("--output");
            arguments.Add(output);
        }

        arguments.AddRange(additionalArguments ?? []);

        return RunDotnetAsync(
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_dotnet_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the dotnet CLI with an arbitrary argument vector, working directory, and environment overrides. This preserves the complete dotnet CLI surface with no command/project/source/runtime/option allowlist or denylist.")]
    public static Task<TalvoraCliCommandResponse> DotnetRun(
        string workingDirectory,
        string[] arguments,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return RunDotnetAsync(
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }
}
