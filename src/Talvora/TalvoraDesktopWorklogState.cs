using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    private const string SharedWorklogOperationId =
        "talvora-live-worklog";

    private sealed record ActiveWorklogItem(
        OperationNarrative Narrative,
        long Sequence);

    private readonly object _worklogGate = new();
    private readonly Dictionary<string, ActiveWorklogItem> _activeWorklog =
        new(StringComparer.Ordinal);
    private long _worklogSequence;
    private string? _pendingFailureMessage;

    private void TryBeginWorklog(
        string operationId,
        OperationNarrative narrative,
        TimeSpan elapsed)
    {
        try
        {
            BeginWorklog(operationId, narrative, elapsed);
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
        TimeSpan elapsed)
    {
        try
        {
            EndWorklog(
                operationId,
                kind,
                title,
                message,
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
        TimeSpan elapsed)
    {
        lock (_worklogGate)
        {
            _activeWorklog[operationId] =
                new ActiveWorklogItem(
                    narrative,
                    ++_worklogSequence);

            Enqueue(
                SharedWorklogOperationId,
                narrative.Subject,
                "Şimdi bunu yapıyorum",
                $"{narrative.Action}\n\nNeden: {narrative.Reason}",
                DesktopProgressKind.Started,
                elapsed);
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
                elapsed);
        }
    }

    private void EndWorklog(
        string operationId,
        DesktopProgressKind kind,
        string title,
        string message,
        TimeSpan elapsed)
    {
        lock (_worklogGate)
        {
            _activeWorklog.Remove(operationId);

            if (kind == DesktopProgressKind.Failed)
            {
                _pendingFailureMessage = message;
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
                        elapsed);
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
                        elapsed);
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(_pendingFailureMessage))
            {
                var pendingFailure = _pendingFailureMessage;
                _pendingFailureMessage = null;
                Enqueue(
                    SharedWorklogOperationId,
                    "Kontrol gerekiyor",
                    "Bir hata buldum",
                    pendingFailure,
                    DesktopProgressKind.Failed,
                    elapsed);
                return;
            }

            Enqueue(
                SharedWorklogOperationId,
                "Çalışma tamamlandı",
                title,
                message,
                kind,
                elapsed);
        }
    }
}
