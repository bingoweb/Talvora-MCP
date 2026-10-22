using Talvora.Shared;

namespace Talvora;

/// <summary>
/// Keep a Codex background job visible as one desktop worklog operation for
/// its actual lifetime rather than closing its card when the MCP start call
/// returns. Progress updates are coalesced by the existing notifier.
/// </summary>
internal sealed partial class TalvoraDesktopProgressNotifier
{
    internal void BeginCodexJob(string jobId, string workingDirectory)
    {
        var narrative = new OperationNarrative(
            OperationCategory.GenericExecution, WorklogCopy.Codex);
        TryBeginWorklog(
            "codex-" + jobId, "codex_background",
            narrative, null, TimeSpan.Zero);
    }

    internal void UpdateCodexJob(
        string jobId,
        int commands,
        int mcpCalls,
        string? activity,
        TimeSpan elapsed)
    {
        var operationId = "codex-" + jobId;
        lock (_worklogGate)
        {
            if (!_activeWorklog.TryGetValue(operationId, out var item))
            {
                return;
            }
            var latest = _activeWorklog.Values.MaxBy(
                static active => active.Sequence);
            if (latest?.Sequence != item.Sequence) { return; }
            var details = $"{commands} komut, {mcpCalls} araç çağrısı.";
            if (!string.IsNullOrWhiteSpace(activity))
            {
                details += Environment.NewLine +
                    FileLog.RedactSensitiveData(activity);
            }
            Enqueue(
                GetCurrentWorklogOperationId(),
                item.ToolName,
                item.Narrative.Titles.Running,
                ComposeWorklogMessage(details, null),
                DesktopProgressKind.Running,
                elapsed);
        }
    }

    internal void EndCodexJob(
        string jobId, string state, TimeSpan elapsed)
    {
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
            null, elapsed);
    }
}
