using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record PenpotMcpEntrypoint(
    string Name,
    string? Title,
    string? Description,
    string InputSchemaJson,
    bool? ReadOnly,
    bool? Destructive,
    bool? Idempotent);

public sealed record PenpotMcpStatusResponse(
    bool Ready,
    string Endpoint,
    int ToolCount,
    IReadOnlyList<PenpotMcpEntrypoint> Entrypoints,
    string? Error);

[McpServerToolType]
public static class PenpotMcpTools
{
    private const int DefaultPort = 4401;
    private const int MaxArgumentsJsonCharacters = 4 * 1024 * 1024;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    [McpServerTool(
        Name = "talvora_penpot_status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PenpotMcpStatusResponse)),
     Description("Probe the local official Penpot MCP server and return its live tools, input schemas, and annotations. The local server normally listens on 127.0.0.1:4401. Use this first for Penpot design work to verify the MCP server and discover the current Penpot tool contract.")]
    public static async Task<PenpotMcpStatusResponse> Status(
        [Description("Local Penpot MCP port. Defaults to 4401.")] int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint(port);

        try
        {
            await using var transport = CreateTransport(endpoint);
            await using var client = await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken);
            var tools = await client.ListToolsAsync(
                cancellationToken: cancellationToken);

            return new PenpotMcpStatusResponse(
                true,
                endpoint.ToString(),
                tools.Count,
                tools.Select(tool => new PenpotMcpEntrypoint(
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
            ex is HttpRequestException or
            TimeoutException or
            McpException or
            InvalidOperationException)
        {
            return new PenpotMcpStatusResponse(
                false,
                endpoint.ToString(),
                0,
                [],
                ex.Message);
        }
    }

    [McpServerTool(
        Name = "talvora_penpot_overview",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true),
     Description("Read the current Penpot file's high-level structure through the official Penpot MCP high_level_overview tool. Use it after talvora_penpot_status to inspect pages, components, styles, tokens, and the active design context before making design changes.")]
    public static Task<CallToolResult> Overview(
        [Description("Local Penpot MCP port. Defaults to 4401.")] int port = DefaultPort,
        CancellationToken cancellationToken = default) =>
        InvokeCoreAsync(
            "high_level_overview",
            new Dictionary<string, object?>(),
            port,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_penpot_read_tool",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true),
     Description("Call a known non-mutating Penpot MCP tool. Discover live names and schemas with talvora_penpot_status first. Penpot 2.18 does not currently publish MCP read-only annotations, so Talvora recognizes the documented overview, API-info, and export tools while leaving unknown or mutating tools on talvora_penpot_call_tool.")]
    public static async Task<CallToolResult> ReadTool(
        [Description("Penpot tool name discovered from talvora_penpot_status.")] string name,
        [Description("JSON object matching the Penpot tool's input schema. Defaults to {}.")] string argumentsJson = "{}",
        [Description("Local Penpot MCP port. Defaults to 4401.")] int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        var arguments = ParseArguments(argumentsJson);
        var endpoint = BuildEndpoint(port);

        await using var transport = CreateTransport(endpoint);
        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);
        var tools = await client.ListToolsAsync(
            cancellationToken: cancellationToken);
        var tool = tools.SingleOrDefault(item =>
            string.Equals(
                item.Name,
                name.Trim(),
                StringComparison.Ordinal));

        if (tool is null)
        {
            throw new McpProtocolException(
                $"Unknown Penpot tool: {name.Trim()}",
                McpErrorCode.InvalidParams);
        }

        if (tool.ProtocolTool.Annotations?.ReadOnlyHint != true &&
            !IsKnownReadOnlyTool(tool.Name))
        {
            throw new McpProtocolException(
                $"Penpot tool '{name.Trim()}' is not known to be read-only. Use talvora_penpot_call_tool for tools that may modify the design.",
                McpErrorCode.InvalidParams);
        }

        return await client.CallToolAsync(
            name.Trim(),
            arguments,
            cancellationToken: cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_penpot_call_tool",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true),
     Description("Call one live Penpot MCP tool with caller-supplied JSON arguments, including tools that can create or modify Penpot design content. Discover the authoritative tool name and schema with talvora_penpot_status first. Returns native Penpot text, structured content, errors, and image blocks unchanged.")]
    public static Task<CallToolResult> CallTool(
        [Description("Penpot tool name discovered from talvora_penpot_status.")] string name,
        [Description("JSON object matching the Penpot tool's input schema. Defaults to {}.")] string argumentsJson = "{}",
        [Description("Local Penpot MCP port. Defaults to 4401.")] int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        var arguments = ParseArguments(argumentsJson);
        return InvokeCoreAsync(
            name.Trim(),
            arguments,
            port,
            cancellationToken);
    }

    private static async Task<CallToolResult> InvokeCoreAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        int port,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(port);
        await using var transport = CreateTransport(endpoint);
        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        return await client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: cancellationToken);
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

    private static bool IsKnownReadOnlyTool(string name) =>
        name is
            "high_level_overview" or
            "penpot_api_info" or
            "export_shape";

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

    private static Uri BuildEndpoint(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new McpProtocolException(
                "port must be between 1 and 65535.",
                McpErrorCode.InvalidParams);
        }

        return new Uri($"http://127.0.0.1:{port}/mcp");
    }

    private static HttpClientTransport CreateTransport(Uri endpoint) =>
        new(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                ConnectionTimeout = ConnectionTimeout,
            });
}
