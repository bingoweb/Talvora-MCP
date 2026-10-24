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
        OverviewChain.NodeSelected += node => ChainNodeRequested?.Invoke(node);
    }

    public OverviewViewModel Model { get; }
    public event Action? RefreshRequested;
    public event Action? RepairRequested;
    public event Action? DetailsRequested;
    public event Action? ShowChainRequested;
    public event Action<ManagedMcpDashboardState>? ServiceRequested;
    public event Action<ChainNode>? ChainNodeRequested;

    public void ApplySnapshot(ControlCenterDashboardSnapshot snapshot, DateTimeOffset checkedAt,
        IReadOnlyList<ControlCenterEventRecord> events)
    {
        Model.ApplySnapshot(snapshot, checkedAt, events);
        OverviewChain.Apply(snapshot, [], full: false);
    }

    public void ApplyComponents(ControlCenterDashboardSnapshot snapshot,
        IReadOnlyList<(Talvora.Shared.ManagedMcpRegistration Owner, ManagedMcpComponentState State)> states) =>
        OverviewChain.Apply(snapshot, states, full: false);

    public void FocusSearch()
    {
        ServiceSearch.Focus();
        ServiceSearch.SelectAll();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
    private void OnRepair(object sender, RoutedEventArgs e) => RepairRequested?.Invoke();
    private void OnDetails(object sender, RoutedEventArgs e) => DetailsRequested?.Invoke();
    private void OnShowChain(object sender, RoutedEventArgs e) => ShowChainRequested?.Invoke();

    private void OnServiceSelected(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? current = e.OriginalSource as DependencyObject;
             current is not null;
             current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase) return;
        }
        OpenSelectedService();
    }

    private void OnServiceKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedService();
            e.Handled = true;
        }
    }

    private void OnServiceMore(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: OverviewServiceItem item })
            ServiceRequested?.Invoke(item.Source);
    }

    private void OpenSelectedService()
    {
        if (ServicesList.SelectedItem is OverviewServiceItem item)
            ServiceRequested?.Invoke(item.Source);
    }
}
