using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraRuntimeCommandResponse(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    int ProcessId,
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

public sealed record TalvoraPythonInfoResponse(
    bool Found,
    string? LauncherExecutable,
    string? InterpreterExecutable,
    string? Version,
    string? Prefix,
    string? BasePrefix,
    bool InVirtualEnvironment,
    bool PipAvailable,
    string? PipVersion);

public sealed record TalvoraPythonVenvResponse(
    string Path,
    string? PythonExecutable,
    TalvoraRuntimeCommandResponse Command);

public sealed record TalvoraDockerInfoResponse(
    bool Found,
    string? Executable,
    string? ClientVersion,
    bool EngineAvailable,
    string? ServerVersion,
    bool ComposeAvailable,
    string? ComposeVersion,
    string? InfoJson,
    string? Error);

public sealed record TalvoraDockerRowsResponse(
    string Command,
    int ExitCode,
    bool TimedOut,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows,
    string StandardOutput,
    string StandardError);

[McpServerToolType]
public static class RuntimeTools
{
    [McpServerTool(
        Name = "talvora_python_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraPythonInfoResponse)),
     Description("Discover Python for the Talvora service or inspect an explicitly supplied Python/py launcher. Returns interpreter identity, version, virtual-environment state, and pip availability. Missing Python is reported structurally rather than treated as an error.")]
    public static async Task<TalvoraPythonInfoResponse> PythonInfo(
        string? pythonExecutable = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var python = ResolvePython(pythonExecutable);
        if (python is null)
        {
            return new TalvoraPythonInfoResponse(
                false,
                null,
                null,
                null,
                null,
                null,
                false,
                false,
                null);
        }

        var probe = """
            import json,sys
            print(json.dumps({
                "executable": sys.executable,
                "version": sys.version.split()[0],
                "prefix": sys.prefix,
                "base_prefix": getattr(sys, "base_prefix", sys.prefix),
                "venv": sys.prefix != getattr(sys, "base_prefix", sys.prefix)
            }, separators=(",",":")))
            """;

        var probeArguments = new List<string>();
        probeArguments.AddRange(python.PrefixArguments);
        probeArguments.Add("-c");
        probeArguments.Add(probe);

        var probeResult = await RunCommandAsync(
            python.Executable,
            probeArguments,
            workingDirectory,
            environment: null,
            timeoutSeconds: 30,
            cancellationToken);

        if (probeResult.ExitCode != 0 || probeResult.TimedOut)
        {
            throw new InvalidOperationException(
                $"Python probe failed. ExitCode={probeResult.ExitCode}, TimedOut={probeResult.TimedOut}, stderr={probeResult.StandardError}");
        }

        using var document = JsonDocument.Parse(
            probeResult.StandardOutput.Trim());
        var root = document.RootElement;

        var pipArguments = new List<string>();
        pipArguments.AddRange(python.PrefixArguments);
        pipArguments.Add("-m");
        pipArguments.Add("pip");
        pipArguments.Add("--version");

        var pipResult = await RunCommandAsync(
            python.Executable,
            pipArguments,
            workingDirectory,
            environment: null,
            timeoutSeconds: 30,
            cancellationToken);

        return new TalvoraPythonInfoResponse(
            true,
            python.Executable,
            root.GetProperty("executable").GetString(),
            root.GetProperty("version").GetString(),
            root.GetProperty("prefix").GetString(),
            root.GetProperty("base_prefix").GetString(),
            root.GetProperty("venv").GetBoolean(),
            pipResult.ExitCode == 0 && !pipResult.TimedOut,
            pipResult.ExitCode == 0
                ? pipResult.StandardOutput.Trim()
                : null);
    }

    [McpServerTool(
        Name = "talvora_python_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRuntimeCommandResponse)),
     Description("Run Python with an arbitrary argument vector, working directory, environment overrides, and timeout. If pythonExecutable is omitted Talvora resolves python/python3/py. No Python option/module/script/path allowlist is applied.")]
    public static Task<TalvoraRuntimeCommandResponse> PythonRun(
        string[] arguments,
        string? pythonExecutable = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var python = RequirePython(pythonExecutable);
        return RunCommandAsync(
            python.Executable,
            python.PrefixArguments.Concat(arguments),
            workingDirectory,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_python_venv_create",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraPythonVenvResponse)),
     Description("Create or recreate a Python virtual environment using python -m venv. Supports system-site-packages, clear, upgrade-deps, without-pip, arbitrary extra venv arguments, and an explicit Python executable. No target-path restriction is applied.")]
    public static async Task<TalvoraPythonVenvResponse> PythonVenvCreate(
        string path,
        string? pythonExecutable = null,
        string? workingDirectory = null,
        bool systemSitePackages = false,
        bool clear = false,
        bool upgradeDeps = false,
        bool withoutPip = false,
        string[]? additionalArguments = null,
        int timeoutSeconds = 600,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Virtual environment path is required.", nameof(path));
        }

        var python = RequirePython(pythonExecutable);
        var target = Path.GetFullPath(
            string.IsNullOrWhiteSpace(workingDirectory)
                ? path
                : Path.Combine(workingDirectory, path));

        var arguments = new List<string>();
        arguments.AddRange(python.PrefixArguments);
        arguments.Add("-m");
        arguments.Add("venv");

        if (systemSitePackages)
        {
            arguments.Add("--system-site-packages");
        }
        if (clear)
        {
            arguments.Add("--clear");
        }
        if (upgradeDeps)
        {
            arguments.Add("--upgrade-deps");
        }
        if (withoutPip)
        {
            arguments.Add("--without-pip");
        }

        arguments.AddRange(additionalArguments ?? []);
        arguments.Add(target);

        var result = await RunCommandAsync(
            python.Executable,
            arguments,
            workingDirectory,
            environment: null,
            timeoutSeconds,
            cancellationToken);

        var venvPython = OperatingSystem.IsWindows()
            ? Path.Combine(target, "Scripts", "python.exe")
            : Path.Combine(target, "bin", "python");

        return new TalvoraPythonVenvResponse(
            target,
            File.Exists(venvPython) ? venvPython : null,
            result);
    }

    [McpServerTool(
        Name = "talvora_pip_install",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRuntimeCommandResponse)),
     Description("Install arbitrary Python packages using python -m pip install. Supports upgrade/pre/index/extra-index/target plus arbitrary additional pip arguments. No package/index/path/option allowlist is applied.")]
    public static Task<TalvoraRuntimeCommandResponse> PipInstall(
        string[] packages,
        string? pythonExecutable = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        bool upgrade = false,
        bool prerelease = false,
        string? indexUrl = null,
        string? extraIndexUrl = null,
        string? target = null,
        string[]? additionalArguments = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);
        if (packages.Length == 0)
        {
            throw new ArgumentException(
                "At least one pip package or requirement specifier is required.",
                nameof(packages));
        }

        var arguments = new List<string> { "-m", "pip", "install" };

        if (upgrade)
        {
            arguments.Add("--upgrade");
        }
        if (prerelease)
        {
            arguments.Add("--pre");
        }
        if (!string.IsNullOrWhiteSpace(indexUrl))
        {
            arguments.Add("--index-url");
            arguments.Add(indexUrl);
        }
        if (!string.IsNullOrWhiteSpace(extraIndexUrl))
        {
            arguments.Add("--extra-index-url");
            arguments.Add(extraIndexUrl);
        }
        if (!string.IsNullOrWhiteSpace(target))
        {
            arguments.Add("--target");
            arguments.Add(target);
        }

        arguments.AddRange(additionalArguments ?? []);
        arguments.AddRange(packages);

        return PythonRun(
            arguments.ToArray(),
            pythonExecutable,
            workingDirectory,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_pip_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRuntimeCommandResponse)),
     Description("Run python -m pip with an arbitrary pip argument vector. No pip subcommand, package, index, target, or option allowlist/denylist is applied.")]
    public static Task<TalvoraRuntimeCommandResponse> PipRun(
        string[] arguments,
        string? pythonExecutable = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return PythonRun(
            new[] { "-m", "pip" }.Concat(arguments).ToArray(),
            pythonExecutable,
            workingDirectory,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_docker_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDockerInfoResponse)),
     Description("Discover Docker CLI, Docker Engine reachability, and Docker Compose availability. Missing Docker/Engine/Compose is reported structurally.")]
    public static async Task<TalvoraDockerInfoResponse> DockerInfo(
        string? dockerExecutable = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var docker = ResolveDocker(dockerExecutable);
        if (docker is null)
        {
            return new TalvoraDockerInfoResponse(
                false,
                null,
                null,
                false,
                null,
                false,
                null,
                null,
                null);
        }

        var clientResult = await RunCommandAsync(
            docker,
            ["version", "--format", "{{.Client.Version}}"],
            workingDirectory,
            environment: null,
            timeoutSeconds: 30,
            cancellationToken);

        var infoResult = await RunCommandAsync(
            docker,
            ["info", "--format", "{{json .}}"],
            workingDirectory,
            environment: null,
            timeoutSeconds: 30,
            cancellationToken);

        string? serverVersion = null;
        if (infoResult.ExitCode == 0 &&
            !string.IsNullOrWhiteSpace(infoResult.StandardOutput))
        {
            try
            {
                using var infoDocument = JsonDocument.Parse(
                    infoResult.StandardOutput.Trim());
                if (infoDocument.RootElement.TryGetProperty(
                        "ServerVersion",
                        out var serverVersionJson))
                {
                    serverVersion =
                        serverVersionJson.GetString();
                }
            }
            catch (JsonException)
            {
            }
        }

        var composeResult = await RunCommandAsync(
            docker,
            ["compose", "version", "--short"],
            workingDirectory,
            environment: null,
            timeoutSeconds: 30,
            cancellationToken);

        return new TalvoraDockerInfoResponse(
            true,
            docker,
            clientResult.ExitCode == 0
                ? clientResult.StandardOutput.Trim()
                : null,
            infoResult.ExitCode == 0 && !infoResult.TimedOut,
            serverVersion,
            composeResult.ExitCode == 0 &&
            !composeResult.TimedOut,
            composeResult.ExitCode == 0
                ? composeResult.StandardOutput.Trim()
                : null,
            infoResult.ExitCode == 0
                ? infoResult.StandardOutput.Trim()
                : null,
            infoResult.ExitCode == 0
                ? null
                : string.IsNullOrWhiteSpace(infoResult.StandardError)
                    ? clientResult.StandardError
                    : infoResult.StandardError);
    }

    [McpServerTool(
        Name = "talvora_docker_ps",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDockerRowsResponse)),
     Description("List Docker containers using docker ps --format json and return each JSON row as structured string fields. Supports all containers and arbitrary Docker filters.")]
    public static async Task<TalvoraDockerRowsResponse> DockerPs(
        bool all = false,
        string[]? filters = null,
        string? dockerExecutable = null,
        string? workingDirectory = null,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        var docker = RequireDocker(dockerExecutable);
        var arguments = new List<string> { "ps" };

        if (all)
        {
            arguments.Add("--all");
        }

        foreach (var filter in filters ?? [])
        {
            arguments.Add("--filter");
            arguments.Add(filter);
        }

        arguments.Add("--format");
        arguments.Add("json");

        var result = await RunCommandAsync(
            docker,
            arguments,
            workingDirectory,
            environment: null,
            timeoutSeconds,
            cancellationToken);

        return ToDockerRows("ps", result);
    }

    [McpServerTool(
        Name = "talvora_docker_images",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDockerRowsResponse)),
     Description("List Docker images using docker image ls --format json and return each JSON row as structured string fields. Supports all images and arbitrary Docker filters.")]
    public static async Task<TalvoraDockerRowsResponse> DockerImages(
        bool all = false,
        string[]? filters = null,
        string? dockerExecutable = null,
        string? workingDirectory = null,
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        var docker = RequireDocker(dockerExecutable);
        var arguments = new List<string> { "image", "ls" };

        if (all)
        {
            arguments.Add("--all");
        }

        foreach (var filter in filters ?? [])
        {
            arguments.Add("--filter");
            arguments.Add(filter);
        }

        arguments.Add("--format");
        arguments.Add("json");

        var result = await RunCommandAsync(
            docker,
            arguments,
            workingDirectory,
            environment: null,
            timeoutSeconds,
            cancellationToken);

        return ToDockerRows("image ls", result);
    }

    [McpServerTool(
        Name = "talvora_docker_logs",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRuntimeCommandResponse)),
     Description("Read Docker container logs with optional tail/since/until/timestamps/details controls. For continuous follow mode use talvora_job_start with docker logs -f or the unrestricted docker runner.")]
    public static Task<TalvoraRuntimeCommandResponse> DockerLogs(
        string container,
        string? tail = null,
        string? since = null,
        string? until = null,
        bool timestamps = false,
        bool details = false,
        string? dockerExecutable = null,
        string? workingDirectory = null,
        int timeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(container))
        {
            throw new ArgumentException(
                "Container name or ID is required.",
                nameof(container));
        }

        var arguments = new List<string> { "logs" };

        if (!string.IsNullOrWhiteSpace(tail))
        {
            arguments.Add("--tail");
            arguments.Add(tail);
        }
        if (!string.IsNullOrWhiteSpace(since))
        {
            arguments.Add("--since");
            arguments.Add(since);
        }
        if (!string.IsNullOrWhiteSpace(until))
        {
            arguments.Add("--until");
            arguments.Add(until);
        }
        if (timestamps)
        {
            arguments.Add("--timestamps");
        }
        if (details)
        {
            arguments.Add("--details");
        }

        arguments.Add(container);

        return DockerRun(
            arguments.ToArray(),
            dockerExecutable,
            workingDirectory,
            environment: null,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_docker_exec",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRuntimeCommandResponse)),
     Description("Run an arbitrary command inside a Docker container. Supports container user, working directory, and arbitrary container environment variables. No container/command/path allowlist is applied.")]
    public static Task<TalvoraRuntimeCommandResponse> DockerExec(
        string container,
        string[] command,
        string? user = null,
        string? containerWorkingDirectory = null,
        Dictionary<string, string>? containerEnvironment = null,
        string? dockerExecutable = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(container))
        {
            throw new ArgumentException(
                "Container name or ID is required.",
                nameof(container));
        }
        ArgumentNullException.ThrowIfNull(command);
        if (command.Length == 0)
        {
            throw new ArgumentException(
                "Container command cannot be empty.",
                nameof(command));
        }

        var arguments = new List<string> { "exec" };

        if (!string.IsNullOrWhiteSpace(user))
        {
            arguments.Add("--user");
            arguments.Add(user);
        }
        if (!string.IsNullOrWhiteSpace(
                containerWorkingDirectory))
        {
            arguments.Add("--workdir");
            arguments.Add(containerWorkingDirectory);
        }

        foreach (var pair in containerEnvironment
                     ?? new Dictionary<string, string>())
        {
            arguments.Add("--env");
            arguments.Add($"{pair.Key}={pair.Value}");
        }

        arguments.Add(container);
        arguments.AddRange(command);

        return DockerRun(
            arguments.ToArray(),
            dockerExecutable,
            workingDirectory,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_docker_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRuntimeCommandResponse)),
     Description("Run Docker with an arbitrary argument vector, working directory, environment overrides, and timeout. This preserves the complete Docker CLI surface with no subcommand/container/image/path/option allowlist or denylist.")]
    public static Task<TalvoraRuntimeCommandResponse> DockerRun(
        string[] arguments,
        string? dockerExecutable = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 600,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var docker = RequireDocker(dockerExecutable);

        return RunCommandAsync(
            docker,
            arguments,
            workingDirectory,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_docker_compose_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraRuntimeCommandResponse)),
     Description("Run docker compose with an arbitrary Compose argument vector, working directory, environment overrides, and timeout. No project/service/file/option allowlist or denylist is applied.")]
    public static Task<TalvoraRuntimeCommandResponse> DockerComposeRun(
        string[] arguments,
        string? dockerExecutable = null,
        string? workingDirectory = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 600,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return DockerRun(
            new[] { "compose" }.Concat(arguments).ToArray(),
            dockerExecutable,
            workingDirectory,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static TalvoraDockerRowsResponse ToDockerRows(
        string command,
        TalvoraRuntimeCommandResponse result)
    {
        var rows =
            new List<IReadOnlyDictionary<string, string>>();

        foreach (var line in SplitLines(
                     result.StandardOutput))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind !=
                    JsonValueKind.Object)
                {
                    continue;
                }

                var row =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                foreach (var property in
                         document.RootElement.EnumerateObject())
                {
                    row[property.Name] =
                        property.Value.ValueKind ==
                        JsonValueKind.String
                            ? property.Value.GetString()
                              ?? string.Empty
                            : property.Value.ToString();
                }

                rows.Add(row);
            }
            catch (JsonException)
            {
            }
        }

        return new TalvoraDockerRowsResponse(
            command,
            result.ExitCode,
            result.TimedOut,
            rows,
            result.StandardOutput,
            result.StandardError);
    }

    private sealed record PythonCommand(
        string Executable,
        string[] PrefixArguments);

    private static PythonCommand RequirePython(
        string? requested)
    {
        return ResolvePython(requested)
            ?? throw new FileNotFoundException(
                "Python was not found. Install Python through Talvora Chocolatey tools or provide pythonExecutable explicitly.");
    }

    private static PythonCommand? ResolvePython(
        string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var explicitPath =
                ResolveExecutable(requested);
            if (explicitPath is null)
            {
                throw new FileNotFoundException(
                    "Requested Python executable was not found.",
                    requested);
            }

            return IsPythonLauncher(explicitPath)
                ? new PythonCommand(
                    explicitPath,
                    ["-3"])
                : new PythonCommand(
                    explicitPath,
                    []);
        }

        foreach (var name in new[]
                 {
                     "python.exe",
                     "python3.exe",
                     "python",
                     "python3",
                     "py.exe",
                     "py",
                 })
        {
            var candidate = ResolveExecutable(name);
            if (candidate is null)
            {
                continue;
            }

            return IsPythonLauncher(candidate)
                ? new PythonCommand(
                    candidate,
                    ["-3"])
                : new PythonCommand(
                    candidate,
                    []);
        }

        return null;
    }

    private static bool IsPythonLauncher(
        string path) =>
        string.Equals(
            Path.GetFileName(path),
            "py.exe",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            Path.GetFileName(path),
            "py",
            StringComparison.OrdinalIgnoreCase);

    private static string RequireDocker(
        string? requested)
    {
        return ResolveDocker(requested)
            ?? throw new FileNotFoundException(
                "Docker CLI was not found. Install Docker through the supported Windows development environment or provide dockerExecutable explicitly.");
    }

    private static string? ResolveDocker(
        string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return ResolveExecutable(requested)
                ?? throw new FileNotFoundException(
                    "Requested Docker executable was not found.",
                    requested);
        }

        foreach (var name in new[]
                 {
                     "docker.exe",
                     "docker",
                 })
        {
            var candidate = ResolveExecutable(name);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var programFiles =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles);
            var dockerDesktop = Path.Combine(
                programFiles,
                "Docker",
                "Docker",
                "resources",
                "bin",
                "docker.exe");

            if (File.Exists(dockerDesktop))
            {
                return dockerDesktop;
            }
        }

        return null;
    }

    private static string? ResolveExecutable(
        string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(command) ||
            command.Contains(Path.DirectorySeparatorChar) ||
            command.Contains(Path.AltDirectorySeparatorChar))
        {
            try
            {
                var full = Path.GetFullPath(command);
                return File.Exists(full)
                    ? full
                    : null;
            }
            catch
            {
                return null;
            }
        }

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT")
               ?? ".COM;.EXE;.BAT;.CMD")
              .Split(
                  ';',
                  StringSplitOptions.RemoveEmptyEntries |
                  StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var directory in
                 (Environment.GetEnvironmentVariable("PATH")
                  ?? string.Empty)
                 .Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            IEnumerable<string> candidates;

            if (Path.HasExtension(command))
            {
                candidates =
                    [Path.Combine(directory, command)];
            }
            else
            {
                candidates =
                    new[] { Path.Combine(directory, command) }
                    .Concat(
                        extensions.Select(extension =>
                            Path.Combine(
                                directory,
                                command +
                                (extension.StartsWith('.')
                                    ? extension
                                    : "." + extension))));
            }

            foreach (var candidate in candidates)
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
        }

        return null;
    }

    private static async Task<TalvoraRuntimeCommandResponse>
        RunCommandAsync(
            string executable,
            IEnumerable<string> arguments,
            string? workingDirectory,
            Dictionary<string, string?>? environment,
            int timeoutSeconds,
            CancellationToken cancellationToken)
    {
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds));
        }

        var argumentList = arguments.ToArray();
        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        if (!Directory.Exists(cwd))
        {
            throw new DirectoryNotFoundException(
                $"Working directory was not found: {cwd}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment
                     ?? new Dictionary<string, string?>())
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process =
            new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start process: {executable}");
        }

        var stdoutTask =
            process.StandardOutput.ReadToEndAsync(
                cancellationToken);
        var stderrTask =
            process.StandardError.ReadToEndAsync(
                cancellationToken);

        using var timeout =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        if (timeoutSeconds > 0)
        {
            timeout.CancelAfter(
                TimeSpan.FromSeconds(timeoutSeconds));
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
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            await process.WaitForExitAsync(
                CancellationToken.None);
        }

        return new TalvoraRuntimeCommandResponse(
            process.ExitCode,
            await stdoutTask,
            await stderrTask,
            timedOut,
            process.Id,
            executable,
            cwd,
            argumentList);
    }

    private static string[] SplitLines(
        string value) =>
        value.Split(
            new[] { "\r\n", "\n", "\r" },
            StringSplitOptions.RemoveEmptyEntries);
}
