using System.Globalization;

namespace Talvora.Tray;

/// <summary>D43: a session's day once, then its time and its measured duration (the Olaylar page's day words).</summary>
internal static class LocalTimeText
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"Bugün", "Dün", "5 Kasım Perşembe"; another year adds the year: "31 Aralık 2025 Çarşamba".</summary>
    internal static string Day(DateTime localDate, DateTime today) =>
        localDate == today ? "Bugün" :
        localDate == today.AddDays(-1) ? "Dün" :
        localDate.Year == today.Year ? localDate.ToString("d MMMM dddd", Turkish) :
        localDate.ToString("d MMMM yyyy dddd", Turkish);

    /// <summary>List row: "Bugün 15:44 · 3 sn".</summary>
    internal static string SessionShort(DateTimeOffset startUtc, DateTimeOffset endUtc, DateTimeOffset now)
    {
        var start = startUtc.ToOffset(now.Offset);
        return $"{Day(start.Date, now.Date)} {start.ToString("HH:mm", Turkish)} · {Duration(endUtc - startUtc)}";
    }

    /// <summary>Detail: "Bugün 15:44:23 → 15:44:26 · 3 sn"; a session across midnight names both days.</summary>
    internal static string SessionLong(DateTimeOffset startUtc, DateTimeOffset endUtc, DateTimeOffset now)
    {
        var start = startUtc.ToOffset(now.Offset);
        var end = endUtc.ToOffset(now.Offset);
        var endDay = end.Date == start.Date ? string.Empty : Day(end.Date, now.Date) + " ";
        return $"{Day(start.Date, now.Date)} {start.ToString("HH:mm:ss", Turkish)} → " +
            $"{endDay}{end.ToString("HH:mm:ss", Turkish)} · {Duration(endUtc - startUtc)}";
    }

    /// <summary>The measured span in whole seconds: "1 sn'den kısa", "3 sn", "4 dk 52 sn", "1 dk", "1 sa 3 dk".</summary>
    internal static string Duration(TimeSpan span)
    {
        var total = (long)Math.Round(Math.Max(0, span.TotalSeconds), MidpointRounding.AwayFromZero);
        if (total == 0) return "1 sn'den kısa";
        if (total < 60) return $"{total} sn";
        if (total < 3600) return total % 60 == 0 ? $"{total / 60} dk" : $"{total / 60} dk {total % 60} sn";
        var minutes = total / 60 % 60;
        return minutes == 0 ? $"{total / 3600} sa" : $"{total / 3600} sa {minutes} dk";
    }
}
