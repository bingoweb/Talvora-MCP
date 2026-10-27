using System.Net.Http;
using System.Text;
using System.Text.Json;

internal static partial class SmokeScenarios
{
    // Q7: focused surfaces omit output schemas (structuredContent is still returned); the full /mcp keeps them.
    // Measured 2026-10-25: dev 146 094 B, admin 51 590 B; budgets = 150 KiB and 55 KiB.
    internal const int DevelopmentToolsListBudgetBytes = 153_600;
    internal const int AdministrationToolsListBudgetBytes = 56_320;

    internal static async Task RunPayloadBudgetLiveAsync(string serverRoot)
    {
        // McpToolMetadataWirePolicy copies every settable Tool property except OutputSchema; a new SDK property
        // must be added to that copy before this list changes.
        string[] copiedToolProperties =
            ["Annotations", "Description", "Icons", "InputSchema", "Meta", "Name", "OutputSchema", "Title"];
        var toolProperties = typeof(ModelContextProtocol.Protocol.Tool).GetProperties()
            .Where(property => property.CanWrite).Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        if (!toolProperties.SequenceEqual(copiedToolProperties))
            throw new InvalidOperationException(
                "Q7: the SDK Tool type changed (" + string.Join(", ", toolProperties) +
                "); update McpToolMetadataWirePolicy.WithoutOutputSchema first.");

        var root = serverRoot.TrimEnd('/');
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        var full = await ReadToolsListAsync(http, root + "/mcp");
        var development = await ReadToolsListAsync(http, root + "/mcp/dev");
        var administration = await ReadToolsListAsync(http, root + "/mcp/admin");

        if (CountOutputSchemas(full.Tools) == 0)
            throw new InvalidOperationException("Q7: the full /mcp surface must keep its output schemas.");
        var requiredNullable = new List<string>();
        foreach (var tool in full.Tools.EnumerateArray())
        {
            if (tool.TryGetProperty("outputSchema", out var schema))
            {
                CollectRequiredNullable(schema, tool.GetProperty("name").GetString() ?? "?", requiredNullable);
            }
        }
        if (requiredNullable.Count != 0)
            throw new InvalidOperationException(
                $"AP1: {requiredNullable.Count} output members can be null but are required, e.g. " +
                string.Join(", ", requiredNullable.Take(5)));
        if (CountOutputSchemas(development.Tools) != 0 || CountOutputSchemas(administration.Tools) != 0)
            throw new InvalidOperationException("Q7: focused surfaces must not send output schemas.");
        if (development.Bytes > DevelopmentToolsListBudgetBytes)
            throw new InvalidOperationException(
                $"Q7: /mcp/dev tools/list is {development.Bytes} bytes, budget {DevelopmentToolsListBudgetBytes}.");
        if (administration.Bytes > AdministrationToolsListBudgetBytes)
            throw new InvalidOperationException(
                $"Q7: /mcp/admin tools/list is {administration.Bytes} bytes, budget {AdministrationToolsListBudgetBytes}.");

        Console.WriteLine(
            $"Q7 tools/list bytes: full={full.Bytes} dev={development.Bytes} admin={administration.Bytes}");
    }

    private static void CollectRequiredNullable(JsonElement schema, string path, List<string> violations)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("properties", out var properties) &&
                properties.ValueKind == JsonValueKind.Object &&
                schema.TryGetProperty("required", out var required) &&
                required.ValueKind == JsonValueKind.Array)
            {
                foreach (var name in required.EnumerateArray())
                {
                    if (name.GetString() is string member &&
                        properties.TryGetProperty(member, out var property) &&
                        property.ValueKind == JsonValueKind.Object &&
                        property.TryGetProperty("type", out var type) &&
                        (type.ValueKind == JsonValueKind.String && type.GetString() == "null" ||
                         type.ValueKind == JsonValueKind.Array &&
                         type.EnumerateArray().Any(item => item.GetString() == "null")))
                    {
                        violations.Add(path + "/" + member);
                    }
                }
            }

            foreach (var child in schema.EnumerateObject())
            {
                CollectRequiredNullable(child.Value, path + "/" + child.Name, violations);
            }
        }
        else if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in schema.EnumerateArray())
            {
                CollectRequiredNullable(item, path, violations);
            }
        }
    }

    private static int CountOutputSchemas(JsonElement tools) =>
        tools.EnumerateArray().Count(tool => tool.TryGetProperty("outputSchema", out _));

    private static async Task<(int Bytes, JsonElement Tools)> ReadToolsListAsync(HttpClient http, string endpoint)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var json = body.TrimStart().StartsWith('{')
            ? body
            : body.Split('\n').First(line => line.StartsWith("data:", StringComparison.Ordinal))[5..];
        using var document = JsonDocument.Parse(json);
        var tools = document.RootElement.GetProperty("result").GetProperty("tools").Clone();
        return (Encoding.UTF8.GetByteCount(json.Trim()), tools);
    }
}
