using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record SboxMcpEntrypoint(
    string Name,
    string? Title,
    string? Description,
    string InputSchemaJson,
    bool? ReadOnly,
    bool? Destructive,
    bool? Idempotent);

public sealed record SboxMcpStatusResponse(
    bool Ready,
    string Endpoint,
    int ToolCount,
    IReadOnlyList<SboxMcpEntrypoint> Entrypoints,
    string? Error);

[McpServerToolType]
public static class SboxMcpTools
{
    private const int DefaultPort = 7269;
    private const int MaxArgumentsJsonCharacters = 4 * 1024 * 1024;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    [McpServerTool(
        Name = "talvora_sbox_status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SboxMcpStatusResponse)),
     Description("Probe the local s&box editor MCP server and return its live top-level entrypoints, input schemas, and annotations. The editor normally listens on 127.0.0.1:7269. Use this before s&box work to verify that the editor MCP server is running and to inspect the current entrypoint contract.")]
    public static async Task<SboxMcpStatusResponse> Status(
        [Description("Local s&box MCP port. Defaults to 7269.")] int port = DefaultPort,
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

            return new SboxMcpStatusResponse(
                true,
                endpoint.ToString(),
                tools.Count,
                tools.Select(tool => new SboxMcpEntrypoint(
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
            return new SboxMcpStatusResponse(
                false,
                endpoint.ToString(),
                0,
                [],
                ex.Message);
        }
    }

    [McpServerTool(
        Name = "talvora_sbox_search_tools",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true),
     Description("Search the live s&box editor MCP registry for scene, asset, project, gameplay, rendering, animation, UI, navigation, screenshot, or custom editor tools. Returns the native s&box search_tools result unchanged so its current parameter schemas remain authoritative.")]
    public static async Task<CallToolResult> SearchTools(
        [Description("Search phrase describing the s&box capability or tool you need.")] string query,
        [Description("Local s&box MCP port. Defaults to 7269.")] int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpProtocolException(
                "query must not be empty.",
                McpErrorCode.InvalidParams);
        }

        if (query.Length > 4096)
        {
            throw new McpProtocolException(
                "query is too long.",
                McpErrorCode.InvalidParams);
        }

        return await InvokeCoreAsync(
            "search_tools",
            new Dictionary<string, object?>
            {
                ["query"] = query,
            },
            port,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_sbox_invoke",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true),
     Description("Invoke one top-level tool on the local s&box editor MCP server with caller-supplied JSON arguments and return its CallToolResult unchanged, including inline image blocks such as viewport screenshots. Use talvora_sbox_status to inspect entrypoint schemas; typically use search_tools, then call_tool or call_tools for discovered editor capabilities.")]
    public static async Task<CallToolResult> Invoke(
        [Description("Top-level s&box MCP entrypoint, for example call_tool, call_tools, list_toolsets, describe_toolset, or search_tools.")] string entrypoint,
        [Description("JSON object passed as the entrypoint arguments. Defaults to {}.")] string argumentsJson = "{}",
        [Description("Local s&box MCP port. Defaults to 7269.")] int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entrypoint))
        {
            throw new McpProtocolException(
                "entrypoint must not be empty.",
                McpErrorCode.InvalidParams);
        }

        if (entrypoint.Length > 256)
        {
            throw new McpProtocolException(
                "entrypoint is too long.",
                McpErrorCode.InvalidParams);
        }

        var arguments = ParseArguments(argumentsJson);
        return await InvokeCoreAsync(
            entrypoint,
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
