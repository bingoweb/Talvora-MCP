using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Talvora.Tray.Controls;
using Talvora.Tray.ViewModels;

namespace Talvora.Tray.Views;

internal sealed partial class OverviewView : System.Windows.Controls.UserControl
{
    public OverviewView()
    {
        InitializeComponent();
        Model = new OverviewViewModel();
        DataContext = Model;
        Model.PropertyChanged += OnModelChanged;
        Board.SocketActivated += state => ServiceRequested?.Invoke(state);
    }

    public OverviewViewModel Model { get; }
    public event Action? RefreshRequested;
    public event Action? RepairRequested;
    public event Action? DetailsRequested;
    public event Action? ShowEventsRequested;
    public event Action? ShowRepairsRequested;
    public event Action<ManagedMcpDashboardState>? ServiceRequested;

    public void ApplySnapshot(ControlCenterDashboardSnapshot snapshot, DateTimeOffset checkedAt,
        IReadOnlyList<ControlCenterEventRecord> events) =>
        Model.ApplySnapshot(snapshot, checkedAt, events);

    // Component health stays on the Bağlantı zinciri page; the switchboard draws service-level health.
    public void ApplyComponents(ControlCenterDashboardSnapshot snapshot,
        IReadOnlyList<(Talvora.Shared.ManagedMcpRegistration Owner, ManagedMcpComponentState State)> states)
    {
    }

    /// <summary>SN1 feed: counters update and every new finished call becomes one light on its cable.</summary>
    public void ApplyTraffic(TrafficFeed? feed) => Board.AddCalls(Model.ApplyTraffic(feed));

    internal Switchboard SwitchboardControl => Board;

    public void FocusSearch()
    {
        if (Board.Children.Count > 0) ((UIElement)Board.Children[0]).Focus();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverviewViewModel.Sockets)) Board.Apply(Model.Sockets);
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
    private void OnRepair(object sender, RoutedEventArgs e) => RepairRequested?.Invoke();
    private void OnDetails(object sender, RoutedEventArgs e) => DetailsRequested?.Invoke();
    private void OnShowEvents(object sender, RoutedEventArgs e) => ShowEventsRequested?.Invoke();
    private void OnShowRepairs(object sender, RoutedEventArgs e) => ShowRepairsRequested?.Invoke();
}
