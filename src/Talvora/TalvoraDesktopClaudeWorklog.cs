using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    internal void BeginClaudeJob(string jobId, string? workingDirectory = null)
    {
        var narrative = new OperationNarrative(
            OperationCategory.GenericExecution, WorklogCopy.Claude);
        TryBeginWorklog("claude-" + jobId, "claude_background", narrative, null, TimeSpan.Zero,
            clientName: "Claude", projectOverride: workingDirectory);
    }

    internal void UpdateClaudeJob(string jobId, int toolCalls, TimeSpan elapsed,
        int failedTools = 0, int permissionDenials = 0, string? activity = null)
    {
        lock (_worklogGate)
        {
            if (!_activeWorklog.TryGetValue("claude-" + jobId, out var item)) { return; }
            if (_activeWorklog.Values
                    .Where(active => ReferenceEquals(active.Session, item.Session))
                    .MaxBy(static active => active.Sequence)?.Sequence != item.Sequence) { return; }
            item.Session.Steps = Math.Max(item.Session.Steps, toolCalls);
            item.Session.ProgressFact = permissionDenials > 0
                ? $"{toolCalls} adım · {permissionDenials} izin reddi"
                : $"{toolCalls} adım · {item.Session.ChangedFiles.Count} dosya değişti";
            if (!string.IsNullOrWhiteSpace(activity))
            {
                var past = activity.StartsWith("Dosya düzenliyor", StringComparison.Ordinal)
                    ? "Kod değişikliği uygulandı" : activity switch
                    {
                        "Testleri çalıştırıyor" => "Testler geçti",
                        "Derliyor" => "Derleme başarılı",
                        "Git işlemi yapıyor" => "Git işlemi yapıldı",
                        _ => "Kaynaklar incelendi",
                    };
                if (item.Session.Timeline.FirstOrDefault()?.Label != past)
                {
                    item.Session.Timeline.Insert(0, new WorklogTimelineEntry(
                        DateTimeOffset.UtcNow, WorklogStepState.Succeeded, past));
                    if (item.Session.Timeline.Count > DesktopProgressProtocol.MaximumTimelineEntries)
                        item.Session.Timeline.RemoveAt(item.Session.Timeline.Count - 1);
                }
            }
            Enqueue(item.Session.Id, item.ToolName, item.Narrative.Titles.Running,
                WorklogCopy.RunningFallback, DesktopProgressKind.Running, elapsed);
        }
    }

    internal void EndClaudeJob(string jobId, string state, TimeSpan elapsed,
        int toolCalls = 0, int failedTools = 0, int permissionDenials = 0,
        string? finalAnswer = null)
    {
        UpdateClaudeJob(jobId, toolCalls, elapsed, failedTools, permissionDenials);
        DesktopProgressEvidence evidence;
        lock (_worklogGate)
        {
            var files = _activeWorklog.TryGetValue("claude-" + jobId, out var item)
                ? item.Session.ChangedFiles.Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
            var safe = FileLog.RedactSensitiveData(finalAnswer ?? "").Trim();
            if (safe.Length > 240) safe = safe[..240];
            evidence = new DesktopProgressEvidence(
                permissionDenials > 0 ? $"{toolCalls} adım · {permissionDenials} izin reddi" :
                    $"{toolCalls} adım · {files.Length} dosya değişti", files,
                Result: safe.Length == 0 ? null : "Claude'un özeti · " + safe);
        }
        var kind = state switch
        {
            "completed" => DesktopProgressKind.Completed,
            "cancelled" => DesktopProgressKind.Cancelled,
            _ => DesktopProgressKind.Failed,
        };
        var titles = WorklogCopy.Claude;
        var title = state switch
        {
            "completed" => titles.Succeeded,
            "cancelled" => WorklogCopy.CancelledTitle,
            _ => titles.Failed,
        };
        TryEndWorklog("claude-" + jobId, kind, title,
            state switch
            {
                "completed" => WorklogCopy.SucceededFallback,
                "cancelled" => "Görev isteğiniz üzerine durduruldu.",
                "timed_out" => "Görev ayrılan sürede bitmedi; yapılan değişiklikler tamamlanmış sayılmıyor.",
                _ when permissionDenials > 0 => "Claude izin gerektiren adımları yapamadı.",
                _ => "Görev tamamlanamadı; yapılan değişiklikler tamamlanmış sayılmıyor.",
            }, evidence, elapsed);
    }
}
