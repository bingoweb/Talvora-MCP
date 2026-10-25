using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Talvora.Tray.Controls;
using Talvora.Tray.ViewModels;

namespace Talvora.Tray.Views;

internal sealed partial class ServiceDetailView : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _elapsedTimer;
    private readonly Stopwatch _elapsed = new();

    internal ServiceDetailView()
    {
        InitializeComponent();
        Model = new ServiceDetailViewModel();
        DataContext = Model;
        _elapsedTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _elapsedTimer.Tick += (_, _) => Model.UpdateElapsed(_elapsed.Elapsed);
        Unloaded += (_, _) =>
        {
            if (!Model.IsBusy) _elapsedTimer.Stop();
        };
    }

    internal ServiceDetailViewModel Model { get; }
    internal event Action? BackRequested;
    internal event Action<Ui4ServiceAction>? ActionRequested;
    internal event Action? EvidenceRequested;

    internal void Open(ManagedMcpDashboardState state)
    {
        Model.ResetBanner();
        Model.IsLoading = true;
        Model.ApplyState(state, DateTimeOffset.Now,
            ControlCenterLifecycleService.IsManuallyStopped(state.Registration));
        ServiceLogo.Source = McpLogoResolver.TryResolve(state.Registration.Id) ??
            (state.Registration.Id.StartsWith("talvora-", StringComparison.OrdinalIgnoreCase)
                ? McpLogoResolver.TryResolve("talvora") : null);
        DetailTabs.SelectedIndex = 0;
    }

    internal void Close() => _elapsedTimer.Stop();

    internal void StartBusy(string operation)
    {
        Model.BeginOperation(operation);
        _elapsed.Restart();
        BackButton.IsEnabled = false;
        _elapsedTimer.Start();
    }

    internal void CompleteBusy(string message, bool success)
    {
        _elapsedTimer.Stop();
        Model.UpdateElapsed(_elapsed.Elapsed);
        _elapsed.Stop();
        Model.FinishOperation(message, success);
        BackButton.IsEnabled = true;
    }

    private void OnBack(object sender, RoutedEventArgs args)
    {
        if (!Model.IsBusy) BackRequested?.Invoke();
    }
    private void OnStart(object sender, RoutedEventArgs args) => ActionRequested?.Invoke(Ui4ServiceAction.Start);
    private void OnStop(object sender, RoutedEventArgs args) => ActionRequested?.Invoke(Ui4ServiceAction.Stop);
    private void OnRestart(object sender, RoutedEventArgs args) => ActionRequested?.Invoke(Ui4ServiceAction.Restart);
    private void OnRepair(object sender, RoutedEventArgs args) => ActionRequested?.Invoke(Ui4ServiceAction.Repair);
    private void OnOpenWeb(object sender, RoutedEventArgs args) => ActionRequested?.Invoke(Ui4ServiceAction.OpenWeb);

    private void OnTabChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, DetailTabs) && DetailTabs.SelectedIndex == 2)
            EvidenceRequested?.Invoke();
    }
}
