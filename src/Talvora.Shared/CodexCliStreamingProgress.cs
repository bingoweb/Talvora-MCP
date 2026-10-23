using System.Text;
using System.Text.Json;

namespace Talvora.Shared;

/// <summary>
/// Incremental, bounded Codex JSONL observation. Unlike a capture of the
/// first N characters, this follows events for the entire life of a task.
/// Only counters and short activity labels are persisted: never prompts,
/// shell command text, reasoning traces or raw command output.
/// </summary>
public sealed class CodexCliStreamingProgress
{
    private const int MaxLineCharacters = 256 * 1024;
    private readonly StringBuilder _pending = new();
    private readonly Queue<string> _recent = new();
    private readonly HashSet<string> _changedFiles = new(StringComparer.OrdinalIgnoreCase);
    private bool _ignoreOversizedLine;
    private string? _sessionId;
    private int _commands;
    private int _failedCommands;
    private int _mcpCalls;
    private DateTime _lastPersisted = DateTime.MinValue;

    public CodexCliEventResult Snapshot() =>
        new(_sessionId, string.Empty, _commands, _failedCommands,
            _mcpCalls, _recent.ToArray(), null, true)
        {
            ChangedFiles = _changedFiles.ToArray(),
        };

    public async Task ObserveAsync(
        ReadOnlyMemory<char> chunk,
        string path,
        CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var character in chunk.Span)
        {
            if (character == '\n')
            {
                if (!_ignoreOversizedLine &&
                    ConsumeLine(_pending.ToString().TrimEnd('\r')))
                {
                    changed = true;
                }
                _pending.Clear();
                _ignoreOversizedLine = false;
                continue;
            }
            if (_ignoreOversizedLine) { continue; }
            if (_pending.Length >= MaxLineCharacters)
            {
                _ignoreOversizedLine = true;
                _pending.Clear();
                continue;
            }
            _pending.Append(character);
        }
        if (changed && DateTime.UtcNow - _lastPersisted >
            TimeSpan.FromMilliseconds(250))
        {
            await PersistAsync(path, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CompleteAsync(string path, CancellationToken cancellationToken)
    {
        if (!_ignoreOversizedLine)
        {
            ConsumeLine(_pending.ToString().TrimEnd('\r'));
        }
        await PersistAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private bool ConsumeLine(string line)
    {
        if (line.Length == 0 || line[0] != '{') { return false; }
        var parsed = CodexCliEventSummary.Parse(line);
        if (!parsed.HasEvents) { return false; }
        if (parsed.SessionId is not null) { _sessionId = parsed.SessionId; }
        _commands += parsed.CompletedCommands;
        _failedCommands += parsed.FailedCommands;
        _mcpCalls += parsed.McpToolCalls;
        foreach (var file in parsed.ChangedFiles)
        {
            if (_changedFiles.Count >= 2000) break;
            _changedFiles.Add(file);
        }
        foreach (var activity in parsed.RecentActivities)
        {
            // Do not persist the shell command arguments or task text.
            var safe = activity.StartsWith("Shell:", StringComparison.Ordinal)
                ? "Shell command completed"
                : FileLog.RedactSensitiveData(activity);
            if (_recent.Count >= 12) { _recent.Dequeue(); }
            _recent.Enqueue(safe);
        }
        return true;
    }

    private async Task PersistAsync(string path, CancellationToken cancellationToken)
    {
        await AtomicFile.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(Snapshot()),
            new UTF8Encoding(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _lastPersisted = DateTime.UtcNow;
    }
}
