using System.Diagnostics;
using System.Reflection;
using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    private static void AssertD99FocusDecisions()
    {
        string? visible = null;
        void Step(string session, DesktopProgressKind kind, bool closed, bool shouldShow)
        {
            var message = new DesktopProgressMessage(
                "d99-" + session, "tool", "Step", "", kind,
                DateTimeOffset.UtcNow, 0,
                WorklogFrame: WorklogTestFrames.Running(session, null),
                SessionClosed: closed);
            var actual = WorklogCardFocus.ShouldPresent(visible, message);
            if (actual != shouldShow)
                throw new InvalidOperationException(
                    $"D99 focus: {visible ?? "(none)"} → {session}, " +
                    $"{kind}, closed={closed}: expected {shouldShow}, got {actual}.");
            if (actual)
                visible = session;
        }

        Step("A", DesktopProgressKind.Started, false, true);
        Step("A", DesktopProgressKind.Running, false, true);
        Step("B", DesktopProgressKind.Running, false, false);
        Step("B", DesktopProgressKind.Info, false, false);
        Step("B", DesktopProgressKind.Started, false, true);
        Step("A", DesktopProgressKind.Running, false, false);
        Step("A", DesktopProgressKind.Completed, false, true);
        Step("B", DesktopProgressKind.Completed, true, false);
        Step("B", DesktopProgressKind.Failed, false, true);
        Step("A", DesktopProgressKind.Cancelled, false, true);
        Step("B", DesktopProgressKind.Cancelled, true, false);
        Step("B", DesktopProgressKind.Cancelled, false, true);
        visible = null;
        Step("C", DesktopProgressKind.Running, true, true);
    }

    private static async Task AssertD99CardReuseInChildAsync()
    {
        AssertD99FocusDecisions();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--d99-wpf-only");
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("D99: isolated WPF test did not start.");
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
        if (child.ExitCode != 0 ||
            !(await output).Contains("D99_CARD_REUSE_GREEN", StringComparison.Ordinal))
            throw new InvalidOperationException("D99 RED: WPF card handoff failed: " + await errors);
        Console.WriteLine("D99_CARD_REUSE_GREEN");
    }

    private static async Task AssertD99CardReuseAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-d99-" + Guid.NewGuid().ToString("N"));
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

                DesktopProgressMessage Message(
                    string id, DesktopProgressKind kind, bool sessionClosed = false)
                {
                    var timestamp = DateTimeOffset.UtcNow;
                    var index = ++sequence;
                    var state = kind switch
                    {
                        DesktopProgressKind.Completed => WorklogStepState.Succeeded,
                        DesktopProgressKind.Failed => WorklogStepState.Failed,
                        DesktopProgressKind.Cancelled => WorklogStepState.Cancelled,
                        _ => WorklogStepState.Running,
                    };
                    var evidence = new DesktopProgressEvidence(
                        CodePreview: kind == DesktopProgressKind.Started
                            ? $"@@ FILE {id}.cs\n-old\n+new\n" : null,
                        CodeChangeId: "d99-code-" + id);
                    var frame = new DesktopWorklogFrame(
                        id, index, timestamp,
                        new WorklogSessionHeader("D99", "ChatGPT · Dev"),
                        new WorklogStep(WorklogStepCategory.SourceEdit, state,
                            $"Step {id}", null, null, 1), [],
                        new WorklogTotals(1, 1, 1, WorklogCheckState.Unknown,
                            WorklogCheckState.Unknown), evidence, 30);
                    return new DesktopProgressMessage(
                        "d99-op-" + id, "talvora_apply_patch", frame.Current.Title,
                        "D99 test", kind, timestamp, 1, evidence,
                        Sequence: index, WorklogFrame: frame,
                        SessionClosed: sessionClosed);
                }

                DesktopProgressNotificationWindow? Visible() =>
                    app.Windows.OfType<DesktopProgressNotificationWindow>()
                        .SingleOrDefault(window => window.IsVisible);
                void Expect(DesktopProgressNotificationWindow original, string id, string point)
                {
                    var visible = Visible();
                    if (!ReferenceEquals(original, visible) || visible.SessionId != id)
                        throw new InvalidOperationException(
                            $"D99 RED {point}: expected reused window on {id}, " +
                            $"got {visible?.SessionId ?? "(none)"}; reused={ReferenceEquals(original, visible)}.");
                }

                presenter.Publish(Message("A", DesktopProgressKind.Started));
                var window = Visible()
                    ?? throw new InvalidOperationException("D99: first accepted frame did not open a card.");
                presenter.Publish(Message("A", DesktopProgressKind.Running));
                Expect(window, "A", "same-session running");
                presenter.Publish(Message("B", DesktopProgressKind.Running));
                Expect(window, "A", "other-session running ignored");
                presenter.Publish(Message("B", DesktopProgressKind.Started));
                Expect(window, "B", "other-session start reuses card");
                presenter.Publish(Message("A", DesktopProgressKind.Running));
                Expect(window, "B", "old-session running ignored");
                presenter.Publish(Message("A", DesktopProgressKind.Completed));
                Expect(window, "A", "old-session completion reuses card");
                presenter.Publish(Message("B", DesktopProgressKind.Completed, sessionClosed: true));
                Expect(window, "A", "other-session close ignored");
                if (!history.LoadRecent().Any(session => session.SessionId == "B"))
                    throw new InvalidOperationException("D99: ignored session-close frame lost its history.");

                presenter.Publish(Message("C", DesktopProgressKind.Started));
                Expect(window, "C", "next start reuses original window");
                typeof(DesktopProgressNotificationWindow)
                    .GetField("manualClose", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, true);
                window.Close();
                presenter.Publish(Message("C", DesktopProgressKind.Running));
                if (Visible() is not null)
                    throw new InvalidOperationException("D99: manual close did not suppress current session C.");
                presenter.Publish(Message("A", DesktopProgressKind.Started));
                if (Visible()?.SessionId != "A")
                    throw new InvalidOperationException("D99: manual close suppressed an earlier session.");
            }
            catch (Exception ex) { failure = ex; }
            finally { app?.Shutdown(); done.TrySetResult(true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(90));
        if (!thread.Join(TimeSpan.FromSeconds(2)))
            throw new InvalidOperationException("D99: UI test thread did not exit.");
        try
        {
            if (failure is not null)
                throw new InvalidOperationException("D99 presenter interleaving failed.", failure);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
