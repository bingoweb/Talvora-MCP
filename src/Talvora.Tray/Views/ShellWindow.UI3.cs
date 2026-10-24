using System.Windows;
using Talvora.Shared;
using Talvora.Tray.Controls;
using Talvora.Tray.Views;

namespace Talvora.Tray;

/// <summary>UI3 adapters preserve the existing health, lifecycle, repair and event services.</summary>
internal sealed partial class ShellWindow
{
    private OverviewView? _overviewView;
    private Talvora.Tray.Views.ChainView? _chainView;
    private ControlCenterDashboardSnapshot? _ui3LoadedComponentSnapshot;
    private ControlCenterDashboardSnapshot? _ui3RequestedComponentSnapshot;
    private IReadOnlyList<(ManagedMcpRegistration Owner, ManagedMcpComponentState State)> _ui3Components = [];
    private CancellationTokenSource? _ui3ComponentCancellation;
    // Component probes start PowerShell and tunnel-client processes; the 8 s dashboard refresh must not repeat
    // them. They run at most once a minute, or at once after "Yenile" / "Sorunu onar" (research 2026-10-24).
    private static readonly TimeSpan Ui3ComponentProbeInterval = TimeSpan.FromSeconds(60);
    private DateTimeOffset _ui3ComponentsRequestedAt = DateTimeOffset.MinValue;
    private bool _ui3ForceComponents;

    private void EnsureUi3Page(int selected)
    {
        if (selected == 0)
        {
            if (_overviewView is null)
            {
                _overviewView = new OverviewView();
                _overviewView.RefreshRequested += () =>
                {
                    _ui3ForceComponents = true;
                    _ = RefreshDashboardAsync();
                };
                _overviewView.RepairRequested += () => _ = RepairUi3Async();
                _overviewView.DetailsRequested += () =>
                {
                    if (_overviewView.Model.RepairTarget is { } target)
                        _ = OpenUi3DetailsAsync(target);
                };
                _overviewView.ServiceRequested += state => _ = OpenUi3DetailsAsync(state);
                _overviewView.ShowChainRequested += () => _shellModel?.SelectPage(3);
                _overviewView.ChainNodeRequested += node =>
                {
                    _shellModel?.SelectPage(3);
                    _chainView?.SelectNode(node);
                };
                OverviewContentHost.Content = _overviewView;
            }
            _overviewView.Model.IsWide = ActualWidth >= 1360;
        }
        else if (selected == 3)
        {
            if (_chainView is null)
            {
                _chainView = new Talvora.Tray.Views.ChainView();
                ChainContentHost.Content = _chainView;
            }
        }
        if (_snapshot is { } snapshot)
            UpdateUi3Snapshot(snapshot);
    }

    private void UpdateUi3Snapshot(ControlCenterDashboardSnapshot snapshot)
    {
        if (_overviewView is not null)
        {
            var events = ControlCenterEventStore.ReadRecent(TimeSpan.FromHours(24), maxRecords: 100);
            _overviewView.ApplySnapshot(snapshot, DateTimeOffset.Now, events);
            if (_ui3Components.Count > 0)
                _overviewView.ApplyComponents(snapshot, _ui3Components);
        }
        // The last probed components stay on screen with their real check time until the next probe.
        if (_chainView is not null)
            _chainView.ApplySnapshot(snapshot, _ui3Components);
        if (_shellModel?.SelectedIndex is 0 or 3 &&
            !ReferenceEquals(_ui3RequestedComponentSnapshot, snapshot) &&
            (_ui3ForceComponents || DateTimeOffset.UtcNow - _ui3ComponentsRequestedAt >= Ui3ComponentProbeInterval))
            _ = LoadUi3ComponentHealthAsync(snapshot);
    }

    private async Task LoadUi3ComponentHealthAsync(ControlCenterDashboardSnapshot snapshot)
    {
        _ui3ComponentCancellation?.Cancel();
        _ui3ComponentCancellation?.Dispose();
        _ui3ComponentCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _ui3RequestedComponentSnapshot = snapshot;
        _ui3ComponentsRequestedAt = DateTimeOffset.UtcNow;
        _ui3ForceComponents = false;
        // Captured outside the try: a newer refresh replaces _ui3ComponentCancellation, and this refresh's own
        // cancellation must stay quiet instead of being logged as "unavailable" (audit 17).
        var token = _ui3ComponentCancellation.Token;
        try
        {
            var tasks = snapshot.Mcps.Select(async state =>
            {
                try
                {
                    var componentStates = await ControlCenterComponentHealthService.GetStatesAsync(
                        state.Registration, token);
                    return componentStates
                        .Select(component => (Owner: state.Registration, State: component)).ToArray();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    TrayLog.Write("UI3 component health read failed", ex);
                    return Array.Empty<(ManagedMcpRegistration Owner, ManagedMcpComponentState State)>();
                }
            });
            var results = await Task.WhenAll(tasks);
            if (token.IsCancellationRequested || !ReferenceEquals(_snapshot, snapshot)) return;
            _ui3Components = results.SelectMany(row => row).ToArray();
            _ui3LoadedComponentSnapshot = snapshot;
            _overviewView?.ApplyComponents(snapshot, _ui3Components);
            _chainView?.ApplySnapshot(snapshot, _ui3Components);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("UI3 component health unavailable", ex);
        }
    }

    private async Task RepairUi3Async()
    {
        var state = _overviewView?.Model.RepairTarget;
        if (state is null || state.Health == ControlCenterHealthState.Ready) return;
        try
        {
            var repaired = await ControlCenterRepairService.RepairAsync(
                state.Registration, state, _lifetimeCts.Token);
            RecordRepairSuccess(state.Registration, repaired);
            _ui3ForceComponents = true;
            await RefreshDashboardAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("UI3 repair failed", ex);
            var friendly = GetFriendlyOperationError(ex);
            RecordOperationFailure(state.Registration, "Onarım başarısız", friendly);
            await ShowOperationErrorAsync(
                state.Registration.DisplayName + " onarılamadı", friendly);
        }
    }

    private Task OpenUi3DetailsAsync(ManagedMcpDashboardState state)
    {
        OpenUi4Detail(state);
        return Task.CompletedTask;
    }
}
