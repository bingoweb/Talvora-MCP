using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// All filesystem operations below run through MCP on the target Windows machine.
// A uniquely named temporary directory is used; no existing user files are touched.
var endpoint = args.Length > 0 ? args[0] : "http://127.0.0.1:7676/mcp";
var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(endpoint),
    TransportMode = HttpTransportMode.StreamableHttp,
    ConnectionTimeout = TimeSpan.FromSeconds(15)
});

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);

var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
var names = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
string[] required = ["talvora_read_text", "talvora_write_text", "talvora_delete",
    "talvora_list", "talvora_run_process", "talvora_system_info"];
var missing = required.Where(name => !names.Contains(name)).ToArray();
if (missing.Length > 0)
    throw new InvalidOperationException($"Missing MCP tools: {string.Join(", ", missing)}");
Console.WriteLine($"PASS tools/list: {tools.Count} tools discovered.");

async Task<CallToolResult> Call(string name, Dictionary<string, object?> arguments)
{
    var result = await client.CallToolAsync(name, arguments, cancellationToken: timeout.Token);
    if (result.IsError is true)
        throw new InvalidOperationException($"{name} failed: {Text(result)}");
    return result;
}

async Task<ProcessResult> RunPowerShell(string command)
{
    var result = await Call("talvora_run_process", new()
    {
        ["executable"] = "powershell.exe",
        ["arguments"] = new[] { "-NoProfile", "-NonInteractive", "-Command", command },
        ["timeoutSeconds"] = 20
    });
    var process = JsonSerializer.Deserialize<ProcessResult>(Text(result),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("Process tool returned no result.");
    if (process.TimedOut || process.ExitCode != 0)
        throw new InvalidOperationException($"Process failed: exit={process.ExitCode}, timeout={process.TimedOut}, stderr={process.StandardError}");
    return process;
}

var info = await Call("talvora_system_info", new());
if (string.IsNullOrWhiteSpace(Text(info)))
    throw new InvalidOperationException("System information is empty.");
Console.WriteLine("PASS talvora_system_info.");

var token = Guid.NewGuid().ToString("N");
var temp = await RunPowerShell($"[Console]::Write([IO.Path]::Combine([IO.Path]::GetTempPath(), 'talvora-smoke-{token}'))");
var root = temp.StandardOutput.Trim();
if (string.IsNullOrWhiteSpace(root) || !root.EndsWith($"talvora-smoke-{token}", StringComparison.Ordinal))
    throw new InvalidOperationException("Remote process did not return the expected unique temporary path.");
Console.WriteLine("PASS talvora_run_process: Windows PowerShell output captured.");

var directory = root + "\\sub directory";
var path = directory + "\\probe.txt";
var expected = $"Talvora MCP probe {token}\nT\u00fcrk\u00e7e: \u011f\u00fc\u015f\u0131\u00f6\u00e7 \u0130\u011e\u00dc\u015e\u00d6\u00c7\n";
var created = false;
try
{
    await Call("talvora_write_text", new() { ["path"] = path, ["content"] = expected });
    created = true;
    Console.WriteLine("PASS talvora_write_text: nested directory with a space created.");

    var read = await Call("talvora_read_text", new() { ["path"] = path });
    if (!string.Equals(Text(read), expected, StringComparison.Ordinal))
        throw new InvalidOperationException("Read-back differs from the exact UTF-8 text written.");
    Console.WriteLine("PASS talvora_read_text: exact Unicode round-trip.");

    var listing = await Call("talvora_list", new() { ["path"] = directory });
    if (!Text(listing).Contains("probe.txt", StringComparison.Ordinal))
        throw new InvalidOperationException("Directory listing does not contain the written file.");
    Console.WriteLine("PASS talvora_list: written file is present.");

    await Call("talvora_delete", new() { ["path"] = root });
    var escapedRoot = root.Replace("'", "''", StringComparison.Ordinal);
    var deleted = await RunPowerShell($"if (Test-Path -LiteralPath '{escapedRoot}') {{ exit 7 }}; [Console]::Write('deleted')");
    if (deleted.StandardOutput != "deleted")
        throw new InvalidOperationException("Independent deletion verification failed.");
    created = false;
    Console.WriteLine("PASS talvora_delete: entire temporary tree removed and checked independently.");

    var missingRead = await client.CallToolAsync("talvora_read_text",
        new Dictionary<string, object?> { ["path"] = path }, cancellationToken: timeout.Token);
    if (missingRead.IsError is not true)
        throw new InvalidOperationException("Reading the deleted file should return an MCP tool error.");
    Console.WriteLine("PASS error handling: deleted file produces IsError=true.");

    await Call("talvora_system_info", new());
    Console.WriteLine("PASS recovery: Gateway still responds after a tool error.");
}
finally
{
    if (created)
    {
        // Clean up only this test's unique temporary tree, even after a failed assertion.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var result = await client.CallToolAsync("talvora_delete",
                new Dictionary<string, object?> { ["path"] = root }, cancellationToken: cleanup.Token);
            if (result.IsError is true) Console.Error.WriteLine($"Cleanup needed at {root}: {Text(result)}");
        }
        catch (Exception error) { Console.Error.WriteLine($"Cleanup needed at {root}: {error.Message}"); }
    }
}

Console.WriteLine($"Talvora MCP smoke GREEN. Endpoint={endpoint}; tools={tools.Count}; file-lifecycle=PASS; process=PASS");

static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);
