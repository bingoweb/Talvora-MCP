using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraWindowsReleaseToolsInfoResponse(
    bool Found,
    string? SdkVersion,
    string? Architecture,
    string? BinDirectory,
    string? SignTool,
    string? ResourceCompiler,
    string? ManifestTool,
    string? MakeAppx,
    string? MakePri);

public static partial class WindowsToolchainTools
{
    [McpServerTool(
        Name = "talvora_windows_release_tools_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWindowsReleaseToolsInfoResponse)),
     Description("Resolve current Windows SDK release/packaging tools from the newest installed SDK version. Optional sdkVersion and architecture select a specific installed SDK/architecture.")]
    public static TalvoraWindowsReleaseToolsInfoResponse
        WindowsReleaseToolsInfo(
            string? sdkVersion = null,
            string architecture = "x64")
    {
        var resolved = ResolveSdkToolDirectory(
            sdkVersion,
            architecture,
            throwIfMissing: false);

        if (resolved is null)
        {
            return new TalvoraWindowsReleaseToolsInfoResponse(
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        var (version, arch, directory) = resolved.Value;

        return new TalvoraWindowsReleaseToolsInfoResponse(
            true,
            version,
            arch,
            directory,
            ExistingTool(directory, "signtool.exe"),
            ExistingTool(directory, "rc.exe"),
            ExistingTool(directory, "mt.exe"),
            ExistingTool(directory, "makeappx.exe"),
            ExistingTool(directory, "makepri.exe"));
    }

    [McpServerTool(
        Name = "talvora_signtool_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run SignTool from the newest installed Windows SDK, or an explicitly selected SDK version/architecture, with an arbitrary argument vector. No certificate/provider/timestamp/digest/file/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> SignToolRun(
        string workingDirectory,
        string[] arguments,
        string? sdkVersion = null,
        string architecture = "x64",
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default) =>
        RunWindowsSdkTool(
            "signtool.exe",
            workingDirectory,
            arguments,
            sdkVersion,
            architecture,
            environment,
            timeoutSeconds,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_rc_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the Windows SDK Resource Compiler rc.exe with an arbitrary argument vector. No include/resource/define/output/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> ResourceCompilerRun(
        string workingDirectory,
        string[] arguments,
        string? sdkVersion = null,
        string architecture = "x64",
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default) =>
        RunWindowsSdkTool(
            "rc.exe",
            workingDirectory,
            arguments,
            sdkVersion,
            architecture,
            environment,
            timeoutSeconds,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_mt_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the Windows SDK Manifest Tool mt.exe with an arbitrary argument vector. No manifest/resource/assembly/catalog/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> ManifestToolRun(
        string workingDirectory,
        string[] arguments,
        string? sdkVersion = null,
        string architecture = "x64",
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default) =>
        RunWindowsSdkTool(
            "mt.exe",
            workingDirectory,
            arguments,
            sdkVersion,
            architecture,
            environment,
            timeoutSeconds,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_makeappx_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run MakeAppx from the Windows SDK with arbitrary pack/unpack/bundle/encrypt/decrypt arguments. No package/layout/output/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> MakeAppxRun(
        string workingDirectory,
        string[] arguments,
        string? sdkVersion = null,
        string architecture = "x64",
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default) =>
        RunWindowsSdkTool(
            "makeappx.exe",
            workingDirectory,
            arguments,
            sdkVersion,
            architecture,
            environment,
            timeoutSeconds,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_makepri_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run MakePri from the Windows SDK with an arbitrary argument vector for PRI resource indexing and packaging. No project/resource/output/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> MakePriRun(
        string workingDirectory,
        string[] arguments,
        string? sdkVersion = null,
        string architecture = "x64",
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default) =>
        RunWindowsSdkTool(
            "makepri.exe",
            workingDirectory,
            arguments,
            sdkVersion,
            architecture,
            environment,
            timeoutSeconds,
            cancellationToken);

    private static Task<TalvoraCliCommandResponse> RunWindowsSdkTool(
        string fileName,
        string workingDirectory,
        string[] arguments,
        string? sdkVersion,
        string architecture,
        Dictionary<string, string?>? environment,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var resolved = ResolveSdkToolDirectory(
            sdkVersion,
            architecture,
            throwIfMissing: true)!.Value;

        var executable = Path.Combine(
            resolved.Directory,
            fileName);

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                $"Windows SDK tool was not found in SDK {resolved.Version}/{resolved.Architecture}: {fileName}",
                executable);
        }

        return RunCliAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static (
        string Version,
        string Architecture,
        string Directory)? ResolveSdkToolDirectory(
            string? sdkVersion,
            string architecture,
            bool throwIfMissing)
    {
        var normalizedArchitecture =
            NormalizeSdkArchitecture(architecture);

        var sdks = DiscoverWindowsSdks();

        var sdk = string.IsNullOrWhiteSpace(sdkVersion)
            ? sdks.FirstOrDefault()
            : sdks.FirstOrDefault(item =>
                string.Equals(
                    item.Version,
                    sdkVersion.Trim(),
                    StringComparison.OrdinalIgnoreCase));

        if (sdk is null)
        {
            if (throwIfMissing)
            {
                throw new DirectoryNotFoundException(
                    string.IsNullOrWhiteSpace(sdkVersion)
                        ? "No installed Windows SDK was found."
                        : $"Windows SDK version is not installed: {sdkVersion}");
            }

            return null;
        }

        var directory = Path.Combine(
            sdk.BinPath,
            normalizedArchitecture);

        if (!Directory.Exists(directory))
        {
            if (throwIfMissing)
            {
                throw new DirectoryNotFoundException(
                    $"Windows SDK architecture directory was not found: {directory}");
            }

            return null;
        }

        return (
            sdk.Version,
            normalizedArchitecture,
            directory);
    }

    private static string NormalizeSdkArchitecture(
        string architecture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);

        return architecture.Trim().ToLowerInvariant() switch
        {
            "x64" or "amd64" => "x64",
            "x86" or "win32" => "x86",
            "arm64" or "aarch64" => "arm64",
            _ => throw new ArgumentException(
                $"Unsupported Windows SDK architecture: {architecture}",
                nameof(architecture)),
        };
    }

    private static string? ExistingTool(
        string directory,
        string fileName)
    {
        var path = Path.Combine(directory, fileName);
        return File.Exists(path)
            ? path
            : null;
    }
}
