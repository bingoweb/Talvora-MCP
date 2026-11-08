using Talvora.Tray.Views;

namespace Talvora.Tray;

/// <summary>UI5 retains the existing structured event store and bounded, redacted log tail.</summary>
internal sealed partial class ShellWindow
{
    private EventsView? _eventsView;

    internal ControlCenterDashboardSnapshot CurrentDashboardSnapshot => _snapshot
        ?? throw new InvalidOperationException("A dashboard snapshot is needed for the UI5 smoke.");

    internal async Task EnsureDashboardSnapshotForSmokeAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (_snapshot is null && DateTime.UtcNow < deadline)
        {
            await RefreshDashboardAsync();
            if (_snapshot is null) await Task.Delay(100);
        }
    }

    private void EnsureUi5Page()
    {
        if (_eventsView is null)
        {
            _eventsView = new EventsView();
            _eventsView.SetLifetimeToken(_lifetimeCts.Token);
            EventsContentHost.Content = _eventsView;
        }

        if (_snapshot is { } snapshot)
            _eventsView.UpdateServices(snapshot);
        if (!_smokeMode)
            _ = _eventsView.RefreshAsync(_lifetimeCts.Token);
    }

    private void UpdateUi5Snapshot(ControlCenterDashboardSnapshot snapshot)
    {
        if (_eventsView is null) return;
        _eventsView.UpdateServices(snapshot);
        if (!_smokeMode && _shellModel?.SelectedIndex == 4)
            _ = _eventsView.RefreshAsync(_lifetimeCts.Token);
    }
}
