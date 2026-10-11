using System.Text.Json;

namespace Talvora.Shared;

/// <summary>
/// Extracts the useful parts of Codex's --json stream without forwarding
/// huge raw transcripts, duplicate reasoning, or command output bodies.
/// </summary>
public sealed record CodexCliEventResult(
    string? SessionId,
    string FinalAnswer,
    int CompletedCommands,
    int FailedCommands,
    int McpToolCalls,
    IReadOnlyList<string> RecentActivities,
    string? LastError,
    bool HasEvents);

public static class CodexCliEventSummary
{
    public static CodexCliEventResult Parse(string output)
    {
        var sessionId = (string?)null;
        var finalAnswer = string.Empty;
        var lastError = (string?)null;
        var commands = 0;
        var failedCommands = 0;
        var mcpCalls = 0;
        var hasEvents = false;
        var activities = new Queue<string>();

        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith('{') || line.Length > 256 * 1024)
            {
                continue;
            }
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = GetString(root, "type");
                if (string.IsNullOrWhiteSpace(type)) { continue; }
                hasEvents = true;
                switch (type)
                {
                    case "thread.started":
                        sessionId = GetString(root, "thread_id") ?? sessionId;
                        break;
                    case "error":
                        lastError = GetString(root, "message") ?? lastError;
                        break;
                    case "turn.failed":
                        if (root.TryGetProperty("error", out var error))
                        {
                            lastError = GetString(error, "message") ??
                                lastError;
                        }
                        break;
                    case "item.completed":
                        if (!root.TryGetProperty("item", out var item) ||
                            item.ValueKind != JsonValueKind.Object)
                        {
                            break;
                        }
                        switch (GetString(item, "type"))
                        {
                            case "agent_message":
                                finalAnswer = GetString(item, "text") ??
                                    finalAnswer;
                                break;
                            case "command_execution":
                                commands++;
                                var exitCode = GetInt(item, "exit_code");
                                if (exitCode.HasValue && exitCode.Value != 0)
                                {
                                    failedCommands++;
                                }
                                Enqueue(activities,
                                    $"Shell: {Abbreviate(GetString(item, "command") ?? "command", 160)}" +
                                    (exitCode is null ? "" : $" (exit {exitCode})"));
                                break;
                            case "mcp_tool_call":
                                mcpCalls++;
                                Enqueue(activities,
                                    $"MCP: {Abbreviate(GetString(item, "server") ?? "server", 60)}/" +
                                    Abbreviate(GetString(item, "tool") ?? "tool", 80));
                                break;
                            case "file_change":
                                Enqueue(activities, "Workspace files edited");
                                break;
                        }
                        break;
                }
            }
            catch (JsonException)
            {
                // A head/tail bounded capture can contain incomplete JSONL
                // records; valid records before/after remain usable.
            }
        }
        return new CodexCliEventResult(
            sessionId,
            finalAnswer,
            commands,
            failedCommands,
            mcpCalls,
            activities.ToArray(),
            lastError,
            hasEvents);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static string Abbreviate(string text, int limit)
    {
        var clean = text.Replace('\r', ' ').Replace('\n', ' ');
        return clean.Length <= limit ? clean : clean[..limit] + "…";
    }

    private static void Enqueue(Queue<string> queue, string activity)
    {
        if (queue.Count == 12) { queue.Dequeue(); }
        queue.Enqueue(activity);
    }
}
