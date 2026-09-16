using ModelContextProtocol.Client;

var endpoint = args.Length > 0 ? args[0] : "http://127.0.0.1:7676/mcp";

var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(endpoint),
    TransportMode = HttpTransportMode.StreamableHttp,
    ConnectionTimeout = TimeSpan.FromSeconds(15)
});

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);

var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

string[] requiredTools =
[
    "talvora_read_text",
    "talvora_write_text",
    "talvora_delete",
    "talvora_list",
    "talvora_run_process",
    "talvora_system_info"
];

var missing = requiredTools.Where(name => !toolNames.Contains(name)).ToArray();
if (missing.Length > 0)
{
    throw new InvalidOperationException($"Talvora MCP is missing required tools: {string.Join(", ", missing)}");
}

var systemInfo = await client.CallToolAsync(
    "talvora_system_info",
    new Dictionary<string, object?>(),
    cancellationToken: timeout.Token);

if (systemInfo.Content.Count == 0)
{
    throw new InvalidOperationException("talvora_system_info returned no content.");
}

Console.WriteLine($"Talvora MCP smoke GREEN. Endpoint={endpoint}; tools={tools.Count}");
