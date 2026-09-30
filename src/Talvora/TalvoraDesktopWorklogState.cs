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
        string Subject,
        string Message,
        DesktopProgressEvidence? Evidence);

    private sealed record PendingCancellation(
        string ToolName,
        string Subject,
        string Message,
        DesktopProgressEvidence? Evidence);

    private readonly object _worklogGate = new();
    private readonly Dictionary<string, ActiveWorklogItem> _activeWorklog =
        new(StringComparer.Ordinal);
    private readonly Queue<string> _recentWorklogUpdates = new();
    private long _worklogSequence;
    private long _worklogGeneration;
    private string? _currentWorklogOperationId;
    private PendingFailure? _pendingFailure;
    private PendingCancellation? _pendingCancellation;
    private string? _lastWorklogHistory;

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
                _recentWorklogUpdates.Clear();
                _lastWorklogHistory = null;
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
                narrative.Subject,
                ComposeWorklogMessage(
                    $"{narrative.Action}\n\nAmaç: {narrative.Reason}",
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
                current.Narrative.Subject,
                ComposeWorklogMessage(
                    $"İşlem devam ediyor. Geçen süre: {FormatElapsed(elapsed)}.",
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
                AddWorklogHistory(
                    $"Hata: {ended.Narrative.Subject}");
                _pendingFailure =
                    new PendingFailure(
                        ended.ToolName,
                        ended.Narrative.Subject,
                        message,
                        evidence);
            }
            else if (kind == DesktopProgressKind.Cancelled)
            {
                AddWorklogHistory(
                    $"Durduruldu: {ended.Narrative.Subject}");
                _pendingCancellation =
                    new PendingCancellation(
                        ended.ToolName,
                        ended.Narrative.Subject,
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
                    if (kind is not
                            DesktopProgressKind.Failed and not
                            DesktopProgressKind.Cancelled)
                    {
                        AddWorklogHistory(
                            $"Tamamlandı: {ended.Narrative.Subject}");
                    }

                    var continuation = kind switch
                    {
                        DesktopProgressKind.Failed =>
                            "Bir paralel adım tamamlanamadı; kalan aktif işlem devam ediyor.",
                        DesktopProgressKind.Cancelled =>
                            "Bir paralel adım durduruldu; kalan aktif işlem devam ediyor.",
                        _ =>
                            "Bir paralel adım tamamlandı; kalan aktif işlem devam ediyor.",
                    };
                    Enqueue(
                        GetCurrentWorklogOperationId(),
                        latest.ToolName,
                        latest.Narrative.Subject,
                        ComposeWorklogMessage(
                            continuation,
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
                    pendingFailure.Subject,
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
                    pendingCancellation.Subject,
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

    private void AddWorklogHistory(string update)
    {
        var cleaned =
            string.Join(
                " ",
                update.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries));
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return;
        }

        if (string.Equals(
                cleaned,
                _lastWorklogHistory,
                StringComparison.Ordinal))
        {
            return;
        }

        _recentWorklogUpdates.Enqueue(cleaned);
        _lastWorklogHistory = cleaned;
        while (_recentWorklogUpdates.Count > 4)
        {
            _recentWorklogUpdates.Dequeue();
        }
    }

    private string ComposeWorklogMessage(
        string current,
        DesktopProgressEvidence? evidence)
    {
        var sections = new List<string>
        {
            current.Trim(),
        };

        var evidenceText =
            DescribeEvidenceForUser(evidence);
        if (!string.IsNullOrWhiteSpace(evidenceText))
        {
            sections.Add(evidenceText);
        }

        if (_recentWorklogUpdates.Count > 0)
        {
            sections.Add(
                "Son yaptıklarım:\n" +
                string.Join(
                    Environment.NewLine,
                    _recentWorklogUpdates.Select(
                        static item => $"• {item}")));
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            sections);
    }

    private static string? DescribeEvidenceForUser(
        DesktopProgressEvidence? evidence)
    {
        if (evidence is null)
        {
            return null;
        }

        var details = new List<string>();
        if (evidence.Files is { Count: > 0 })
        {
            var names =
                evidence.Files
                    .Take(4)
                    .Select(Path.GetFileName)
                    .Where(static name =>
                        !string.IsNullOrWhiteSpace(name))
                    .ToArray();
            if (names.Length > 0)
            {
                details.Add(
                    $"{evidence.Files.Count} dosyada çalışıyorum: {string.Join(", ", names)}" +
                    (evidence.Files.Count > names.Length
                        ? " ve diğerleri"
                        : string.Empty));
            }
        }

        if (evidence.AddedLines is > 0 ||
            evidence.RemovedLines is > 0)
        {
            details.Add(
                $"Gerçek kod değişikliği: +{evidence.AddedLines ?? 0} / -{evidence.RemovedLines ?? 0} satır.");
        }

        if (!string.IsNullOrWhiteSpace(evidence.Result))
        {
            details.Add(evidence.Result);
        }

        return details.Count == 0
            ? null
            : string.Join(Environment.NewLine, details);
    }
}
