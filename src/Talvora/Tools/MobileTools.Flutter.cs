using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public static partial class MobileTools
{
    [McpServerTool(
        Name = "talvora_flutter_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraFlutterInfoResponse)),
     Description("Resolve Flutter and Dart and report their executable paths and versions. Dart bundled with Flutter is preferred when available; missing components are reported structurally.")]
    public static async Task<TalvoraFlutterInfoResponse> FlutterInfo(
        CancellationToken cancellationToken = default)
    {
        var flutter = ResolveFlutter();
        var dart = ResolveDart(flutter);

        string? flutterVersion = null;
        string? dartVersion = null;

        if (flutter is not null)
        {
            var result = await RunCliCheckedAsync(
                flutter,
                Environment.CurrentDirectory,
                ["--version"],
                120,
                cancellationToken);
            flutterVersion = FirstNonEmptyLine(
                result.StandardOutput,
                result.StandardError);
        }

        if (dart is not null)
        {
            var result = await RunCliCheckedAsync(
                dart,
                Environment.CurrentDirectory,
                ["--version"],
                60,
                cancellationToken);
            dartVersion = FirstNonEmptyLine(
                result.StandardOutput,
                result.StandardError);
        }

        return new TalvoraFlutterInfoResponse(
            flutter is not null,
            flutter,
            flutterVersion,
            dart is not null,
            dart,
            dartVersion,
            ResolveFlutterRoot(flutter));
    }

    [McpServerTool(
        Name = "talvora_flutter_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the Flutter CLI with an arbitrary argument vector, working directory, environment overrides, and timeout. No command/device/platform/build-mode/package/option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> FlutterRun(
        string workingDirectory,
        string[] arguments,
        string? flutterExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 3600,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = ResolveRequiredExecutable(
            flutterExecutable,
            "Flutter",
            ["flutter.bat", "flutter.cmd", "flutter"],
            [@"C:\tools\flutter\bin\flutter.bat"]);

        return RunCliAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_dart_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run the Dart CLI with an arbitrary argument vector, working directory, environment overrides, and timeout. No command/package/script/VM-option allowlist is applied.")]
    public static Task<TalvoraCliCommandResponse> DartRun(
        string workingDirectory,
        string[] arguments,
        string? dartExecutable = null,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = ResolveRequiredExecutable(
            dartExecutable,
            "Dart",
            ["dart.bat", "dart.cmd", "dart"],
            [@"C:\tools\flutter\bin\dart.bat"]);

        return RunCliAsync(
            executable,
            workingDirectory,
            arguments,
            environment,
            timeoutSeconds,
            cancellationToken);
    }

    private static string? ResolveFlutter() =>
        CommandResolver.Resolve(
            ["flutter.bat", "flutter.cmd", "flutter"],
            [@"C:\tools\flutter\bin\flutter.bat"]);

    private static string? ResolveDart(string? flutter)
    {
        var candidates = new List<string>();

        var flutterRoot = ResolveFlutterRoot(flutter);
        if (!string.IsNullOrWhiteSpace(flutterRoot))
        {
            candidates.Add(Path.Combine(flutterRoot, "bin", "dart.bat"));
            candidates.Add(Path.Combine(
                flutterRoot,
                "bin",
                "cache",
                "dart-sdk",
                "bin",
                "dart.exe"));
        }

        candidates.Add(@"C:\tools\flutter\bin\dart.bat");

        return CommandResolver.Resolve(
            ["dart.exe", "dart.bat", "dart.cmd", "dart"],
            candidates);
    }

    private static string? ResolveFlutterRoot(string? flutter)
    {
        if (string.IsNullOrWhiteSpace(flutter))
        {
            return null;
        }

        var bin = Path.GetDirectoryName(flutter);
        if (string.IsNullOrWhiteSpace(bin))
        {
            return null;
        }

        return Directory.GetParent(bin)?.FullName;
    }
}
