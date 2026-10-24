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

    private void EnsureUi3Page(int selected)
    {
        if (selected == 0)
        {
            if (_overviewView is null)
            {
                _overviewView = new OverviewView();
                _overviewView.RefreshRequested += () => _ = RefreshDashboardAsync();
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
        }
        if (_chainView is not null)
            _chainView.ApplySnapshot(snapshot,
                ReferenceEquals(_ui3LoadedComponentSnapshot, snapshot) ? _ui3Components : []);
        if (_shellModel?.SelectedIndex is 0 or 3 &&
            !ReferenceEquals(_ui3RequestedComponentSnapshot, snapshot))
            _ = LoadUi3ComponentHealthAsync(snapshot);
    }

    private async Task LoadUi3ComponentHealthAsync(ControlCenterDashboardSnapshot snapshot)
    {
        _ui3ComponentCancellation?.Cancel();
        _ui3ComponentCancellation?.Dispose();
        _ui3ComponentCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _ui3RequestedComponentSnapshot = snapshot;
        try
        {
            var token = _ui3ComponentCancellation.Token;
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
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested ||
            _ui3ComponentCancellation?.IsCancellationRequested == true)
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

    private async Task OpenUi3DetailsAsync(ManagedMcpDashboardState state)
    {
        _shellModel?.SelectPage(1);
        await ShowDetailAsync(state);
    }
}
