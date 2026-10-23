namespace Talvora.Shared;

/// <summary>Tray-local code cache: a preview arrives once, later session frames reference its identity.</summary>
public sealed class DesktopProgressCodeState
{
    private string? _sessionId;
    private DesktopProgressEvidence? _latest;
    public string? Title { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }

    /// <summary>Time of the real code change shown in the pane, or null when the session has no code.</summary>
    public DateTimeOffset? CodeChangedUtc => _latest is null ? null : TimestampUtc;

    public DesktopProgressMessage Restore(DesktopProgressMessage message)
    {
        var sessionId = message.WorklogFrame?.SessionId ?? message.OperationId;
        if (_sessionId != sessionId)
        {
            _sessionId = sessionId;
            _latest = null;
            Title = null;
        }
        var evidence = message.Evidence;
        if (!string.IsNullOrEmpty(evidence?.CodePreview))
        {
            _latest = evidence;
            Title = message.Title;
            TimestampUtc = message.TimestampUtc;
        }
        else if (evidence?.CodeChangeId is { } id && _latest?.CodeChangeId == id)
        {
            evidence = evidence with {
                CodePreview = _latest.CodePreview,
                AddedLines = _latest.AddedLines,
                RemovedLines = _latest.RemovedLines };
        }
        else
        {
            _latest = null;
            Title = null;
        }
        // The Phosphor card reads the frame: give it the same restored evidence (audit 2026-10-23 (12)).
        return message with
        {
            Evidence = evidence,
            WorklogFrame = message.WorklogFrame is null ? null : message.WorklogFrame with { Evidence = evidence },
        };
    }
}
