using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record StitchMcpEntrypoint(
    string Name,
    string? Title,
    string? Description,
    string InputSchemaJson,
    bool? ReadOnly,
    bool? Destructive,
    bool? Idempotent);

public sealed record StitchMcpStatusResponse(
    bool Ready,
    string Version,
    int ToolCount,
    IReadOnlyList<StitchMcpEntrypoint> Entrypoints,
    string? Error);

[McpServerToolType]
public static class StitchMcpTools
{
    private const int MaxArgumentsJsonCharacters = 4 * 1024 * 1024;

    [McpServerTool(
        Name = "talvora_stitch_mcp_status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(StitchMcpStatusResponse)),
     Description("Start the official Google Stitch stdio MCP server under Talvora, discover its live tools, schemas, and annotations, and report the current MCP surface. This is future-proof discovery for Stitch capabilities added after Talvora was built.")]
    public static async Task<StitchMcpStatusResponse> Status(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (node, cli, profile) = ResolveRuntime();
            var transport = CreateTransport(node, cli, profile);
            await using var client = await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken);
            var tools = await client.ListToolsAsync(
                cancellationToken: cancellationToken);

            var version = await ReadVersionAsync(
                node,
                cli,
                profile,
                cancellationToken);

            return new StitchMcpStatusResponse(
                true,
                version,
                tools.Count,
                tools.Select(tool => new StitchMcpEntrypoint(
                        tool.Name,
                        tool.ProtocolTool.Title,
                        tool.Description,
                        tool.ProtocolTool.InputSchema.GetRawText(),
                        tool.ProtocolTool.Annotations?.ReadOnlyHint,
                        tool.ProtocolTool.Annotations?.DestructiveHint,
                        tool.ProtocolTool.Annotations?.IdempotentHint))
                    .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                    .ToArray(),
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is McpException or
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            TimeoutException)
        {
            return new StitchMcpStatusResponse(
                false,
                "unknown",
                0,
                [],
                FileLog.RedactSensitiveData(ex.Message));
        }
    }

    [McpServerTool(
        Name = "talvora_stitch_mcp_call_tool",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true),
     Description("Call any live tool exposed by Google Stitch's own stdio MCP server. Discover the authoritative names and JSON schemas with talvora_stitch_mcp_status first. This preserves access to future Stitch MCP tools without waiting for a Talvora release.")]
    public static async Task<CallToolResult> CallTool(
        [Description("Stitch MCP tool name discovered from talvora_stitch_mcp_status.")] string name,
        [Description("JSON object matching the live Stitch MCP tool input schema. Defaults to {}.")] string argumentsJson = "{}",
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        var arguments = ParseArguments(argumentsJson);
        var (node, cli, profile) = ResolveRuntime();

        var transport = CreateTransport(node, cli, profile);
        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(
            cancellationToken: cancellationToken);
        if (!tools.Any(tool =>
                string.Equals(
                    tool.Name,
                    name.Trim(),
                    StringComparison.Ordinal)))
        {
            throw new McpProtocolException(
                $"Unknown Stitch MCP tool: {name.Trim()}",
                McpErrorCode.InvalidParams);
        }

        return await client.CallToolAsync(
            name.Trim(),
            arguments,
            cancellationToken: cancellationToken);
    }

    private static StdioClientTransport CreateTransport(
        string node,
        string cli,
        string profile)
    {
        var env = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        env["HOME"] = profile;
        env["USERPROFILE"] = profile;
        env["APPDATA"] = Path.Combine(profile, "AppData", "Roaming");
        env["LOCALAPPDATA"] = Path.Combine(profile, "AppData", "Local");
        env["STITCH_CONFIG_DIR"] = Path.Combine(profile, ".stitch");
        env["NO_COLOR"] = "1";
        env["FORCE_COLOR"] = "0";

        return new StdioClientTransport(
            new StdioClientTransportOptions
            {
                Name = "Google Stitch",
                Command = node,
                Arguments = [cli, "mcp", "start"],
                WorkingDirectory = profile,
                InheritEnvironmentVariables = false,
                EnvironmentVariables = env,
                ShutdownTimeout = TimeSpan.FromSeconds(5),
            });
    }

    private static (string Node, string Cli, string Profile) ResolveRuntime()
    {
        var node = CommandResolver.Resolve(
            ["node.exe", "node"],
            [@"C:\Program Files\nodejs\node.exe"])
            ?? throw new FileNotFoundException(
                "Node.js was not found for Google Stitch.");

        var context = WindowsSessionLauncher.GetDefaultInteractiveUser();
        var profile = context.UserProfile
            ?? throw new InvalidOperationException(
                "Logged-on Windows user's profile directory could not be resolved.");

        var configured =
            Environment.GetEnvironmentVariable("TALVORA_STITCH_CLI");
        var cli =
            !string.IsNullOrWhiteSpace(configured)
                ? Path.GetFullPath(configured)
                : Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonApplicationData),
                    "Talvora",
                    "Tools",
                    "Stitch",
                    "node_modules",
                    "@google",
                    "stitch",
                    "dist",
                    "stitch.js");
        if (!File.Exists(cli))
        {
            throw new FileNotFoundException(
                "Google Stitch CLI was not found in Talvora's managed tools directory.",
                cli);
        }

        return (node, cli, profile);
    }

    private static async Task<string> ReadVersionAsync(
        string node,
        string cli,
        string profile,
        CancellationToken cancellationToken)
    {
        var result = await InteractiveUserProcessRunner.RunAsync(
            node,
            profile,
            [cli, "--version"],
            new Dictionary<string, string?>
            {
                ["NO_COLOR"] = "1",
                ["FORCE_COLOR"] = "0",
            },
            30,
            cancellationToken);
        return TextLines.FirstNonEmpty(
                result.StandardOutput,
                result.StandardError)
            ?? "unknown";
    }

    private static void ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new McpProtocolException(
                "name must not be empty.",
                McpErrorCode.InvalidParams);
        }

        if (value.Length > 256)
        {
            throw new McpProtocolException(
                "name is too long.",
                McpErrorCode.InvalidParams);
        }
    }

    private static Dictionary<string, object?> ParseArguments(
        string argumentsJson)
    {
        if (argumentsJson is null)
        {
            throw new McpProtocolException(
                "argumentsJson must not be null.",
                McpErrorCode.InvalidParams);
        }

        if (argumentsJson.Length > MaxArgumentsJsonCharacters)
        {
            throw new McpProtocolException(
                $"argumentsJson exceeds the {MaxArgumentsJsonCharacters} character limit.",
                McpErrorCode.InvalidParams);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    argumentsJson)
                ?? [];
        }
        catch (JsonException ex)
        {
            throw new McpProtocolException(
                $"argumentsJson must be a valid JSON object: {ex.Message}",
                McpErrorCode.InvalidParams);
        }
    }
}
