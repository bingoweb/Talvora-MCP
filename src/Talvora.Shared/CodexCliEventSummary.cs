using System.Text.Json;
using System.Text.RegularExpressions;

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
    bool HasEvents)
{
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
}

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
        var changedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                                Enqueue(activities, ClassifyCommand(GetString(item, "command") ?? ""));
                                break;
                            case "mcp_tool_call":
                                mcpCalls++;
                                Enqueue(activities, ClassifyTool(GetString(item, "tool") ?? ""));
                                break;
                            case "file_change":
                                if (GetString(item, "status") is "failed" or "declined") break;
                                if (!item.TryGetProperty("changes", out var changes) ||
                                    changes.ValueKind != JsonValueKind.Array) break;
                                foreach (var change in changes.EnumerateArray())
                                {
                                    if (change.ValueKind != JsonValueKind.Object ||
                                        GetString(change, "path") is not { Length: > 0 and <= 1024 } path ||
                                        changedFiles.Count >= 2000) continue;
                                    changedFiles.Add(path);
                                    var name = Path.GetFileName(path.Replace('\\', '/'));
                                    if (!string.IsNullOrWhiteSpace(name))
                                        Enqueue(activities, $"Dosya düzenliyor · {Abbreviate(name, 80)}");
                                }
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
            hasEvents)
        {
            ChangedFiles = changedFiles.ToArray(),
        };
    }

    internal static string ClassifyCommand(string command)
    {
        if (Regex.IsMatch(command, @"\b(dotnet\s+test|pytest|unittest|vitest|jest|npm\s+(run\s+)?test|pnpm\s+test)\b", RegexOptions.IgnoreCase))
            return "Testleri çalıştırıyor";
        if (Regex.IsMatch(command, @"\b(dotnet\s+(build|publish)|msbuild|cargo\s+build|npm\s+run\s+build|pnpm\s+build)\b", RegexOptions.IgnoreCase))
            return "Derliyor";
        if (Regex.IsMatch(command, @"\bgit\s+(add|commit|push|pull|fetch|status|diff|log|show)\b", RegexOptions.IgnoreCase))
            return "Git işlemi yapıyor";
        return "Kaynakları inceliyor";
    }

    private static string ClassifyTool(string tool) =>
        tool.Contains("git", StringComparison.OrdinalIgnoreCase) ? "Talvora aracı kullanıyor · Git" :
        tool.Contains("test", StringComparison.OrdinalIgnoreCase) ? "Talvora aracı kullanıyor · Test" :
        tool.Contains("build", StringComparison.OrdinalIgnoreCase) ? "Talvora aracı kullanıyor · Derleme" :
        tool.Contains("edit", StringComparison.OrdinalIgnoreCase) || tool.Contains("patch", StringComparison.OrdinalIgnoreCase)
            ? "Talvora aracı kullanıyor · Kod değişikliği" :
        "Talvora aracı kullanıyor · İnceleme";

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
