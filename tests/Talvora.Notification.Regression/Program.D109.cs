using System.Diagnostics;
using System.Reflection;
using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    private static void AssertD109SessionTabs()
    {
        var start = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

        static DesktopProgressMessage Message(
            string session, long sequence, WorklogStepState state,
            DesktopProgressKind kind = DesktopProgressKind.Running,
            string? project = "Talvora-MCP", string actor = "ChatGPT · Dev",
            int expectedNextUpdateSeconds = 15, bool sessionClosed = false)
        {
            var frame = WorklogTestFrames.Running(session, null, sequence) with
            {
                Header = new WorklogSessionHeader(project, actor),
                Current = WorklogTestFrames.Running(session, null, sequence).Current
                    with { State = state },
                ExpectedNextUpdateSeconds = expectedNextUpdateSeconds,
            };
            return new DesktopProgressMessage(
                "d109-op-" + session, "talvora_apply_patch", "D109", "Test",
                kind, frame.TimestampUtc, 0,
                Lane: DesktopProgressLane.Worklog, Sequence: sequence,
                WorklogFrame: frame, SessionClosed: sessionClosed);
        }

        static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("D109 tabs: " + reason);
        }

        static WorklogSessionTab Find(IReadOnlyList<WorklogSessionTab> tabs, string session) =>
            tabs.Single(tab => tab.SessionId == session);

        var registry = new WorklogSessionTabs();
        var a = Message("A", 1, WorklogStepState.Running, project: "ilk-proje", actor: "Codex");
        var b = Message("B", 1, WorklogStepState.Waiting, project: "ikinci-proje", actor: "Claude");
        var codeA = start.AddSeconds(-6);
        registry.Observe(a, start, codeA);
        registry.Observe(b, start.AddSeconds(1), null);
        var initial = registry.Snapshot(start.AddSeconds(2), "A");
        Require(initial.Count == 2 && initial[0].Index == 0 && initial[0].SessionId == "A" &&
                initial[1].Index == 1 && initial[1].SessionId == "B",
            "first arrival must get index zero and the next index one");
        Require(initial[0].IsShown && !initial[1].IsShown, "shown session is marked");
        Require(Find(initial, "A").ProjectName == "ilk-proje" &&
                Find(initial, "A").ActorName == "Codex" &&
                Find(initial, "B").ProjectName == "ikinci-proje" &&
                Find(initial, "B").ActorName == "Claude",
            "header project and actor must be preserved");
        Require(Find(initial, "B").State == WorklogSessionTabState.Running,
            "Waiting must map to Running");

        Require(registry.TryGetNewest("A", out var newestA, out var newestCode) &&
                ReferenceEquals(newestA, a) && newestCode == codeA,
            "newest message must preserve reference and code-change time");
        Require(!registry.TryGetNewest("absent", out _, out _), "unknown session must not resolve");
        var stale = Message("A", 0, WorklogStepState.Failed, project: "yanlis-proje");
        registry.Observe(stale, start.AddSeconds(3), start.AddSeconds(3));
        Require(registry.TryGetNewest("A", out newestA, out newestCode) &&
                ReferenceEquals(newestA, a) && newestCode == codeA,
            "an older sequence must not replace the newest message or its code time");
        registry.Observe(Message("A", 1, WorklogStepState.Waiting, project: "esit-sekans"),
            start.AddSeconds(4), start.AddSeconds(4));
        var equal = Find(registry.Snapshot(start.AddSeconds(5), "A"), "A");
        Require(equal.ProjectName == "esit-sekans" && equal.State == WorklogSessionTabState.Running,
            "equal sequence must refresh newest frame");
        Require(registry.TryGetNewest("A", out newestA, out newestCode) &&
                newestCode == start.AddSeconds(4), "equal sequence must refresh code-change time");
        registry.Observe(Message("A", 2, WorklogStepState.Succeeded),
            start.AddSeconds(6), null);
        Require(Find(registry.Snapshot(start.AddSeconds(6), "A"), "A").State ==
                WorklogSessionTabState.Succeeded, "Succeeded maps to Succeeded");
        registry.Observe(Message("A", 3, WorklogStepState.Failed), start.AddSeconds(7), null);
        Require(Find(registry.Snapshot(start.AddSeconds(7), "A"), "A").State ==
                WorklogSessionTabState.Failed, "Failed maps to Failed");
        registry.Observe(Message("A", 4, WorklogStepState.Cancelled),
            start.AddSeconds(8), null);
        Require(Find(registry.Snapshot(start.AddSeconds(8), "A"), "A").State ==
                WorklogSessionTabState.Cancelled, "Cancelled maps to Cancelled");

        registry.Observe(Message("A", 5, WorklogStepState.Running, sessionClosed: true),
            start.AddSeconds(9), null);
        Require(registry.Snapshot(start.AddSeconds(10), "B").Any(tab => tab.SessionId == "A"),
            "SessionClosed alone must not forget a running tab");
        registry.Observe(Message("A", 6, WorklogStepState.Succeeded),
            start.AddMinutes(1), null);
        Require(registry.Snapshot(start.AddMinutes(4), "B").Any(tab => tab.SessionId == "A"),
            "a succeeded tab expires after, not at, three minutes");
        var pastSuccess = registry.Snapshot(start.AddMinutes(4).AddSeconds(1), "B");
        Require(pastSuccess.Count == 1 && pastSuccess[0].SessionId == "B" &&
                pastSuccess[0].Index == 1, "successful session must leave and preserve B index");
        Require(!registry.TryGetNewest("A", out _, out _),
            "expired session is forgotten by Snapshot");

        registry.Observe(Message("C", 1, WorklogStepState.Running),
            start.AddMinutes(4).AddSeconds(2), null);
        var reused = registry.Snapshot(start.AddMinutes(4).AddSeconds(3), "C");
        Require(reused.Count == 2 && reused[0].SessionId == "C" && reused[0].Index == 0 &&
                reused[1].SessionId == "B" && reused[1].Index == 1,
            "new session must reuse the lowest free index and order snapshot by Index");

        var ignoredLane = Message("ignored", 1, WorklogStepState.Running) with
        {
            Lane = DesktopProgressLane.Alert,
        };
        var ignoredFrame = Message("frameless", 1, WorklogStepState.Running) with
        {
            WorklogFrame = null,
        };
        registry.Observe(ignoredLane, start, null);
        registry.Observe(ignoredFrame, start, null);
        Require(registry.Snapshot(start.AddMinutes(4).AddSeconds(4), "C").Count == 2,
            "alert-lane or frameless records must be ignored");
        Require(!registry.TryGetNewest("ignored", out _, out _) &&
                !registry.TryGetNewest("frameless", out _, out _),
            "ignored records must not be in registry");

        var expiry = new WorklogSessionTabs();
        expiry.Observe(Message("fast", 1, WorklogStepState.Running,
            expectedNextUpdateSeconds: 4), start, null);
        Require(Find(expiry.Snapshot(start.AddSeconds(20), null), "fast").State ==
                WorklogSessionTabState.Running,
            "expected next update has a minimum 10 seconds times two");
        Require(Find(expiry.Snapshot(start.AddSeconds(21), null), "fast").State ==
                WorklogSessionTabState.Silent,
            "running becomes silent once stale budget is exceeded");
        expiry.Observe(Message("slow", 1, WorklogStepState.Running,
            expectedNextUpdateSeconds: 45), start.AddSeconds(30), null);
        Require(Find(expiry.Snapshot(start.AddSeconds(120), null), "slow").State ==
                WorklogSessionTabState.Running,
            "long next update overrides 10-second minimum");
        Require(Find(expiry.Snapshot(start.AddSeconds(121), null), "slow").State ==
                WorklogSessionTabState.Silent,
            "long update becomes silent only past double expected time");

        var lifetimes = new WorklogSessionTabs();
        lifetimes.Observe(Message("success", 1, WorklogStepState.Succeeded), start, null);
        lifetimes.Observe(Message("failure", 1, WorklogStepState.Failed), start, null);
        lifetimes.Observe(Message("cancel", 1, WorklogStepState.Cancelled), start, null);
        lifetimes.Observe(Message("silent", 1, WorklogStepState.Running), start, null);
        lifetimes.Observe(Message("visible", 1, WorklogStepState.Succeeded), start, null);
        Require(lifetimes.Snapshot(start.AddMinutes(3), "visible").Count == 5,
            "exact three-minute success threshold must not evict");
        Require(!lifetimes.Snapshot(start.AddMinutes(3).AddSeconds(1), "visible")
                .Any(tab => tab.SessionId == "success"), "success lifetime is three minutes");
        Require(!lifetimes.Snapshot(start.AddMinutes(5).AddSeconds(1), "visible")
                .Any(tab => tab.SessionId == "cancel"), "cancelled lifetime is five minutes");
        Require(lifetimes.Snapshot(start.AddMinutes(8), "visible")
                .Any(tab => tab.SessionId == "failure"), "failed lifetime is eight minutes");
        var remaining = lifetimes.Snapshot(start.AddMinutes(8).AddSeconds(1), "visible");
        Require(remaining.Count == 1 && remaining[0].SessionId == "visible" &&
                remaining[0].IsShown,
            "failed/silent expire after eight minutes but shown tab never leaves");
        Require(!lifetimes.Snapshot(start.AddHours(1), "never-observed")
                .Any(tab => tab.SessionId == "never-observed"),
            "unobserved shown id never creates a tab");
        Console.WriteLine("D109_SESSION_TABS_GREEN");
    }

    private static async Task AssertD109ShowSessionInChildAsync()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--d109-wpf-only");
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("D109: isolated WPF test did not start.");
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
        if (child.ExitCode != 0 ||
            !(await output).Contains("D109_SHOW_SESSION_GREEN", StringComparison.Ordinal))
            throw new InvalidOperationException("D109: WPF session switch failed: " + await errors);
        Console.WriteLine("D109_SHOW_SESSION_GREEN");
    }

    private static async Task AssertD109ShowSessionAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-d109-" + Guid.NewGuid().ToString("N"));
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
                        Header = new WorklogSessionHeader("d109-proje-" + id, "ChatGPT · Dev"),
                    };
                    return new DesktopProgressMessage(
                        "d109-op-" + id, "talvora_apply_patch", frame.Current.Title,
                        "D109 regression", kind, DateTimeOffset.UtcNow, 1,
                        Sequence: index, WorklogFrame: frame);
                }

                DesktopProgressNotificationWindow? Visible() =>
                    app.Windows.OfType<DesktopProgressNotificationWindow>()
                        .SingleOrDefault(window => window.IsVisible);
                void Expect(DesktopProgressNotificationWindow window, string id, string step)
                {
                    var actual = Visible();
                    if (!ReferenceEquals(actual, window) || actual?.SessionId != id)
                        throw new InvalidOperationException(
                            $"D109 {step}: expected same window on {id}, got " +
                            $"{actual?.SessionId ?? "(none)"}.");
                }

                presenter.Publish(Message("A", DesktopProgressKind.Started));
                var original = Visible()
                    ?? throw new InvalidOperationException("D109: initial session A did not open.");
                presenter.Publish(Message("B", DesktopProgressKind.Running));
                Expect(original, "A", "background heartbeat");
                var tabs = presenter.GetSessionTabs(DateTimeOffset.UtcNow);
                if (tabs.Count != 2 ||
                    tabs.Single(tab => tab.SessionId == "B").IsShown ||
                    !tabs.Single(tab => tab.SessionId == "A").IsShown)
                    throw new InvalidOperationException("D109: both sessions were not registered.");
                if (!presenter.ShowSession("B"))
                    throw new InvalidOperationException("D109: switching to B was refused.");
                Expect(original, "B", "manual switch");
                if (presenter.ShowSession("B") || presenter.ShowSession("unknown"))
                    throw new InvalidOperationException("D109: same/unknown session must not switch.");
                Expect(original, "B", "same or unknown target");

                presenter.Publish(Message("A", DesktopProgressKind.Running));
                Expect(original, "B", "old session heartbeat");
                presenter.Publish(Message("A", DesktopProgressKind.Started));
                Expect(original, "A", "D99 Started takes the card back");
            }
            catch (Exception ex) { failure = ex; }
            finally { app?.Shutdown(); done.TrySetResult(true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(90));
        if (!thread.Join(TimeSpan.FromSeconds(2)))
            throw new InvalidOperationException("D109: UI test thread did not exit.");
        try
        {
            if (failure is not null)
                throw new InvalidOperationException("D109: presenter session switching failed.", failure);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
