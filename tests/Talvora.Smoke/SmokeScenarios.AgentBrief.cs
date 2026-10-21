using ModelContextProtocol.Client;
using System.Text.Json;

internal static partial class SmokeScenarios
{
    internal static async Task RunAgentBriefLiveAsync(
        string serverRoot, string repositoryPath)
    {
        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(serverRoot.TrimEnd('/') + "/mcp/dev"),
                TransportMode = HttpTransportMode.StreamableHttp,
                ConnectionTimeout = TimeSpan.FromSeconds(20),
            }));

        var root = Path.GetFullPath(repositoryPath);
        var response = await CallAsync(client, root);
        var sections = response.GetProperty("agentsSections").GetString()!;
        var context = response.GetProperty("context").GetString()!;
        foreach (var mustContain in new[] { "ç", "ğ", "—", "§" })
        {
            if (!context.Contains(mustContain, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"agent_brief context lost the character '{mustContain}'.");
            }
        }
        if (!sections.StartsWith(
                "## PERMANENT USER-APPROVED AGENT PROMPT", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "agent_brief must start with the permanent-prompt section.");
        }
        foreach (var heading in new[]
        {
            "## WHO MUST FOLLOW THESE RULES",
            "## Language policy",
            "## EXECUTION MANDATE",
            "## TOKEN ECONOMY",
            "## QUEUE ADDITIONS",
            "## CONTINUE PROTOCOL",
        })
        {
            if (!sections.Contains(heading, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Agent brief omitted AGENTS.md section: {heading}");
            }
        }

        var status = File.ReadAllText(Path.Combine(
            root, "docs", "codex", "STATUS.md"));
        var queue = status[status.IndexOf(
            "## Agent queue", StringComparison.Ordinal)..];
        var firstOpen = queue.Split('\n')
            .First(line => line.StartsWith("- [ ] ", StringComparison.Ordinal));
        var cardId = firstOpen[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (!context.Contains("CARD " + cardId, StringComparison.Ordinal) ||
            response.GetProperty("workspaceRoot").GetString() != root ||
            response.GetProperty("truncated").GetBoolean() ||
            root.Length + sections.Length + context.Length > 60_000)
        {
            throw new InvalidOperationException(
                "Agent brief returned invalid workspace, active card, or size.");
        }

        var specified = await CallAsync(client, root, "A2");
        if (!specified.GetProperty("context").GetString()!
                .Contains("CARD A2", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Agent brief did not forward the explicit card.");
        }

        var emptyRoot = Path.Combine(Path.GetTempPath(),
            "Talvora-AgentBrief-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRoot);
        try
        {
            var withoutAgent = await CallAsync(client, emptyRoot);
            if (withoutAgent.GetProperty("agentsSections").GetString() != "" ||
                withoutAgent.GetProperty("context").GetString() != "" ||
                withoutAgent.GetProperty("truncated").GetBoolean())
            {
                throw new InvalidOperationException(
                    "Workspace without AGENTS.md must return empty sections.");
            }

            File.WriteAllText(Path.Combine(emptyRoot, "AGENTS.md"),
                "## WHO MUST FOLLOW THESE RULES\n" +
                new string('x', 75_000) +
                "\n## Language policy\nSmall section.\n");
            var oversized = await CallAsync(client, emptyRoot);
            var actualSize = oversized.GetProperty("workspaceRoot").GetString()!.Length +
                oversized.GetProperty("agentsSections").GetString()!.Length +
                oversized.GetProperty("context").GetString()!.Length;
            if (!oversized.GetProperty("truncated").GetBoolean() ||
                actualSize > 60_000)
            {
                throw new InvalidOperationException(
                    "Agent brief failed to cap an oversized AGENTS.md.");
            }
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
        }
    }

    private static async Task<JsonElement> CallAsync(
        McpClient client, string workspaceRoot, string? card = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["workspaceRoot"] = workspaceRoot,
        };
        if (card is not null)
        {
            arguments["card"] = card;
        }
        var result = await client.CallToolAsync(
            "talvora_agent_brief", arguments);
        if (result.IsError is true ||
            result.StructuredContent is not { } structured)
        {
            throw new InvalidOperationException(
                "Agent brief MCP call returned no successful structured result.");
        }
        return structured;
    }
}
