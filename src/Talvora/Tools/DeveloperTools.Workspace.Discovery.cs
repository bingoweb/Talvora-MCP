using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class DeveloperTools
{
    private static readonly HashSet<string> GeneratedWorkspaceDirectories =
        new(
            [
                ".git",
                ".gradle",
                ".idea",
                ".vs",
                ".dart_tool",
                ".pub-cache",
                "node_modules",
                "bin",
                "obj",
                "target",
                "build",
                "dist",
                "out",
                "coverage",
            ],
            StringComparer.OrdinalIgnoreCase);

    [McpServerTool(
        Name = "talvora_workspace_inspect",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWorkspaceInspectResponse)),
     Description("Statically inspect a software workspace without executing project code. Discovers .NET, Node, Python, Rust, Go, Maven, Gradle/Android, Flutter/Dart, CMake, and Docker project roots plus package-manager/toolchain pins, lockfiles, and package scripts.")]
    public static TalvoraWorkspaceInspectResponse WorkspaceInspect(
        string root,
        int maxDepth = 6,
        int maxProjects = 500,
        bool includeGenerated = false,
        bool followReparsePoints = false,
        CancellationToken cancellationToken = default)
    {
        if (maxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }

        if (maxProjects < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxProjects));
        }

        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException(
                $"Workspace root was not found: {fullRoot}");
        }

        var projects = new List<TalvoraWorkspaceProject>();
        var errors = new List<string>();
        var queue = new Queue<(DirectoryInfo Directory, int Depth)>();
        var visited = new HashSet<string>(PathComparer)
        {
            fullRoot,
        };

        queue.Enqueue((new DirectoryInfo(fullRoot), 0));
        var truncated = false;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = queue.Dequeue();

            FileInfo[] files;
            DirectoryInfo[] directories;

            try
            {
                files = directory
                    .EnumerateFiles()
                    .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                directories = directory
                    .EnumerateDirectories()
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(
                    $"{directory.FullName}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            InspectWorkspaceDirectory(
                fullRoot,
                directory,
                files,
                directories,
                projects,
                errors,
                cancellationToken);

            if (maxProjects > 0 && projects.Count >= maxProjects)
            {
                if (projects.Count > maxProjects)
                {
                    projects.RemoveRange(
                        maxProjects,
                        projects.Count - maxProjects);
                }

                truncated = true;
                break;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var child in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!includeGenerated &&
                    GeneratedWorkspaceDirectories.Contains(child.Name))
                {
                    continue;
                }

                var isReparse =
                    (child.Attributes & FileAttributes.ReparsePoint) != 0;
                if (isReparse && !followReparsePoints)
                {
                    continue;
                }

                var key = child.FullName;
                if (isReparse)
                {
                    try
                    {
                        key =
                            child.ResolveLinkTarget(returnFinalTarget: true)
                                ?.FullName
                            ?? child.FullName;
                    }
                    catch (Exception ex) when (
                        ex is IOException or UnauthorizedAccessException)
                    {
                        errors.Add(
                            $"{child.FullName}: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }
                }

                key = Path.GetFullPath(key);
                if (visited.Add(key))
                {
                    queue.Enqueue((child, depth + 1));
                }
            }
        }

        return new TalvoraWorkspaceInspectResponse(
            fullRoot,
            projects.Count,
            truncated,
            projects
                .OrderBy(project => project.Directory, PathComparer)
                .ThenBy(project => project.Ecosystem, StringComparer.Ordinal)
                .ToArray(),
            errors);
    }

    private static void InspectWorkspaceDirectory(
        string workspaceRoot,
        DirectoryInfo directory,
        IReadOnlyList<FileInfo> files,
        IReadOnlyList<DirectoryInfo> directories,
        List<TalvoraWorkspaceProject> projects,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FileInfo? Find(string name) =>
            files.FirstOrDefault(file =>
                string.Equals(
                    file.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));

        var solution = files.FirstOrDefault(file =>
            file.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            file.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase));

        var dotnetProject = files.FirstOrDefault(file =>
            file.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            file.Extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            file.Extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase));

        if (solution is not null || dotnetProject is not null)
        {
            var marker = solution ?? dotnetProject!;
            projects.Add(new TalvoraWorkspaceProject(
                directory.FullName,
                "dotnet",
                Path.GetFileNameWithoutExtension(marker.Name),
                [marker.Name],
                null,
                ReadDotnetSdkVersion(
                    directory.FullName,
                    workspaceRoot,
                    errors),
                []));
        }

        var packageJson = Find("package.json");
        if (packageJson is not null)
        {
            var package = ReadNodeProject(
                packageJson.FullName,
                directory.FullName,
                files,
                errors);
            projects.Add(package);
        }

        var pyproject = Find("pyproject.toml");
        var requirements = Find("requirements.txt");
        if (pyproject is not null || requirements is not null)
        {
            var markers = new[] { pyproject?.Name, requirements?.Name }
                .Where(value => value is not null)
                .Cast<string>()
                .ToArray();

            projects.Add(new TalvoraWorkspaceProject(
                directory.FullName,
                "python",
                directory.Name,
                markers,
                null,
                ReadPythonVersionHint(pyproject?.FullName, errors),
                []));
        }

        var cargo = Find("Cargo.toml");
        if (cargo is not null)
        {
            projects.Add(new TalvoraWorkspaceProject(
                directory.FullName,
                "rust",
                ReadTomlName(cargo.FullName, directory.Name, errors),
                [cargo.Name],
                "cargo",
                ReadRustToolchain(directory.FullName, errors),
                []));
        }

        var goMod = Find("go.mod");
        if (goMod is not null)
        {
            projects.Add(new TalvoraWorkspaceProject(
                directory.FullName,
                "go",
                ReadGoModuleName(goMod.FullName, directory.Name, errors),
                [goMod.Name],
                "go",
                ReadGoToolchain(goMod.FullName, errors),
                []));
        }

        var pom = Find("pom.xml");
        if (pom is not null)
        {
            var markers = new List<string> { pom.Name };
            var mvnw = Find("mvnw.cmd");
            if (mvnw is not null)
            {
                markers.Add(mvnw.Name);
            }

            projects.Add(new TalvoraWorkspaceProject(
                directory.FullName,
                "maven",
                directory.Name,
                markers,
                "maven",
                ReadMavenWrapperVersion(directory.FullName, errors),
                []));
        }

        var buildGradle = Find("build.gradle") ?? Find("build.gradle.kts");
        var settingsGradle = Find("settings.gradle") ?? Find("settings.gradle.kts");
        if (buildGradle is not null || settingsGradle is not null)
        {
            var markers = new List<string>();
            if (buildGradle is not null)
            {
                markers.Add(buildGradle.Name);
            }
            if (settingsGradle is not null)
            {
                markers.Add(settingsGradle.Name);
            }

            var gradlew = Find("gradlew.bat");
            if (gradlew is not null)
            {
                markers.Add(gradlew.Name);
            }

            var isAndroid =
                directories.Any(child =>
                    string.Equals(
                        child.Name,
                        "app",
                        StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(
                        Path.Combine(
                            child.FullName,
                            "src",
                            "main",
                            "AndroidManifest.xml")));

            projects.Add(new TalvoraWorkspaceProject(
                directory.FullName,
                isAndroid ? "android-gradle" : "gradle",
                directory.Name,
                markers,
                "gradle",
                ReadGradleWrapperVersion(directory.FullName, errors),
                []));
        }

        var pubspec = Find("pubspec.yaml");
        if (pubspec is not null)
        {
            var flutter = IsFlutterPubspec(pubspec.FullName, errors);
            projects.Add(new TalvoraWorkspaceProject(
                directory.FullName,
                flutter ? "flutter" : "dart",
                ReadYamlName(pubspec.FullName, directory.Name, errors),
                [pubspec.Name],
                flutter ? "flutter" : "dart",
                ReadDartSdkConstraint(pubspec.FullName, errors),
                []));
        }

        var cmake = Find("CMakeLists.txt");
        if (cmake is not null)
        {
            projects.Add(new TalvoraWorkspaceProject(
                directory.FullName,
                "cmake",
                directory.Name,
                [cmake.Name],
                null,
                null,
                []));
        }

        var dockerfile = Find("Dockerfile");
        var compose =
            Find("compose.yml") ??
            Find("compose.yaml") ??
            Find("docker-compose.yml") ??
            Find("docker-compose.yaml");

        if (dockerfile is not null || compose is not null)
        {
            var markers = new[] { dockerfile?.Name, compose?.Name }
                .Where(value => value is not null)
                .Cast<string>()
                .ToArray();

            projects.Add(new TalvoraWorkspaceProject(
                directory.FullName,
                "docker",
                directory.Name,
                markers,
                "docker",
                null,
                []));
        }
    }

    private static TalvoraWorkspaceProject ReadNodeProject(
        string packageJsonPath,
        string directory,
        IReadOnlyList<FileInfo> files,
        List<string> errors)
    {
        var name = new DirectoryInfo(directory).Name;
        string? packageManager = null;
        var scripts = Array.Empty<string>();

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(packageJsonPath));
            var root = document.RootElement;

            if (root.TryGetProperty("name", out var nameValue) &&
                nameValue.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(nameValue.GetString()))
            {
                name = nameValue.GetString()!;
            }

            if (root.TryGetProperty("packageManager", out var managerValue) &&
                managerValue.ValueKind == JsonValueKind.String)
            {
                packageManager = managerValue.GetString();
            }

            if (root.TryGetProperty("scripts", out var scriptsValue) &&
                scriptsValue.ValueKind == JsonValueKind.Object)
            {
                scripts = scriptsValue
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            errors.Add(
                $"{packageJsonPath}: {ex.GetType().Name}: {ex.Message}");
        }

        var lockFiles = files
            .Where(file =>
                file.Name.Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase) ||
                file.Name.Equals("yarn.lock", StringComparison.OrdinalIgnoreCase) ||
                file.Name.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase) ||
                file.Name.Equals("bun.lock", StringComparison.OrdinalIgnoreCase) ||
                file.Name.Equals("bun.lockb", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.Name)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        packageManager ??= InferNodePackageManager(lockFiles);

        return new TalvoraWorkspaceProject(
            directory,
            "node",
            name,
            new[] { "package.json" }
                .Concat(lockFiles)
                .ToArray(),
            packageManager,
            null,
            scripts);
    }

    private static string? InferNodePackageManager(
        IReadOnlyList<string> lockFiles)
    {
        if (lockFiles.Any(file =>
                file.Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase)))
        {
            return "pnpm";
        }

        if (lockFiles.Any(file =>
                file.Equals("yarn.lock", StringComparison.OrdinalIgnoreCase)))
        {
            return "yarn";
        }

        if (lockFiles.Any(file =>
                file.Equals("bun.lock", StringComparison.OrdinalIgnoreCase) ||
                file.Equals("bun.lockb", StringComparison.OrdinalIgnoreCase)))
        {
            return "bun";
        }

        if (lockFiles.Any(file =>
                file.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase)))
        {
            return "npm";
        }

        return null;
    }

    private static string? ReadDotnetSdkVersion(
        string directory,
        string workspaceRoot,
        List<string> errors)
    {
        var current = new DirectoryInfo(directory);
        var root = Path.GetFullPath(workspaceRoot);

        while (current is not null)
        {
            var globalJson = Path.Combine(current.FullName, "global.json");
            if (File.Exists(globalJson))
            {
                try
                {
                    using var document = JsonDocument.Parse(
                        File.ReadAllText(globalJson));

                    if (document.RootElement.TryGetProperty(
                            "sdk",
                            out var sdk) &&
                        sdk.TryGetProperty(
                            "version",
                            out var version) &&
                        version.ValueKind == JsonValueKind.String)
                    {
                        return version.GetString();
                    }
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    errors.Add(
                        $"{globalJson}: {ex.GetType().Name}: {ex.Message}");
                }

                return null;
            }

            if (PathComparer.Equals(current.FullName, root))
            {
                break;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? ReadGoToolchain(
        string path,
        List<string> errors)
    {
        var lines = ReadAllLinesSafe(path, errors);
        var toolchain = lines.FirstOrDefault(line =>
            line.TrimStart().StartsWith(
                "toolchain ",
                StringComparison.Ordinal));
        if (toolchain is not null)
        {
            return toolchain.Trim()["toolchain ".Length..].Trim();
        }

        var go = lines.FirstOrDefault(line =>
            line.TrimStart().StartsWith(
                "go ",
                StringComparison.Ordinal));
        return go is null
            ? null
            : "go" + go.Trim()["go ".Length..].Trim();
    }

    private static string ReadGoModuleName(
        string path,
        string fallback,
        List<string> errors)
    {
        var line = ReadAllLinesSafe(path, errors)
            .FirstOrDefault(value =>
                value.TrimStart().StartsWith(
                    "module ",
                    StringComparison.Ordinal));

        return line is null
            ? fallback
            : line.Trim()["module ".Length..].Trim();
    }

    private static string? ReadRustToolchain(
        string directory,
        List<string> errors)
    {
        var toml = Path.Combine(directory, "rust-toolchain.toml");
        if (File.Exists(toml))
        {
            var text = ReadAllTextSafe(toml, errors);
            var match = Regex.Match(
                text,
                @"(?m)^\s*channel\s*=\s*[""'](?<value>[^""']+)[""']\s*$",
                RegexOptions.CultureInvariant);
            return match.Success
                ? match.Groups["value"].Value
                : null;
        }

        var plain = Path.Combine(directory, "rust-toolchain");
        return File.Exists(plain)
            ? ReadAllTextSafe(plain, errors).Trim()
            : null;
    }

    private static string? ReadGradleWrapperVersion(
        string directory,
        List<string> errors) =>
        ReadWrapperVersion(
            Path.Combine(
                directory,
                "gradle",
                "wrapper",
                "gradle-wrapper.properties"),
            @"gradle-(?<version>[^/\\-]+(?:\.[^/\\-]+)*)-(?:bin|all)\.zip",
            errors);

    private static string? ReadMavenWrapperVersion(
        string directory,
        List<string> errors) =>
        ReadWrapperVersion(
            Path.Combine(
                directory,
                ".mvn",
                "wrapper",
                "maven-wrapper.properties"),
            @"apache-maven-(?<version>[^/\\]+?)-bin\.(?:zip|tar\.gz)",
            errors);

    private static string? ReadWrapperVersion(
        string path,
        string pattern,
        List<string> errors)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var text = ReadAllTextSafe(path, errors);
        var match = Regex.Match(
            text,
            pattern,
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["version"].Value
            : null;
    }

    private static string ReadTomlName(
        string path,
        string fallback,
        List<string> errors)
    {
        var text = ReadAllTextSafe(path, errors);
        var match = Regex.Match(
            text,
            @"(?m)^\s*name\s*=\s*[""'](?<value>[^""']+)[""']\s*$",
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["value"].Value
            : fallback;
    }

    private static string ReadYamlName(
        string path,
        string fallback,
        List<string> errors)
    {
        var text = ReadAllTextSafe(path, errors);
        var match = Regex.Match(
            text,
            @"(?m)^name:\s*(?<value>[^#\r\n]+)",
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["value"].Value.Trim()
            : fallback;
    }

    private static bool IsFlutterPubspec(
        string path,
        List<string> errors)
    {
        var text = ReadAllTextSafe(path, errors);
        return Regex.IsMatch(
            text,
            @"(?m)^\s*sdk:\s*flutter\s*$",
            RegexOptions.CultureInvariant);
    }

    private static string? ReadDartSdkConstraint(
        string path,
        List<string> errors)
    {
        var text = ReadAllTextSafe(path, errors);
        var match = Regex.Match(
            text,
            @"(?m)^\s*sdk:\s*[""']?(?<value>[^""'#\r\n]+)",
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["value"].Value.Trim()
            : null;
    }

    private static string? ReadPythonVersionHint(
        string? pyprojectPath,
        List<string> errors)
    {
        if (pyprojectPath is null)
        {
            return null;
        }

        var text = ReadAllTextSafe(pyprojectPath, errors);
        var match = Regex.Match(
            text,
            @"(?m)^\s*requires-python\s*=\s*[""'](?<value>[^""']+)[""']",
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["value"].Value
            : null;
    }

    private static string ReadAllTextSafe(
        string path,
        List<string> errors)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(
                $"{path}: {ex.GetType().Name}: {ex.Message}");
            return string.Empty;
        }
    }

    private static string[] ReadAllLinesSafe(
        string path,
        List<string> errors)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(
                $"{path}: {ex.GetType().Name}: {ex.Message}");
            return [];
        }
    }
}
