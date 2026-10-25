using System.Reflection;
using System.Diagnostics;
using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    private static async Task AssertN8NotificationsInChildAsync()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--n8-only");
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("N8: isolated WPF process did not start.");
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
        if (child.ExitCode != 0 || !(await output).Contains("N8_NOTIFICATIONS_GREEN", StringComparison.Ordinal))
            throw new InvalidOperationException("N8 isolated presenter regression failed: " + await errors);
        Console.WriteLine("N8_NOTIFICATIONS_GREEN");
    }

    private static async Task AssertN8NotificationsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "Talvora-N8-" + Guid.NewGuid().ToString("N"));
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            ControlCenterApplication? app = null;
            try
            {
                SettingsPreferencesStore.Initialize(smokeMode: true);
                var defaults = SettingsPreferencesStore.Current;
                if (!defaults.ShowCodePreview || defaults.FailureAndAgentOnly || defaults.QuietMode)
                    throw new InvalidOperationException("N8: defaults must be on/off/off.");

                app = new ControlCenterApplication(smokeTest: true);
                var history = new WorklogHistoryStore(Path.Combine(root, "history.jsonl"));
                using var presenter = new DesktopProgressNotificationPresenter(
                    app, Path.Combine(root, "placement.json"), history);

                DesktopProgressNotificationWindow? Window(string id) =>
                    app.Windows.OfType<DesktopProgressNotificationWindow>()
                        .SingleOrDefault(w => w.SessionId == id);
                DesktopWorklogFrame? Frame(DesktopProgressNotificationWindow w) =>
                    (DesktopWorklogFrame?)typeof(DesktopProgressNotificationWindow)
                        .GetField("lastFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(w);
                DesktopProgressMessage Message(string id, string actor, DesktopProgressKind kind,
                    bool terminal = false, long sequence = 1)
                {
                    var frame = WorklogTestFrames.Running(id, "+keep this code();") with
                    {
                        Header = new WorklogSessionHeader("N8", actor),
                        Current = WorklogTestFrames.Running(id, null).Current with
                        {
                            State = kind == DesktopProgressKind.Failed
                                ? WorklogStepState.Failed
                                : kind == DesktopProgressKind.Completed
                                    ? WorklogStepState.Succeeded : WorklogStepState.Running,
                        },
                    };
                    return new DesktopProgressMessage(id, "talvora_apply_patch", frame.Current.Title,
                        "N8 test", kind, DateTimeOffset.UtcNow, 1,
                        Evidence: frame.Evidence, Sequence: sequence, WorklogFrame: frame, SessionClosed: terminal);
                }
                void Set(SettingsPreferences value) =>
                    SettingsPreferencesStore.SetAsync(value).GetAwaiter().GetResult();

                presenter.Publish(Message("default-code", "ChatGPT", DesktopProgressKind.Started));
                var first = Window("default-code")
                    ?? throw new InvalidOperationException("N8: default card was not shown.");
                if (Frame(first)?.Evidence?.CodePreview is null)
                    throw new InvalidOperationException("N8: default preview missing.");

                Set(defaults with { ShowCodePreview = false });
                if (Frame(first)?.Evidence?.CodePreview is null)
                    throw new InvalidOperationException("N8: toggle altered an already visible card.");
                // Audit 21: the next update of an already visible card keeps the preview choice it opened with.
                presenter.Publish(Message("default-code", "ChatGPT", DesktopProgressKind.Running, sequence: 2));
                if (Frame(first)?.Evidence?.CodePreview is null)
                    throw new InvalidOperationException("N8: preview toggle stripped code from a visible card's update.");
                presenter.Publish(Message("no-code", "ChatGPT", DesktopProgressKind.Started));
                if (Window("no-code") is not { } hidden ||
                    Frame(hidden)?.Evidence?.CodePreview is not null)
                    throw new InvalidOperationException("N8: preview-off card still shows code.");

                Set(defaults);
                presenter.Publish(Message("code-on", "ChatGPT", DesktopProgressKind.Started));
                if (Window("code-on") is not { } restored ||
                    Frame(restored)?.Evidence?.CodePreview is null)
                    throw new InvalidOperationException("N8: turning preview back on failed.");

                Set(defaults with { FailureAndAgentOnly = true });
                if (Frame(restored)?.Evidence?.CodePreview is null)
                    throw new InvalidOperationException("N8: filter toggle altered a visible card.");
                restored.Close();
                presenter.Publish(Message("filtered", "ChatGPT", DesktopProgressKind.Completed, terminal: true));
                if (Window("filtered") is not null ||
                    !history.LoadRecent().Any(x => x.SessionId == "filtered"))
                    throw new InvalidOperationException("N8: filtered completion not history-only.");
                presenter.Publish(Message("failed", "ChatGPT", DesktopProgressKind.Failed, terminal: true));
                if (Window("failed") is not { } failedCard)
                    throw new InvalidOperationException("N8: failed card filtered.");
                failedCard.Close();
                presenter.Publish(Message("codex", "Codex", DesktopProgressKind.Started));
                if (Window("codex") is not { } codexCard)
                    throw new InvalidOperationException("N8: Codex agent card filtered.");
                codexCard.Close();
                presenter.Publish(Message("claude", "Claude", DesktopProgressKind.Started));
                if (Window("claude") is not { } claudeCard)
                    throw new InvalidOperationException("N8: Claude agent card filtered.");
                claudeCard.Close();
                Set(defaults);
                presenter.Publish(Message("normal", "ChatGPT", DesktopProgressKind.Started));
                var visible = Window("normal")
                    ?? throw new InvalidOperationException("N8: filter off did not show normal card.");

                Set(defaults with { QuietMode = true, ShowCodePreview = false });
                if (!visible.IsVisible)
                    throw new InvalidOperationException("N8: quiet toggle closed an existing card.");
                // Audit 21: a card that is already visible still receives its own completion while quiet.
                presenter.Publish(Message("normal", "ChatGPT", DesktopProgressKind.Completed, terminal: true, sequence: 2));
                if (Frame(visible)?.Current.State != WorklogStepState.Succeeded)
                    throw new InvalidOperationException("N8: quiet mode froze an already visible card.");
                presenter.Publish(Message("quiet", "Claude", DesktopProgressKind.Failed, terminal: true));
                if (Window("quiet") is not null || !visible.IsVisible ||
                    history.LoadRecent().SingleOrDefault(x => x.SessionId == "quiet")?.Evidence?.CodePreview is null)
                    throw new InvalidOperationException("N8: quiet mode must keep full history only.");

                var persisted = SettingsPreferencesStore.Load(SettingsPreferencesStore.PreferencePath);
                if (!persisted.QuietMode || persisted.ShowCodePreview)
                    throw new InvalidOperationException("N8: settings were not persisted.");
                Set(defaults);
                presenter.Publish(Message("quiet-off", "ChatGPT", DesktopProgressKind.Started));
                if (Window("quiet-off") is null)
                    throw new InvalidOperationException("N8: quiet mode off did not restore cards.");
            }
            catch (Exception ex) { failure = ex; }
            finally { app?.Shutdown(); done.TrySetResult(true); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(90));
        if (!thread.Join(TimeSpan.FromSeconds(2)))
            throw new InvalidOperationException("N8: UI test thread did not exit.");
        try
        {
            if (failure is not null) throw new InvalidOperationException("N8 behaviour failed.", failure);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        Console.WriteLine("N8_NOTIFICATIONS_GREEN");
    }
}
