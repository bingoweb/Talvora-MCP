using Talvora.Shared;
using Talvora.Tray.Worklog;

internal static partial class Program
{
    /// <summary>
    /// D104v (audit 65; user decision 2026-11-16 "Her geçmiş satırında"): every history row of the desktop card carries
    /// its step's own purpose ("ne"); Talvora's own texts keep a number with its word and never break a short token at
    /// its slash (display only, the stored text is unchanged); the ruler prints "ara" only from one measured second.
    /// </summary>
    private static void AssertD104vCardText()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var current = new WorklogStep(WorklogStepCategory.Verification, WorklogStepState.Failed,
            "Derleme başarısız · Talvora.Tray.csproj · 3 hata", "56 kontrol · 0 hata", null, 18.4);
        var frame = new DesktopWorklogFrame("d104v", 1, now, new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"), current,
            [
                new(now, WorklogStepState.Failed, current.Title, 18.4, "Derleme hatası görülüyor"),
                new(now.AddSeconds(-30), WorklogStepState.Succeeded, "Komut tamamlandı", 0.7, "Defender kayıtları kontrol ediliyor"),
                new(now.AddSeconds(-60), WorklogStepState.Succeeded, "Uzak değişiklikler alındı · origin/main", 2.7),
                new(now.AddSeconds(-90), WorklogStepState.Succeeded, "Testler geçti · 1 837 test", 41.3, "   "),
            ],
            new WorklogTotals(4, 120, 0, WorklogCheckState.Failed, WorklogCheckState.Unknown), null, 15);
        var model = new WorklogCardViewModel();
        WorklogCardMapper.Apply(model, frame, "MSI", stale: false);

        // The finished step folds into the command line (§7b); the three older rows stay, newest first.
        if (model.Timeline.Count != 3)
            throw new InvalidOperationException($"D104v: expected three history rows after the fold, got {model.Timeline.Count}.");
        if (model.Timeline[0].Purpose != "Defender kayıtları kontrol ediliyor" || !model.Timeline[0].HasPurpose)
            throw new InvalidOperationException("D104v: a history row must carry its own step's purpose.");
        if (model.Timeline[1].HasPurpose || model.Timeline[2].HasPurpose)
            throw new InvalidOperationException("D104v: a row whose step had no purpose (missing or blank) must not draw a 'ne' line.");

        if (model.Timeline[1].Label != "Uzak değişiklikler alındı · origin/main" ||
            model.Timeline[1].DisplayLabel != "Uzak değişiklikler alındı · origin/\u2060main")
            throw new InvalidOperationException($"D104v: a short token must not break at its slash, got '{model.Timeline[1].DisplayLabel}'.");
        if (model.Timeline[2].DisplayLabel != "Testler geçti · 1\u00a0837\u00a0test")
            throw new InvalidOperationException($"D104v: a number must stay with its digits and its word, got '{model.Timeline[2].DisplayLabel}'.");
        if (model.Title != current.Title || model.TitleMain != "Derleme başarısız" ||
            model.TitleDetail != " · Talvora.Tray.csproj · 3\u00a0hata")
            throw new InvalidOperationException($"D104v: the command line must keep '3 hata' together, got '{model.TitleDetail}'.");
        if (model.ResultLine != "56\u00a0kontrol · 0\u00a0hata" || model.FactLine != current.FactLine)
            throw new InvalidOperationException($"D104v: the result line must keep each count with its word, got '{model.ResultLine}'.");

        // "ara" from one measured second: a session that was all work prints no gap.
        WorklogSignalCheck(now, sessionSeconds: 1.0, stepSeconds: 1.0, expectedGap: null);
        WorklogSignalCheck(now, sessionSeconds: 10, stepSeconds: 9.4, expectedGap: null);
        WorklogSignalCheck(now, sessionSeconds: 10, stepSeconds: 9.0, expectedGap: "ara 1,0 sn");
    }

    private static void WorklogSignalCheck(DateTimeOffset now, double sessionSeconds, double stepSeconds, string? expectedGap)
    {
        var step = new WorklogStep(WorklogStepCategory.SourceEdit, WorklogStepState.Succeeded, "Kod değişikliği uygulandı", null, null, stepSeconds);
        var frame = new DesktopWorklogFrame("d104v-gap", 1, now, new WorklogSessionHeader("Talvora-MCP", "Claude"), step,
            [new(now, WorklogStepState.Succeeded, step.Title, stepSeconds)],
            new WorklogTotals(1, sessionSeconds, 1, WorklogCheckState.Unknown, WorklogCheckState.Unknown), null, 60);
        var legend = WorklogCardMapper.BuildSignalLegend(frame);
        if (legend.Gap != expectedGap)
            throw new InvalidOperationException(
                $"D104v: session {sessionSeconds} s with {stepSeconds} s of work must print gap '{expectedGap ?? "(none)"}', got '{legend.Gap ?? "(none)"}'.");
    }
}
