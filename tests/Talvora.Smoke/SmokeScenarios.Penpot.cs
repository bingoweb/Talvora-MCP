using ModelContextProtocol.Client;
using static SmokeSupport;

internal static partial class SmokeScenarios
{
    private static readonly string[] RequiredPenpotNativeTools =
    [
        "execute_code",
        "high_level_overview",
        "penpot_api_info",
        "export_shape",
        "import_image",
    ];

    private static readonly string[] RequiredTalvoraPenpotTools =
    [
        "talvora_penpot_status",
        "talvora_penpot_overview",
        "talvora_penpot_read_tool",
        "talvora_penpot_call_tool",
    ];

    internal static async Task RunPenpotIntegrationAsync(
        string talvoraRoot,
        string penpotEndpoint)
    {
        await using var penpotClient =
            await CreatePenpotSmokeClientAsync(
                penpotEndpoint);
        var nativeTools =
            await penpotClient.ListToolsAsync();
        var nativeToolNames =
            nativeTools.Select(tool => tool.Name)
                .ToHashSet(StringComparer.Ordinal);

        foreach (var required in RequiredPenpotNativeTools)
        {
            if (!nativeToolNames.Contains(required))
            {
                throw new InvalidOperationException(
                    $"Penpot MCP is missing required tool: {required}");
            }
        }

        var talvoraEndpoint =
            talvoraRoot.TrimEnd('/') + "/mcp/dev";
        await using var talvoraClient =
            await CreatePenpotSmokeClientAsync(
                talvoraEndpoint);
        var talvoraTools =
            await talvoraClient.ListToolsAsync();
        var byName =
            talvoraTools.ToDictionary(
                tool => tool.Name,
                StringComparer.Ordinal);

        foreach (var required in RequiredTalvoraPenpotTools)
        {
            if (!byName.ContainsKey(required))
            {
                throw new InvalidOperationException(
                    $"Talvora Dev is missing Penpot proxy tool: {required}");
            }
        }

        var status =
            await EnsureSuccess(
                byName["talvora_penpot_status"],
                new Dictionary<string, object?>
                {
                    ["port"] =
                        new Uri(penpotEndpoint).Port,
                });

        if (status.StructuredContent is not { } statusJson)
        {
            throw new InvalidOperationException(
                "Talvora Penpot status returned no structured content.");
        }

        if (!statusJson.GetProperty("ready").GetBoolean())
        {
            var error =
                statusJson.TryGetProperty("error", out var errorJson) &&
                errorJson.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? errorJson.GetString()
                    : null;
            throw new InvalidOperationException(
                $"Talvora Penpot status is not ready: {error}");
        }

        var entrypoints =
            statusJson.GetProperty("entrypoints")
                .EnumerateArray()
                .Select(entry =>
                    entry.GetProperty("name").GetString())
                .Where(name =>
                    !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

        foreach (var required in RequiredPenpotNativeTools)
        {
            if (!entrypoints.Contains(required))
            {
                throw new InvalidOperationException(
                    $"Talvora Penpot status did not report native tool: {required}");
            }
        }

        if (statusJson.GetProperty("toolCount").GetInt32() !=
            nativeTools.Count)
        {
            throw new InvalidOperationException(
                "Talvora Penpot status tool count does not match the native MCP server.");
        }
    }

    private static async Task<McpClient> CreatePenpotSmokeClientAsync(
        string endpoint)
    {
        var transport =
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint =
                        new Uri(endpoint),
                    TransportMode =
                        HttpTransportMode.StreamableHttp,
                    ConnectionTimeout =
                        TimeSpan.FromSeconds(20),
                });

        return await McpClient.CreateAsync(
            transport);
    }
}
