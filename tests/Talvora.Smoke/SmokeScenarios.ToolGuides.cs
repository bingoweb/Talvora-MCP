using System.Net.Http;
using System.Text;
using System.Text.Json;

internal static partial class SmokeScenarios
{
    // H1: every guide matches the live schema, its example passes Talvora's own argument validator, its parameter
    // lines reach the wire, and a failed call carries the guide.
    internal static async Task RunToolGuidesLiveAsync(string serverRoot)
    {
        var root = serverRoot.TrimEnd('/');
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        var full = await ReadToolsListAsync(http, root + "/mcp");
        var tools = full.Tools.EnumerateArray().ToDictionary(
            tool => tool.GetProperty("name").GetString()!, tool => tool, StringComparer.Ordinal);
        foreach (var (name, guide) in Talvora.Shared.TalvoraToolGuides.All)
        {
            if (!tools.TryGetValue(name, out var tool))
                throw new InvalidOperationException($"H1: guide for {name}, but /mcp has no such tool.");
            var schema = tool.GetProperty("inputSchema");
            var properties = schema.GetProperty("properties");
            foreach (var parameter in guide.Parameters.Keys)
            {
                if (!properties.TryGetProperty(parameter, out var property))
                    throw new InvalidOperationException($"H1: {name} has no parameter {parameter}.");
                if (!property.TryGetProperty("description", out _))
                    throw new InvalidOperationException($"H1: {name}.{parameter} reached /mcp without a description.");
            }

            using var example = JsonDocument.Parse(guide.Example);
            var arguments = example.RootElement.EnumerateObject().ToDictionary(
                pair => pair.Name, pair => pair.Value.Clone(), StringComparer.Ordinal);
            if (Talvora.ToolArgumentValidator.Validate(schema, arguments) is { } problem)
                throw new InvalidOperationException($"H1: the {name} example is invalid: {problem}");
        }

        var focusedDevelopment = await ReadToolsListAsync(http, root + "/mcp/dev");
        var administration = await ReadToolsListAsync(http, root + "/mcp/admin");
        var unguided = focusedDevelopment.Tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()!)
            .Concat(administration.Tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()!))
            .Distinct(StringComparer.Ordinal)
            .Where(name => !Talvora.Shared.TalvoraToolGuides.All.ContainsKey(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unguided.Length != 0)
            throw new InvalidOperationException("H2: tools without a guide: " + string.Join(", ", unguided));

        using var request = new HttpRequestMessage(HttpMethod.Post, root + "/mcp/dev")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"talvora_read_source","arguments":{}}}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!body.Contains("How to call talvora_read_source", StringComparison.Ordinal))
            throw new InvalidOperationException("H1: a failed call does not carry its tool guide.");

        using var thrown = new HttpRequestMessage(HttpMethod.Post, root + "/mcp/dev")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"talvora_read_source","arguments":{"path":"C:\\Windows\\Temp\\talvora-h3-missing-file.txt"}}}""",
                Encoding.UTF8,
                "application/json"),
        };
        thrown.Headers.Accept.ParseAdd("application/json");
        thrown.Headers.Accept.ParseAdd("text/event-stream");
        using var thrownResponse = await http.SendAsync(thrown);
        var thrownBody = await thrownResponse.Content.ReadAsStringAsync();
        if (thrownBody.Contains("An error occurred invoking", StringComparison.Ordinal) ||
            !thrownBody.Contains("talvora_read_source failed (", StringComparison.Ordinal) ||
            !thrownBody.Contains("How to call talvora_read_source", StringComparison.Ordinal))
            throw new InvalidOperationException("H3: a thrown tool error reaches the model without its reason and guide: " + thrownBody);
        using var exited = new HttpRequestMessage(HttpMethod.Post, root + "/mcp/dev")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"talvora_run_process","arguments":{"executable":"cmd.exe","arguments":["/d","/c","exit 3"],"timeoutSeconds":30}}}""",
                Encoding.UTF8,
                "application/json"),
        };
        exited.Headers.Accept.ParseAdd("application/json");
        exited.Headers.Accept.ParseAdd("text/event-stream");
        using var exitedResponse = await http.SendAsync(exited);
        var exitedBody = await exitedResponse.Content.ReadAsStringAsync();
        if (!exitedBody.Contains("\"isError\":true", StringComparison.Ordinal) ||
            !exitedBody.Contains("\"exitCode\":3", StringComparison.Ordinal) ||
            exitedBody.Contains("How to call talvora_run_process", StringComparison.Ordinal))
            throw new InvalidOperationException("H4: a command's own non-zero exit must be isError with its exit code and without the usage guide: " + exitedBody);
        Console.WriteLine($"H1 tool guides: {Talvora.Shared.TalvoraToolGuides.All.Count} verified");
    }
}
