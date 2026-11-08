using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D61: unfinished worklog sessions survive a new session and tray shutdown.</summary>
    private static void AssertD61UnclosedSessionReachesHistory()
    {
        var t0 = new DateTimeOffset(2026, 11, 8, 15, 0, 0, TimeSpan.Zero);
        var directory = Path.Combine(Path.GetTempPath(), "talvora-d61-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "worklog-sessions.jsonl");
            var store = new WorklogHistoryStore(path, () => t0.AddHours(1));
            var open = new WorklogOpenSessions();

            static DesktopProgressMessage Step(
                string id, DateTimeOffset start, int seconds, string title, bool closed = false)
            {
                var end = start.AddSeconds(seconds);
                var frame = new DesktopWorklogFrame(
                    id, seconds, end,
                    new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"),
                    new WorklogStep(WorklogStepCategory.SourceEdit, WorklogStepState.Succeeded,
                        title, null, null, 1),
                    [new WorklogTimelineEntry(end, WorklogStepState.Succeeded, title, 1)],
                    new WorklogTotals(2, seconds, 0, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                    null, 15);
                return new DesktopProgressMessage(
                    "d61-" + title, "talvora_apply_patch", title, "",
                    DesktopProgressKind.Completed, end, 1, null,
                    WorklogFrame: frame, SessionClosed: closed);
            }

            var first = Step("d61-same-id", t0, 10, "İlk adım");
            var latest = Step("d61-same-id", t0, 20, "Son adım");
            if (open.Observe(first).Count != 0 || open.Observe(latest).Count != 0)
                throw new InvalidOperationException("D61: an open session must not be written on each step.");

            var next = Step("d61-same-id", t0.AddSeconds(40), 10, "Yeni oturum");
            var displaced = open.Observe(next);
            if (displaced.Count != 1 || displaced[0].TimestampUtc != t0.AddSeconds(20))
                throw new InvalidOperationException("D61: the newer session must flush the last step of the previous one.");
            store.AppendTerminal(displaced[0], unfinished: true);
            var saved = new WorklogHistoryStore(path, () => t0.AddHours(1)).LoadRecent();
            if (saved.Count != 1 || saved[0].StartedAtUtc != t0 ||
                saved[0].EndedAtUtc != t0.AddSeconds(20) || saved[0].Current.Title != "Son adım")
                throw new InvalidOperationException("D61: unfinished history must preserve the original start and latest end.");

            store.AppendTerminal(displaced[0] with { SessionClosed = true });
            if (store.LoadRecent().Count != 1)
                throw new InvalidOperationException("D61: a later normal close must replace, never duplicate, history.");

            if (open.Observe(next with { SessionClosed = true }).Count != 0)
                throw new InvalidOperationException("D61: a closed session must not flush itself.");
            var remaining = Step("d61-another-id", t0.AddMinutes(2), 5, "Açık oturum");
            if (open.Observe(remaining).Count != 0)
                throw new InvalidOperationException("D61: a new open session must remain in memory.");
            var drained = open.Drain();
            if (drained.Count != 1 || drained[0].WorklogFrame?.SessionId != "d61-another-id" ||
                open.Drain().Count != 0)
                throw new InvalidOperationException("D61: tray shutdown must flush each remaining session exactly once.");
            store.AppendTerminal(drained[0], unfinished: true);
            if (new WorklogHistoryStore(path, () => t0.AddHours(1)).LoadRecent().Count != 2)
                throw new InvalidOperationException("D61: a drained open session must reach durable history.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Console.WriteLine("D61_UNCLOSED_SESSION_REACHES_HISTORY_GREEN");
    }
}
