using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Talvora.Shared;

namespace Talvora.Tray;

/// <summary>One closed desktop worklog session, persisted for the Control Center (§8).</summary>
internal sealed record WorklogHistorySession(
    string SessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    WorklogSessionHeader Header,
    WorklogStep Current,
    IReadOnlyList<WorklogTimelineEntry> Timeline,
    WorklogTotals Totals,
    DesktopProgressEvidence? Evidence,
    DesktopProgressKind Kind)
{
    [JsonIgnore]
    public string OutcomeLabel => Kind switch
    {
        DesktopProgressKind.Completed => "Tamamlandı",
        DesktopProgressKind.Failed => "Başarısız",
        DesktopProgressKind.Cancelled => "İptal edildi",
        _ => "Devam ediyor",
    };

    [JsonIgnore]
    public string StartedLocal => StartedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    [JsonIgnore]
    public string EndedLocal => EndedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    [JsonIgnore]
    public IReadOnlyList<WorklogHistoryTimelineRow> TimelineRows =>
        Timeline.Select(entry => new WorklogHistoryTimelineRow(
            entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"), entry.Label)).ToArray();

    [JsonIgnore]
    public bool HasCodePreview => !string.IsNullOrWhiteSpace(Evidence?.CodePreview);
}

internal sealed record WorklogHistoryTimelineRow(string LocalTime, string Label);

/// <summary>Single-user, bounded JSONL store. Only completed sessions enter history.</summary>
internal sealed class WorklogHistoryStore
{
    internal const int MaximumSessions = 2000;
    internal const long MaximumBytes = 20L * 1024 * 1024;
    private const int MaximumPreviewBytes = 8 * 1024;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private List<WorklogHistorySession>? _sessions;

    public WorklogHistoryStore(string? path = null, Func<DateTimeOffset>? clock = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora", "ControlCenter", "worklog-sessions.jsonl");
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public void AppendTerminal(DesktopProgressMessage message)
    {
        if (!message.SessionClosed ||
            message.WorklogFrame is not { } frame ||
            message.Lane != DesktopProgressLane.Worklog ||
            message.Kind is not (DesktopProgressKind.Completed or
                DesktopProgressKind.Failed or DesktopProgressKind.Cancelled))
            return;

        lock (_gate)
        {
            EnsureLoaded();
            var end = message.TimestampUtc;
            var start = end.AddSeconds(-Math.Max(0, frame.Totals.SessionSeconds));
            var session = new WorklogHistorySession(
                frame.SessionId, start, end,
                frame.Header with
                {
                    ProjectName = Redact(frame.Header.ProjectName),
                    ActorName = Redact(frame.Header.ActorName)!,
                    TaskNote = Redact(frame.Header.TaskNote),
                },
                frame.Current with
                {
                    Title = Redact(frame.Current.Title)!,
                    FactLine = Redact(frame.Current.FactLine),
                    Purpose = Redact(frame.Current.Purpose),
                    Outcome = frame.Current.Outcome is { } outcome
                        ? outcome with
                        {
                            PlainReason = Redact(outcome.PlainReason)!,
                            NextStep = Redact(outcome.NextStep),
                        } : null,
                    Changes = frame.Current.Changes?.Select(change => change with
                    {
                        File = Redact(change.File)!,
                        Places = change.Places.Select(place => Redact(place)!).ToArray(),
                    }).ToArray(),
                },
                frame.Timeline.Select(row => row with { Label = Redact(row.Label)! }).ToArray(),
                frame.Totals,
                message.Evidence is { } evidence ? evidence with
                {
                    Summary = Redact(evidence.Summary),
                    Files = evidence.Files?.Select(file => Redact(file)!).ToArray(),
                    CodePreview = LimitUtf8(Redact(evidence.CodePreview), MaximumPreviewBytes),
                    Result = Redact(evidence.Result),
                    CodeChangeId = Redact(evidence.CodeChangeId),
                } : null,
                message.Kind);
            _sessions!.RemoveAll(item => item.SessionId == session.SessionId);
            _sessions.Add(session);
            _sessions = Prune(_sessions);
            Save();
        }
    }

    public IReadOnlyList<WorklogHistorySession> LoadRecent()
    {
        lock (_gate)
        {
            // The presenter writes through another instance; refresh from the shared file.
            _sessions = null;
            EnsureLoaded();
            return _sessions!.ToArray();
        }
    }

    private void EnsureLoaded()
    {
        if (_sessions is not null) return;
        var sessions = new List<(WorklogHistorySession Session, int Bytes)>();
        if (File.Exists(_path))
        {
            var lines = File.ReadAllLines(_path, Utf8NoBom);
            var parsed = new (WorklogHistorySession? Session, int Bytes)[lines.Length];
            Parallel.For(0, lines.Length,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) },
                index =>
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line)) return;
                try
                {
                    var item = JsonSerializer.Deserialize<WorklogHistorySession>(line, JsonOptions);
                    if (item is not null && !string.IsNullOrWhiteSpace(item.SessionId))
                        parsed[index] = (item, Utf8NoBom.GetByteCount(line) + Environment.NewLine.Length);
                }
                catch (JsonException)
                {
                    // Ignore an interrupted trailing append or damaged entry.
                }
            });
            foreach (var row in parsed)
                if (row.Session is { } session)
                    sessions.Add((session, row.Bytes));
        }

        var cutoff = _clock().Subtract(Retention);
        var retained = new List<WorklogHistorySession>();
        long bytes = 0;
        foreach (var row in sessions.Where(row => row.Session.EndedAtUtc >= cutoff)
                     .OrderByDescending(row => row.Session.EndedAtUtc)
                     .DistinctBy(row => row.Session.SessionId)
                     .Take(MaximumSessions))
        {
            if (bytes + row.Bytes > MaximumBytes) break;
            retained.Add(row.Session);
            bytes += row.Bytes;
        }
        // Reading never rewrites a fixture or the current journal; the next terminal
        // append atomically rotates the physical file to the same bounds.
        _sessions = retained;
    }

    private List<WorklogHistorySession> Prune(IEnumerable<WorklogHistorySession> source)
    {
        var cutoff = _clock().Subtract(Retention);
        var ordered = source.Select((item, index) => (Item: item, Index: index))
            .Where(row => row.Item.EndedAtUtc >= cutoff)
            .OrderByDescending(row => row.Item.EndedAtUtc)
            .ThenByDescending(row => row.Index)
            .Select(row => row.Item)
            .DistinctBy(item => item.SessionId)
            .Take(MaximumSessions);
        var retained = new List<WorklogHistorySession>();
        long size = 0;
        foreach (var item in ordered)
        {
            var lineSize = Utf8NoBom.GetByteCount(JsonSerializer.Serialize(item, JsonOptions)) + Environment.NewLine.Length;
            if (size + lineSize > MaximumBytes) break;
            retained.Add(item);
            size += lineSize;
        }
        return retained;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllLines(temporary, _sessions!.Select(s => JsonSerializer.Serialize(s, JsonOptions)), Utf8NoBom);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string? Redact(string? value) =>
        value is null ? null : FileLog.RedactSensitiveData(value);

    private static string? LimitUtf8(string? value, int limit)
    {
        if (value is null || Utf8NoBom.GetByteCount(value) <= limit) return value;
        var chars = 0;
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > limit) break;
            bytes += rune.Utf8SequenceLength;
            chars += rune.Utf16SequenceLength;
        }
        return value[..chars];
    }
}
