using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class DeveloperTools
{
    [McpServerTool(
        Name = "talvora_workspace_commands",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWorkspaceCommandsResponse)),
     Description("Statically infer useful restore/install/build/test/run/analyze commands for discovered projects without executing them. maxProjects=0/maxCommands=0 request finite server maximum pages. Use projectOffset/nextProjectOffset for project pages and commandOffset/nextCommandOffset for command pages while the workspace is unchanged.")]
    public static TalvoraWorkspaceCommandsResponse WorkspaceCommands(
        string root,
        int maxDepth = 6,
        int maxProjects = 500,
        int maxCommands = 1000,
        long projectOffset = 0,
        long commandOffset = 0,
        bool includeGenerated = false,
        bool followReparsePoints = false,
        CancellationToken cancellationToken = default)
    {
        if (maxCommands < 0 || projectOffset < 0 || commandOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                "maxCommands, projectOffset, and commandOffset cannot be negative.");
        }

        var effectiveMaxCommands =
            maxCommands == 0
                ? AbsoluteWorkspaceCommands
                : Math.Min(
                    maxCommands,
                    AbsoluteWorkspaceCommands);

        var inspection = WorkspaceInspect(
            root,
            maxDepth,
            maxProjects,
            projectOffset,
            includeGenerated,
            followReparsePoints,
            cancellationToken);

        var commands = new List<TalvoraWorkspaceCommand>();
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        long seenCommands = 0;

        foreach (var project in inspection.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var command in BuildWorkspaceCommands(project))
            {
                var key =
                    command.WorkingDirectory +
                    "\u001f" +
                    command.Executable +
                    "\u001f" +
                    string.Join("\u001f", command.Arguments);

                if (!seen.Add(key))
                {
                    continue;
                }

                if (seenCommands < commandOffset)
                {
                    seenCommands++;
                    continue;
                }

                if (commands.Count >= effectiveMaxCommands)
                {
                    return new TalvoraWorkspaceCommandsResponse(
                        inspection.Root,
                        commands.Count,
                        true,
                        commands,
                        inspection.Errors,
                        projectOffset,
                        null,
                        commandOffset,
                        checked(commandOffset + commands.Count));
                }

                commands.Add(command);
                seenCommands++;
            }
        }

        return new TalvoraWorkspaceCommandsResponse(
            inspection.Root,
            commands.Count,
            inspection.Truncated,
            commands,
            inspection.Errors,
            projectOffset,
            inspection.NextProjectOffset,
            commandOffset,
            null);
    }

    private static IEnumerable<TalvoraWorkspaceCommand>
        BuildWorkspaceCommands(
            TalvoraWorkspaceProject project)
    {
        var directory = project.Directory;

        switch (project.Ecosystem)
        {
            case "dotnet":
            {
                var target = project.Markers.FirstOrDefault() ?? ".";
                yield return Command(
                    project,
                    "restore",
                    "dotnet",
                    ["restore", target],
                    "inferred from .NET project/solution marker");
                yield return Command(
                    project,
                    "build",
                    "dotnet",
                    ["build", target],
                    "inferred from .NET project/solution marker");
                yield return Command(
                    project,
                    "test",
                    "dotnet",
                    ["test", target],
                    "inferred from .NET project/solution marker");
                yield break;
            }

            case "node":
            {
                var manager =
                    ExtractPackageManagerName(project.PackageManager) ??
                    "npm";

                var installArgs = manager == "npm" &&
                    project.Markers.Any(marker =>
                        marker.Equals(
                            "package-lock.json",
                            StringComparison.OrdinalIgnoreCase))
                    ? new[] { "ci" }
                    : new[] { "install" };

                yield return Command(
                    project,
                    "install",
                    manager,
                    installArgs,
                    project.PackageManager is null
                        ? "inferred from lockfile/default"
                        : "package.json packageManager");

                foreach (var script in project.Scripts)
                {
                    yield return Command(
                        project,
                        "script:" + script,
                        manager,
                        ["run", script],
                        "package.json scripts");
                }

                yield break;
            }

            case "python":
            {
                if (project.Markers.Any(marker =>
                        marker.Equals(
                            "requirements.txt",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    yield return Command(
                        project,
                        "install",
                        "python",
                        ["-m", "pip", "install", "-r", "requirements.txt"],
                        "requirements.txt");
                }
                else
                {
                    yield return Command(
                        project,
                        "install",
                        "python",
                        ["-m", "pip", "install", "-e", "."],
                        "pyproject.toml");
                }

                yield return Command(
                    project,
                    "test",
                    "python",
                    ["-m", "pytest"],
                    "common Python test entry point");
                yield break;
            }

            case "rust":
                yield return Command(
                    project,
                    "build",
                    "cargo",
                    ["build"],
                    "Cargo.toml");
                yield return Command(
                    project,
                    "test",
                    "cargo",
                    ["test"],
                    "Cargo.toml");
                yield return Command(
                    project,
                    "run",
                    "cargo",
                    ["run"],
                    "Cargo.toml");
                yield break;

            case "go":
                yield return Command(
                    project,
                    "build",
                    "go",
                    ["build", "./..."],
                    "go.mod");
                yield return Command(
                    project,
                    "test",
                    "go",
                    ["test", "./..."],
                    "go.mod");
                yield return Command(
                    project,
                    "run",
                    "go",
                    ["run", "."],
                    "go.mod");
                yield break;

            case "maven":
            {
                var wrapper = Path.Combine(directory, "mvnw.cmd");
                var executable = File.Exists(wrapper)
                    ? wrapper
                    : "mvn";

                yield return Command(
                    project,
                    "build",
                    executable,
                    ["clean", "verify"],
                    File.Exists(wrapper)
                        ? "Maven Wrapper"
                        : "pom.xml");
                yield return Command(
                    project,
                    "test",
                    executable,
                    ["test"],
                    File.Exists(wrapper)
                        ? "Maven Wrapper"
                        : "pom.xml");
                yield break;
            }

            case "gradle":
            case "android-gradle":
            {
                var wrapper = Path.Combine(directory, "gradlew.bat");
                var executable = File.Exists(wrapper)
                    ? wrapper
                    : "gradle";

                yield return Command(
                    project,
                    "build",
                    executable,
                    ["build"],
                    File.Exists(wrapper)
                        ? "Gradle Wrapper"
                        : "Gradle project");
                yield return Command(
                    project,
                    "test",
                    executable,
                    ["test"],
                    File.Exists(wrapper)
                        ? "Gradle Wrapper"
                        : "Gradle project");
                yield break;
            }

            case "flutter":
                yield return Command(
                    project,
                    "install",
                    "flutter",
                    ["pub", "get"],
                    "pubspec.yaml");
                yield return Command(
                    project,
                    "analyze",
                    "flutter",
                    ["analyze"],
                    "pubspec.yaml");
                yield return Command(
                    project,
                    "test",
                    "flutter",
                    ["test"],
                    "pubspec.yaml");
                yield return Command(
                    project,
                    "run",
                    "flutter",
                    ["run"],
                    "pubspec.yaml");
                yield break;

            case "dart":
                yield return Command(
                    project,
                    "install",
                    "dart",
                    ["pub", "get"],
                    "pubspec.yaml");
                yield return Command(
                    project,
                    "analyze",
                    "dart",
                    ["analyze"],
                    "pubspec.yaml");
                yield return Command(
                    project,
                    "test",
                    "dart",
                    ["test"],
                    "pubspec.yaml");
                yield return Command(
                    project,
                    "run",
                    "dart",
                    ["run"],
                    "pubspec.yaml");
                yield break;

            case "cmake":
            {
                var buildDirectory = Path.Combine(directory, "build");
                yield return Command(
                    project,
                    "configure",
                    "cmake",
                    ["-S", ".", "-B", buildDirectory],
                    "CMakeLists.txt");
                yield return Command(
                    project,
                    "build",
                    "cmake",
                    ["--build", buildDirectory],
                    "CMakeLists.txt");
                yield return Command(
                    project,
                    "test",
                    "ctest",
                    ["--test-dir", buildDirectory],
                    "CMakeLists.txt");
                yield break;
            }

            case "docker":
            {
                var compose = project.Markers.FirstOrDefault(marker =>
                    marker.Equals("compose.yml", StringComparison.OrdinalIgnoreCase) ||
                    marker.Equals("compose.yaml", StringComparison.OrdinalIgnoreCase) ||
                    marker.Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase) ||
                    marker.Equals("docker-compose.yaml", StringComparison.OrdinalIgnoreCase));

                if (compose is not null)
                {
                    yield return Command(
                        project,
                        "up",
                        "docker",
                        ["compose", "-f", compose, "up"],
                        compose);
                }

                if (project.Markers.Any(marker =>
                        marker.Equals(
                            "Dockerfile",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    yield return Command(
                        project,
                        "build",
                        "docker",
                        ["build", "-t", project.Name, "."],
                        "Dockerfile");
                }

                yield break;
            }
        }
    }

    private static TalvoraWorkspaceCommand Command(
        TalvoraWorkspaceProject project,
        string purpose,
        string executable,
        IReadOnlyList<string> arguments,
        string source) =>
        new(
            project.Directory,
            project.Ecosystem,
            purpose,
            executable,
            arguments,
            source);

    private static string? ExtractPackageManagerName(
        string? packageManager)
    {
        if (string.IsNullOrWhiteSpace(packageManager))
        {
            return null;
        }

        var at = packageManager.IndexOf('@');
        return at > 0
            ? packageManager[..at]
            : packageManager;
    }
}
