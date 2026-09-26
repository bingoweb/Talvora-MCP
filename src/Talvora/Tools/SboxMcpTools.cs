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
        Name = "talvora_sbox_editor_status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true),
     Description("Read the live s&box editor status, including engine version, open project and scene, play mode, compile state, registered tool count, and project/log paths. Use this at the start of an s&box task and after code hotload. Returns the native editor_status result unchanged.")]
    public static Task<CallToolResult> EditorStatus(
        [Description("Local s&box MCP port. Defaults to 7269.")] int port = DefaultPort,
        CancellationToken cancellationToken = default) =>
        InvokeCoreAsync(
            "editor_status",
            new Dictionary<string, object?>(),
            port,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_sbox_list_toolsets",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true),
     Description("List the live s&box MCP toolsets and the tool names in each group. Use this to browse project-dependent capabilities, then use talvora_sbox_describe_toolset for the authoritative schemas.")]
    public static Task<CallToolResult> ListToolsets(
        [Description("Local s&box MCP port. Defaults to 7269.")] int port = DefaultPort,
        CancellationToken cancellationToken = default) =>
        InvokeCoreAsync(
            "list_toolsets",
            new Dictionary<string, object?>(),
            port,
            cancellationToken);

    [McpServerTool(
        Name = "talvora_sbox_describe_toolset",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true),
     Description("Describe one live s&box MCP toolset with every tool and its current input schema. Toolset names come from talvora_sbox_list_toolsets. Use the returned schemas instead of guessing parameters.")]
    public static Task<CallToolResult> DescribeToolset(
        [Description("Toolset name exactly as returned by talvora_sbox_list_toolsets.")] string name,
        [Description("Local s&box MCP port. Defaults to 7269.")] int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name, "name");
        return InvokeCoreAsync(
            "describe_toolset",
            new Dictionary<string, object?>
            {
                ["name"] = name.Trim(),
            },
            port,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_sbox_search_tools",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true),
     Description("Search the live s&box editor MCP registry for scene, asset, project, gameplay, rendering, animation, UI, navigation, screenshot, or custom editor tools. Returns the native s&box search_tools result unchanged so its current parameter schemas remain authoritative.")]
    public static async Task<CallToolResult> SearchTools(
        [Description("Space-separated search terms. Empty lists every currently registered s&box tool.")] string query = "",
        [Description("Local s&box MCP port. Defaults to 7269.")] int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
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
        Name = "talvora_sbox_call_tool",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true),
     Description("Call one live s&box editor tool discovered by talvora_sbox_search_tools or talvora_sbox_describe_toolset. The nested arguments JSON must match the discovered schema. Returns native text, structured content, errors, and inline PNG image blocks unchanged.")]
    public static Task<CallToolResult> CallTool(
        [Description("s&box tool name discovered from the live registry.")] string name,
        [Description("JSON object matching that s&box tool's input schema. Defaults to {}.")] string argumentsJson = "{}",
        [Description("Local s&box MCP port. Defaults to 7269.")] int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name, "name");
        var arguments = ParseArguments(argumentsJson);
        return InvokeCoreAsync(
            "call_tool",
            new Dictionary<string, object?>
            {
                ["name"] = name.Trim(),
                ["arguments"] = arguments,
            },
            port,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_sbox_call_tools",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = true),
     Description("Call several live s&box editor tools in order in one round trip. Pass a JSON array of {name, arguments} objects using schemas from live discovery. s&box stops the batch after the first failed call and reports how many later calls were skipped.")]
    public static Task<CallToolResult> CallTools(
        [Description("JSON array of s&box calls, for example [{\"name\":\"find_objects\",\"arguments\":{\"name\":\"crate\"}}].")] string callsJson,
        [Description("Local s&box MCP port. Defaults to 7269.")] int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        var calls = ParseCalls(callsJson);
        return InvokeCoreAsync(
            "call_tools",
            new Dictionary<string, object?>
            {
                ["calls"] = calls,
            },
            port,
            cancellationToken);
    }

    [McpServerTool(
        Name = "talvora_sbox_read_console",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true),
     Description("Read recent s&box editor/game console output using the native read_console entrypoint. Use the returned cursor as since on the next call so build, hotload, runtime, warning, and error output is not reread.")]
    public static Task<CallToolResult> ReadConsole(
        [Description("Maximum recent matching entries, 1..500.")] int limit = 50,
        [Description("Minimum s&box log level, for example Trace, Info, Warning, or Error.")] string minimumLevel = "Trace",
        [Description("Optional case-insensitive message/logger substring filter.")] string filter = "",
        [Description("Cursor from the previous read_console result; 0 reads the most recent buffered output.")] long since = 0,
        [Description("Local s&box MCP port. Defaults to 7269.")] int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new McpProtocolException(
                "limit must be between 1 and 500.",
                McpErrorCode.InvalidParams);
        }

        if (minimumLevel.Length > 64)
        {
            throw new McpProtocolException(
                "minimumLevel is too long.",
                McpErrorCode.InvalidParams);
        }

        if (filter.Length > 4096)
        {
            throw new McpProtocolException(
                "filter is too long.",
                McpErrorCode.InvalidParams);
        }

        if (since < 0)
        {
            throw new McpProtocolException(
                "since must be zero or greater.",
                McpErrorCode.InvalidParams);
        }

        return InvokeCoreAsync(
            "read_console",
            new Dictionary<string, object?>
            {
                ["limit"] = limit,
                ["minimumLevel"] = minimumLevel,
                ["filter"] = filter,
                ["since"] = since,
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

    private static void ValidateName(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new McpProtocolException(
                $"{parameterName} must not be empty.",
                McpErrorCode.InvalidParams);
        }

        if (value.Length > 256)
        {
            throw new McpProtocolException(
                $"{parameterName} is too long.",
                McpErrorCode.InvalidParams);
        }
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

    private static JsonElement ParseCalls(
        string callsJson)
    {
        if (callsJson is null)
        {
            throw new McpProtocolException(
                "callsJson must not be null.",
                McpErrorCode.InvalidParams);
        }

        if (callsJson.Length > MaxArgumentsJsonCharacters)
        {
            throw new McpProtocolException(
                $"callsJson exceeds the {MaxArgumentsJsonCharacters} character limit.",
                McpErrorCode.InvalidParams);
        }

        try
        {
            using var document =
                JsonDocument.Parse(callsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new McpProtocolException(
                    "callsJson must be a JSON array.",
                    McpErrorCode.InvalidParams);
            }

            var count = root.GetArrayLength();
            if (count is < 1 or > 256)
            {
                throw new McpProtocolException(
                    "callsJson must contain between 1 and 256 calls.",
                    McpErrorCode.InvalidParams);
            }

            var index = 0;
            foreach (var call in root.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object ||
                    !call.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(name.GetString()))
                {
                    throw new McpProtocolException(
                        $"callsJson[{index}] must contain a non-empty string name.",
                        McpErrorCode.InvalidParams);
                }

                if (call.TryGetProperty("arguments", out var arguments) &&
                    arguments.ValueKind != JsonValueKind.Object &&
                    arguments.ValueKind != JsonValueKind.Null)
                {
                    throw new McpProtocolException(
                        $"callsJson[{index}].arguments must be a JSON object or null.",
                        McpErrorCode.InvalidParams);
                }

                index++;
            }

            return root.Clone();
        }
        catch (JsonException ex)
        {
            throw new McpProtocolException(
                $"callsJson must be valid JSON: {ex.Message}",
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
