using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public static partial class BuildRunnerTools
{
    [McpServerTool(
        Name = "talvora_java_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJavaInfoResponse)),
     Description("Resolve Java/JDK executables and report java/javac versions plus JAVA_HOME. Missing Java components are reported structurally instead of throwing.")]
    public static async Task<TalvoraJavaInfoResponse> JavaInfo(
        CancellationToken cancellationToken = default)
    {
        var java = ResolveJavaExecutable("java");
        var javac = ResolveJavaExecutable("javac");

        string? javaVersion = null;
        string? javacVersion = null;

        if (java is not null)
        {
            var result = await RunCliCheckedAsync(
                java,
                Environment.CurrentDirectory,
                ["--version"],
                timeoutSeconds: 30,
                cancellationToken: cancellationToken);
            javaVersion = FirstNonEmptyLine(
                result.StandardOutput,
                result.StandardError);
        }

        if (javac is not null)
        {
            var result = await RunCliCheckedAsync(
                javac,
                Environment.CurrentDirectory,
                ["--version"],
                timeoutSeconds: 30,
                cancellationToken: cancellationToken);
            javacVersion = FirstNonEmptyLine(
                result.StandardOutput,
                result.StandardError);
        }

        return new TalvoraJavaInfoResponse(
            java is not null,
            java,
            javaVersion,
            javac is not null,
            javac,
            javacVersion,
            ResolveJavaHome(java, javac));
    }

    [McpServerTool(
        Name = "talvora_java_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the Java launcher with an arbitrary argument vector in any accessible working directory. An explicit javaExecutable overrides JAVA_HOME/PATH resolution; no class/module/JVM-option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> JavaRun(
        string workingDirectory,
        string[] arguments,
        string? javaExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = ResolveRequestedExecutable(
            javaExecutable,
            () => ResolveJavaExecutable("java"),
            "java");

        return RunCliAsync(
            executable,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_javac_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the Java compiler with an arbitrary argument vector in any accessible working directory. An explicit javacExecutable overrides JAVA_HOME/PATH resolution; no source/classpath/module/processor option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> JavacRun(
        string workingDirectory,
        string[] arguments,
        string? javacExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = ResolveRequestedExecutable(
            javacExecutable,
            () => ResolveJavaExecutable("javac"),
            "javac");

        return RunCliAsync(
            executable,
            NormalizeWorkingDirectory(workingDirectory),
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_gradle_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraBuildToolInfoResponse)),
     Description("Resolve Gradle for a working directory. Explicit gradleExecutable wins; otherwise a project-local gradlew.bat is preferred, then the machine Gradle installation. Wrapper version is read from gradle-wrapper.properties without executing the wrapper.")]
    public static async Task<TalvoraBuildToolInfoResponse> GradleInfo(
        string? workingDirectory = null,
        bool preferWrapper = true,
        string? gradleExecutable = null,
        CancellationToken cancellationToken = default)
    {
        var cwd = NormalizeOptionalWorkingDirectory(workingDirectory);
        var resolved = ResolveGradle(cwd, preferWrapper, gradleExecutable);

        if (resolved.Executable is null)
        {
            return new TalvoraBuildToolInfoResponse(
                "gradle", false, null, null, false, null, cwd);
        }

        string? version = null;
        string? distribution = null;

        if (resolved.UsingWrapper)
        {
            distribution = ReadWrapperDistribution(
                Path.Combine(cwd, "gradle", "wrapper", "gradle-wrapper.properties"));
            version = TryExtractGradleVersion(distribution);
        }
        else
        {
            var result = await RunCliCheckedAsync(
                resolved.Executable,
                cwd,
                ["--version"],
                timeoutSeconds: 60,
                cancellationToken: cancellationToken);
            version = ParseLineValue(result.StandardOutput, "Gradle ");
        }

        return new TalvoraBuildToolInfoResponse(
            "gradle",
            true,
            resolved.Executable,
            version,
            resolved.UsingWrapper,
            distribution,
            cwd);
    }

    [McpServerTool(
        Name = "talvora_gradle_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run Gradle with an arbitrary task/option argument vector. Explicit gradleExecutable wins; otherwise a project-local gradlew.bat is preferred, then machine Gradle. No task/plugin/repository/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> GradleRun(
        string workingDirectory,
        string[] arguments,
        bool preferWrapper = true,
        string? gradleExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var cwd = NormalizeWorkingDirectory(workingDirectory);
        var resolved = ResolveGradle(cwd, preferWrapper, gradleExecutable);

        if (resolved.Executable is null)
        {
            throw new FileNotFoundException(
                "Gradle was not found. Add gradlew.bat to the project or install Gradle on the machine.");
        }

        return RunCliAsync(
            resolved.Executable,
            cwd,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_maven_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraBuildToolInfoResponse)),
     Description("Resolve Maven for a working directory. Explicit mavenExecutable wins; otherwise a project-local mvnw.cmd is preferred, then the machine Maven installation. Wrapper version is read from maven-wrapper.properties without executing the wrapper.")]
    public static async Task<TalvoraBuildToolInfoResponse> MavenInfo(
        string? workingDirectory = null,
        bool preferWrapper = true,
        string? mavenExecutable = null,
        CancellationToken cancellationToken = default)
    {
        var cwd = NormalizeOptionalWorkingDirectory(workingDirectory);
        var resolved = ResolveMaven(cwd, preferWrapper, mavenExecutable);

        if (resolved.Executable is null)
        {
            return new TalvoraBuildToolInfoResponse(
                "maven", false, null, null, false, null, cwd);
        }

        string? version = null;
        string? distribution = null;

        if (resolved.UsingWrapper)
        {
            distribution = ReadWrapperDistribution(
                Path.Combine(cwd, ".mvn", "wrapper", "maven-wrapper.properties"));
            version = TryExtractMavenVersion(distribution);
        }
        else
        {
            var result = await RunCliCheckedAsync(
                resolved.Executable,
                cwd,
                ["--version"],
                timeoutSeconds: 60,
                cancellationToken: cancellationToken);
            version = ParseLineValue(result.StandardOutput, "Apache Maven ");
        }

        return new TalvoraBuildToolInfoResponse(
            "maven",
            true,
            resolved.Executable,
            version,
            resolved.UsingWrapper,
            distribution,
            cwd);
    }

    [McpServerTool(
        Name = "talvora_maven_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run Maven with an arbitrary goal/phase/option argument vector. Explicit mavenExecutable wins; otherwise a project-local mvnw.cmd is preferred, then machine Maven. No goal/plugin/repository/profile/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> MavenRun(
        string workingDirectory,
        string[] arguments,
        bool preferWrapper = true,
        string? mavenExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var cwd = NormalizeWorkingDirectory(workingDirectory);
        var resolved = ResolveMaven(cwd, preferWrapper, mavenExecutable);

        if (resolved.Executable is null)
        {
            throw new FileNotFoundException(
                "Maven was not found. Add mvnw.cmd to the project or install Maven on the machine.");
        }

        return RunCliAsync(
            resolved.Executable,
            cwd,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static (string? Executable, bool UsingWrapper) ResolveGradle(
        string workingDirectory,
        bool preferWrapper,
        string? explicitExecutable)
    {
        if (!string.IsNullOrWhiteSpace(explicitExecutable))
        {
            return (ResolveExplicitExecutable(explicitExecutable, "Gradle"), false);
        }

        var wrapper = Path.Combine(workingDirectory, "gradlew.bat");
        if (preferWrapper && File.Exists(wrapper))
        {
            return (wrapper, true);
        }

        return (
            CommandResolver.Resolve(
                ["gradle.exe", "gradle.bat", "gradle.cmd", "gradle"]),
            false);
    }

    private static (string? Executable, bool UsingWrapper) ResolveMaven(
        string workingDirectory,
        bool preferWrapper,
        string? explicitExecutable)
    {
        if (!string.IsNullOrWhiteSpace(explicitExecutable))
        {
            return (ResolveExplicitExecutable(explicitExecutable, "Maven"), false);
        }

        var wrapper = Path.Combine(workingDirectory, "mvnw.cmd");
        if (preferWrapper && File.Exists(wrapper))
        {
            return (wrapper, true);
        }

        var resolved = CommandResolver.Resolve(
            ["mvn.cmd", "mvn.exe", "mvn"]);
        if (resolved is not null)
        {
            return (resolved, false);
        }

        var chocolateyInstall =
            Environment.GetEnvironmentVariable("ChocolateyInstall");
        if (string.IsNullOrWhiteSpace(chocolateyInstall))
        {
            chocolateyInstall = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "chocolatey");
        }

        var mavenRoot = Path.Combine(chocolateyInstall, "lib", "maven");
        if (Directory.Exists(mavenRoot))
        {
            var candidate = Directory
                .EnumerateFiles(mavenRoot, "mvn.cmd", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (candidate is not null)
            {
                return (candidate, false);
            }
        }

        return (null, false);
    }

    private static string? ResolveJavaExecutable(string command)
    {
        var candidates = new List<string>();

        foreach (var javaHome in EnumerateModernJavaHomes())
        {
            candidates.Add(
                Path.Combine(
                    javaHome,
                    "bin",
                    command + ".exe"));
        }

        return CommandResolver.Resolve(
            [command + ".exe", command],
            candidates);
    }

    private static IEnumerable<string> EnumerateModernJavaHomes()
    {
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            var machineJavaHome =
                Environment.GetEnvironmentVariable(
                    "JAVA_HOME",
                    EnvironmentVariableTarget.Machine);

            if (!string.IsNullOrWhiteSpace(machineJavaHome) &&
                Directory.Exists(machineJavaHome))
            {
                var full = Path.GetFullPath(machineJavaHome);
                if (seen.Add(full))
                {
                    yield return full;
                }
            }
        }

        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            var adoptiumRoot = Path.Combine(
                programFiles,
                "Eclipse Adoptium");

            if (Directory.Exists(adoptiumRoot))
            {
                foreach (var directory in Directory
                             .EnumerateDirectories(
                                 adoptiumRoot,
                                 "jdk-*",
                                 SearchOption.TopDirectoryOnly)
                             .Select(path => new
                             {
                                 Path = path,
                                 Version = ParseJdkDirectoryVersion(
                                     Path.GetFileName(path)),
                                 LastWrite =
                                     Directory.GetLastWriteTimeUtc(path),
                             })
                             .OrderByDescending(
                                 item => item.Version)
                             .ThenByDescending(
                                 item => item.LastWrite))
                {
                    var full = Path.GetFullPath(directory.Path);
                    if (seen.Add(full))
                    {
                        yield return full;
                    }
                }
            }
        }

        var processJavaHome =
            Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(processJavaHome) &&
            Directory.Exists(processJavaHome))
        {
            var full = Path.GetFullPath(processJavaHome);
            if (seen.Add(full))
            {
                yield return full;
            }
        }
    }

    private static Version ParseJdkDirectoryVersion(
        string directoryName)
    {
        const string prefix = "jdk-";
        var value = directoryName.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase)
            ? directoryName[prefix.Length..]
            : directoryName;

        var separator = value.IndexOfAny(['-', '+']);
        if (separator >= 0)
        {
            value = value[..separator];
        }

        return Version.TryParse(value, out var version)
            ? version
            : new Version(0, 0);
    }

    private static string ResolveRequestedExecutable(
        string? explicitExecutable,
        Func<string?> resolver,
        string displayName)
    {
        if (!string.IsNullOrWhiteSpace(explicitExecutable))
        {
            return ResolveExplicitExecutable(explicitExecutable, displayName);
        }

        return resolver()
            ?? throw new FileNotFoundException(
                $"{displayName} executable was not found.");
    }

    private static string ResolveExplicitExecutable(
        string executable,
        string displayName)
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
            $"{displayName} executable was not found.",
            full);
    }

    private static string NormalizeOptionalWorkingDirectory(
        string? workingDirectory) =>
        string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetFullPath(Environment.CurrentDirectory)
            : NormalizeWorkingDirectory(workingDirectory);

    private static string? ResolveJavaHome(
        string? java,
        string? javac)
    {
        var executable = javac ?? java;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            var bin = Path.GetDirectoryName(executable);
            var resolved = string.IsNullOrWhiteSpace(bin)
                ? null
                : Directory.GetParent(bin)?.FullName;

            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return EnumerateModernJavaHomes().FirstOrDefault();
    }

    private static string? FirstNonEmptyLine(params string[] values)
    {
        foreach (var value in values)
        {
            var line = TextLines
                .Split(value)
                .FirstOrDefault(
                    item => !string.IsNullOrWhiteSpace(item));

            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return null;
    }

    private static string? ParseLineValue(
        string output,
        string prefix)
    {
        var line = TextLines
            .Split(output)
            .FirstOrDefault(
                item => item.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase));

        return line is null
            ? FirstNonEmptyLine(output)
            : line[prefix.Length..].Trim();
    }

    private static string? ReadWrapperDistribution(
        string propertiesPath)
    {
        if (!File.Exists(propertiesPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(propertiesPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(
                    "distributionUrl=",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return trimmed[
                "distributionUrl=".Length..]
                .Replace("\\:", ":", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return null;
    }

    private static string? TryExtractGradleVersion(
        string? distribution)
    {
        if (string.IsNullOrWhiteSpace(distribution))
        {
            return null;
        }

        var match = Regex.Match(
            distribution,
            @"gradle-(?<version>[^/\\-]+(?:\.[^/\\-]+)*)-(?:bin|all)\.zip",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["version"].Value
            : null;
    }

    private static string? TryExtractMavenVersion(
        string? distribution)
    {
        if (string.IsNullOrWhiteSpace(distribution))
        {
            return null;
        }

        var match = Regex.Match(
            distribution,
            @"apache-maven-(?<version>[^/\\]+?)-bin\.(?:zip|tar\.gz)",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["version"].Value
            : null;
    }
}