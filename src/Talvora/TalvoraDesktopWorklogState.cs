using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    private const string SharedWorklogOperationId =
        "talvora-live-worklog";

    private sealed record ActiveWorklogItem(
        OperationNarrative Narrative,
        DesktopProgressEvidence? Evidence,
        long Sequence);

    private sealed record PendingFailure(
        string Message,
        DesktopProgressEvidence? Evidence);

    private readonly object _worklogGate = new();
    private readonly Dictionary<string, ActiveWorklogItem> _activeWorklog =
        new(StringComparer.Ordinal);
    private long _worklogSequence;
    private PendingFailure? _pendingFailure;

    private void TryBeginWorklog(
        string operationId,
        OperationNarrative narrative,
        DesktopProgressEvidence? evidence,
        TimeSpan elapsed)
    {
        try
        {
            BeginWorklog(
                operationId,
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
        OperationNarrative narrative,
        DesktopProgressEvidence? evidence,
        TimeSpan elapsed)
    {
        lock (_worklogGate)
        {
            _activeWorklog[operationId] =
                new ActiveWorklogItem(
                    narrative,
                    evidence,
                    ++_worklogSequence);

            Enqueue(
                SharedWorklogOperationId,
                narrative.Subject,
                "Şimdi bunu yapıyorum",
                $"{narrative.Action}\n\nNeden: {narrative.Reason}",
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
                SharedWorklogOperationId,
                displayName,
                "Hâlâ bununla uğraşıyorum",
                $"{displayName}.\n\nYaklaşık {FormatElapsed(elapsed)} oldu. Bitince sonucu burada göstereceğim.",
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
            _activeWorklog.Remove(operationId);

            if (kind == DesktopProgressKind.Failed)
            {
                _pendingFailure =
                    new PendingFailure(
                        message,
                        evidence);
            }

            if (_activeWorklog.Count > 0)
            {
                if (kind == DesktopProgressKind.Failed)
                {
                    Enqueue(
                        SharedWorklogOperationId,
                        "Çalışmaya devam ediyorum",
                        title,
                        message,
                        kind,
                        elapsed,
                        evidence);
                    return;
                }

                var latest =
                    _activeWorklog.Values
                        .MaxBy(static item => item.Sequence);
                if (latest is not null)
                {
                    Enqueue(
                        SharedWorklogOperationId,
                        latest.Narrative.Subject,
                        "Hâlâ çalışıyorum",
                        $"{latest.Narrative.Action}\n\nDiğer adımlar da tamamlanmayı bekliyor.",
                        DesktopProgressKind.Running,
                        elapsed,
                        latest.Evidence);
                }

                return;
            }

            if (_pendingFailure is not null)
            {
                var pendingFailure = _pendingFailure;
                _pendingFailure = null;
                Enqueue(
                    SharedWorklogOperationId,
                    "Kontrol gerekiyor",
                    "Bir hata buldum",
                    pendingFailure.Message,
                    DesktopProgressKind.Failed,
                    elapsed,
                    pendingFailure.Evidence);
                return;
            }

            Enqueue(
                SharedWorklogOperationId,
                "Çalışma tamamlandı",
                title,
                message,
                kind,
                elapsed,
                evidence);
        }
    }
}
