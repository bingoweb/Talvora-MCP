using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraCliCommandResponse(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    int ProcessId,
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    long ElapsedMilliseconds);

public sealed record TalvoraDotnetInfoResponse(
    bool Found,
    string? Executable,
    string? Version,
    IReadOnlyList<string> Sdks,
    IReadOnlyList<string> Runtimes,
    string? Info);

public sealed record TalvoraNodeInfoResponse(
    bool NodeFound,
    string? NodeExecutable,
    string? NodeVersion,
    bool NpmFound,
    string? NpmExecutable,
    string? NpmVersion);

[McpServerToolType]
public static class BuildRunnerTools
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
            SplitLines(sdks.StandardOutput),
            SplitLines(runtimes.StandardOutput),
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

    private static Task<TalvoraCliCommandResponse> RunDotnetAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var dotnet = ResolveDotnet()
            ?? throw new FileNotFoundException(
                "dotnet CLI was not found. Install the .NET SDK/runtime or provide it on the machine PATH.");

        return RunCliAsync(
            dotnet,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static Task<TalvoraCliCommandResponse> RunNpmAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var npm = ResolveNpm()
            ?? throw new FileNotFoundException(
                "npm was not found. Install Node.js/npm (for example through Chocolatey) and make it available on the machine.");

        return RunCliAsync(
            npm,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static void AddTarget(List<string> arguments, string? target)
    {
        if (!string.IsNullOrWhiteSpace(target))
        {
            arguments.Add(target);
        }
    }

    private static string NormalizeWorkingDirectory(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("workingDirectory is required.", nameof(workingDirectory));
        }

        var full = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Working directory was not found: {full}");
        }

        return full;
    }

    private static async Task<TalvoraCliCommandResponse> RunCliCheckedAsync(
        string executable,
        string workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        var result = await RunCliAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException(
                $"CLI command failed. Executable={executable}, ExitCode={result.ExitCode}, TimedOut={result.TimedOut}, stderr={result.StandardError}");
        }

        return result;
    }

    private static async Task<TalvoraCliCommandResponse> RunCliAsync(
        string executable,
        string workingDirectory,
        IEnumerable<string> arguments,
        Dictionary<string, string?>? environment,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var requestedArguments = arguments.ToArray();
        var startInfo = CreateStartInfo(executable, workingDirectory, requestedArguments);

        foreach (var pair in environment ?? new Dictionary<string, string?>())
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start CLI executable: {executable}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutSeconds > 0)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch { }
            await process.WaitForExitAsync(CancellationToken.None);
        }

        stopwatch.Stop();

        return new TalvoraCliCommandResponse(
            process.ExitCode,
            await stdoutTask,
            await stderrTask,
            timedOut,
            process.Id,
            executable,
            workingDirectory,
            requestedArguments,
            stopwatch.ElapsedMilliseconds);
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var extension = Path.GetExtension(executable);
        var isCommandScript = OperatingSystem.IsWindows() &&
                              (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                               extension.Equals(".bat", StringComparison.OrdinalIgnoreCase));

        ProcessStartInfo startInfo;

        if (isCommandScript)
        {
            var commandProcessor = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "cmd.exe");

            startInfo = new ProcessStartInfo
            {
                FileName = commandProcessor,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(executable);

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
        else
        {
            startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        return startInfo;
    }

    private static string? ResolveDotnet()
    {
        var candidates = new List<string>();

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            candidates.Add(Path.Combine(dotnetRoot, "dotnet.exe"));
            candidates.Add(Path.Combine(dotnetRoot, "dotnet"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "dotnet", "dotnet.exe"));
        }

        return ResolveFromCandidatesAndPath(candidates, "dotnet.exe", "dotnet");
    }

    private static string? ResolveNode()
    {
        var candidates = new List<string>();

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "nodejs", "node.exe"));
        }

        var chocolateyInstall = Environment.GetEnvironmentVariable("ChocolateyInstall");
        if (!string.IsNullOrWhiteSpace(chocolateyInstall))
        {
            candidates.Add(Path.Combine(chocolateyInstall, "bin", "node.exe"));
        }

        return ResolveFromCandidatesAndPath(candidates, "node.exe", "node");
    }

    private static string? ResolveNpm()
    {
        var candidates = new List<string>();

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "nodejs", "npm.cmd"));
            candidates.Add(Path.Combine(programFiles, "nodejs", "npm.exe"));
        }

        var chocolateyInstall = Environment.GetEnvironmentVariable("ChocolateyInstall");
        if (!string.IsNullOrWhiteSpace(chocolateyInstall))
        {
            candidates.Add(Path.Combine(chocolateyInstall, "bin", "npm.exe"));
            candidates.Add(Path.Combine(chocolateyInstall, "bin", "npm.cmd"));
        }

        return ResolveFromCandidatesAndPath(
            candidates,
            "npm.exe",
            "npm.cmd",
            "npm");
    }

    private static string? ResolveFromCandidatesAndPath(
        IEnumerable<string> initialCandidates,
        params string[] commandNames)
    {
        var candidates = new List<string>(initialCandidates);

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var commandName in commandNames)
            {
                candidates.Add(Path.Combine(directory, commandName));
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string[] SplitLines(string value) =>
        value.Split(
            new[] { "\r\n", "\n", "\r" },
            StringSplitOptions.RemoveEmptyEntries);
}
