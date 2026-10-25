using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    private static async Task AssertUi5VisualFixtureAsync(ShellWindow window, string theme)
    {
        window.ShellModel.SelectPage(4);
        if (window.EventsContentHost.Content is not Views.EventsView eventsView)
            throw new InvalidOperationException("UI5 events route did not open the new view.");

        await eventsView.RefreshAsync(CancellationToken.None);
        if (eventsView.ServiceFilterBox.SelectedItem is not
            ViewModels.EventServiceFilter { Id: null })
            throw new InvalidOperationException("UI5 must default the service filter to Tümü.");
        var now = DateTimeOffset.Now;
        ControlCenterEventRecord[] fixture =
        [
            Entry(now.AddMinutes(-5), ControlCenterEventSeverity.Warning,
                "recovery", "talvora-admin", "Güvenli tünel onarılıyor",
                "Bağlantı yeniden deneniyor."),
            Entry(now.AddMinutes(-28), ControlCenterEventSeverity.Info,
                "operation", "gitea", "Gitea başlatıldı",
                "Servis kullanıma hazır."),
            Entry(now.AddDays(-1), ControlCenterEventSeverity.Error,
                "connection", "talvora-dev", "Bağlantı kesildi",
                "Güvenli bağlantı kontrol edilecek."),
        ];
        var serviceNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["talvora-admin"] = "Talvora Admin",
            ["talvora-dev"] = "Talvora Dev",
            ["gitea"] = "Gitea",
        };
        eventsView.Model.ApplyEvents([], now, serviceNames);
        eventsView.Model.IsLoading = true;
        window.UpdateLayout();
        if (eventsView.EmptyStateText.IsVisible)
            throw new InvalidOperationException("UI5 empty state appeared during the first read.");
        eventsView.Model.IsLoading = false;
        window.UpdateLayout();
        if (!eventsView.EmptyStateText.IsVisible ||
            eventsView.EmptyStateText.Text != "Son 7 günde kaydedilmiş olay yok.")
            throw new InvalidOperationException("UI5 seven-day empty state was not rendered.");
        eventsView.Model.SearchText = "eşleşmeyen";
        window.UpdateLayout();
        if (!eventsView.EmptyStateText.IsVisible ||
            eventsView.EmptyStateText.Text != "Bu filtrelerle eşleşen olay yok.")
            throw new InvalidOperationException("UI5 filtered empty state was not rendered.");
        eventsView.Model.SearchText = "";
        eventsView.Model.ApplyEvents(fixture, now, serviceNames);
        eventsView.ServiceFilterBox.SelectedItem = eventsView.Model.ServiceOptions.Single(option =>
            option.Id == "talvora-admin");
        eventsView.Model.ApplyEvents(fixture, now.AddMinutes(1), serviceNames);
        if (eventsView.ServiceFilterBox.SelectedItem is not
                ViewModels.EventServiceFilter { Id: "talvora-admin" } ||
            eventsView.Model.Groups.Sum(group => group.Items.Count) != 1)
            throw new InvalidOperationException("UI5 service selection was lost after event refresh.");
        eventsView.ServiceFilterBox.SelectedItem = eventsView.Model.ServiceOptions[0];
        window.UpdateLayout();
        if (eventsView.Model.Groups.Count != 2 ||
            eventsView.Model.Groups[0].Title != "Bugün" ||
            eventsView.Model.Groups[1].Title != "Dün")
            throw new InvalidOperationException("UI5 grouped timeline was not rendered.");
        SaveVisualSnapshotIfRequested(window, "ui5-events-" + theme);

        eventsView.OpenRawLog();
        window.UpdateLayout();
        var field = typeof(Views.EventsView).GetField("_rawLogTimer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var timer = field?.GetValue(eventsView) as DispatcherTimer
            ?? throw new InvalidOperationException("UI5 raw log timer was not instantiated.");
        if (!timer.IsEnabled)
            throw new InvalidOperationException("UI5 raw log timer must run when the panel is visible.");

        eventsView.Model.RawLogText = "Authorization: Bearer fake-ui5-copy-secret";
        var oldClipboard = System.Windows.Clipboard.GetDataObject();
        try
        {
            eventsView.LiveEvidence.CopyButton.RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var value = System.Windows.Clipboard.GetText();
            if (value.Contains("fake-ui5-copy-secret", StringComparison.Ordinal))
                throw new InvalidOperationException("UI5 clipboard copy leaked a test secret.");
        }
        finally
        {
            if (oldClipboard is not null) System.Windows.Clipboard.SetDataObject(oldClipboard);
        }
        SaveVisualSnapshotIfRequested(window, "ui5-log-" + theme);
        eventsView.Model.RawLogVisible = false;
        if (timer.IsEnabled)
            throw new InvalidOperationException("UI5 raw log timer must stop on collapse.");
        eventsView.OpenRawLog();
        window.WindowState = WindowState.Minimized;
        if (timer.IsEnabled)
            throw new InvalidOperationException("UI5 raw log timer must stop while the window is minimized.");
        window.WindowState = WindowState.Normal;
        if (!timer.IsEnabled)
            throw new InvalidOperationException("UI5 raw log timer must resume when the window is restored.");
        window.ShellModel.SelectPage(0);
        if (timer.IsEnabled)
            throw new InvalidOperationException("UI5 raw log timer must stop when the events page is hidden.");
    }

    private static ControlCenterEventRecord Entry(DateTimeOffset at,
        ControlCenterEventSeverity severity, string category, string mcpId,
        string title, string detail) => new()
    {
        FirstOccurredAtUtc = at.ToUniversalTime(),
        LastOccurredAtUtc = at.ToUniversalTime(),
        Severity = severity,
        Category = category,
        McpId = mcpId,
        Title = title,
        Detail = detail
    };
}
