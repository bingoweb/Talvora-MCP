using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraFlutterInfoResponse(
    bool FlutterFound,
    string? FlutterExecutable,
    string? FlutterVersion,
    bool DartFound,
    string? DartExecutable,
    string? DartVersion,
    string? FlutterRoot);

public sealed record TalvoraAndroidSdkInfoResponse(
    bool Found,
    string? SdkRoot,
    string? AdbExecutable,
    string? AdbVersion,
    string? AndroidCliExecutable,
    string? AndroidCliVersion,
    string? EmulatorExecutable,
    string? EmulatorVersion,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<string> BuildTools,
    IReadOnlyList<string> SystemImages);

[McpServerToolType]
public static partial class MobileTools
{
}
