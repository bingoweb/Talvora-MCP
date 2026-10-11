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
        var subject = "Codex projeyi gelistiriyor";
        var narrative = new OperationNarrative(
            OperationCategory.GenericExecution,
            subject,
            "Codex kodlari inceliyor, degistiriyor ve dogruluyor.",
            "Uzun suren kod gelistirme gorevini tamamlamak icin.",
            "Codex gelistirme gorevini tamamladi.");
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
            var details =
                $"Codex devam ediyor. {commands} komut, {mcpCalls} MCP cagrisi." +
                $" Gecen sure: {FormatElapsed(elapsed)}.";
            if (!string.IsNullOrWhiteSpace(activity))
            {
                details += Environment.NewLine +
                    FileLog.RedactSensitiveData(activity);
            }
            Enqueue(
                GetCurrentWorklogOperationId(),
                item.ToolName,
                item.Narrative.Subject,
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
        TryEndWorklog(
            "codex-" + jobId, kind,
            "Codex gelistirme gorevi",
            state switch
            {
                "completed" => "Codex gorevi tamamladi. Sonuc Talvora uzerinden incelenebilir.",
                "cancelled" => "Codex gorevi kullanici istegiyle durduruldu.",
                "timed_out" => "Codex gorevi belirlenen zaman sinirina ulasti.",
                _ => "Codex gorevi tamamlanamadi. Son durum Talvora kaydinda.",
            },
            null, elapsed);
    }
}
