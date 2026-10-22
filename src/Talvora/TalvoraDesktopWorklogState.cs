using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    private const string SharedWorklogOperationIdPrefix =
        "talvora-live-worklog";

    private sealed record ActiveWorklogItem(
        string ToolName,
        OperationNarrative Narrative,
        DesktopProgressEvidence? Evidence,
        long Sequence);

    private sealed record PendingFailure(
        string ToolName,
        string Title,
        string Message,
        DesktopProgressEvidence? Evidence);

    private sealed record PendingCancellation(
        string ToolName,
        string Title,
        string Message,
        DesktopProgressEvidence? Evidence);

    private readonly object _worklogGate = new();
    private readonly Dictionary<string, ActiveWorklogItem> _activeWorklog =
        new(StringComparer.Ordinal);
    private long _worklogSequence;
    private long _worklogGeneration;
    private string? _currentWorklogOperationId;
    private PendingFailure? _pendingFailure;
    private PendingCancellation? _pendingCancellation;

    private void TryBeginWorklog(
        string operationId,
        string toolName,
        OperationNarrative narrative,
        DesktopProgressEvidence? evidence,
        TimeSpan elapsed)
    {
        try
        {
            BeginWorklog(
                operationId,
                toolName,
                narrative,
                evidence,
                elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Desktop worklog start failed without affecting the tool operation.");
        }
    }

    private void TryUpdateWorklogProgress(
        string operationId,
        string displayName,
        TimeSpan elapsed)
    {
        try
        {
            UpdateWorklogProgress(
                operationId,
                displayName,
                elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Desktop worklog progress update failed without affecting the tool operation.");
        }
    }

    private void TryEndWorklog(
        string operationId,
        DesktopProgressKind kind,
        string title,
        string message,
        DesktopProgressEvidence? evidence,
        TimeSpan elapsed)
    {
        try
        {
            EndWorklog(
                operationId,
                kind,
                title,
                message,
                evidence,
                elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Desktop worklog terminal update failed without affecting the tool operation.");
        }
    }

    private void BeginWorklog(
        string operationId,
        string toolName,
        OperationNarrative narrative,
        DesktopProgressEvidence? evidence,
        TimeSpan elapsed)
    {
        lock (_worklogGate)
        {
            if (_activeWorklog.Count == 0)
            {
                _currentWorklogOperationId =
                    $"{SharedWorklogOperationIdPrefix}-{++_worklogGeneration:x16}";
                _pendingFailure = null;
                _pendingCancellation = null;
            }

            _activeWorklog[operationId] =
                new ActiveWorklogItem(
                    toolName,
                    narrative,
                    evidence,
                    ++_worklogSequence);

            Enqueue(
                GetCurrentWorklogOperationId(),
                toolName,
                narrative.Titles.Running,
                ComposeWorklogMessage(
                    WorklogCopy.RunningFallback,
                    evidence),
                DesktopProgressKind.Started,
                elapsed,
                evidence);
        }
    }

    private void UpdateWorklogProgress(
        string operationId,
        string displayName,
        TimeSpan elapsed)
    {
        lock (_worklogGate)
        {
            if (!_activeWorklog.TryGetValue(
                    operationId,
                    out var current))
            {
                return;
            }

            var latest =
                _activeWorklog.Values
                    .MaxBy(static item => item.Sequence);
            if (latest is null ||
                current.Sequence != latest.Sequence)
            {
                return;
            }

            Enqueue(
                GetCurrentWorklogOperationId(),
                current.ToolName,
                current.Narrative.Titles.Running,
                ComposeWorklogMessage(
                    WorklogCopy.RunningFallback,
                    current.Evidence),
                DesktopProgressKind.Running,
                elapsed,
                current.Evidence);
        }
    }

    private void EndWorklog(
        string operationId,
        DesktopProgressKind kind,
        string title,
        string message,
        DesktopProgressEvidence? evidence,
        TimeSpan elapsed)
    {
        lock (_worklogGate)
        {
            if (!_activeWorklog.Remove(
                    operationId,
                    out var ended))
            {
                return;
            }

            if (kind == DesktopProgressKind.Failed)
            {
                _pendingFailure =
                    new PendingFailure(
                        ended.ToolName,
                        title,
                        message,
                        evidence);
            }
            else if (kind == DesktopProgressKind.Cancelled)
            {
                _pendingCancellation =
                    new PendingCancellation(
                        ended.ToolName,
                        title,
                        message,
                        evidence);
            }

            if (_activeWorklog.Count > 0)
            {
                var latest =
                    _activeWorklog.Values
                        .MaxBy(static item => item.Sequence);
                if (latest is not null)
                {
                    Enqueue(
                        GetCurrentWorklogOperationId(),
                        latest.ToolName,
                        latest.Narrative.Titles.Running,
                        ComposeWorklogMessage(
                            WorklogCopy.RunningFallback,
                            latest.Evidence),
                        DesktopProgressKind.Running,
                        elapsed,
                        latest.Evidence);
                }

                return;
            }

            var sharedOperationId =
                GetCurrentWorklogOperationId();

            if (_pendingFailure is not null)
            {
                var pendingFailure = _pendingFailure;
                _pendingFailure = null;
                _pendingCancellation = null;
                Enqueue(
                    sharedOperationId,
                    pendingFailure.ToolName,
                    pendingFailure.Title,
                    ComposeWorklogMessage(
                        pendingFailure.Message,
                        pendingFailure.Evidence),
                    DesktopProgressKind.Failed,
                    elapsed,
                    pendingFailure.Evidence);
                _currentWorklogOperationId = null;
                return;
            }

            if (_pendingCancellation is not null)
            {
                var pendingCancellation = _pendingCancellation;
                _pendingCancellation = null;
                Enqueue(
                    sharedOperationId,
                    pendingCancellation.ToolName,
                    pendingCancellation.Title,
                    ComposeWorklogMessage(
                        pendingCancellation.Message,
                        pendingCancellation.Evidence),
                    DesktopProgressKind.Cancelled,
                    elapsed,
                    pendingCancellation.Evidence);
                _currentWorklogOperationId = null;
                return;
            }

            Enqueue(
                sharedOperationId,
                ended.ToolName,
                title,
                ComposeWorklogMessage(
                    message,
                    evidence),
                kind,
                elapsed,
                evidence);
            _currentWorklogOperationId = null;
        }
    }

    private string GetCurrentWorklogOperationId() =>
        _currentWorklogOperationId ??
        throw new InvalidOperationException(
            "Desktop worklog generation is not active.");

    private string ComposeWorklogMessage(
        string current,
        DesktopProgressEvidence? evidence) =>
        current.Trim();
}
