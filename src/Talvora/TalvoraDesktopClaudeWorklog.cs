using Talvora.Shared;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    internal void BeginClaudeJob(string jobId)
    {
        var narrative = new OperationNarrative(OperationCategory.GenericExecution,
            "Claude kod üzerinde çalışıyor",
            "Kodları inceliyor, düzenliyor ve sonucu kontrol ediyor.",
            "İstediğiniz kod değişikliğini hazırlamak için.",
            "Claude çalışmasını tamamladı.");
        TryBeginWorklog("claude-" + jobId, "claude_background", narrative, null, TimeSpan.Zero);
    }

    internal void UpdateClaudeJob(string jobId, int toolCalls, TimeSpan elapsed)
    {
        lock (_worklogGate)
        {
            if (!_activeWorklog.TryGetValue("claude-" + jobId, out var item)) { return; }
            if (_activeWorklog.Values.MaxBy(static active => active.Sequence)?.Sequence != item.Sequence) { return; }
            Enqueue(GetCurrentWorklogOperationId(), item.ToolName, item.Narrative.Subject,
                ComposeWorklogMessage(
                    $"Claude çalışmaya devam ediyor. Başlatılan geliştirme adımı: {toolCalls}. Geçen süre: {FormatElapsed(elapsed)}.",
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
        TryEndWorklog("claude-" + jobId, kind, "Claude kod çalışması",
            state switch
            {
                "completed" => "Claude işi tamamladı. Sonucu Talvora üzerinden inceleyebilirsiniz.",
                "cancelled" => "Claude çalışması isteğinizle durduruldu.",
                "timed_out" => "Claude ayrılan sürede bitiremedi. Yapılan değişiklikler tamamlanmış sayılmıyor. Devam etmek için son durum incelenecek.",
                _ => "Claude işi tamamlayamadı. Yapılan değişiklikler tamamlanmış sayılmıyor. Hesap girişi, erişim izinleri ve son durum incelenecek.",
            }, null, elapsed);
    }
}
