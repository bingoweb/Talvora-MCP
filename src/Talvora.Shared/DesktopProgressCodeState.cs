namespace Talvora.Shared;

/// <summary>Tray-local code cache: a preview arrives once, later session frames reference its identity.</summary>
public sealed class DesktopProgressCodeState
{
    private string? _sessionId;
    private DesktopProgressEvidence? _latest;
    public string? Title { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }

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
        return message with { Evidence = evidence };
    }
}
