using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class MobileTools
{
    [McpServerTool(
        Name = "talvora_android_sdk_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraAndroidSdkInfoResponse)),
     Description("Discover the modern Android SDK root and canonical platform-tools, cmdline-tools/latest Android CLI, emulator, installed platforms, build-tools, and system images. Deprecated SDK Tools/sdkmanager paths are intentionally not used.")]
    public static async Task<TalvoraAndroidSdkInfoResponse> AndroidSdkInfo(
        CancellationToken cancellationToken = default)
    {
        var sdkRoot = ResolveAndroidSdkRoot();
        var adb = ResolveAdb(sdkRoot);
        var androidCli = ResolveAndroidCli(sdkRoot);
        var emulator = ResolveEmulator(sdkRoot);

        string? adbVersion = null;
        string? emulatorVersion = null;

        if (adb is not null)
        {
            var result = await RunCliCheckedAsync(
                adb,
                sdkRoot,
                ["version"],
                60,
                cancellationToken);
            adbVersion = FirstNonEmptyLine(
                result.StandardOutput,
                result.StandardError);
        }

        if (emulator is not null)
        {
            var result = await RunCliCheckedAsync(
                emulator,
                sdkRoot,
                ["-version"],
                60,
                cancellationToken);
            emulatorVersion = FirstNonEmptyLine(
                result.StandardOutput,
                result.StandardError);
        }

        return new TalvoraAndroidSdkInfoResponse(
            sdkRoot is not null,
            sdkRoot,
            adb,
            adbVersion,
            androidCli,
            ReadFileProductVersion(androidCli),
            emulator,
            emulatorVersion,
            ListSdkDirectories(sdkRoot, "platforms"),
            ListSdkDirectories(sdkRoot, "build-tools"),
            ListSdkDirectories(sdkRoot, "system-images"));
    }

    [McpServerTool(
        Name = "talvora_adb_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the canonical Android SDK platform-tools adb executable with an arbitrary argument vector. No device/serial/transport/shell/package/file/port/command allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> AdbRun(
        string[] arguments,
        string? workingDirectory = null,
        string? adbExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var sdkRoot = ResolveAndroidSdkRoot();
        var executable = adbExecutable is null
            ? ResolveAdb(sdkRoot)
            : ResolveRequiredExecutable(
                adbExecutable,
                "adb",
                [adbExecutable]);

        if (executable is null)
        {
            throw new FileNotFoundException(
                "Modern Android SDK platform-tools adb executable was not found.");
        }

        return RunCliAsync(
            executable,
            workingDirectory ?? sdkRoot,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_android_cli_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the modern Android CLI from cmdline-tools/latest with arbitrary arguments, including android sdk operations. Deprecated sdkmanager compatibility is not exposed.")]
    public static Task<TalvoraCliCommandResponse> AndroidCliRun(
        string[] arguments,
        string? workingDirectory = null,
        string? androidExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 3600,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var sdkRoot = ResolveAndroidSdkRoot();
        var executable = androidExecutable is null
            ? ResolveAndroidCli(sdkRoot)
            : ResolveRequiredExecutable(
                androidExecutable,
                "Android CLI",
                [androidExecutable]);

        if (executable is null)
        {
            throw new FileNotFoundException(
                "Modern Android CLI was not found under cmdline-tools/latest.");
        }

        return RunCliAsync(
            executable,
            workingDirectory ?? sdkRoot,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_emulator_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the canonical Android SDK emulator executable with arbitrary arguments. No AVD/device/snapshot/network/graphics/feature/port/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> EmulatorRun(
        string[] arguments,
        string? workingDirectory = null,
        string? emulatorExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 3600,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var sdkRoot = ResolveAndroidSdkRoot();
        var executable = emulatorExecutable is null
            ? ResolveEmulator(sdkRoot)
            : ResolveRequiredExecutable(
                emulatorExecutable,
                "Android emulator",
                [emulatorExecutable]);

        if (executable is null)
        {
            throw new FileNotFoundException(
                "Modern Android SDK emulator executable was not found.");
        }

        return RunCliAsync(
            executable,
            workingDirectory ?? sdkRoot,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static string? ResolveAndroidSdkRoot()
    {
        foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var machineValue = OperatingSystem.IsWindows()
                ? Environment.GetEnvironmentVariable(
                    variable,
                    EnvironmentVariableTarget.Machine)
                : null;

            if (!string.IsNullOrWhiteSpace(machineValue) &&
                Directory.Exists(machineValue))
            {
                return Path.GetFullPath(machineValue);
            }

            var processValue = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(processValue) &&
                Directory.Exists(processValue))
            {
                return Path.GetFullPath(processValue);
            }
        }

        var candidates = new List<string>
        {
            @"C:\Android\android-sdk",
        };

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "Android", "Sdk"));
        }

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(Directory.Exists);
    }

    private static string? ResolveAdb(string? sdkRoot) =>
        ResolveCanonicalSdkExecutable(
            sdkRoot,
            "platform-tools",
            "adb.exe");

    private static string? ResolveAndroidCli(string? sdkRoot) =>
        ResolveCanonicalSdkExecutable(
            sdkRoot,
            "cmdline-tools",
            "latest",
            "bin",
            "android.exe");

    private static string? ResolveEmulator(string? sdkRoot) =>
        ResolveCanonicalSdkExecutable(
            sdkRoot,
            "emulator",
            "emulator.exe");

    private static string? ResolveCanonicalSdkExecutable(
        string? sdkRoot,
        params string[] relativeParts)
    {
        if (string.IsNullOrWhiteSpace(sdkRoot))
        {
            return null;
        }

        var parts = new string[relativeParts.Length + 1];
        parts[0] = sdkRoot;
        Array.Copy(
            relativeParts,
            0,
            parts,
            1,
            relativeParts.Length);

        var candidate = Path.Combine(parts);
        return File.Exists(candidate)
            ? Path.GetFullPath(candidate)
            : null;
    }

    private static string? ReadFileProductVersion(
        string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable) ||
            !File.Exists(executable))
        {
            return null;
        }

        var info = FileVersionInfo.GetVersionInfo(executable);
        return string.IsNullOrWhiteSpace(info.ProductVersion)
            ? info.FileVersion
            : info.ProductVersion;
    }

    private static IReadOnlyList<string> ListSdkDirectories(
        string? sdkRoot,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(sdkRoot))
        {
            return [];
        }

        var root = Path.Combine(sdkRoot, relativePath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
    }
}
