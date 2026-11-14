using System.Windows;
using System.Windows.Automation;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    /// <summary>D44: the overview's repair count is a link that opens Olaylar on "Onarımlar".</summary>
    private static void AssertD44RepairCountOpensRepairs(ShellWindow window, Views.OverviewView overview,
        ControlCenterDashboardSnapshot snapshot, string variant)
    {
        var now = DateTimeOffset.Now;
        var repair = new ControlCenterEventRecord
        {
            Severity = ControlCenterEventSeverity.Info, Category = "recovery", Title = "Talvora Admin onarıldı",
            Count = 2, McpId = "talvora-admin", DedupKey = "recovery:talvora-admin:success",
            FirstOccurredAtUtc = now.AddMinutes(-30), LastOccurredAtUtc = now.AddMinutes(-10),
        };
        overview.ApplySnapshot(snapshot, now, [repair]);
        window.UpdateLayout();
        // WL3 (audit 53): the counts line under the description repeated "x/y bileşen hazır"; it is gone.
        if (EnumerateVisualElements(overview).OfType<System.Windows.Controls.TextBlock>()
                .Any(text => text.IsVisible && text.Text == overview.Model.Summary))
            throw new InvalidOperationException("WL3: the overview repeats the component counts.");
        var link = EnumerateVisualElements(overview).OfType<System.Windows.Controls.Button>()
            .FirstOrDefault(button => AutomationProperties.GetName(button) == "son 24 saatte 2 otomatik onarım")
            ?? throw new InvalidOperationException("D44: the repair count is not a named link.");
        if (!link.IsVisible || overview.Model.Summary.Contains("onarım", StringComparison.Ordinal))
            throw new InvalidOperationException("D44: the repair count must be its own visible link.");
        SaveVisualSnapshotIfRequested(window, "d44-link-" + variant);
        link.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.UpdateLayout();
        if (window.ShellModel.SelectedIndex != 4 ||
            window.EventsContentHost.Content is not Views.EventsView events ||
            events.Model.SelectedCategory != "Onarımlar")
            throw new InvalidOperationException("D44: the repair count must open Olaylar on Onarımlar.");
        // WL3: Olaylar lists the repair the overview counted (the page gets the same record the smoke applied).
        events.ApplySmokeFixture([repair], now,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["talvora-admin"] = "Talvora Admin" });
        window.UpdateLayout();
        if (!events.Model.Groups.SelectMany(group => group.Items).Any(item => item.Title == "Talvora Admin onarıldı"))
            throw new InvalidOperationException("WL3: Olaylar must list the repair the overview counted.");
        SaveVisualSnapshotIfRequested(window, "d44-repairs-" + variant);
        // WL3: the raw-log toggle says what a click does next.
        events.Model.RawLogVisible = true;
        window.UpdateLayout();
        if (events.RawLogToggle.Content as string != "Ham günlüğü gizle" ||
            AutomationProperties.GetName(events.RawLogToggle) != "Ham günlüğü gizle")
            throw new InvalidOperationException("WL3: the open raw log must offer 'Ham günlüğü gizle'.");
        events.Model.RawLogVisible = false;
        window.UpdateLayout();
        if (events.RawLogToggle.Content as string != "Ham günlüğü göster")
            throw new InvalidOperationException("WL3: the closed raw log must offer 'Ham günlüğü göster'.");
        events.ApplySmokeFixture([], now, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        events.ShowCategory("Tümü");
        window.ShellModel.SelectPage(0);
        overview.ApplySnapshot(snapshot, now, []);
        window.UpdateLayout();
    }
}
