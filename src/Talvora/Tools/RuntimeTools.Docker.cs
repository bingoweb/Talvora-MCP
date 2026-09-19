using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class RuntimeTools
{
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

        foreach (var line in TextLines.Split(
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
}
