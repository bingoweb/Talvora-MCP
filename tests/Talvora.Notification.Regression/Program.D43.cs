using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D43: a session names its day once, then its time and its measured duration.</summary>
    private static void AssertD43SessionTimeText()
    {
        var offset = TimeSpan.FromHours(3);
        var now = new DateTimeOffset(2026, 11, 7, 18, 0, 0, offset);
        var start = new DateTimeOffset(2026, 11, 7, 15, 44, 23, offset);
        var checks = new (string Actual, string Expected)[]
        {
            (LocalTimeText.SessionShort(start, start.AddSeconds(3.4), now), "Bugün 15:44 · 3 sn"),
            (LocalTimeText.SessionLong(start, start.AddSeconds(3.4), now), "Bugün 15:44:23 → 15:44:26 · 3 sn"),
            (LocalTimeText.SessionShort(start, start.AddSeconds(0.007), now), "Bugün 15:44 · 1 sn'den kısa"),
            (LocalTimeText.SessionShort(start.AddDays(-1), start.AddDays(-1).AddSeconds(292), now),
                "Dün 15:44 · 4 dk 52 sn"),
            (LocalTimeText.SessionLong(new DateTimeOffset(2026, 11, 6, 23, 58, 10, offset),
                new DateTimeOffset(2026, 11, 7, 0, 3, 2, offset), now), "Dün 23:58:10 → Bugün 00:03:02 · 4 dk 52 sn"),
            (LocalTimeText.SessionShort(start.AddDays(-2), start.AddDays(-2).AddSeconds(3780), now),
                "5 Kasım Perşembe 15:44 · 1 sa 3 dk"),
            (LocalTimeText.SessionShort(new DateTimeOffset(2025, 12, 31, 9, 5, 0, offset),
                new DateTimeOffset(2025, 12, 31, 9, 6, 0, offset), now), "31 Aralık 2025 Çarşamba 09:05 · 1 dk"),
        };
        foreach (var (actual, expected) in checks)
        {
            if (actual != expected)
                throw new InvalidOperationException($"D43: expected \"{expected}\", got \"{actual}\".");
        }
        Console.WriteLine("D43_SESSION_TIME_TEXT_GREEN");
    }
}
