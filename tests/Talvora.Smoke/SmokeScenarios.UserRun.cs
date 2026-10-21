using ModelContextProtocol.Client;

internal static partial class SmokeScenarios
{
    internal static async Task RunUserRunLiveAsync(
        string serverRoot, string repositoryPath)
    {
        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(serverRoot.TrimEnd('/') + "/mcp/dev"),
                TransportMode = HttpTransportMode.StreamableHttp,
                ConnectionTimeout = TimeSpan.FromSeconds(20),
            }));

        var result = await client.CallToolAsync(
            "talvora_user_run",
            new Dictionary<string, object?>
            {
                ["executable"] = "whoami.exe",
                ["arguments"] = Array.Empty<string>(),
                ["workingDirectory"] = Path.GetFullPath(repositoryPath),
                ["timeoutSeconds"] = 30,
            });
        if (result.IsError is true ||
            result.StructuredContent is not { } structured ||
            !structured.GetProperty("success").GetBoolean() ||
            structured.GetProperty("exitCode").GetInt32() != 0 ||
            structured.GetProperty("timedOut").GetBoolean())
        {
            throw new InvalidOperationException(
                "Signed-in user process execution failed.");
        }

        var user = structured.GetProperty("user").GetString() ?? string.Empty;
        var actual = structured.GetProperty("standardOutput").GetString()?.Trim()
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(user) ||
            user.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(user, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"User-run must return the signed-in user. Identity={user}; whoami={actual}.");
        }
    }
}
