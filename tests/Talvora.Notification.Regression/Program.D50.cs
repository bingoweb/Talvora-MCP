using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D50: two sessions of the same project and actor are two history entries; a repeated close is one.</summary>
    private static void AssertD50HistoryKeepsEverySession()
    {
        var offset = TimeSpan.FromHours(3);
        var now = new DateTimeOffset(2026, 11, 7, 18, 0, 0, offset);
        var directory = Path.Combine(Path.GetTempPath(), "talvora-d50-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "worklog-sessions.jsonl");
            var store = new WorklogHistoryStore(path, () => now);
            void Close(DateTimeOffset start, string title)
            {
                var closed = start.AddSeconds(93);
                var frame = new DesktopWorklogFrame(
                    "d50-same-project-and-actor", 2, closed,
                    new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"),
                    new WorklogStep(WorklogStepCategory.SourceEdit, WorklogStepState.Succeeded, title, null, null, 3),
                    [new WorklogTimelineEntry(start.AddSeconds(3), WorklogStepState.Succeeded, title, 3)],
                    new WorklogTotals(1, 93, 1, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                    null, 15);
                store.AppendTerminal(new DesktopProgressMessage(
                    "d50", "talvora_apply_patch", title, "", DesktopProgressKind.Completed, closed, 3, null,
                    WorklogFrame: frame, SessionClosed: true));
            }

            Close(new DateTimeOffset(2026, 11, 7, 15, 0, 0, offset), "İlk oturum");
            Close(new DateTimeOffset(2026, 11, 7, 15, 10, 0, offset), "İkinci oturum");
            Close(new DateTimeOffset(2026, 11, 7, 15, 10, 0, offset), "İkinci oturum");
            foreach (var reader in new[] { store, new WorklogHistoryStore(path, () => now) })
            {
                var titles = reader.LoadRecent()
                    .Where(session => session.SessionId == "d50-same-project-and-actor")
                    .Select(session => session.Current.Title)
                    .ToArray();
                if (titles is not ["İkinci oturum", "İlk oturum"])
                    throw new InvalidOperationException(
                        $"D50: expected both sessions newest first, got [{string.Join(", ", titles)}].");
            }
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
        Console.WriteLine("D50_HISTORY_KEEPS_EVERY_SESSION_GREEN");
    }
}
