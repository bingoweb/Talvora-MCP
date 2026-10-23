using Talvora.Shared;

namespace Talvora;

/// <summary>
/// Keep a Codex background job visible as one desktop worklog operation for
/// its actual lifetime rather than closing its card when the MCP start call
/// returns. Progress updates are coalesced by the existing notifier.
/// </summary>
internal sealed partial class TalvoraDesktopProgressNotifier
{
    private readonly Dictionary<string, CodexWorkspaceSnapshot> _codexSnapshots = new(StringComparer.Ordinal);

    internal void BeginCodexJob(string jobId, string workingDirectory)
    {
        // Capture before Codex runs: the baseline includes any pre-existing dirty files.
        try
        {
            var snapshot = CodexWorkspaceSnapshot.Capture(workingDirectory);
            lock (_worklogGate) { _codexSnapshots[jobId] = snapshot; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Desktop evidence is best effort and must not block the job.
        }
        var narrative = new OperationNarrative(
            OperationCategory.GenericExecution, WorklogCopy.Codex);
        TryBeginWorklog(
            "codex-" + jobId, "codex_background",
            narrative, null, TimeSpan.Zero,
            clientName: "Codex", projectOverride: workingDirectory);
    }

    internal void UpdateCodexJob(
        string jobId,
        int commands,
        int mcpCalls,
        string? activity,
        TimeSpan elapsed,
        IReadOnlyList<string>? changedFiles = null)
    {
        var operationId = "codex-" + jobId;
        lock (_worklogGate)
        {
            if (!_activeWorklog.TryGetValue(operationId, out var item))
            {
                return;
            }
            var latest = _activeWorklog.Values
                .Where(active => ReferenceEquals(active.Session, item.Session))
                .MaxBy(static active => active.Sequence);
            if (latest?.Sequence != item.Sequence) { return; }
            DesktopProgressEvidence? codeEvidence = null;
            if (changedFiles is not null && _codexSnapshots.TryGetValue(jobId, out var snapshot))
            {
                foreach (var path in changedFiles)
                {
                    var relative = snapshot.SafeRelativeName(path);
                    if (relative is null) continue;
                    item.Session.ChangedFiles.Add(relative);
                    codeEvidence = snapshot.ObserveAppliedFile(path) ?? codeEvidence;
                }
            }
            var steps = commands + mcpCalls + item.Session.ChangedFiles.Count;
            item.Session.Steps = Math.Max(item.Session.Steps, steps);
            item.Session.ProgressFact = $"{steps} adım · {item.Session.ChangedFiles.Count} dosya değişti";
            if (!string.IsNullOrWhiteSpace(activity))
            {
                var past = activity.StartsWith("Dosya düzenliyor · ", StringComparison.Ordinal)
                    ? "Kod değişikliği uygulandı · " + activity["Dosya düzenliyor · ".Length..]
                    : activity switch
                    {
                        "Testleri çalıştırıyor" => "Testler geçti",
                        "Derliyor" => "Derleme başarılı",
                        "Git işlemi yapıyor" => "Git işlemi yapıldı",
                        "Kaynakları inceliyor" => "Kaynaklar incelendi",
                        _ => "Talvora aracı kullanıldı",
                    };
                if (item.Session.Timeline.FirstOrDefault()?.Label != past)
                {
                    item.Session.Timeline.Insert(0, new WorklogTimelineEntry(
                        DateTimeOffset.UtcNow, WorklogStepState.Succeeded, past));
                    if (item.Session.Timeline.Count > DesktopProgressProtocol.MaximumTimelineEntries)
                        item.Session.Timeline.RemoveAt(item.Session.Timeline.Count - 1);
                }
            }
            Enqueue(
                item.Session.Id,
                item.ToolName,
                item.Narrative.Titles.Running,
                WorklogCopy.RunningFallback,
                DesktopProgressKind.Running,
                elapsed, codeEvidence);
        }
    }

    internal void EndCodexJob(
        string jobId, string state, TimeSpan elapsed,
        int completedCommands = 0, int mcpCalls = 0,
        IReadOnlyList<string>? changedFiles = null, string? finalAnswer = null)
    {
        UpdateCodexJob(jobId, completedCommands, mcpCalls, null, elapsed, changedFiles);
        DesktopProgressEvidence evidence;
        lock (_worklogGate)
        {
            _codexSnapshots.Remove(jobId);
            var files = _activeWorklog.TryGetValue("codex-" + jobId, out var current)
                ? current.Session.ChangedFiles.Order(StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
            var safeSummary = FileLog.RedactSensitiveData(finalAnswer ?? "").Trim();
            if (safeSummary.Length > 240) safeSummary = safeSummary[..240];
            evidence = new DesktopProgressEvidence(
                $"{completedCommands + mcpCalls + files.Length} adım · {files.Length} dosya değişti",
                files, Result: safeSummary.Length == 0 ? null : "Codex'in özeti · " + safeSummary);
        }
        var kind = state switch
        {
            "completed" => DesktopProgressKind.Completed,
            "cancelled" => DesktopProgressKind.Cancelled,
            _ => DesktopProgressKind.Failed,
        };
        var titles = WorklogCopy.Codex;
        var title = state switch
        {
            "completed" => titles.Succeeded,
            "cancelled" => WorklogCopy.CancelledTitle,
            _ => titles.Failed,
        };
        TryEndWorklog(
            "codex-" + jobId, kind,
            title,
            state switch
            {
                "completed" => WorklogCopy.SucceededFallback,
                "cancelled" => "Görev isteğiniz üzerine durduruldu.",
                "timed_out" => "Görev ayrılan sürede bitmedi; yapılan değişiklikler tamamlanmış sayılmıyor.",
                _ => "Görev tamamlanamadı; yapılan değişiklikler tamamlanmış sayılmıyor.",
            },
            evidence, elapsed);
    }
}
