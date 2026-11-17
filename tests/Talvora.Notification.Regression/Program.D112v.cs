using Talvora.Shared;
using Talvora.Tray.Worklog;

internal static partial class Program
{
    /// <summary>
    /// D112v: the session time ruler stays hidden while it would draw fewer than two blocks (measured finished steps
    /// plus a running block), so a one-step card does not repeat its headline; it appears with the second block.
    /// </summary>
    private static void AssertD112vRulerNeedsTwoBlocks()
    {
        static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("D112v ruler: " + reason);
        }

        var now = new DateTimeOffset(2026, 11, 1, 13, 20, 5, TimeSpan.Zero);
        WorklogTimelineEntry Measured(double endedSecondsAgo, double seconds, string label) =>
            new(now.AddSeconds(-endedSecondsAgo), WorklogStepState.Succeeded, label, seconds);
        WorklogStep Finished(string title) =>
            new(WorklogStepCategory.Verification, WorklogStepState.Succeeded, title, null, null, 10);
        WorklogStep Running(string title, double elapsed) =>
            new(WorklogStepCategory.Verification, WorklogStepState.Running, title, null, null, elapsed);
        DesktopWorklogFrame Frame(WorklogStep current, int steps, params WorklogTimelineEntry[] newestFirst) =>
            new("d112v", 1, now, new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"), current, newestFirst,
                new WorklogTotals(steps, 60, 0, WorklogCheckState.Unknown, WorklogCheckState.Unknown), null, 30);
        WorklogCardViewModel Apply(DesktopWorklogFrame frame, WorklogCardViewModel? model = null)
        {
            model ??= new WorklogCardViewModel();
            WorklogCardMapper.Apply(model, frame, "MSI", stale: false);
            return model;
        }

        var oneFinishedFrame = Frame(Finished("Derleme başarılı"), 1, Measured(5, 10, "Derleme başarılı"));
        var runningFrame = Frame(
            Running("Testler çalışıyor", 12), 2,
            new WorklogTimelineEntry(now, WorklogStepState.Running, "Testler çalışıyor"),
            Measured(20, 10, "Derleme başarılı"));

        // (a) one finished measured step: one block, ruler hidden, no legend.
        var one = Apply(oneFinishedFrame);
        Require(one.Signal.Count == 1, $"(a) Signal keeps its one block, got {one.Signal.Count}");
        Require(!one.HasSignal, "(a) one finished measured step must not show the ruler");
        Require(one.SignalLegend is null, "(a) one finished measured step has no legend");

        // (b) only a running step: one block, ruler hidden, no legend.
        var onlyRunning = Apply(Frame(Running("Testler çalışıyor", 12), 1));
        Require(onlyRunning.Signal.Count == 1, $"(b) Signal keeps the running block, got {onlyRunning.Signal.Count}");
        Require(!onlyRunning.HasSignal, "(b) a lone running step must not show the ruler");
        Require(onlyRunning.SignalLegend is null, "(b) a lone running step has no legend");

        // (c) one finished measured step plus a running step: two blocks, ruler and legend shown.
        var finishedAndRunning = Apply(runningFrame);
        Require(finishedAndRunning.HasSignal, "(c) a finished step plus a running one shows the ruler");
        Require(finishedAndRunning.SignalLegend?.Work.Contains("1 adım · 1 sürüyor", StringComparison.Ordinal) == true,
            $"(c) legend counts one finished and one running step, got '{finishedAndRunning.SignalLegend?.Work}'");

        // (d) two finished measured steps: ruler and legend shown.
        var two = Apply(Frame(Finished("Testler geçti"), 2,
            Measured(5, 10, "Testler geçti"),
            Measured(20, 10, "Derleme başarılı")));
        Require(two.HasSignal, "(d) two finished measured steps show the ruler");
        Require(two.SignalLegend?.Work.Contains("2 adım", StringComparison.Ordinal) == true,
            $"(d) legend counts two steps, got '{two.SignalLegend?.Work}'");

        // (e) one measured and one unmeasured finished step: one block, ruler hidden.
        var mixed = Apply(Frame(Finished("Testler geçti"), 2,
            new WorklogTimelineEntry(now.AddSeconds(-5), WorklogStepState.Succeeded, "Testler geçti"),
            Measured(20, 10, "Derleme başarılı")));
        Require(!mixed.HasSignal, "(e) an unmeasured step is no block: the ruler stays hidden");
        Require(mixed.SignalLegend is null, "(e) no legend for the hidden ruler");

        // (f) the second block arrives on one model: HasSignal changes and the ruler appears.
        var model = new WorklogCardViewModel();
        Apply(oneFinishedFrame, model);
        Require(!model.HasSignal, "(f) the first frame leaves the ruler hidden");
        var changes = new List<string?>();
        model.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Apply(runningFrame, model);
        Require(changes.Contains(nameof(WorklogCardViewModel.HasSignal)), "(f) HasSignal raises a change notification");
        Require(model.HasSignal, "(f) the ruler appears when the second block arrives");

        Console.WriteLine("D112V_RULER_TWO_BLOCKS_GREEN");
    }
}
