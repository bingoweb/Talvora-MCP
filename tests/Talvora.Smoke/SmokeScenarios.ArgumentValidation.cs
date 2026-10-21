using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Talvora;

internal static partial class SmokeScenarios
{
    internal static void RunArgumentValidationUnit()
    {
        using var schemaDocument = JsonDocument.Parse(
            """{"type":"object","properties":{"root":{"type":"string"},"query":{"type":"string"},"regex":{"type":"boolean"}},"required":["root","query"]}""");
        var schema = schemaDocument.RootElement;
        var invalidNames = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"path":"x","pattern":"y"}""")!;
        const string expected =
            "InvalidArguments: missing 'root', 'query'; unknown 'path', 'pattern'. Expected: root (string, required), query (string, required), regex (boolean)";
        var actual = ToolArgumentValidator.Validate(schema, invalidNames);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Argument validator returned unexpected missing/unknown result: {actual}");
        }

        var wrongType = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"root":"x","query":"y","regex":"yes"}""")!;
        var wrongTypeMessage = ToolArgumentValidator.Validate(schema, wrongType);
        if (wrongTypeMessage is null ||
            !wrongTypeMessage.Contains(
                "wrong type 'regex' (expected boolean)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Argument validator missed wrong JSON type.");
        }

        var valid = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"root":"x","query":"y","regex":true}""")!;
        if (ToolArgumentValidator.Validate(schema, valid) is not null)
        {
            throw new InvalidOperationException("Argument validator rejected valid arguments.");
        }
    }

    internal static async Task RunArgumentValidationLiveAsync(string serverRoot)
    {
        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(serverRoot.TrimEnd('/') + "/mcp/dev"),
                TransportMode = HttpTransportMode.StreamableHttp,
                ConnectionTimeout = TimeSpan.FromSeconds(20),
            }));
        var result = await client.CallToolAsync(
            "talvora_search_text",
            new Dictionary<string, object?>
            {
                ["path"] = "x",
                ["pattern"] = "y",
            });
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        if (result.IsError is not true ||
            text is null ||
            !text.StartsWith(
                "InvalidArguments: missing 'root', 'query'", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Live argument-validation refusal was missing. IsError={result.IsError}; Text={text}");
        }

        Console.WriteLine("Q6_LIVE_INVALID_ARGUMENTS=" + text);
    }
}
