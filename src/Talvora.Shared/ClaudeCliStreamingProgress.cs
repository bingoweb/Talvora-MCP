using System.Text;
using System.Text.Json;

namespace Talvora.Shared;

public sealed record ClaudeCliProgress(
    string? SessionId, int ToolCalls, int FailedTools, int PermissionDenials,
    string[] RecentActivities, bool HasResult, bool IsError, string FinalAnswer,
    string? Error, DateTime? LastProgressAtUtc, ClaudeAppliedCodeResult? CodeChange = null);

/// <summary>Bounded JSONL observer independent of the retained stdout head/tail.</summary>
public sealed class ClaudeCliStreamingProgress
{
    private const int MaximumLine = 1024 * 1024;
    private readonly StringBuilder _line = new();
    private readonly Queue<string> _activities = new();
    private bool _oversized;
    private string? _sessionId;
    private int _tools, _failed, _denials;
    private bool _hasResult, _isError;
    private string _answer = "";
    private string? _error;
    private DateTime? _lastProgress;
    private DateTime _lastPersisted;

    private readonly string? _codeWorkspaceRoot;
    private readonly Dictionary<string, string> _codeTools = new(StringComparer.Ordinal);
    private ClaudeAppliedCodeResult? _codeChange;

    public ClaudeCliStreamingProgress(string? codeWorkspaceRoot = null) => _codeWorkspaceRoot = codeWorkspaceRoot;

    private void ObserveCodeResult(JsonElement root, JsonElement block)
    {
        var type = Text(block, "type");
        if (type == "tool_use" && Text(block, "name") is "Edit" or "MultiEdit" or "Write" &&
            Text(block, "id") is { Length: <= 160 } id && _codeTools.Count < 128)
            _codeTools[id] = Text(block, "name")!;
        if (type != "tool_result" || Text(block, "tool_use_id") is not { } resultId ||
            !_codeTools.Remove(resultId, out var tool) ||
            block.TryGetProperty("is_error", out var failed) && failed.ValueKind == JsonValueKind.True ||
            !root.TryGetProperty("tool_use_result", out var actual)) return;
        _codeChange = ClaudeCodeResultFilter.Capture(resultId, tool, actual, _codeWorkspaceRoot) ?? _codeChange;
    }

    public ClaudeCliProgress Snapshot() => new(
        _sessionId, _tools, _failed, _denials, _activities.ToArray(),
        _hasResult, _isError, _answer, _error, _lastProgress, _codeChange);

    public async Task ObserveAsync(ReadOnlyMemory<char> chunk, string path, CancellationToken token)
    {
        foreach (var ch in chunk.Span)
        {
            if (ch == '\n')
            {
                if (!_oversized) { ConsumeLine(_line.ToString()); }
                _line.Clear();
                _oversized = false;
            }
            else if (!_oversized)
            {
                if (_line.Length >= MaximumLine) { _line.Clear(); _oversized = true; }
                else { _line.Append(ch); }
            }
        }
        if (DateTime.UtcNow - _lastPersisted > TimeSpan.FromMilliseconds(500))
        {
            await PersistAsync(path, token).ConfigureAwait(false);
        }
    }

    public async Task CompleteAsync(string path, CancellationToken token)
    {
        if (!_oversized) { ConsumeLine(_line.ToString()); }
        await PersistAsync(path, token).ConfigureAwait(false);
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private void ConsumeLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return; }
            _sessionId = Text(root, "session_id") ?? _sessionId;
            var type = Text(root, "type");
            if (type is null) { return; }
            _lastProgress = DateTime.UtcNow;
            if (type == "result")
            {
                _hasResult = true;
                _isError = root.TryGetProperty("is_error", out var error) &&
                    error.ValueKind == JsonValueKind.True;
                var subtype = Text(root, "subtype");
                _isError |= subtype is not null && subtype != "success";
                _answer = Safe(Text(root, "result") ?? "");
                if (root.TryGetProperty("permission_denials", out var denials) &&
                    denials.ValueKind == JsonValueKind.Array)
                {
                    _denials = Math.Max(_denials, denials.GetArrayLength());
                }
                if (_isError)
                {
                    _error = root.TryGetProperty("errors", out var errors) &&
                        errors.ValueKind == JsonValueKind.Array
                        ? Safe(string.Join(Environment.NewLine, errors.EnumerateArray()
                            .Where(x => x.ValueKind == JsonValueKind.String)
                            .Select(x => x.GetString())))
                        : "Claude Code could not complete the task.";
                }
                return;
            }
            if (type == "system" && Text(root, "subtype") == "permission_denied")
            {
                _denials++;
                AddActivity("İzin gerektiren bir işlem çalıştırılamadı.");
            }
            if (type is not ("assistant" or "user") ||
                !root.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array) { return; }
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) { continue; }
                ObserveCodeResult(root, block);
                if (Text(block, "type") == "tool_use")
                {
                    _tools++;
                    // Never persist raw command text, arguments, reasoning or tool output.
                    AddActivity("Claude bir geliştirme adımı üzerinde çalışıyor.");
                }
                if (Text(block, "type") == "tool_result" &&
                    block.TryGetProperty("is_error", out var failed) &&
                    failed.ValueKind == JsonValueKind.True) { _failed++; }
            }
        }
        catch (JsonException) { /* Incomplete, oversized or non-JSON diagnostics. */ }
    }

    private void AddActivity(string value)
    {
        if (_activities.Count >= 12) { _activities.Dequeue(); }
        _activities.Enqueue(value);
    }

    private static string Safe(string value)
    {
        var safe = FileLog.RedactSensitiveData(value);
        return safe.Length <= 128 * 1024 ? safe : safe[..(128 * 1024)];
    }

    private async Task PersistAsync(string path, CancellationToken token)
    {
        await AtomicFile.WriteAllTextAsync(path, JsonSerializer.Serialize(Snapshot()),
            new UTF8Encoding(false), cancellationToken: token).ConfigureAwait(false);
        _lastPersisted = DateTime.UtcNow;
    }
}
