using ModelContextProtocol.Client;
using Talvora.Shared;
using ModelContextProtocol.Protocol;
using static SmokeSupport;

if (args.Length >= 1 &&
    string.Equals(
        args[0],
        "--playwright-mcp",
        StringComparison.Ordinal))
{
    var playwrightEndpoint =
        args.Length > 1
            ? args[1]
            : "http://127.0.0.1:8932/mcp";
    var smokeUrl =
        args.Length > 2
            ? args[2]
            : "https://example.com/";

    await using var playwrightTransport = new HttpClientTransport(
        new HttpClientTransportOptions
        {
            Endpoint = new Uri(playwrightEndpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(20),
        });

    await using var playwrightClient =
        await McpClient.CreateAsync(playwrightTransport);

    var playwrightTools = await playwrightClient.ListToolsAsync();
    var playwrightToolNames = playwrightTools
        .Select(tool => tool.Name)
        .ToHashSet(StringComparer.Ordinal);

    var requiredPlaywrightTools = new[]
    {
        "browser_tabs",
        "browser_navigate",
        "browser_snapshot",
        "browser_run_code_unsafe",
        "browser_file_upload",
        "browser_take_screenshot",
        "browser_pdf_save",
        "browser_network_requests",
        "browser_start_tracing",
        "browser_stop_tracing",
    };

    foreach (var requiredTool in requiredPlaywrightTools)
    {
        if (!playwrightToolNames.Contains(requiredTool))
        {
            throw new InvalidOperationException(
                $"Missing Playwright MCP capability tool: {requiredTool}");
        }
    }

    var smokeUrlJson = System.Text.Json.JsonSerializer.Serialize(smokeUrl);
    var code =
        "async (page) => { " +
        "const smokePage = await page.context().newPage(); " +
        "try { " +
        $"await smokePage.goto({smokeUrlJson}); " +
        "const snapshot = await smokePage.locator('body').ariaSnapshot(); " +
        "return { url: smokePage.url(), snapshot }; " +
        "} finally { await smokePage.close(); } " +
        "}";

    var isolatedSmoke = await playwrightClient.CallToolAsync(
        "browser_run_code_unsafe",
        new Dictionary<string, object?>
        {
            ["code"] = code,
        });

    if (isolatedSmoke.IsError is true)
    {
        throw new InvalidOperationException(
            "Playwright isolated browser smoke returned an MCP error.");
    }
    Console.WriteLine("PLAYWRIGHT MCP SMOKE GREEN");
    Console.WriteLine($"endpoint={playwrightEndpoint}");
    Console.WriteLine($"toolCount={playwrightTools.Count}");
    Console.WriteLine(
        $"requiredTools={string.Join(',', requiredPlaywrightTools)}");
    return;
}

if (args.Length >= 3 &&
    string.Equals(
        args[0],
        "--dev-server-fixture",
        StringComparison.Ordinal))
{
    if (!int.TryParse(
            args[1],
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var fixturePort))
    {
        throw new ArgumentException("Invalid dev-server fixture port.");
    }

    await RunDevServerFixtureAsync(
        fixturePort,
        args[2]);
    return;
}

if (args.Length == 1 &&
    string.Equals(
        args[0],
        "--shared-infrastructure-only",
        StringComparison.Ordinal))
{
    await SmokeScenarios.RunSharedInfrastructureAsync();
    Console.WriteLine("TALVORA SHARED INFRASTRUCTURE GREEN");
    return;
}

var devServerOnly =
    args.Length > 0 &&
    string.Equals(
        args[0],
        "--dev-server-only",
        StringComparison.Ordinal);

var endpoint = devServerOnly
    ? args.Length > 1
        ? args[1]
        : "http://127.0.0.1:7676/mcp"
    : args.Length > 0
        ? args[0]
        : "http://127.0.0.1:7676/mcp";

var repositoryPath = devServerOnly
    ? args.Length > 2
        ? Path.GetFullPath(args[2])
        : Directory.GetCurrentDirectory()
    : args.Length > 1
        ? Path.GetFullPath(args[1])
        : Directory.GetCurrentDirectory();
var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(endpoint),
    TransportMode = HttpTransportMode.StreamableHttp,
    ConnectionTimeout = TimeSpan.FromSeconds(20),
});

await using var client = await McpClient.CreateAsync(transport);
var tools = await client.ListToolsAsync();
var byName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

var required = TalvoraToolManifest.Names;

foreach (var name in required)
{
    if (!byName.ContainsKey(name)) throw new InvalidOperationException($"Missing MCP tool: {name}");
}

if (devServerOnly)
{
    await SmokeScenarios.RunDevServerAsync(
        byName,
        Guid.NewGuid().ToString("N"),
        repositoryPath);
    Console.WriteLine("TALVORA DEV SERVER SMOKE GREEN");
    return;
}

await SmokeScenarios.RunSharedInfrastructureAsync();

await SmokeScenarios.RunServiceAsync(byName);

var smokeId = Guid.NewGuid().ToString("N");
await SmokeScenarios.RunEnvironmentAsync(byName, smokeId);

await SmokeScenarios.RunWorkspaceAsync(byName, smokeId, repositoryPath);

await SmokeScenarios.RunRegistryAsync(byName, smokeId);

await SmokeScenarios.RunSqliteAsync(byName, smokeId);

await SmokeScenarios.RunStructuredConfigAsync(byName, smokeId);

await SmokeScenarios.RunDevServerAsync(byName, smokeId, repositoryPath);

await SmokeScenarios.RunApplicationDevelopmentAsync(
    byName,
    smokeId,
    repositoryPath);

Console.WriteLine("TALVORA MCP SMOKE GREEN");
Console.WriteLine($"endpoint={endpoint}");
Console.WriteLine($"tools={string.Join(',', required)}");
