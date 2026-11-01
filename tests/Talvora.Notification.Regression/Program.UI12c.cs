using Talvora.Shared;
using Talvora.Tray.Worklog;

internal static partial class Program
{
    /// <summary>
    /// UI12c "zaman cetveli": one linear real-time scale; work = union of measured step intervals; the gap is shown
    /// only when every finished step in the window was measured; the running step uses its measured elapsed time.
    /// </summary>
    private static void AssertUi12cTimeRuler()
    {
        var now = new DateTimeOffset(2026, 11, 1, 13, 20, 5, TimeSpan.Zero);
        var start = now.AddSeconds(-545);
        WorklogTimelineEntry Step(double begin, double seconds, WorklogStepState state, string label) =>
            new(start.AddSeconds(begin + seconds), state, label, seconds);
        DesktopWorklogFrame Frame(WorklogStep current, int steps, params WorklogTimelineEntry[] newestFirst) =>
            new("s", 1, now, new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"), current, newestFirst,
                new WorklogTotals(steps, 545, 0, WorklogCheckState.Unknown, WorklogCheckState.Unknown), null, 30);
        var done = new WorklogStep(WorklogStepCategory.Verification, WorklogStepState.Succeeded, "Kalite kontrolü geçti", null, null, 312.4);

        var frame = Frame(done, 4,
            Step(170, 312.4, WorklogStepState.Succeeded, "Kalite kontrolü geçti"),
            Step(140, 18.9, WorklogStepState.Succeeded, "Derleme başarılı"),
            Step(95, 0.3, WorklogStepState.Failed, "Kod değişikliği uygulanamadı"),
            Step(5, 7.6, WorklogStepState.Succeeded, "Testler geçti"));
        var peaks = WorklogCardMapper.BuildSignal(frame);
        if (peaks.Count != 4 || Math.Abs(peaks[0].StartFraction - 5 / 545d) > 1e-6 || Math.Abs(peaks[0].WidthFraction - 7.6 / 545) > 1e-6)
            throw new InvalidOperationException("UI12c: a block must sit on the linear session axis (start and width in real seconds).");
        if (peaks[3].Label != "Kalite kontrolü geçti" || Math.Abs(peaks[3].Seconds - 312.4) > 1e-9 || peaks[1].State != WorklogRowState.Failed)
            throw new InvalidOperationException("UI12c: a block must carry its real label, measured seconds and state.");

        var legend = WorklogCardMapper.BuildSignalLegend(frame);
        if (legend.Work != "iş 5:39 · 4 adım" || legend.Failures != "1 hata" || legend.Gap != "ara 3:26")
            throw new InvalidOperationException($"UI12c: legend must print measured totals, got '{legend.Work}' / '{legend.Failures}' / '{legend.Gap}'.");
        if (legend.StartLocal != start.ToLocalTime().DateTime)
            throw new InvalidOperationException("UI12c: the axis must start at frame time minus the measured session length.");

        var overlap = WorklogCardMapper.BuildSignalLegend(Frame(done, 2,
            Step(20, 20, WorklogStepState.Succeeded, "B"),
            Step(10, 20, WorklogStepState.Succeeded, "A")));
        if (overlap.Work != "iş 30 sn · 2 adım" || overlap.Failures is not null)
            throw new InvalidOperationException($"UI12c: parallel steps must count their union once, got '{overlap.Work}'.");

        var unmeasured = WorklogCardMapper.BuildSignalLegend(Frame(done, 2,
            Step(20, 5, WorklogStepState.Succeeded, "B"),
            new WorklogTimelineEntry(start.AddSeconds(12), WorklogStepState.Succeeded, "A")));
        if (unmeasured.Gap is not null)
            throw new InvalidOperationException("UI12c: with an unmeasured finished step the gap is unknown and must not be shown.");

        var running = Frame(
            new WorklogStep(WorklogStepCategory.Verification, WorklogStepState.Running, "Derleniyor", null, null, 12),
            2,
            new WorklogTimelineEntry(now, WorklogStepState.Running, "Derleniyor"),
            Step(5, 7.6, WorklogStepState.Succeeded, "Testler geçti"));
        var live = WorklogCardMapper.BuildSignal(running);
        if (live.Count != 2 || live[1].State != WorklogRowState.Running || Math.Abs(live[1].StartFraction - 533 / 545d) > 1e-6)
            throw new InvalidOperationException("UI12c: the running step must be drawn from its measured elapsed time up to the frame time.");
        var liveLegend = WorklogCardMapper.BuildSignalLegend(running);
        if (liveLegend.Work != "iş 20 sn · 1 adım · 1 sürüyor" || liveLegend.Gap != "ara 8:45")
            throw new InvalidOperationException($"UI12c: the running step counts with its measured elapsed time, got '{liveLegend.Work}' / '{liveLegend.Gap}'.");

        // UI12d: measured seconds round to the nearest value, never down.
        if (WorklogMeasure.Format(13.7) != "14 sn" || WorklogMeasure.Format(59.6) != "1:00" || WorklogMeasure.Format(0.36) != "0,4 sn")
            throw new InvalidOperationException("UI12d: durations must round to the nearest value.");

        if (WorklogCardMapper.BuildSignal(Frame(done, 1, new WorklogTimelineEntry(now, WorklogStepState.Succeeded, "A"))).Count != 0)
            throw new InvalidOperationException("UI12c: nothing is drawn without a measurement.");
    }
}
