using ModelContextProtocol.Client;
using Talvora.Shared;
using static SmokeSupport;

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
var semanticSourceEditOnly =
    args.Length > 0 &&
    string.Equals(
        args[0],
        "--semantic-source-edit-only",
        StringComparison.Ordinal);

var endpoint = semanticSourceEditOnly
    ? args.Length > 1
        ? args[1]
        : "http://127.0.0.1:7676/mcp"
    : devServerOnly
    ? args.Length > 1
        ? args[1]
        : "http://127.0.0.1:7676/mcp"
    : args.Length > 0
        ? args[0]
        : "http://127.0.0.1:7676/mcp";

var repositoryPath = semanticSourceEditOnly
    ? args.Length > 2
        ? Path.GetFullPath(args[2])
        : Directory.GetCurrentDirectory()
    : devServerOnly
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

if (semanticSourceEditOnly)
{
    await SmokeScenarios.RunSemanticSourceEditAsync(
        tools,
        byName,
        repositoryPath);
    Console.WriteLine("TALVORA SEMANTIC SOURCE EDIT SMOKE GREEN");
    return;
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
