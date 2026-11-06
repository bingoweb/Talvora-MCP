using Talvora.Tray.ViewModels;

internal static partial class Program
{
    private static void AssertUi3StatusNarrator()
    {
        var checkedAt = new DateTimeOffset(2026, 10, 24, 14, 32, 0,
            TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 24, 14, 32, 0)));
        static StatusNarratorEntry Item(string name, ComponentVisualState state,
            bool optional = false, string reason = "") =>
            new(name, state, optional, reason);

        var ready = new[]
        {
            Item("Talvora Çekirdeği", ComponentVisualState.Ready),
            Item("Talvora Dev", ComponentVisualState.Ready),
            Item("Talvora Admin", ComponentVisualState.Ready),
        };
        var allReady = StatusNarrator.Build(ready, checkedAt);
        Ui3Check(allReady.Title == "Her şey yolunda." &&
                 allReady.Detail == "3 bileşenin hepsi hazır. Son kontrol 14:32.",
                 "The approved all-ready copy or check time changed.");

        var someOptionalOff = ready.Append(Item("Gitea", ComponentVisualState.Ready))
            .Append(Item("Penpot", ComponentVisualState.Neutral, optional: true)).ToArray();
        var optional = StatusNarrator.Build(someOptionalOff, checkedAt);
        Ui3Check(optional.Title == "Her şey yolunda." &&
                 optional.Detail == "4/5 bileşen hazır; Penpot isteğe bağlı olarak kapalı.",
                 "The approved optional-off sentence changed.");

        var attention = ready.Append(Item("Talvora Admin", ComponentVisualState.Attention,
            reason: "Bağlantı yavaş.")).ToArray();
        var one = StatusNarrator.Build(attention, checkedAt);
        Ui3Check(one.Title == "Talvora Admin dikkat istiyor: Bağlantı yavaş.",
                 "Single attention must name its plain-language reason.");

        var multiple = attention.Append(Item("Gitea", ComponentVisualState.Attention,
            reason: "Yanıt gelmiyor.")).ToArray();
        var several = StatusNarrator.Build(multiple, checkedAt);
        Ui3Check(several.Title == "2 bileşen dikkat istiyor. En önemlisi: Talvora Admin.",
                 "Several attention cases must prioritise the first important component.");

        var unreachable = StatusNarrator.Build(
            [Item("Talvora Çekirdeği", ComponentVisualState.Offline)], checkedAt);
        Ui3Check(unreachable.Title ==
                 "Talvora hizmetine şu anda ulaşılamıyor. Bilgisayarınızdaki dosyalar etkilenmedi.",
                 "Unavailable core must use the approved safe-state wording.");

        var repairing = StatusNarrator.Build(
            [Item("Talvora Admin", ComponentVisualState.Repairing,
                reason: "00:15")], checkedAt);
        Ui3Check(repairing.Title == "Talvora Admin onarılıyor… (00:15)",
                 "Repairing must use the approved progress sentence.");
        AssertD26RecoveryCount();
        // D27: a dropped line reads like its lamp; reasons end as sentences.
        var dropped = StatusNarrator.Build(
        [
            Item("Talvora Çekirdeği", ComponentVisualState.Ready),
            Item("Talvora Dev", ComponentVisualState.Ready),
            Item("Talvora Admin", ComponentVisualState.Offline, reason: "Ulaşılamıyor"),
            Item("Gitea", ComponentVisualState.Ready),
        ], checkedAt);
        Ui3Check(dropped.Title == "Talvora Admin yanıt vermiyor." &&
                 dropped.Detail == "Ulaşılamıyor. 3/4 bileşen hazır.",
                 "D27: a dropped line must say it does not answer.");
        var noPeriod = StatusNarrator.Build(
            ready.Append(Item("Talvora Admin", ComponentVisualState.Attention, reason: "Bağlantı yavaş")).ToArray(),
            checkedAt);
        Ui3Check(noPeriod.Title == "Talvora Admin dikkat istiyor: Bağlantı yavaş.",
                 "D27: the reason must end as a sentence.");
        Console.WriteLine("UI3_NARRATOR_GREEN");
    }

    // D26: only successful repairs count as "otomatik onarım".
    private static void AssertD26RecoveryCount()
    {
        var now = DateTimeOffset.Now;
        var core = new Talvora.Shared.ManagedMcpRegistration
        {
            Id = "talvora", DisplayName = "Talvora Çekirdeği", Description = "Talvora Çekirdeği",
            Endpoint = "http://127.0.0.1:7676/mcp",
        };
        var snapshot = new Talvora.Tray.ControlCenterDashboardSnapshot(
            [new Talvora.Tray.ManagedMcpDashboardState(core, Talvora.Tray.ControlCenterHealthState.Ready, "Hazır", "Hazır")],
            "Her şey hazır", "", 1, 0, 0);
        Talvora.Tray.ControlCenterEventRecord Event(Talvora.Tray.ControlCenterEventSeverity severity, string title, int count) =>
            new()
            {
                Severity = severity, Category = "recovery", Title = title, Count = count,
                FirstOccurredAtUtc = now.AddMinutes(-30), LastOccurredAtUtc = now.AddMinutes(-10),
            };
        var model = new OverviewViewModel();
        model.ApplySnapshot(snapshot, now,
        [
            Event(Talvora.Tray.ControlCenterEventSeverity.Info, "Talvora Admin onarıldı", 2),
            Event(Talvora.Tray.ControlCenterEventSeverity.Warning, "Hizmet sağlık bakımı yarıda kaldı", 7),
            Event(Talvora.Tray.ControlCenterEventSeverity.Error, "Talvora Dev: müdahale gerekiyor", 1),
        ]);
        Ui3Check(model.RecoveryCount == 2, "D26: only successful repairs count as otomatik onarım.");
    }

    private static void Ui3Check(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException("UI3: " + reason);
    }
}
