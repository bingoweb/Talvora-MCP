using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Talvora.Shared;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    /// <summary>WL3: the history list and its detail column show only what a session really has.</summary>
    private static void AssertWl3HistoryPolish(ShellWindow window, string variant)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "talvora-wl3-smoke-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        window.ShellModel.SelectPage(2);
        var previous = window.HistoryContentHost.Content;
        try
        {
            var store = new WorklogHistoryStore(System.IO.Path.Combine(directory, "worklog-sessions.jsonl"));
            var now = DateTimeOffset.UtcNow;
            AppendWl3Session(store, now.AddMinutes(-9), "wl3-note", "D79 — Bitmiş başlıklar",
                new WorklogStep(WorklogStepCategory.SourceEdit, WorklogStepState.Succeeded,
                    "Proje talimatları alındı", null, new WorklogOutcome(null, "", null), 2),
                new WorklogTotals(1, 2, 0, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                null, DesktopProgressKind.Completed);
            AppendWl3Session(store, now.AddMinutes(-4), "wl3-failed", null,
                new WorklogStep(WorklogStepCategory.Verification, WorklogStepState.Failed,
                    "Testler başarısız · Talvora.Notification.Regression", "1 test başarısız",
                    new WorklogOutcome(WorklogFailureKind.TestFailures,
                        "Bir test beklenen sonucu vermedi.", "Hatalı testi düzeltip yeniden çalıştır."), 12),
                new WorklogTotals(3, 40, 2, WorklogCheckState.Succeeded, WorklogCheckState.Failed),
                new DesktopProgressEvidence(Summary: "2 dosya değişti", Files: ["src/Talvora/WorklogSubject.cs"]),
                DesktopProgressKind.Failed);

            var page = new Views.WorklogHistoryView(store);
            page.Reload();
            window.HistoryContentHost.Content = page;
            window.UpdateLayout();
            var list = FindVisualChild<System.Windows.Controls.ListBox>(page)
                ?? throw new InvalidOperationException("WL3: the history list is missing.");
            var note = page.Model.Sessions.Single(session => session.SessionId == "wl3-note");
            var failed = page.Model.Sessions.Single(session => session.SessionId == "wl3-failed");
            var noteCard = (FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(note);
            var failedCard = (FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(failed);

            // Item 11: a task note is the card's headline; the last step moves under it.
            var noteTexts = VisibleTexts(noteCard);
            if (!noteTexts.Contains("D79 — Bitmiş başlıklar") ||
                !noteTexts.Contains("Son adım: Proje talimatları alındı"))
                throw new InvalidOperationException("WL3: a session with a task note must list the note and its last step.");
            // Item 10: no "0 dosya değişti"; item 2: one caption line with the outcome dot.
            if (noteTexts.Contains(" dosya değişti") || !VisibleTexts(failedCard).Contains(" dosya değişti"))
                throw new InvalidOperationException("WL3: the files part must show only when files changed.");
            AssertWl3Dot(noteCard, "Tv.Ready", "completed");
            AssertWl3Dot(failedCard, "Tv.Offline", "failed");
            SaveVisualSnapshotIfRequested(window, "wl3-list-" + variant);

            // Items 1, 7 and 8: an empty line leaves no hole; a failure says why and what next.
            list.SelectedItem = note;
            window.UpdateLayout();
            var detail = FindVisualChildren<ScrollViewer>(page).First(viewer => viewer.IsVisible && viewer.Content is Border);
            var empty = FindVisualChildren<TextBlock>(detail).Where(text =>
                text.IsVisible && string.IsNullOrEmpty(text.Text) && text.ActualHeight > 0).ToArray();
            if (empty.Length > 0)
                throw new InvalidOperationException($"WL3: the detail column shows {empty.Length} empty line(s).");
            if (VisibleTexts(detail).Contains("Kanıtlar"))
                throw new InvalidOperationException("WL3: a session without evidence must not show the evidence heading.");
            if (VisibleTexts(detail).Any(text => text.StartsWith("Sonraki adım", StringComparison.Ordinal)))
                throw new InvalidOperationException("WL3: a session without a next step must not print 'Sonraki adım'.");
            SaveVisualSnapshotIfRequested(window, "wl3-detail-note-" + variant);
            list.SelectedItem = failed;
            window.UpdateLayout();
            var failedDetail = VisibleTexts(detail);
            if (!failedDetail.Contains("Bir test beklenen sonucu vermedi.") ||
                !failedDetail.Contains("Sonraki adım: Hatalı testi düzeltip yeniden çalıştır.") ||
                !failedDetail.Contains("Kanıtlar"))
                throw new InvalidOperationException("WL3: a failed session must show its reason, next step and evidence.");
            SaveVisualSnapshotIfRequested(window, "wl3-detail-failed-" + variant);

            // Item 9: an empty search result says so instead of "nothing recorded yet".
            page.Model.SearchText = "eşleşmeyen arama";
            window.UpdateLayout();
            if (!VisibleTexts(page).Contains("Aramayla eşleşen çalışma yok."))
                throw new InvalidOperationException("WL3: an empty search must read 'Aramayla eşleşen çalışma yok.'.");
            page.Model.SearchText = "";
            window.UpdateLayout();
        }
        finally
        {
            window.HistoryContentHost.Content = previous;
            window.ShellModel.SelectPage(0);
            window.UpdateLayout();
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    private static void AppendWl3Session(WorklogHistoryStore store, DateTimeOffset at, string id, string? taskNote,
        WorklogStep current, WorklogTotals totals, DesktopProgressEvidence? evidence, DesktopProgressKind kind)
    {
        var frame = new DesktopWorklogFrame(
            id, 2, at,
            new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev", taskNote),
            current,
            [new WorklogTimelineEntry(at, current.State, current.Title, current.ElapsedSeconds)],
            totals, evidence, 15);
        store.AppendTerminal(new DesktopProgressMessage(
            id, "talvora_user_run", current.Title, "", kind, at, current.ElapsedSeconds, evidence,
            WorklogFrame: frame, SessionClosed: true));
    }

    private static HashSet<string> VisibleTexts(DependencyObject root) =>
        FindVisualChildren<TextBlock>(root).Where(text => text.IsVisible).Select(text => text.Text).ToHashSet(StringComparer.Ordinal);

    private static void AssertWl3Dot(FrameworkElement card, string brushKey, string outcome)
    {
        var expected = ((SolidColorBrush)card.FindResource(brushKey)).Color;
        if (!FindVisualChildren<Ellipse>(card).Any(dot => dot.IsVisible && dot.Width == 8 &&
                dot.Fill is SolidColorBrush fill && fill.Color == expected))
            throw new InvalidOperationException($"WL3: a {outcome} session needs an 8 px {brushKey} dot.");
    }
}
