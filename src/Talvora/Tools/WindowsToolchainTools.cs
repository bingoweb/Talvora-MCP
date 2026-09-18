using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraVisualStudioInstance(
    string InstanceId,
    string InstallationName,
    string InstallationPath,
    string InstallationVersion,
    string ProductId,
    bool IsComplete,
    bool IsLaunchable,
    string? DisplayName,
    string? Description);

public sealed record TalvoraWindowsSdkEntry(
    string Version,
    string BinPath,
    bool HasX64,
    bool HasX86,
    bool HasArm64,
    bool HasRc,
    bool HasMt,
    bool HasSigntool);

public sealed record TalvoraWindowsToolchainInfoResponse(
    string? VsWhereExecutable,
    IReadOnlyList<TalvoraVisualStudioInstance> VisualStudioInstances,
    string? MsBuildExecutable,
    IReadOnlyList<TalvoraWindowsSdkEntry> WindowsSdks,
    string? CMakeExecutable,
    string? CMakeVersion,
    string? NinjaExecutable,
    string? NinjaVersion,
    TalvoraToolInfoResponse Msbuild,
    TalvoraWindowsSdkInfoResponse WindowsSdk,
    TalvoraToolInfoResponse CMake,
    TalvoraToolInfoResponse Ninja);

public sealed record TalvoraVsDevEnvironmentResponse(
    string InstallationPath,
    string Architecture,
    string HostArchitecture,
    string DeveloperCommandScript,
    IReadOnlyDictionary<string, string> Environment);

public sealed record TalvoraVisualStudioInstancesResponse(
    int Count,
    IReadOnlyList<TalvoraVisualStudioInstance> Instances);

public sealed record TalvoraVsDevEnvironmentCompatResponse(
    string InstallationPath,
    string Architecture,
    string HostArchitecture,
    string ScriptPath,
    int Count,
    IReadOnlyDictionary<string, string> Environment);

public sealed record TalvoraToolInfoResponse(
    bool Found,
    string? Executable,
    string? Version,
    string? Invocation);

public sealed record TalvoraWindowsSdkInfoResponse(
    bool Found,
    string? KitsRoot10,
    string? LatestVersion,
    IReadOnlyList<TalvoraWindowsSdkEntry> Sdks);

public sealed record TalvoraPeInfoResponse(
    string Path,
    bool IsPe,
    long Length,
    string? Machine,
    bool IsDll,
    bool IsExe,
    bool IsManaged,
    string? Subsystem,
    int SectionCount);

public sealed record TalvoraFileVersionInfoResponse(
    string Path,
    string? FileVersion,
    string? ProductVersion,
    string? ProductName,
    string? CompanyName,
    string? FileDescription,
    string? OriginalFilename,
    string? InternalName,
    string? LegalCopyright,
    bool IsDebug,
    bool IsPatched,
    bool IsPreRelease,
    bool IsPrivateBuild,
    bool IsSpecialBuild);

[McpServerToolType]
public static class WindowsToolchainTools
{
    [McpServerTool(
        Name = "talvora_visual_studio_instances",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraVisualStudioInstancesResponse)),
     Description("Return Visual Studio / Build Tools instances discovered by vswhere. Missing vswhere returns count=0 and an empty instance list.")]
    public static async Task<TalvoraVisualStudioInstancesResponse> VisualStudioInstancesCompat(
        CancellationToken cancellationToken = default)
    {
        var instances = await DiscoverVisualStudioInstancesAsync(cancellationToken);
        return new TalvoraVisualStudioInstancesResponse(instances.Count, instances);
    }

    [McpServerTool(
        Name = "talvora_vs_dev_environment",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraVsDevEnvironmentCompatResponse)),
     Description("Capture the Visual Studio developer command-prompt environment. This compatibility surface returns scriptPath/count plus the complete environment dictionary.")]
    public static async Task<TalvoraVsDevEnvironmentCompatResponse> VsDevEnvironmentCompat(
        string? installationPath = null,
        string architecture = "x64",
        string hostArchitecture = "x64",
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        var result = await VsDevEnvironment(
            installationPath,
            architecture,
            hostArchitecture,
            timeoutSeconds,
            cancellationToken);

        return new TalvoraVsDevEnvironmentCompatResponse(
            result.InstallationPath,
            result.Architecture,
            result.HostArchitecture,
            result.DeveloperCommandScript,
            result.Environment.Count,
            result.Environment);
    }

    [McpServerTool(
        Name = "talvora_msbuild_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraToolInfoResponse)),
     Description("Resolve MSBuild. Prefers Visual Studio MSBuild.exe when installed and otherwise falls back to dotnet msbuild, which keeps MSBuild available on machines with only the .NET SDK.")]
    public static async Task<TalvoraToolInfoResponse> MsBuildInfo(
        CancellationToken cancellationToken = default)
    {
        var instances = await DiscoverVisualStudioInstancesAsync(cancellationToken);
        var msbuild = ResolveMsBuild(instances);

        if (!string.IsNullOrWhiteSpace(msbuild) && File.Exists(msbuild))
        {
            var nativeVersion = await ReadFirstLineAsync(
                msbuild,
                ["-version", "-nologo"],
                cancellationToken);

            return new TalvoraToolInfoResponse(
                true,
                msbuild,
                nativeVersion,
                "MSBuild.exe");
        }

        var dotnet = ResolveCommand(
            ["dotnet.exe", "dotnet"],
            [@"C:\Program Files\dotnet\dotnet.exe"]);

        if (dotnet is null)
        {
            return new TalvoraToolInfoResponse(false, null, null, null);
        }

        var result = await RunCliAsync(
            dotnet,
            Environment.CurrentDirectory,
            ["msbuild", "-version", "-nologo"],
            null,
            60,
            cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            return new TalvoraToolInfoResponse(false, dotnet, null, "dotnet msbuild");
        }

        var fallbackVersion = SplitLines(result.StandardOutput)
            .LastOrDefault(line => Version.TryParse(line.Trim(), out _))
            ?? SplitLines(result.StandardOutput).LastOrDefault();

        return new TalvoraToolInfoResponse(
            true,
            dotnet,
            fallbackVersion?.Trim(),
            "dotnet msbuild");
    }

    [McpServerTool(
        Name = "talvora_windows_sdk_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWindowsSdkInfoResponse)),
     Description("Return Windows SDK root/version discovery. Missing SDKs are reported structurally instead of throwing.")]
    public static TalvoraWindowsSdkInfoResponse WindowsSdkInfo()
    {
        var sdks = DiscoverWindowsSdks();
        var kitsRoot = ResolveKitsRoot10();

        return new TalvoraWindowsSdkInfoResponse(
            sdks.Count > 0 || !string.IsNullOrWhiteSpace(kitsRoot),
            kitsRoot,
            sdks.FirstOrDefault()?.Version,
            sdks);
    }

    [McpServerTool(
        Name = "talvora_cmake_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraToolInfoResponse)),
     Description("Resolve CMake from PATH or common install locations and report version. Missing CMake is reported structurally.")]
    public static async Task<TalvoraToolInfoResponse> CMakeInfo(
        CancellationToken cancellationToken = default)
    {
        var executable = ResolveCommand(
            ["cmake.exe", "cmake"],
            [
                @"C:\Program Files\CMake\bin\cmake.exe",
                @"C:\Program Files (x86)\CMake\bin\cmake.exe",
            ]);

        return await ReadToolInfoAsync(executable, ["--version"], "cmake", cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_ninja_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraToolInfoResponse)),
     Description("Resolve Ninja from PATH or common Chocolatey locations and report version. Missing Ninja is reported structurally.")]
    public static async Task<TalvoraToolInfoResponse> NinjaInfo(
        CancellationToken cancellationToken = default)
    {
        var executable = ResolveCommand(
            ["ninja.exe", "ninja"],
            [@"C:\ProgramData\chocolatey\bin\ninja.exe"]);

        return await ReadToolInfoAsync(executable, ["--version"], "ninja", cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_pe_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraPeInfoResponse)),
     Description("Inspect any accessible Windows Portable Executable (PE) file and return machine, subsystem, managed/native, DLL/EXE, size, and section information. Non-PE files return isPe=false instead of throwing.")]
    public static TalvoraPeInfoResponse PeInfo(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("PE source file was not found.", fullPath);
        }

        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);

            if (!pe.HasMetadata && pe.PEHeaders.PEHeader is null)
            {
                return new TalvoraPeInfoResponse(
                    fullPath, false, info.Length, null, false, false, false, null, 0);
            }

            var coff = pe.PEHeaders.CoffHeader;
            var header = pe.PEHeaders.PEHeader;
            var characteristics = coff.Characteristics;
            var isDll = (characteristics & System.Reflection.PortableExecutable.Characteristics.Dll) != 0;
            var isExe = !isDll;
            var isManaged = pe.HasMetadata;

            return new TalvoraPeInfoResponse(
                fullPath,
                header is not null,
                info.Length,
                coff.Machine.ToString(),
                isDll,
                isExe,
                isManaged,
                header?.Subsystem.ToString(),
                pe.PEHeaders.SectionHeaders.Length);
        }
        catch (BadImageFormatException)
        {
            return new TalvoraPeInfoResponse(
                fullPath, false, info.Length, null, false, false, false, null, 0);
        }
    }

    [McpServerTool(
        Name = "talvora_file_version_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraFileVersionInfoResponse)),
     Description("Return Windows file-version metadata for any accessible file. Fields may be null when the file has no version resource.")]
    public static TalvoraFileVersionInfoResponse FileVersionInfoTool(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("File was not found.", fullPath);
        }

        var info = FileVersionInfo.GetVersionInfo(fullPath);
        return new TalvoraFileVersionInfoResponse(
            fullPath,
            info.FileVersion,
            info.ProductVersion,
            info.ProductName,
            info.CompanyName,
            info.FileDescription,
            info.OriginalFilename,
            info.InternalName,
            info.LegalCopyright,
            info.IsDebug,
            info.IsPatched,
            info.IsPreRelease,
            info.IsPrivateBuild,
            info.IsSpecialBuild);
    }

    [McpServerTool(
        Name = "talvora_windows_toolchain_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWindowsToolchainInfoResponse)),
     Description("Discover Visual Studio/vswhere, MSBuild, installed Windows SDKs, CMake, and Ninja on the machine. Missing components are reported structurally instead of throwing.")]
    public static async Task<TalvoraWindowsToolchainInfoResponse> Info(
        CancellationToken cancellationToken = default)
    {
        var vsWhere = ResolveVsWhere();
        var instances = await DiscoverVisualStudioInstancesAsync(cancellationToken);

        var msBuild = ResolveMsBuild(instances);
        var sdks = DiscoverWindowsSdks();

        var cmake = ResolveCommand(
            ["cmake.exe", "cmake"],
            [
                @"C:\Program Files\CMake\bin\cmake.exe",
                @"C:\Program Files (x86)\CMake\bin\cmake.exe",
            ]);
        var ninja = ResolveCommand(
            ["ninja.exe", "ninja"],
            [
                @"C:\ProgramData\chocolatey\bin\ninja.exe",
            ]);

        var cmakeVersion = cmake is null
            ? null
            : await ReadFirstLineAsync(cmake, ["--version"], cancellationToken);
        var ninjaVersion = ninja is null
            ? null
            : await ReadFirstLineAsync(ninja, ["--version"], cancellationToken);

        var msbuildInfo = await MsBuildInfo(cancellationToken);
        var sdkInfo = WindowsSdkInfo();
        var cmakeInfo = await CMakeInfo(cancellationToken);
        var ninjaInfo = await NinjaInfo(cancellationToken);

        return new TalvoraWindowsToolchainInfoResponse(
            vsWhere,
            instances,
            msBuild,
            sdks,
            cmake,
            cmakeVersion,
            ninja,
            ninjaVersion,
            msbuildInfo,
            sdkInfo,
            cmakeInfo,
            ninjaInfo);
    }

    [McpServerTool(
        Name = "talvora_vs_instances",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraVisualStudioInstancesResponse)),
     Description("Alias of talvora_visual_studio_instances. Returns count plus all Visual Studio / Build Tools instances discovered through vswhere.")]
    public static async Task<TalvoraVisualStudioInstancesResponse> VisualStudioInstances(
        CancellationToken cancellationToken = default)
    {
        var instances = await DiscoverVisualStudioInstancesAsync(cancellationToken);
        return new TalvoraVisualStudioInstancesResponse(instances.Count, instances);
    }

    [McpServerTool(
        Name = "talvora_windows_sdk_list",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWindowsSdkInfoResponse)),
     Description("Alias of talvora_windows_sdk_info. Returns found/root/latest-version plus all discovered Windows SDK entries.")]
    public static TalvoraWindowsSdkInfoResponse WindowsSdkList() =>
        WindowsSdkInfo();

    [McpServerTool(
        Name = "talvora_vsdev_environment",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraVsDevEnvironmentResponse)),
     Description("Capture the environment produced by Visual Studio VsDevCmd.bat for a selected architecture/host architecture. This exposes the complete developer command prompt environment without restricting tool paths or variables.")]
    public static async Task<TalvoraVsDevEnvironmentResponse> VsDevEnvironment(
        string? installationPath = null,
        string architecture = "x64",
        string hostArchitecture = "x64",
        int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var instances = await DiscoverVisualStudioInstancesAsync(cancellationToken);
        var selectedPath = string.IsNullOrWhiteSpace(installationPath)
            ? instances.FirstOrDefault()?.InstallationPath
            : Path.GetFullPath(installationPath);

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            throw new FileNotFoundException("No Visual Studio or Build Tools installation was found.");
        }

        var script = Path.Combine(
            selectedPath,
            "Common7",
            "Tools",
            "VsDevCmd.bat");

        if (!File.Exists(script))
        {
            throw new FileNotFoundException("VsDevCmd.bat was not found.", script);
        }

        var cmd = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");

        var startInfo = new ProcessStartInfo
        {
            FileName = cmd,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(
            $"call \"{script}\" -arch={architecture} -host_arch={hostArchitecture} >nul && set");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to launch Visual Studio developer command environment.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutSeconds > 0)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("VsDevCmd.bat environment capture timed out.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"VsDevCmd.bat failed with exit code {process.ExitCode}: {stderr}");
        }

        var environment = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(stdout))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            environment[line[..separator]] = line[(separator + 1)..];
        }

        return new TalvoraVsDevEnvironmentResponse(
            selectedPath,
            architecture,
            hostArchitecture,
            script,
            environment);
    }

    [McpServerTool(
        Name = "talvora_msbuild_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run MSBuild.exe with an arbitrary argument vector, working directory, and environment overrides. No target/property/project/option allowlist or denylist is applied.")]
    public static async Task<TalvoraCliCommandResponse> MsBuildRun(
        string workingDirectory,
        string[] arguments,
        string? msBuildExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = string.IsNullOrWhiteSpace(msBuildExecutable)
            ? ResolveMsBuild(await DiscoverVisualStudioInstancesAsync(cancellationToken))
            : Path.GetFullPath(msBuildExecutable);

        if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
        {
            return await RunCliAsync(
                executable,
                workingDirectory,
                arguments,
                environment,
                timeoutSeconds,
                cancellationToken);
        }

        var dotnet = ResolveCommand(
            ["dotnet.exe", "dotnet"],
            [@"C:\Program Files\dotnet\dotnet.exe"])
            ?? throw new FileNotFoundException("Neither MSBuild.exe nor dotnet msbuild was found.");

        var fallbackArguments = new List<string> { "msbuild" };
        fallbackArguments.AddRange(arguments);

        return await RunCliAsync(
            dotnet,
            workingDirectory,
            fallbackArguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_cmake_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run CMake with an arbitrary argument vector, working directory, and environment overrides. No generator/preset/source/build-directory/option allowlist or denylist is applied.")]
    public static Task<TalvoraCliCommandResponse> CMakeRun(
        string workingDirectory,
        string[] arguments,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = ResolveCommand(
            ["cmake.exe", "cmake"],
            [
                @"C:\Program Files\CMake\bin\cmake.exe",
                @"C:\Program Files (x86)\CMake\bin\cmake.exe",
            ]) ?? throw new FileNotFoundException("CMake was not found.");

        return RunCliAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_ninja_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run Ninja with an arbitrary argument vector, working directory, and environment overrides. No target/build-directory/option allowlist or denylist is applied.")]
    public static Task<TalvoraCliCommandResponse> NinjaRun(
        string workingDirectory,
        string[] arguments,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = ResolveCommand(
            ["ninja.exe", "ninja"],
            [
                @"C:\ProgramData\chocolatey\bin\ninja.exe",
            ]) ?? throw new FileNotFoundException("Ninja was not found.");

        return RunCliAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static async Task<IReadOnlyList<TalvoraVisualStudioInstance>> DiscoverVisualStudioInstancesAsync(
        CancellationToken cancellationToken)
    {
        var vsWhere = ResolveVsWhere();
        return vsWhere is null
            ? []
            : await ReadVisualStudioInstancesAsync(vsWhere, cancellationToken);
    }

    private static string? ResolveKitsRoot10()
    {
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10",
            @"C:\Program Files\Windows Kits\10",
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<TalvoraToolInfoResponse> ReadToolInfoAsync(
        string? executable,
        IReadOnlyList<string> arguments,
        string invocation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new TalvoraToolInfoResponse(false, null, null, null);
        }

        var result = await RunCliAsync(
            executable,
            Environment.CurrentDirectory,
            arguments,
            null,
            30,
            cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            return new TalvoraToolInfoResponse(false, executable, null, invocation);
        }

        var firstLine = SplitLines(result.StandardOutput).FirstOrDefault()?.Trim();
        var version = firstLine;
        if (!string.IsNullOrWhiteSpace(firstLine))
        {
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            version = parts.LastOrDefault() ?? firstLine;
        }

        return new TalvoraToolInfoResponse(true, executable, version, invocation);
    }

    private static string? ResolveVsWhere()
    {
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe",
            @"C:\Program Files\Microsoft Visual Studio\Installer\vswhere.exe",
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<IReadOnlyList<TalvoraVisualStudioInstance>> ReadVisualStudioInstancesAsync(
        string vsWhere,
        CancellationToken cancellationToken)
    {
        var result = await RunCliAsync(
            vsWhere,
            Environment.CurrentDirectory,
            [
                "-all",
                "-products",
                "*",
                "-format",
                "json",
                "-utf8",
            ],
            null,
            60,
            cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException(
                $"vswhere failed with exit code {result.ExitCode}: {result.StandardError}");
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        var instances = new List<TalvoraVisualStudioInstance>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            instances.Add(new TalvoraVisualStudioInstance(
                GetString(item, "instanceId") ?? string.Empty,
                GetString(item, "installationName") ?? string.Empty,
                GetString(item, "installationPath") ?? string.Empty,
                GetString(item, "installationVersion") ?? string.Empty,
                GetString(item, "productId") ?? string.Empty,
                GetBoolean(item, "isComplete"),
                GetBoolean(item, "isLaunchable"),
                GetNestedString(item, "catalog", "productDisplayVersion")
                    ?? GetNestedString(item, "properties", "displayName"),
                GetNestedString(item, "properties", "description")));
        }

        return instances;
    }

    private static string? ResolveMsBuild(
        IReadOnlyList<TalvoraVisualStudioInstance> instances)
    {
        foreach (var instance in instances
                     .OrderByDescending(item => item.InstallationVersion, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = new[]
            {
                Path.Combine(instance.InstallationPath, "MSBuild", "Current", "Bin", "MSBuild.exe"),
                Path.Combine(instance.InstallationPath, "MSBuild", "15.0", "Bin", "MSBuild.exe"),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var knownRoots = new[]
        {
            @"C:\Program Files\Microsoft Visual Studio",
            @"C:\Program Files (x86)\Microsoft Visual Studio",
        };

        foreach (var root in knownRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var match = Directory.EnumerateFiles(
                        root,
                        "MSBuild.exe",
                        SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (match is not null)
                {
                    return match;
                }
            }
            catch
            {
            }
        }

        return ResolveCommand(["MSBuild.exe", "msbuild"], []);
    }

    private static IReadOnlyList<TalvoraWindowsSdkEntry> DiscoverWindowsSdks()
    {
        var roots = new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10\bin",
            @"C:\Program Files\Windows Kits\10\bin",
        };

        var result = new List<TalvoraWindowsSdkEntry>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root)
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);
                if (!Version.TryParse(name, out _))
                {
                    continue;
                }

                result.Add(new TalvoraWindowsSdkEntry(
                    name,
                    directory,
                    Directory.Exists(Path.Combine(directory, "x64")),
                    Directory.Exists(Path.Combine(directory, "x86")),
                    Directory.Exists(Path.Combine(directory, "arm64")),
                    File.Exists(Path.Combine(directory, "x64", "rc.exe")),
                    File.Exists(Path.Combine(directory, "x64", "mt.exe")),
                    File.Exists(Path.Combine(directory, "x64", "signtool.exe"))));
            }
        }

        return result
            .GroupBy(item => item.Version, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => Version.Parse(item.Version))
            .ToArray();
    }

    private static async Task<string?> ReadFirstLineAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunCliAsync(
            executable,
            Environment.CurrentDirectory,
            arguments,
            null,
            30,
            cancellationToken);

        if (result.ExitCode != 0 || result.TimedOut)
        {
            return null;
        }

        return SplitLines(result.StandardOutput).FirstOrDefault();
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

        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        if (!Directory.Exists(cwd))
        {
            throw new DirectoryNotFoundException($"Working directory was not found: {cwd}");
        }

        var requestedArguments = arguments.ToArray();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in requestedArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment ?? new Dictionary<string, string?>())
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start: {executable}");
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
            cwd,
            requestedArguments,
            stopwatch.ElapsedMilliseconds);
    }

    private static string? ResolveCommand(
        IEnumerable<string> commandNames,
        IEnumerable<string> explicitCandidates)
    {
        var candidates = new List<string>(explicitCandidates);

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

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static string? GetNestedString(
        JsonElement element,
        string objectProperty,
        string valueProperty)
    {
        if (!element.TryGetProperty(objectProperty, out var nested) ||
            nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(nested, valueProperty);
    }
}