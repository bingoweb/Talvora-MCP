using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D45: a session's duration ends with its last step, not with the card's quiet close 90 s later.</summary>
    private static void AssertD45WorkEndsWithLastStep()
    {
        var offset = TimeSpan.FromHours(3);
        var now = new DateTimeOffset(2026, 11, 7, 18, 0, 0, offset);
        var start = new DateTimeOffset(2026, 11, 7, 15, 44, 23, offset);
        var closed = start.AddSeconds(93);
        var directory = Path.Combine(Path.GetTempPath(), "talvora-d45-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new WorklogHistoryStore(Path.Combine(directory, "worklog-sessions.jsonl"), () => now);
            var frame = new DesktopWorklogFrame(
                "d45", 2, closed,
                new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"),
                new WorklogStep(WorklogStepCategory.SourceEdit, WorklogStepState.Succeeded,
                    "Düzenleme tamamlandı.", "1 dosya değişti", null, 3),
                [new WorklogTimelineEntry(start.AddSeconds(3), WorklogStepState.Succeeded, "Düzenleme tamamlandı.", 3)],
                new WorklogTotals(1, 93, 1, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                null, 15);
            store.AppendTerminal(new DesktopProgressMessage(
                "d45", "talvora_apply_patch", "Düzenleme tamamlandı.", "",
                DesktopProgressKind.Completed, closed, 3, null,
                WorklogFrame: frame, SessionClosed: true));
            var session = store.LoadRecent().Single(item => item.SessionId == "d45");
            if (session.WorkEndedAtUtc != start.AddSeconds(3) ||
                LocalTimeText.SessionShort(session.StartedAtUtc, session.WorkEndedAtUtc, now) != "Bugün 15:44 · 3 sn" ||
                LocalTimeText.SessionLong(session.StartedAtUtc, session.WorkEndedAtUtc, now) !=
                    "Bugün 15:44:23 → 15:44:26 · 3 sn")
                throw new InvalidOperationException(
                    $"D45: the work must end with the last step (15:44:26), got {session.WorkEndedAtUtc.ToOffset(offset):HH:mm:ss}.");
            if ((session with { Timeline = [] }).WorkEndedAtUtc != session.EndedAtUtc)
                throw new InvalidOperationException("D45: a session without steps keeps its stored end.");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        Console.WriteLine("D45_WORK_ENDS_WITH_LAST_STEP_GREEN");
    }
}
