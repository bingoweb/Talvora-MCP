using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

[McpServerToolType]
public static class ToolchainTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [McpServerTool(
        Name = "talvora_toolchain_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(Dictionary<string, JsonElement>)),
     Description("Inspect pnpm, yarn, bun, cmake or ninja using the existing discovery/version probe and preserve its output fields. Other dedicated probes retain their structured or selectable capabilities.")]
    public static async Task<Dictionary<string, JsonElement>> Info(
        string tool,
        CancellationToken cancellationToken = default)
    {
        object result = tool switch
        {
            "pnpm" => await BuildRunnerTools.PnpmInfo(cancellationToken),
            "yarn" => await BuildRunnerTools.YarnInfo(cancellationToken),
            "bun" => await BuildRunnerTools.BunInfo(cancellationToken),
            "cmake" => await WindowsToolchainTools.CMakeInfo(cancellationToken),
            "ninja" => await WindowsToolchainTools.NinjaInfo(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
        };
        return JsonSerializer.SerializeToElement(result, JsonOptions)
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    [McpServerTool(
        Name = "talvora_toolchain_run",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCliCommandResponse)),
     Description("Run cmake or ninja with arbitrary argv, working directory, environment overrides and timeout. Preserve the existing process/output fields. Dedicated runners keep executable, SDK, architecture, launcher or interactive-user selection.")]
    public static Task<TalvoraCliCommandResponse> Run(
        string tool,
        string[] arguments,
        string workingDirectory,
        Dictionary<string, string?>? environment = null,
        int timeoutSeconds = 1800,
        CancellationToken cancellationToken = default) =>
        tool switch
        {
            "cmake" => WindowsToolchainTools.CMakeRun(
                workingDirectory, arguments, environment, timeoutSeconds, cancellationToken),
            "ninja" => WindowsToolchainTools.NinjaRun(
                workingDirectory, arguments, environment, timeoutSeconds, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
        };
}
