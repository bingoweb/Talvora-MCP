namespace Talvora.Shared;

/// <summary>Tray-local code cache: a preview arrives once, later session frames reference its identity.</summary>
public sealed class DesktopProgressCodeState
{
    private const int MaximumSessions = 16;
    private readonly List<SessionCode> _sessions = [];
    private SessionCode? _current;

    private sealed class SessionCode(string sessionId)
    {
        public string SessionId { get; } = sessionId;
        public DesktopProgressEvidence? Latest { get; set; }
        public string? Title { get; set; }
        public DateTimeOffset TimestampUtc { get; set; }
    }

    public string? Title { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }

    /// <summary>Time of the real code change shown in the pane, or null when the session has no code.</summary>
    public DateTimeOffset? CodeChangedUtc => _current?.Latest is null ? null : TimestampUtc;

    public DesktopProgressMessage Restore(DesktopProgressMessage message)
    {
        var sessionId = message.WorklogFrame?.SessionId ?? message.OperationId;
        var position = _sessions.FindIndex(session =>
            string.Equals(session.SessionId, sessionId, StringComparison.Ordinal));
        var current = position >= 0 ? _sessions[position] : new SessionCode(sessionId);
        if (position >= 0)
            _sessions.RemoveAt(position);
        _sessions.Add(current);
        if (_sessions.Count > MaximumSessions)
            _sessions.RemoveAt(0);
        _current = current;

        var evidence = message.Evidence;
        if (!string.IsNullOrEmpty(evidence?.CodePreview))
        {
            current.Latest = evidence;
            current.Title = message.Title;
            current.TimestampUtc = message.TimestampUtc;
        }
        else if (evidence?.CodeChangeId is { } id && current.Latest?.CodeChangeId == id)
        {
            evidence = evidence with {
                CodePreview = current.Latest.CodePreview,
                AddedLines = current.Latest.AddedLines,
                RemovedLines = current.Latest.RemovedLines };
        }
        else
        {
            current.Latest = null;
            current.Title = null;
        }
        Title = current.Title;
        TimestampUtc = current.TimestampUtc;
        // The Phosphor card reads the frame: give it the same restored evidence (audit 2026-10-23 (12)).
        return message with
        {
            Evidence = evidence,
            WorklogFrame = message.WorklogFrame is null ? null : message.WorklogFrame with { Evidence = evidence },
        };
    }
}
