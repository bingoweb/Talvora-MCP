using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Talvora.Shared;
using Talvora.Tray;
using Talvora.Tray.Worklog;

internal static partial class Program
{
    private static void AssertD109vWindowList()
    {
        static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("D109v window list: " + reason);
        }

        static WorklogSessionTab Tab(
            int index, string? project, string actor, WorklogSessionTabState state, bool shown = false) =>
            new(index, "session-" + index, project, actor, state, shown);

        static void Entry(
            WorklogWindowEntry entry, int index, string text, bool shown, int rank, string spoken, string? help)
        {
            Require(entry.Index == index && entry.Text == text && entry.IsShown == shown && entry.Rank == rank &&
                    entry.AutomationName == spoken && entry.HelpText == help,
                $"entry {index} was ({entry.Index}, '{entry.Text}', {entry.IsShown}, {entry.Rank}, " +
                $"'{entry.AutomationName}', '{entry.HelpText}'), expected ({index}, '{text}', {shown}, {rank}, " +
                $"'{spoken}', '{help}')");
        }

        const string Switch = "Kartı bu oturuma geçirir";

        // Fallback: a session the tray has not observed is never invented.
        var fallback = WorklogCardMapper.MapWindowList([], "Classroom");
        Require(fallback.Entries.Count == 1 && fallback.TotalSessions == 1, "fallback has one entry");
        Entry(fallback.Entries[0], 0, "0:Classroom*", true, 0, "Classroom, bu kart", null);
        Require(fallback.Entries[0].SessionId == "", "fallback entry has an empty session id");
        Require(fallback.AutomationName == "Açık oturumlar: Classroom, bu kart", "fallback list name");
        Require(WorklogCardMapper.MapWindowList([], null).Entries[0].Text == "0:talvora*", "null project is talvora");
        Require(WorklogCardMapper.MapWindowList([], "   ").Entries[0].Text == "0:talvora*", "blank project is talvora");
        Require(WorklogCardMapper.MapWindowList([], " Kalem ").Entries[0].Text == "0:Kalem*", "project is trimmed");
        var longFallback = WorklogCardMapper.MapWindowList([], "abcdefghijklmnopqrst").Entries[0];
        Require(longFallback.Text == "0:abcdefghijklmno…*" && longFallback.AutomationName == "abcdefghijklmnopqrst, bu kart",
            "the fallback name is cut like every entry, the spoken name never");
        var unshown = WorklogCardMapper.MapWindowList(
            [Tab(0, "baska", "Codex", WorklogSessionTabState.Running)], "Kalem");
        Require(unshown.Entries.Count == 1 && unshown.TotalSessions == 1 &&
                unshown.Entries[0].Text == "0:Kalem*" && unshown.Entries[0].SessionId == "",
            "tabs without a shown one fall back to the shown project");

        // The four flags, the shown one, and every state word.
        var states = WorklogCardMapper.MapWindowList(
        [
            Tab(0, "Talvora-MCP", "ChatGPT · Dev", WorklogSessionTabState.Failed, shown: true),
            Tab(1, "fatura-hesabi", "codex", WorklogSessionTabState.Running),
            Tab(2, "Kalem", "claude", WorklogSessionTabState.Silent),
            Tab(3, "Classroom", "codex", WorklogSessionTabState.Failed),
        ], "Talvora-MCP");
        Require(states.Entries.Count == 4 && states.TotalSessions == 4, "four tabs give four entries");
        Entry(states.Entries[0], 0, "0:Talvora-MCP*", true, 0, "Talvora-MCP, bu kart", null);
        Entry(states.Entries[1], 1, "1:fatura-hesabi#", false, 1, "fatura-hesabi, sürüyor", Switch);
        Entry(states.Entries[2], 2, "2:Kalem~", false, 2, "Kalem, sessiz", Switch);
        Entry(states.Entries[3], 3, "3:Classroom!", false, 3, "Classroom, hata verdi", Switch);
        Require(states.Entries[1].SessionId == "session-1", "entry keeps its session id");
        Require(states.AutomationName ==
                "Açık oturumlar: Talvora-MCP, bu kart; fatura-hesabi, sürüyor; Kalem, sessiz; Classroom, hata verdi",
            "list name: " + states.AutomationName);

        var quiet = WorklogCardMapper.MapWindowList(
        [
            Tab(0, "Talvora-MCP", "Codex", WorklogSessionTabState.Running, shown: true),
            Tab(1, "ders", "Codex", WorklogSessionTabState.Succeeded),
            Tab(2, "plan", "Codex", WorklogSessionTabState.Cancelled),
        ], "Talvora-MCP");
        Entry(quiet.Entries[1], 1, "1:ders", false, 1, "ders, tamamlandı", Switch);
        Entry(quiet.Entries[2], 2, "2:plan", false, 2, "plan, iptal edildi", Switch);
        Require(quiet.AutomationName ==
                "Açık oturumlar: Talvora-MCP, bu kart; ders, tamamlandı; plan, iptal edildi",
            "quiet list name: " + quiet.AutomationName);

        // A project-less tab is talvora; blank and padded names are trimmed.
        var nameless = WorklogCardMapper.MapWindowList(
        [
            Tab(0, null, "Codex", WorklogSessionTabState.Running, shown: true),
            Tab(1, "  Kalem ", "Codex", WorklogSessionTabState.Running),
        ], null);
        Require(nameless.Entries[0].Text == "0:talvora*" && nameless.Entries[1].Text == "1:Kalem#" &&
                nameless.Entries[1].AutomationName == "Kalem, sürüyor",
            "a missing project is talvora, a padded project is trimmed");
        var blank = WorklogCardMapper.MapWindowList(
        [
            Tab(0, "Kalem", "Codex", WorklogSessionTabState.Running, shown: true),
            Tab(1, "   ", "Codex", WorklogSessionTabState.Running),
        ], "Kalem");
        Require(blank.Entries[1].Text == "1:talvora#" && blank.Entries[1].AutomationName == "talvora, sürüyor",
            "a blank project is talvora");

        // Same name: the actor's first word, in the display after the cut and in the spoken name.
        var twins = WorklogCardMapper.MapWindowList(
        [
            Tab(0, "Talvora-MCP", "ChatGPT · Dev", WorklogSessionTabState.Running, shown: true),
            Tab(1, "Talvora-MCP", "Claude", WorklogSessionTabState.Running),
            Tab(2, "talvora-mcp", "", WorklogSessionTabState.Succeeded),
            Tab(3, "Kalem", "Codex", WorklogSessionTabState.Running),
        ], "Talvora-MCP");
        Entry(twins.Entries[0], 0, "0:Talvora-MCP·chatgpt*", true, 0, "Talvora-MCP (chatgpt), bu kart", null);
        Entry(twins.Entries[1], 1, "1:Talvora-MCP·claude#", false, 1, "Talvora-MCP (claude), sürüyor", Switch);
        Entry(twins.Entries[2], 2, "2:talvora-mcp", false, 3, "talvora-mcp, tamamlandı", Switch);
        Entry(twins.Entries[3], 3, "3:Kalem#", false, 2, "Kalem, sürüyor", Switch);

        // 16 characters stay, 20 characters end in an ellipsis; the spoken name is never cut.
        var cut = WorklogCardMapper.MapWindowList(
        [
            Tab(0, "abcdefghijklmnop", "Codex", WorklogSessionTabState.Running, shown: true),
            Tab(1, "abcdefghijklmnopqrst", "Codex", WorklogSessionTabState.Succeeded),
        ], "abcdefghijklmnop");
        Entry(cut.Entries[0], 0, "0:abcdefghijklmnop*", true, 0, "abcdefghijklmnop, bu kart", null);
        Entry(cut.Entries[1], 1, "1:abcdefghijklmno…", false, 1, "abcdefghijklmnopqrst, tamamlandı", Switch);
        var cutTwins = WorklogCardMapper.MapWindowList(
        [
            Tab(0, "abcdefghijklmnopqrst", "Codex", WorklogSessionTabState.Running, shown: true),
            Tab(1, "abcdefghijklmnopqrst", "Claude", WorklogSessionTabState.Succeeded),
        ], "abcdefghijklmnopqrst");
        Require(cutTwins.Entries[0].Text == "0:abcdefghijklmno…·codex*" &&
                cutTwins.Entries[0].AutomationName == "abcdefghijklmnopqrst (codex), bu kart" &&
                cutTwins.Entries[1].AutomationName == "abcdefghijklmnopqrst (claude), tamamlandı",
            "the actor is appended after the cut: " + cutTwins.Entries[0].Text);

        // The cut counts text elements: a decomposed letter (g + U+0306) and an emoji are never split.
        const string Breve = "ğ";
        var decomposed = WorklogCardMapper.MapWindowList(
        [
            Tab(0, "abcdefghijklmn" + Breve + "xyzuvw", "Codex", WorklogSessionTabState.Running, shown: true),
            Tab(1, "abcdefghijklmno" + Breve, "Codex", WorklogSessionTabState.Succeeded),
            Tab(2, "abcdefghijklmn😀pqrstu", "Codex", WorklogSessionTabState.Succeeded),
            Tab(3, "abcdefghijklmno😀", "Codex", WorklogSessionTabState.Succeeded),
        ], "abcdefghijklmn" + Breve + "xyzuvw");
        Require(decomposed.Entries[0].Text == "0:abcdefghijklmn" + Breve + "…*",
            "the 15th text element is a decomposed letter and stays whole: " + decomposed.Entries[0].Text);
        Require(decomposed.Entries[1].Text == "1:abcdefghijklmno" + Breve,
            "16 text elements (17 chars) are not cut: " + decomposed.Entries[1].Text);
        Require(decomposed.Entries[2].Text == "2:abcdefghijklmn😀…",
            "an emoji at the cut stays whole: " + decomposed.Entries[2].Text);
        Require(decomposed.Entries[3].Text == "3:abcdefghijklmno😀",
            "an emoji as the 16th text element is not cut: " + decomposed.Entries[3].Text);
        Require(decomposed.Entries[0].AutomationName == "abcdefghijklmn" + Breve + "xyzuvw, bu kart",
            "the spoken name stays uncut");
        var decomposedFallback = WorklogCardMapper.MapWindowList([], "abcdefghijklmn" + Breve + "xyzuvw").Entries[0];
        Require(decomposedFallback.Text == "0:abcdefghijklmn" + Breve + "…*",
            "the fallback cut counts text elements: " + decomposedFallback.Text);
        Require(WorklogCardMapper.MapWindowList([], "abcdefghijklmn😀pqrstu").Entries[0].Text == "0:abcdefghijklmn😀…*",
            "the fallback keeps an emoji whole");
        Require(WorklogCardMapper.MapWindowList([], "abcdefghijklmno" + Breve).Entries[0].Text ==
                "0:abcdefghijklmno" + Breve + "*",
            "the fallback does not cut 16 text elements");

        // Seven sessions: the shown one and three others (Running, Silent, Failed, rest; ties by index).
        var seven = WorklogCardMapper.MapWindowList(
        [
            Tab(0, "p0", "Codex", WorklogSessionTabState.Succeeded, shown: true),
            Tab(1, "p1", "Codex", WorklogSessionTabState.Succeeded),
            Tab(2, "p2", "Codex", WorklogSessionTabState.Failed),
            Tab(3, "p3", "Codex", WorklogSessionTabState.Silent),
            Tab(4, "p4", "Codex", WorklogSessionTabState.Cancelled),
            Tab(5, "p5", "Codex", WorklogSessionTabState.Running),
            Tab(6, "p6", "Codex", WorklogSessionTabState.Running),
        ], "p0");
        Require(seven.TotalSessions == 7, "seven tabs count seven");
        Require(seven.Entries.Select(entry => entry.Index).SequenceEqual([0, 3, 5, 6]) &&
                seven.Entries.Select(entry => entry.Rank).SequenceEqual([0, 3, 1, 2]),
            "choice and ranks: " + string.Join(",", seven.Entries.Select(entry => $"{entry.Index}/{entry.Rank}")));
        Require(seven.Entries[0].Text == "0:p0*" && seven.Entries[1].Text == "3:p3~" &&
                seven.Entries[2].Text == "5:p5#" && seven.Entries[3].Text == "6:p6#",
            "texts of the chosen entries");
        Require(seven.AutomationName ==
                "Açık oturumlar: p0, bu kart; p1, tamamlandı; p2, hata verdi; p3, sessiz; p4, iptal edildi; " +
                "p5, sürüyor; p6, sürüyor",
            "the list name keeps every tab: " + seven.AutomationName);
        var rest = WorklogCardMapper.MapWindowList(
        [
            Tab(0, "p0", "Codex", WorklogSessionTabState.Running, shown: true),
            Tab(1, "p1", "Codex", WorklogSessionTabState.Succeeded),
            Tab(2, "p2", "Codex", WorklogSessionTabState.Cancelled),
            Tab(3, "p3", "Codex", WorklogSessionTabState.Succeeded),
            Tab(4, "p4", "Codex", WorklogSessionTabState.Failed),
        ], "p0");
        Require(rest.Entries.Select(entry => entry.Index).SequenceEqual([0, 1, 2, 4]) &&
                rest.Entries.Select(entry => entry.Rank).SequenceEqual([0, 2, 3, 1]),
            "failed first, then the rest by index: " +
            string.Join(",", rest.Entries.Select(entry => $"{entry.Index}/{entry.Rank}")));

        // Signature: equal lists share one, a different flag changes it.
        var again = WorklogCardMapper.MapWindowList(
        [
            Tab(0, "p0", "Codex", WorklogSessionTabState.Succeeded, shown: true),
            Tab(1, "p1", "Codex", WorklogSessionTabState.Succeeded),
            Tab(2, "p2", "Codex", WorklogSessionTabState.Failed),
            Tab(3, "p3", "Codex", WorklogSessionTabState.Silent),
            Tab(4, "p4", "Codex", WorklogSessionTabState.Cancelled),
            Tab(5, "p5", "Codex", WorklogSessionTabState.Running),
            Tab(6, "p6", "Codex", WorklogSessionTabState.Running),
        ], "p0");
        Require(again.Signature == seven.Signature && !string.IsNullOrEmpty(seven.Signature),
            "an identical list has the same signature");
        Require(states.Signature != quiet.Signature && fallback.Signature != states.Signature,
            "different lists have different signatures");
        var flipped = WorklogCardMapper.MapWindowList(
        [
            Tab(0, "p0", "Codex", WorklogSessionTabState.Succeeded, shown: true),
            Tab(1, "p1", "Codex", WorklogSessionTabState.Succeeded),
            Tab(2, "p2", "Codex", WorklogSessionTabState.Failed),
            Tab(3, "p3", "Codex", WorklogSessionTabState.Running),
            Tab(4, "p4", "Codex", WorklogSessionTabState.Cancelled),
            Tab(5, "p5", "Codex", WorklogSessionTabState.Running),
            Tab(6, "p6", "Codex", WorklogSessionTabState.Running),
        ], "p0");
        Require(flipped.Signature != seven.Signature, "a changed flag changes the signature");

        // Apply fills the view model: its own project without tabs, the list with them.
        var model = new WorklogCardViewModel();
        var changes = new List<string?>();
        model.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        var frame = WorklogTestFrames.Running("d109v-apply", null);
        WorklogCardMapper.Apply(model, frame, "MSI", stale: false);
        Require(model.WindowList.Entries.Count == 1 && model.WindowList.Entries[0].Text == "0:Talvora-MCP*",
            "Apply without tabs shows the frame's own project");
        Require(changes.Contains("WindowList"), "WindowList raises change notification");
        WorklogCardMapper.Apply(model, frame, "MSI", stale: false,
            sessionTabs:
            [
                Tab(0, "Talvora-MCP", "Codex", WorklogSessionTabState.Running, shown: true),
                Tab(1, "Kalem", "Claude", WorklogSessionTabState.Failed),
            ]);
        Require(model.WindowList.Entries.Select(entry => entry.Text).SequenceEqual(["0:Talvora-MCP*", "1:Kalem!"]),
            "Apply with tabs shows them");
        Console.WriteLine("D109V_WINDOW_LIST_GREEN");
    }

    private static void RunD109vSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60)))
            throw new InvalidOperationException("D109v panel: the STA thread did not exit.");
        if (failure is not null)
            throw new InvalidOperationException("D109v panel fit failed.", failure);
    }

    private static void AssertD109vPanelFit()
    {
        RunD109vSta(() =>
        {
            static void Require(bool condition, string reason)
            {
                if (!condition) throw new InvalidOperationException("D109v panel: " + reason);
            }

            static (WorklogWindowListPanel Panel, Border[] Children) Build(
                int total, params (int Rank, double Width)[] items)
            {
                var panel = new WorklogWindowListPanel { TotalCount = total };
                var children = items.Select(item =>
                {
                    var child = new Border { Width = item.Width, Height = 16 };
                    WorklogWindowListPanel.SetRank(child, item.Rank);
                    panel.Children.Add(child);
                    return child;
                }).ToArray();
                return (panel, children);
            }

            static void Layout(WorklogWindowListPanel panel, double width)
            {
                panel.Measure(new Size(width, 24));
                panel.Arrange(new Rect(0, 0, width, 24));
            }

            Require(WorklogWindowListPanel.GetRank(new Border()) == int.MaxValue, "default rank is int.MaxValue");

            // Index order: ranks 0, 2, 1, 3 with widths 60, 40, 50, 30 (sum 180).
            var items = new (int Rank, double Width)[] { (0, 60), (2, 40), (1, 50), (3, 30) };

            var (wide, wideChildren) = Build(6, items);
            Layout(wide, 400);
            Require(wide.VisibleCount == 4 && wide.HiddenCount == 2,
                $"wide space shows all four and hides TotalCount minus four, got {wide.VisibleCount}/{wide.HiddenCount}");
            Require(wideChildren.All(wide.IsVisibleChild), "every child is visible in wide space");
            var expectedX = new[] { 0d, 60, 100, 150 };
            for (var i = 0; i < 4; i++)
            {
                var offset = VisualTreeHelper.GetOffset(wideChildren[i]);
                Require(Math.Abs(offset.X - expectedX[i]) < 0.01 && Math.Abs(offset.Y - 4) < 0.01,
                    $"child {i} sits at ({offset.X}, {offset.Y}), expected ({expectedX[i]}, 4): index order, centred");
            }

            var (exact, _) = Build(4, items);
            Layout(exact, 180);
            Require(exact.VisibleCount == 4 && exact.HiddenCount == 0,
                "all children fit exactly when nothing is hidden and no more-text is reserved");

            // Narrow: the two lowest ranks (children 0 and 2) fit with the reserved more-text (~17 DIP).
            var (narrow, narrowChildren) = Build(6, items);
            Layout(narrow, 140);
            Require(narrow.VisibleCount == 2 && narrow.HiddenCount == 4,
                $"narrow space keeps two, got {narrow.VisibleCount}/{narrow.HiddenCount}");
            Require(narrow.IsVisibleChild(narrowChildren[0]) && narrow.IsVisibleChild(narrowChildren[2]) &&
                    !narrow.IsVisibleChild(narrowChildren[1]) && !narrow.IsVisibleChild(narrowChildren[3]),
                "the lowest ranks stay");
            var first = VisualTreeHelper.GetOffset(narrowChildren[0]);
            var third = VisualTreeHelper.GetOffset(narrowChildren[2]);
            Require(first.X == 0 && Math.Abs(third.X - 60) < 0.01 &&
                    LayoutInformation.GetLayoutSlot(narrowChildren[1]).Width == 0 &&
                    LayoutInformation.GetLayoutSlot(narrowChildren[3]).Width == 0,
                "kept children stay in index order without overlap, the others take no room");

            // The more-text is reserved: 110 DIP would hold two children alone, but not two plus "+4".
            var (reserved, _) = Build(6, items);
            Layout(reserved, 114);
            Require(reserved.VisibleCount == 1 && reserved.HiddenCount == 5,
                $"the more-text is reserved, got {reserved.VisibleCount}/{reserved.HiddenCount}");

            // Below the shown child's width only the shown child stays.
            var (tiny, tinyChildren) = Build(6, items);
            Layout(tiny, 40);
            Require(tiny.VisibleCount == 1 && tiny.IsVisibleChild(tinyChildren[0]) &&
                    !tinyChildren.Skip(1).Any(tiny.IsVisibleChild),
                "only the shown child stays below its width");
            Require(tiny.DesiredSize.Width <= 40.01, "the panel never asks for more than it is given");

            var (alone, aloneChildren) = Build(1, (0, 60));
            Layout(alone, 400);
            Require(alone.VisibleCount == 1 && alone.HiddenCount == 0 && alone.IsVisibleChild(aloneChildren[0]),
                "a single entry shows no more-text");

            var panelName = new WorklogWindowListPanel();
            AutomationProperties.SetName(panelName, "Açık oturumlar: x, bu kart");
            var peer = UIElementAutomationPeer.CreatePeerForElement(panelName);
            Require(peer.GetAutomationControlType() == AutomationControlType.Group &&
                    peer.GetClassName() == "WorklogWindowList" &&
                    peer.GetName() == "Açık oturumlar: x, bu kart",
                "automation peer is a Group named WorklogWindowList with the list name");
            Require(panelName.MoreOpacity == 0.7, "more-text opacity defaults to 0.7");
        });
        Console.WriteLine("D109V_PANEL_FIT_GREEN");
    }

    private static async Task AssertD109vSwitchInChildAsync()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--d109v-wpf-only");
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("D109v: isolated WPF test did not start.");
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
        if (child.ExitCode != 0 ||
            !(await output).Contains("D109V_SWITCH_GREEN", StringComparison.Ordinal))
            throw new InvalidOperationException("D109v: WPF window list regression failed: " + await errors);
        Console.WriteLine("D109V_SWITCH_GREEN");
    }

    private static T? D109vFind<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed && match(typed)) return typed;
            if (D109vFind(child, match) is { } deeper) return deeper;
        }

        return null;
    }

    private static void D109vPumpUntil(Func<bool> done, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            if (done() || clock.Elapsed > timeout)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private sealed class D109vFakeSource : IWorklogSessionSource
    {
        internal IReadOnlyList<WorklogSessionTab> Tabs { get; set; } = [];

        internal List<string?> ShownRequests { get; } = [];

        internal List<string> Switched { get; } = [];

        internal bool Throw { get; set; }

        public IReadOnlyList<WorklogSessionTab> GetSessionTabs(DateTimeOffset nowUtc, string? shownSessionId)
        {
            ShownRequests.Add(shownSessionId);
            return Tabs;
        }

        public bool ShowSession(string sessionId)
        {
            Switched.Add(sessionId);
            if (Throw) throw new InvalidOperationException("fake switch failure");
            return true;
        }
    }

    private static async Task AssertD109vSwitchAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-d109v-" + Guid.NewGuid().ToString("N"));
        var done = NewSignal();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            ControlCenterApplication? app = null;
            try
            {
                SettingsPreferencesStore.Initialize(smokeMode: true);
                app = new ControlCenterApplication(smokeTest: true);
                var history = new WorklogHistoryStore(Path.Combine(root, "history.jsonl"));
                using var presenter = new DesktopProgressNotificationPresenter(
                    app, Path.Combine(root, "placement.json"), history);
                var sequence = 0L;
                DesktopProgressMessage Message(string id, DesktopProgressKind kind)
                {
                    var index = ++sequence;
                    var frame = WorklogTestFrames.Running(id, null, index) with
                    {
                        Header = new WorklogSessionHeader("d109v-" + id, "ChatGPT · Dev"),
                    };
                    return new DesktopProgressMessage(
                        "d109v-op-" + id, "talvora_apply_patch", frame.Current.Title,
                        "D109v regression", kind, DateTimeOffset.UtcNow, 1,
                        Sequence: index, WorklogFrame: frame);
                }

                DesktopProgressNotificationWindow? Visible() =>
                    app.Windows.OfType<DesktopProgressNotificationWindow>()
                        .SingleOrDefault(window => window.IsVisible);
                void Require(bool condition, string reason)
                {
                    if (!condition) throw new InvalidOperationException("D109v: " + reason);
                }

                WorklogWindowListPanel PanelOf(DesktopProgressNotificationWindow window) =>
                    ((WorklogCardView)window.Content).WindowListPanel;
                string[] Texts(DesktopProgressNotificationWindow window) =>
                    PanelOf(window).Children.OfType<Button>()
                        .Select(button => ((TextBlock)button.Content).Text).ToArray();
                Button EntryButton(DesktopProgressNotificationWindow window, string text) =>
                    PanelOf(window).Children.OfType<Button>()
                        .Single(button => ((TextBlock)button.Content).Text == text);
                void Expect(DesktopProgressNotificationWindow window, string id, string step)
                {
                    var actual = Visible();
                    Require(ReferenceEquals(actual, window) && actual?.SessionId == id,
                        $"{step}: expected same window on {id}, got {actual?.SessionId ?? "(none)"}");
                }

                var windowType = typeof(DesktopProgressNotificationWindow);
                const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

                // 1. The card lists A (shown) and B (observed), and a click on B's entry switches the card.
                presenter.Publish(Message("A", DesktopProgressKind.Started));
                var card = Visible() ?? throw new InvalidOperationException("D109v: session A did not open.");
                presenter.Publish(Message("B", DesktopProgressKind.Running));
                presenter.Publish(Message("A", DesktopProgressKind.Running));
                Expect(card, "A", "background heartbeat");
                Require(Texts(card).SequenceEqual(["0:d109v-A*", "1:d109v-B#"]),
                    "the panel lists A shown and B running: " + string.Join(" ", Texts(card)));
                Require(AutomationProperties.GetName(PanelOf(card)) ==
                        "Açık oturumlar: d109v-A, bu kart; d109v-B, sürüyor",
                    "list automation name: " + AutomationProperties.GetName(PanelOf(card)));
                var entryB = EntryButton(card, "1:d109v-B#");
                var entryA = EntryButton(card, "0:d109v-A*");
                Require(AutomationProperties.GetName(entryB) == "d109v-B, sürüyor" &&
                        AutomationProperties.GetHelpText(entryB) == "Kartı bu oturuma geçirir" &&
                        AutomationProperties.GetName(entryA) == "d109v-A, bu kart" &&
                        AutomationProperties.GetHelpText(entryA) == "",
                    "entry automation names and help text");
                Require(ReferenceEquals(entryB.Cursor, Cursors.Hand) && ReferenceEquals(entryA.Cursor, Cursors.Arrow) &&
                        ((TextBlock)entryA.Content).FontWeight == FontWeights.SemiBold &&
                        ((TextBlock)entryA.Content).Opacity == 1.0 &&
                        ((TextBlock)entryB.Content).FontWeight == FontWeights.Normal &&
                        ((TextBlock)entryB.Content).Opacity == 0.7,
                    "cursor, weight and opacity of shown and other entries");
                Require(WorklogWindowListPanel.GetRank(entryA) == 0 && WorklogWindowListPanel.GetRank(entryB) == 1,
                    "ranks are attached to the buttons");

                var moved = 0;
                card.UserMoveCompleted += (_, _) => moved++;
                var isInteractive = windowType.GetMethod(
                    "IsInteractiveElement", BindingFlags.Static | BindingFlags.NonPublic)!;
                Require((bool)isInteractive.Invoke(null, [entryB.Content])! &&
                        (bool)isInteractive.Invoke(null, [entryA.Content])! &&
                        !(bool)isInteractive.Invoke(null, [PanelOf(card)])!,
                    "a press on an entry never starts the card's drag or open path");

                entryB.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Expect(card, "B", "click on B's entry");
                Require(Texts(card).SequenceEqual(["0:d109v-A#", "1:d109v-B*"]),
                    "after the switch B is shown and A running: " + string.Join(" ", Texts(card)));
                var before = Texts(card);
                EntryButton(card, "1:d109v-B*").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Expect(card, "B", "click on the shown entry");
                Require(Texts(card).SequenceEqual(before), "a click on the shown entry changes nothing");
                Require(moved == 0, "no click raised the card's move/open path");

                // 2. D109 review gap (a): B's card closed by the user's close path, then shown again by a click.
                var close = D109vFind<Button>(card, button => AutomationProperties.GetName(button) == "Kapat")
                    ?? throw new InvalidOperationException("D109v: the close button was not found.");
                close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                D109vPumpUntil(() => !card.IsVisible && Visible() is null, TimeSpan.FromSeconds(10));
                Require(Visible() is null, "the user's close path closes the card");
                presenter.Publish(Message("A", DesktopProgressKind.Started));
                var second = Visible() ?? throw new InvalidOperationException("D109v: A did not reopen the card.");
                Expect(second, "A", "A reopens the card");
                Require(presenter.ShowSession("B"), "B's entry switches after its card was closed by the user");
                Expect(second, "B", "switch after the user closed B");
                var heartbeat = Message("B", DesktopProgressKind.Running);
                presenter.Publish(heartbeat);
                Expect(second, "B", "B heartbeat after the switch");
                Require(((DesktopWorklogFrame)windowType.GetField("lastFrame", Private)!.GetValue(second)!).Sequence ==
                        heartbeat.Sequence,
                    "a B Running frame reaches the card after the user's close and the switch");

                // 3. D109 review gap (b): a tab past its lifetime is refused and changes nothing.
                Require(!presenter.ShowSession("A", DateTimeOffset.UtcNow.AddMinutes(9)),
                    "a silent tab past its eight minutes must not be shown");
                Expect(second, "B", "refused switch");

                // 4. The window on its own: stale at once, lifetimes counted from the receive time.
                var alone = new DesktopProgressNotificationWindow();
                var aloneModel = (WorklogCardViewModel)((WorklogCardView)alone.Content).DataContext;
                var running = WorklogTestFrames.Running("d109v-alone", null) with { ExpectedNextUpdateSeconds = 10 };
                alone.Update(running, receivedUtc: DateTimeOffset.UtcNow.AddMinutes(-5));
                Require(aloneModel.State == WorklogVisualState.Waiting && aloneModel.Title == "Yanıt bekleniyor",
                    "a running frame received five minutes ago is stale at once");
                alone.Update(running);
                Require(aloneModel.State == WorklogVisualState.Running, "a fresh frame is not stale");
                alone.Update(running, receivedUtc: DateTimeOffset.UtcNow.AddSeconds(-5));
                Require(aloneModel.State == WorklogVisualState.Running, "a frame received five seconds ago is not stale");

                var dismissField = windowType.GetField("dismissAfter", Private)!;
                TimeSpan Dismiss() => (TimeSpan)dismissField.GetValue(alone)!;
                DesktopWorklogFrame Terminal(WorklogStepState state) =>
                    running with { Current = running.Current with { State = state } };
                alone.Update(Terminal(WorklogStepState.Succeeded));
                Require(Dismiss() == WorklogSessionTabs.SucceededLifetime, "success lifetime without a receive time");
                alone.Update(Terminal(WorklogStepState.Failed));
                Require(Dismiss() == WorklogSessionTabs.FailedLifetime, "failure lifetime without a receive time");
                alone.Update(Terminal(WorklogStepState.Cancelled));
                Require(Dismiss() == WorklogSessionTabs.CancelledLifetime, "cancel lifetime without a receive time");
                alone.Update(Terminal(WorklogStepState.Succeeded), receivedUtc: DateTimeOffset.UtcNow.AddMinutes(-2));
                Require(Dismiss() <= TimeSpan.FromMinutes(1) && Dismiss() > TimeSpan.FromSeconds(50),
                    "a success received two minutes ago has about one minute left, got " + Dismiss());
                alone.Update(Terminal(WorklogStepState.Failed), receivedUtc: DateTimeOffset.UtcNow.AddMinutes(-3));
                Require(Dismiss() <= TimeSpan.FromMinutes(5) && Dismiss() > TimeSpan.FromMinutes(4.5),
                    "a failure received three minutes ago has about five minutes left, got " + Dismiss());
                alone.Update(Terminal(WorklogStepState.Succeeded), receivedUtc: DateTimeOffset.UtcNow.AddMinutes(-10));
                Require(Dismiss() == TimeSpan.FromSeconds(1), "a frame past its lifetime stays at least one second");
                // A clock set back makes "now - received" negative: the remaining time is never longer than the lifetime.
                alone.Update(Terminal(WorklogStepState.Succeeded), receivedUtc: DateTimeOffset.UtcNow.AddHours(1));
                Require(Dismiss() == WorklogSessionTabs.SucceededLifetime,
                    "a success received an hour in the future keeps its full lifetime, got " + Dismiss());
                alone.Update(Terminal(WorklogStepState.Failed), receivedUtc: DateTimeOffset.UtcNow.AddHours(1));
                Require(Dismiss() == WorklogSessionTabs.FailedLifetime,
                    "a failure received an hour in the future keeps its full lifetime, got " + Dismiss());

                // 5. The window reads its tabs from the source on Update and on the stale tick, and a click asks it.
                var source = new D109vFakeSource
                {
                    Tabs =
                    [
                        new WorklogSessionTab(0, "d109v-alone", "Talvora-MCP", "Codex", WorklogSessionTabState.Running, true),
                        new WorklogSessionTab(1, "other", "Kalem", "Claude", WorklogSessionTabState.Failed, false),
                    ],
                };
                alone.SessionSource = source;
                alone.Update(Terminal(WorklogStepState.Succeeded));
                Require(source.ShownRequests[^1] == "d109v-alone", "the tabs are asked for the session the window shows");
                Require(Texts(alone).SequenceEqual(["0:Talvora-MCP*", "1:Kalem!"]), "Update applies the source's tabs");
                // The shown entry (`*`) is not a switch: a click on it asks the source nothing.
                EntryButton(alone, "0:Talvora-MCP*").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Require(source.Switched.Count == 0 && alone.SessionId == "d109v-alone",
                    "a click on the shown entry switches nothing: " + string.Join(",", source.Switched));
                source.Tabs =
                [
                    new WorklogSessionTab(0, "d109v-alone", "Talvora-MCP", "Codex", WorklogSessionTabState.Running, true),
                    new WorklogSessionTab(1, "other", "Kalem", "Claude", WorklogSessionTabState.Running, false),
                ];
                windowType.GetMethod("CheckForStaleWork", Private)!.Invoke(alone, null);
                Require(Texts(alone).SequenceEqual(["0:Talvora-MCP*", "1:Kalem#"]),
                    "the stale tick refreshes the list even for a terminal card");
                EntryButton(alone, "1:Kalem#").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Require(source.Switched.SequenceEqual(["other"]), "a click asks the source to show that session");
                source.Throw = true;
                EntryButton(alone, "1:Kalem#").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Require(source.Switched.Count == 2, "a failing switch is swallowed after being asked");
                alone.Close();

                // 6. A switch shows the stored frame at once: no typing, no row-in, while every other update keeps its motion.
                DesktopProgressMessage Custom(
                    string id, string title, string rowLabel, DesktopProgressKind kind,
                    WorklogStepState state = WorklogStepState.Running)
                {
                    var index = ++sequence;
                    var template = WorklogTestFrames.Running(id, null, index);
                    var frame = template with
                    {
                        Header = new WorklogSessionHeader("d109v-" + id, "ChatGPT · Dev"),
                        Current = template.Current with { Title = title, State = state },
                        Timeline = [new WorklogTimelineEntry(DateTimeOffset.UtcNow, WorklogStepState.Succeeded, rowLabel, 1.0)],
                        Totals = new WorklogTotals(2, 5, 1, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                    };
                    return new DesktopProgressMessage(
                        "d109v-op-" + id, "talvora_apply_patch", title, "D109v regression", kind,
                        DateTimeOffset.UtcNow, 1, Sequence: index, WorklogFrame: frame);
                }

                void Flush() => D109vPumpUntil(() => false, TimeSpan.FromMilliseconds(400));
                // Controls that expect motion to play run only where it can (no client-area animation or high contrast: none plays).
                var motionPlays = SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
                if (!motionPlays) Console.WriteLine("D109V_MOTION_CONTROLS_SKIPPED");
                string Typed(WorklogCardView view) => view.TitleMainRun.Text + view.TitleDetailRun.Text;
                FrameworkElement? FirstRow(WorklogCardView view) =>
                    view.History.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;

                presenter.Publish(Custom("M1", "Birinci adım · a.cs", "eski satır", DesktopProgressKind.Started));
                presenter.Publish(Custom("M2", "İkinci adım · b.cs", "yeni satır", DesktopProgressKind.Started));
                var motionCard = Visible() ?? throw new InvalidOperationException("D109v: the motion card did not open.");
                Expect(motionCard, "M2", "M2 takes the card");
                var motionView = (WorklogCardView)motionCard.Content;
                var motionModel = (WorklogCardViewModel)motionView.DataContext;
                Flush();
                // Control: an ordinary update with a new first row still plays the row-in.
                presenter.Publish(Custom("M2", "İkinci adım · b.cs", "ikinci yeni satır", DesktopProgressKind.Running));
                Flush();
                if (motionPlays) Require(FirstRow(motionView)?.RenderTransform is TranslateTransform,
                    "an ordinary update keeps the row-in motion (control)");
                // Control: an ordinary title change still types in.
                presenter.Publish(Custom("M2", "Üçüncü adım · c.cs", "ikinci yeni satır", DesktopProgressKind.Running));
                if (motionPlays) Require(Typed(motionView) != motionModel.TitleMain + motionModel.TitleDetail,
                    "an ordinary title change still types in (control)");
                Flush();

                Require(presenter.ShowSession("M1"), "M1 is switched to");
                Expect(motionCard, "M1", "switch to M1");
                Require(motionModel.Title == "Birinci adım · a.cs", "the card holds M1's title");
                Require(Typed(motionView) == motionModel.TitleMain + motionModel.TitleDetail,
                    "right after a switch the title is fully shown, not typing: '" + Typed(motionView) + "'");
                Flush();
                Require(Typed(motionView) == motionModel.TitleMain + motionModel.TitleDetail,
                    "the switched title stays whole");
                var switchedRow = FirstRow(motionView);
                Require(switchedRow is not null && switchedRow.RenderTransform is not TranslateTransform,
                    "a switch plays no row-in and counts no arrival");

                // 6b. A switch to a finished session plays no transition moment (sweep, failure jitter).
                presenter.Publish(Custom("M3", "Bitti adım · g.cs", "satır g", DesktopProgressKind.Completed, WorklogStepState.Succeeded));
                if (motionPlays) Require(motionView.SweepBand.Opacity == 1, "an ordinary success plays the sweep (control)");
                Flush();
                motionView.SweepShift.BeginAnimation(TranslateTransform.XProperty, null);
                motionView.SweepBand.Opacity = 0;
                presenter.Publish(Custom("M4", "Sürüyor · h.cs", "satır h", DesktopProgressKind.Started));
                Flush();
                Require(presenter.ShowSession("M3"), "M3 (succeeded) is switched to");
                Expect(motionCard, "M3", "switch to M3");
                Require(motionModel.State == WorklogVisualState.Succeeded, "the card holds M3's succeeded state");
                Require(motionView.SweepBand.Opacity == 0 && !motionView.SweepShift.HasAnimatedProperties,
                    "a switch to a succeeded session plays no sweep");

                presenter.Publish(Custom("M5", "Hata adım · i.cs", "satır i", DesktopProgressKind.Failed, WorklogStepState.Failed));
                if (motionPlays) Require(motionView.CardShift.HasAnimatedProperties, "an ordinary failure plays the jitter (control)");
                Flush();
                motionView.CardShift.BeginAnimation(TranslateTransform.XProperty, null);
                motionView.StatusBar.BeginAnimation(UIElement.OpacityProperty, null);
                presenter.Publish(Custom("M6", "Sürüyor · j.cs", "satır j", DesktopProgressKind.Started));
                Flush();
                Require(presenter.ShowSession("M5"), "M5 (failed) is switched to");
                Expect(motionCard, "M5", "switch to M5");
                Require(motionModel.State == WorklogVisualState.Failed, "the card holds M5's failed state");
                Require(!motionView.CardShift.HasAnimatedProperties && !motionView.StatusBar.HasAnimatedProperties,
                    "a switch to a failed session plays no jitter");

                // 6c. A switch while the previous title is still typing, to a frame with the same title: shown whole.
                const string SameTitle = "Aynı başlık · k.cs";
                presenter.Publish(Custom("M8", SameTitle, "satır k", DesktopProgressKind.Started));
                Flush();
                presenter.Publish(Custom("M10", "Önceki adım · l.cs", "satır l", DesktopProgressKind.Started));
                Flush();
                presenter.Publish(Custom("M9", SameTitle, "satır m", DesktopProgressKind.Started));
                Expect(motionCard, "M9", "M9 takes the card");
                if (motionPlays) Require(Typed(motionView) != motionModel.TitleMain + motionModel.TitleDetail,
                    "the title of M9 is still typing (control)");
                Require(presenter.ShowSession("M8"), "M8 (same title) is switched to");
                Expect(motionCard, "M8", "switch to M8");
                Require(motionModel.Title == SameTitle &&
                        Typed(motionView) == motionModel.TitleMain + motionModel.TitleDetail,
                    "a switch settles a title that is still typing: '" + Typed(motionView) + "'");

                // 7. A number freed by a session that left goes to the next new session (Snapshot before Observe).
                presenter.Dispose();
                var clockNow = DateTimeOffset.UtcNow;
                using var tabsPresenter = new DesktopProgressNotificationPresenter(
                    app, Path.Combine(root, "placement-tabs.json"),
                    new WorklogHistoryStore(Path.Combine(root, "history-tabs.jsonl")))
                {
                    Clock = () => clockNow.AddMinutes(-4),
                };
                tabsPresenter.Publish(Custom("X", "Bitti", "satır", DesktopProgressKind.Completed, WorklogStepState.Succeeded));
                var finishedCard = Visible() ?? throw new InvalidOperationException("D109v: X did not open a card.");
                Require(tabsPresenter.GetSessionTabs(clockNow.AddMinutes(-4)).Single().Index == 0, "X holds number 0");
                finishedCard.Close();
                Require(Visible() is null, "no card is visible once X's card closed");
                tabsPresenter.Clock = () => clockNow;
                tabsPresenter.Publish(Custom("C", "Yeni", "satır", DesktopProgressKind.Started));
                var afterwards = tabsPresenter.GetSessionTabs(clockNow);
                Require(afterwards.Count == 1 && afterwards[0].SessionId == "C" && afterwards[0].Index == 0,
                    "X (a success past its three minutes) is gone and C took number 0: " +
                    string.Join(",", afterwards.Select(tab => $"{tab.SessionId}/{tab.Index}")));
            }
            catch (Exception ex) { failure = ex; }
            finally { app?.Shutdown(); done.TrySetResult(true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(90));
        if (!thread.Join(TimeSpan.FromSeconds(2)))
            throw new InvalidOperationException("D109v: UI test thread did not exit.");
        try
        {
            if (failure is not null)
                throw new InvalidOperationException("D109v: the card's window list failed.", failure);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
