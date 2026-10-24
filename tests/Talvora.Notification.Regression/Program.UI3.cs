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
        Console.WriteLine("UI3_NARRATOR_GREEN");
    }

    private static void Ui3Check(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException("UI3: " + reason);
    }
}
