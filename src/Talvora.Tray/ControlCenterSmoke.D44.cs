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
        overview.ApplySnapshot(snapshot, now,
        [
            new ControlCenterEventRecord
            {
                Severity = ControlCenterEventSeverity.Info, Category = "recovery", Title = "Talvora Admin onarıldı",
                Count = 2, McpId = "talvora-admin", DedupKey = "recovery:talvora-admin:success",
                FirstOccurredAtUtc = now.AddMinutes(-30), LastOccurredAtUtc = now.AddMinutes(-10),
            },
        ]);
        window.UpdateLayout();
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
        SaveVisualSnapshotIfRequested(window, "d44-repairs-" + variant);
        events.ShowCategory("Tümü");
        window.ShellModel.SelectPage(0);
        overview.ApplySnapshot(snapshot, now, []);
        window.UpdateLayout();
    }
}
