using Talvora.Shared;
using Talvora.Tools;

namespace Talvora;

/// <summary>
/// Keeps a detached Remotion step visible as one live worklog operation (ROADMAP RM2): the card shows the
/// real frame counter from the render log while it runs and the real output file when it ends.
/// </summary>
internal sealed partial class TalvoraDesktopProgressNotifier
{
    private static readonly TimeSpan RemotionPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RemotionMaximumWatch = TimeSpan.FromHours(3);

    internal void BeginRemotionRun(
        string runId, string action, string? projectRoot, string? compositionId,
        string resultPath, string logPath)
    {
        var titles = action switch
        {
            "create" => WorklogCopy.RemotionCreate,
            "compositions" => WorklogCopy.RemotionCheck,
            "still" => WorklogCopy.RemotionStill,
            _ => WorklogCopy.RemotionRender,
        };
        var subject = WorklogSubject.Shorten(compositionId ?? (projectRoot is null ? null : Path.GetFileName(projectRoot)));
        var narrative = new OperationNarrative(
            OperationCategory.ProcessOperation, WorklogSubject.WithSubject(titles, subject), subject);
        var operationId = "remotion-" + runId;
        TryBeginWorklog(operationId, "remotion_run", narrative, null, TimeSpan.Zero,
            projectOverride: projectRoot);
        _ = WatchRemotionRunAsync(operationId, runId, narrative, resultPath, logPath);
    }

    private async Task WatchRemotionRunAsync(
        string operationId, string runId, OperationNarrative narrative, string resultPath, string logPath)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string? lastFact = null;
        try
        {
            while (stopwatch.Elapsed < RemotionMaximumWatch)
            {
                await Task.Delay(RemotionPollInterval, _deliveryStopCts.Token).ConfigureAwait(false);
                var status = RemotionTools.ReadStatus(runId, resultPath, logPath);
                if (status.State is "succeeded" or "failed")
                {
                    var succeeded = status.State == "succeeded";
                    var fact = succeeded ? DescribeRemotionResult(status) : null;
                    TryEndWorklog(operationId,
                        succeeded ? DesktopProgressKind.Completed : DesktopProgressKind.Failed,
                        succeeded ? narrative.Titles.Succeeded : narrative.Titles.Failed,
                        succeeded ? WorklogCopy.SucceededFallback : RemotionFailureReason(status),
                        fact is null ? null : new DesktopProgressEvidence(Summary: fact, Result: status.LogTail),
                        stopwatch.Elapsed);
                    return;
                }

                var progress = status.FramesDone is { } done && status.FramesTotal is { } total
                    ? $"{done}/{total} kare"
                    : null;
                if (progress != lastFact)
                {
                    lastFact = progress;
                    UpdateRemotionProgress(operationId, progress, stopwatch.Elapsed);
                }
            }
            TryEndWorklog(operationId, DesktopProgressKind.Failed, narrative.Titles.Failed,
                "Animasyon üç saatte bitmedi; tamamlanmış sayılmıyor.", null, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            // Service is stopping; the run itself continues in the user's session.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Remotion worklog watch failed without affecting the run.");
        }
    }

    private void UpdateRemotionProgress(string operationId, string? progress, TimeSpan elapsed)
    {
        lock (_worklogGate)
        {
            if (!_activeWorklog.TryGetValue(operationId, out var item))
            {
                return;
            }
            item.Session.ProgressFact = progress;
            Enqueue(item.Session.Id, item.ToolName, item.Narrative.Titles.Running,
                progress ?? WorklogCopy.RunningFallback, DesktopProgressKind.Running, elapsed,
                progress is null ? null : new DesktopProgressEvidence(Summary: progress));
        }
    }

    /// <summary>"TalvoraIntro-20261023-163100.mp4 · 198 KB" or "2 kompozisyon: TalvoraIntro, Logo".</summary>
    private static string? DescribeRemotionResult(TalvoraRemotionStatusResponse status)
    {
        if (status.Compositions.Count > 0)
        {
            return $"{status.Compositions.Count} kompozisyon: " + string.Join(", ", status.Compositions.Take(4));
        }
        if (status.OutputPath is { } output)
        {
            var size = status.OutputBytes is { } bytes
                ? bytes >= 1024 * 1024 ? $" · {bytes / (1024.0 * 1024.0):0.0} MB" : $" · {Math.Max(1, bytes / 1024)} KB"
                : string.Empty;
            return Path.GetFileName(output) + size;
        }
        return status.ProjectRoot is { } project ? Path.GetFileName(project) : null;
    }

    private static string RemotionFailureReason(TalvoraRemotionStatusResponse status) =>
        status.Diagnostics is { } diagnostics &&
        (diagnostics.Contains("Module build failed", StringComparison.Ordinal) ||
         diagnostics.Contains("Syntax Error", StringComparison.Ordinal) ||
         diagnostics.Contains("error TS", StringComparison.Ordinal))
            ? "Animasyon kodu derlenemedi. Ajan hatayı düzeltip yeniden deneyebilir; hiçbir dosya silinmedi."
            : "Animasyon oluşturulamadı. Ayrıntı günlükte; ajan nedeni okuyup yeniden deneyebilir.";
}
