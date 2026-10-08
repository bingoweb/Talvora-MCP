using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

/// <summary>Read-only DirectShow camera media-type inspection through FFmpeg.</summary>
[McpServerToolType]
public static class CameraModeTools
{
    [McpServerTool(Name = "talvora_camera_modes", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(TalvoraProcessResult)),
     Description("Query supported DirectShow video resolutions, pixel formats and FPS on Windows without recording or changing camera settings. Requires ffmpeg on PATH. Supply the Windows camera name (e.g. USB 2.0 Camera).")]
    public static Task<TalvoraProcessResult> ListModes(
        string cameraName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cameraName);
        if (cameraName.Length > 200 || cameraName.Contains('\0') ||
            cameraName.Contains('\r') || cameraName.Contains('\n'))
        {
            throw new ArgumentException("Invalid camera name.", nameof(cameraName));
        }

        return ProcessTools.RunProcess(
            "C:\\ProgramData\\chocolatey\\bin\\ffmpeg.exe",
            ["-hide_banner", "-f", "dshow", "-list_options", "true",
                "-i", $"video={cameraName}"],
            timeoutSeconds: 20, maxCapturedCharactersPerStream: 64 * 1024,
            cancellationToken: cancellationToken);
    }
}
