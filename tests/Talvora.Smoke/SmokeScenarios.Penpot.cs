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

    private static readonly string[] RequiredAuthenticatedPenpotTools =
    [
        "execute_code",
        "high_level_overview",
        "penpot_api_info",
        "export_shape",
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
        await VerifyTalvoraPenpotAiPluginAsync(talvoraRoot);

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

        var configurationSource =
            statusJson.GetProperty("configurationSource").GetString();
        var authenticated =
            statusJson.GetProperty("authenticated").GetBoolean();
        var isManagedAuthenticated =
            string.Equals(
                configurationSource,
                "managed-authenticated-endpoint",
                StringComparison.Ordinal);
        var requiredStatusTools =
            isManagedAuthenticated
                ? RequiredAuthenticatedPenpotTools
                : RequiredPenpotNativeTools;

        if (isManagedAuthenticated && !authenticated)
        {
            throw new InvalidOperationException(
                "Talvora Penpot managed endpoint is not authenticated.");
        }

        foreach (var required in requiredStatusTools)
        {
            if (!entrypoints.Contains(required))
            {
                throw new InvalidOperationException(
                    $"Talvora Penpot status did not report required tool: {required}");
            }
        }

        if (!isManagedAuthenticated &&
            statusJson.GetProperty("toolCount").GetInt32() !=
                nativeTools.Count)
        {
            throw new InvalidOperationException(
                "Talvora Penpot status tool count does not match the native MCP server.");
        }

        var describedEndpoint =
            statusJson.GetProperty("endpoint").GetString() ?? string.Empty;
        if (isManagedAuthenticated &&
            !describedEndpoint.Contains(
                "credentials=<redacted>",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Talvora Penpot managed endpoint did not redact its credentials.");
        }
    }

    private static async Task VerifyTalvoraPenpotAiPluginAsync(
        string talvoraRoot)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        var root = talvoraRoot.TrimEnd('/');

        using var healthResponse =
            await httpClient.GetAsync(
                root + "/penpot-ai/healthz");
        if (!healthResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Talvora Penpot AI health failed: {(int)healthResponse.StatusCode}");
        }

        using var healthJson =
            System.Text.Json.JsonDocument.Parse(
                await healthResponse.Content.ReadAsStringAsync());
        if (!healthJson.RootElement.GetProperty("ready").GetBoolean())
        {
            throw new InvalidOperationException(
                "Talvora Penpot AI health reported ready=false.");
        }

        using var manifestResponse =
            await httpClient.GetAsync(
                root + "/penpot-ai/manifest.json");
        manifestResponse.EnsureSuccessStatusCode();
        using var manifestJson =
            System.Text.Json.JsonDocument.Parse(
                await manifestResponse.Content.ReadAsStringAsync());
        if (manifestJson.RootElement.GetProperty("name").GetString() != "Talvora AI" ||
            manifestJson.RootElement.GetProperty("version").GetInt32() != 2)
        {
            throw new InvalidOperationException(
                "Talvora Penpot AI manifest identity is invalid.");
        }

        foreach (var asset in new[] { "plugin.js", "index.html", "icon.svg" })
        {
            using var response =
                await httpClient.GetAsync(
                    root + "/penpot-ai/" + asset);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Talvora Penpot AI asset failed: {asset} -> {(int)response.StatusCode}");
            }

            if (asset == "plugin.js")
            {
                var pluginScript =
                    await response.Content.ReadAsStringAsync();
                foreach (var marker in new[]
                         {
                             "normalizeIntentText",
                             "INTENT_RULES",
                             "detectPromptCommands",
                             "duzelt",
                             "responsive",
                             "koda cevir",
                         })
                {
                    if (!pluginScript.Contains(marker, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Talvora Penpot AI natural-language intent marker is missing: {marker}");
                    }
                }

                if (pluginScript.Contains(
                        "Komutu anladim ama otomasyon eslesmedi",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Talvora Penpot AI still exposes the obsolete unmatched-automation fallback.");
                }
            }
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
