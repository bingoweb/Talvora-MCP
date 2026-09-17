using ModelContextProtocol.Client;

var endpoint = args.Length > 0 ? args[0] : "http://127.0.0.1:7676/mcp";
var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(endpoint),
    TransportMode = HttpTransportMode.StreamableHttp,
    ConnectionTimeout = TimeSpan.FromSeconds(20),
});

await using var client = await McpClient.CreateAsync(transport);
var tools = await client.ListToolsAsync();
var byName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

string[] required =
[
    "talvora_system_info",
    "talvora_read_text",
    "talvora_write_text",
    "talvora_delete",
    "talvora_list",
    "talvora_run_process",
];

foreach (var name in required)
{
    if (!byName.ContainsKey(name)) throw new InvalidOperationException($"Missing MCP tool: {name}");
}

static async Task EnsureSuccess(McpClientTool tool, Dictionary<string, object?> arguments)
{
    var result = await tool.CallAsync(arguments);
    if (result.IsError is true)
    {
        throw new InvalidOperationException($"Tool failed: {tool.Name}");
    }
}

await EnsureSuccess(byName["talvora_system_info"], []);

var root = Path.Combine(Path.GetTempPath(), "Talvora-Smoke-" + Guid.NewGuid().ToString("N"));
var file = Path.Combine(root, "hello.txt");
try
{
    await EnsureSuccess(byName["talvora_write_text"], new()
    {
        ["path"] = file,
        ["content"] = "talvora-smoke",
    });

    await EnsureSuccess(byName["talvora_read_text"], new() { ["path"] = file });
    await EnsureSuccess(byName["talvora_list"], new() { ["path"] = root });
    await EnsureSuccess(byName["talvora_run_process"], new()
    {
        ["executable"] = "cmd.exe",
        ["arguments"] = new[] { "/d", "/c", "echo", "talvora-smoke" },
        ["timeoutSeconds"] = 30,
    });
    await EnsureSuccess(byName["talvora_delete"], new() { ["path"] = file });
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

Console.WriteLine("TALVORA MCP SMOKE GREEN");
Console.WriteLine($"endpoint={endpoint}");
Console.WriteLine($"tools={string.Join(',', required)}");
