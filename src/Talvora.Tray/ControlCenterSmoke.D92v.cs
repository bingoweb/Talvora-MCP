using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    // D92v fixture: one step with the agent's purpose, one without, and a step label too long for one line.
    private const string D92vPurpose =
        "Geçmiş görünümünde her adımın amacı, adımın altında ikinci bir satır olarak gösteriliyor.";
    private const string D92vLongLabel =
        "Kod değişikliği uygulandı · WorklogHistoryView.xaml, ControlCenterSmoke.cs, WorklogHistoryStore.cs, " +
        "DesktopProgressProtocol.cs ve TalvoraDesktopWorklogState.cs";

    /// <summary>
    /// D92v (user decision 2026-11-15 "Her adımda saklansın"): each step of the history's timeline shows its own
    /// "ne" as a quiet second line, a step without a purpose shows none, and no line runs past the detail card.
    /// </summary>
    private static void AssertD92vStepPurposes(Views.WorklogHistoryView page, string variant)
    {
        var texts = FindVisualChildren<TextBlock>(page).ToArray();
        var purposes = texts
            .Where(text => AutomationProperties.GetAutomationId(text) == "HistoryStepPurpose" && text.IsVisible)
            .ToArray();
        if (purposes.Length != 1 || purposes[0].Text != D92vPurpose)
            throw new InvalidOperationException(
                $"D92v ({variant}): exactly the one step with a purpose must show it; visible purpose lines: {purposes.Length}.");
        // A measured step prints its stopwatch time like the live card ("3,0 sn"); an unmeasured one prints nothing.
        var durations = texts
            .Where(text => AutomationProperties.GetAutomationId(text) == "HistoryStepDuration" && text.IsVisible)
            .ToArray();
        if (durations.Length != 1 || durations[0].Text != "3,0 sn")
            throw new InvalidOperationException(
                $"D92v ({variant}): exactly the one measured step must print '3,0 sn'; visible durations: {durations.Length}.");

        var heading = texts.SingleOrDefault(text => text.Text == "Zaman çizelgesi" && text.IsVisible)
            ?? throw new InvalidOperationException($"D92v ({variant}): the timeline heading is missing.");
        DependencyObject? node = heading;
        while (node is not null and not Border)
            node = VisualTreeHelper.GetParent(node);
        if (node is not Border card || card.ActualWidth <= 0)
            throw new InvalidOperationException($"D92v ({variant}): the detail card was not found.");

        var label = texts.SingleOrDefault(text => text.Text == D92vLongLabel && text.IsVisible)
            ?? throw new InvalidOperationException($"D92v ({variant}): the long step label is missing.");
        // User decision 2026-11-16 "Eskiden yeniye": the history reads from the first step to the last, although
        // the store keeps newest first; the older long-label step stands above the newer step's purpose line.
        if (label.TranslatePoint(new System.Windows.Point(0, 0), card).Y >=
            purposes[0].TranslatePoint(new System.Windows.Point(0, 0), card).Y)
            throw new InvalidOperationException($"D92v ({variant}): the timeline must read oldest step first.");
        foreach (var text in new[] { label, purposes[0], durations[0] })
        {
            var right = text.TranslatePoint(new System.Windows.Point(text.ActualWidth, 0), card).X;
            if (right > card.ActualWidth - card.Padding.Right + 0.5)
                throw new InvalidOperationException(
                    $"D92v ({variant}): a timeline line runs past the detail card ({right:0} > {card.ActualWidth - card.Padding.Right:0}).");
        }
    }
}
