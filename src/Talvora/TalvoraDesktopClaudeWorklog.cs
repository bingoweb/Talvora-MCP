using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    internal void BeginClaudeJob(string jobId)
    {
        var narrative = new OperationNarrative(
            OperationCategory.GenericExecution, WorklogCopy.Claude);
        TryBeginWorklog("claude-" + jobId, "claude_background", narrative, null, TimeSpan.Zero,
            clientName: "Claude");
    }

    internal void UpdateClaudeJob(string jobId, int toolCalls, TimeSpan elapsed)
    {
        lock (_worklogGate)
        {
            if (!_activeWorklog.TryGetValue("claude-" + jobId, out var item)) { return; }
            if (_activeWorklog.Values
                    .Where(active => ReferenceEquals(active.Session, item.Session))
                    .MaxBy(static active => active.Sequence)?.Sequence != item.Sequence) { return; }
            Enqueue(item.Session.Id, item.ToolName, item.Narrative.Titles.Running,
                ComposeWorklogMessage(
                    $"{toolCalls} geliştirme adımı.",
                    null), DesktopProgressKind.Running, elapsed);
        }
    }

    internal void EndClaudeJob(string jobId, string state, TimeSpan elapsed)
    {
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
                _ => "Görev tamamlanamadı; yapılan değişiklikler tamamlanmış sayılmıyor.",
            }, null, elapsed);
    }
}
