using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    private const string SharedWorklogOperationIdPrefix =
        "talvora-live-worklog";

    private sealed record ActiveWorklogItem(
        OperationNarrative Narrative,
        DesktopProgressEvidence? Evidence,
        long Sequence);

    private sealed record PendingFailure(
        string Message,
        DesktopProgressEvidence? Evidence);

    private sealed record PendingCancellation(
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
            if (_activeWorklog.Count == 0)
            {
                _currentWorklogOperationId =
                    $"{SharedWorklogOperationIdPrefix}-{++_worklogGeneration:x16}";
                _pendingFailure = null;
                _pendingCancellation = null;
                _recentWorklogUpdates.Clear();
            }

            _activeWorklog[operationId] =
                new ActiveWorklogItem(
                    narrative,
                    evidence,
                    ++_worklogSequence);

            AddWorklogHistory(
                $"Şimdi: {narrative.Action}");

            Enqueue(
                GetCurrentWorklogOperationId(),
                narrative.Subject,
                "Şimdi bunu yapıyorum",
                ComposeWorklogMessage(
                    $"{narrative.Action}\n\nNeden: {narrative.Reason}",
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
                displayName,
                "Hâlâ bununla uğraşıyorum",
                ComposeWorklogMessage(
                    $"{displayName}.\n\nYaklaşık {FormatElapsed(elapsed)} oldu. İşlem hâlâ devam ediyor; yeni gerçek veri geldikçe bu kartı güncelliyorum.",
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
            if (!_activeWorklog.Remove(operationId))
            {
                return;
            }

            if (kind == DesktopProgressKind.Failed)
            {
                AddWorklogHistory(
                    $"Sorun çıktı: {message}");
                _pendingFailure =
                    new PendingFailure(
                        message,
                        evidence);
            }
            else if (kind == DesktopProgressKind.Cancelled)
            {
                AddWorklogHistory(
                    $"Durduruldu: {message}");
                _pendingCancellation =
                    new PendingCancellation(
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
                            "Bir adımı tamamladım; sıradaki iş devam ediyor.");
                    }

                    var continuation = kind switch
                    {
                        DesktopProgressKind.Failed =>
                            $"{latest.Narrative.Action}\n\nBir paralel adımda sorun çıktı; diğer işi durdurmadan devam ediyorum.",
                        DesktopProgressKind.Cancelled =>
                            $"{latest.Narrative.Action}\n\nBir paralel adım durduruldu; kalan iş üzerinde çalışmaya devam ediyorum.",
                        _ =>
                            $"{latest.Narrative.Action}\n\nBir önceki adımı tamamladım; sıradaki iş üzerinde çalışmaya devam ediyorum.",
                    };
                    Enqueue(
                        GetCurrentWorklogOperationId(),
                        latest.Narrative.Subject,
                        "Hâlâ çalışıyorum",
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
                    "Kontrol gerekiyor",
                    "Bir hata buldum",
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
                    "Çalışma durduruldu",
                    "Bir iş durduruldu",
                    ComposeWorklogMessage(
                        pendingCancellation.Message,
                        pendingCancellation.Evidence),
                    DesktopProgressKind.Cancelled,
                    elapsed,
                    pendingCancellation.Evidence);
                _currentWorklogOperationId = null;
                return;
            }

            AddWorklogHistory(
                $"Tamamlandı: {message.Replace(Environment.NewLine, " ").Trim()}");
            Enqueue(
                sharedOperationId,
                "Çalışma tamamlandı",
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

        _recentWorklogUpdates.Enqueue(cleaned);
        while (_recentWorklogUpdates.Count > 6)
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
