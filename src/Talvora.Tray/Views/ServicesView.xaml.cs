using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Talvora.Shared;
using Talvora.Tray.Controls;
using Talvora.Tray.ViewModels;

namespace Talvora.Tray.Views;

internal sealed partial class ServicesView : System.Windows.Controls.UserControl
{
    private readonly ServiceDetailView _detail;

    internal ServicesView()
    {
        InitializeComponent();
        Model = new ServicesViewModel();
        DataContext = Model;
        _detail = new ServiceDetailView();
        _detail.BackRequested += BackToList;
        _detail.ActionRequested += action =>
        {
            if (_detail.Model.Source is { } state)
                ActionRequested?.Invoke(state, action);
        };
        DetailHost.Content = _detail;
    }

    internal ServicesViewModel Model { get; }
    internal ServiceDetailView Detail => _detail;
    internal bool IsDetailOpen => DetailHost.Visibility == Visibility.Visible;
    internal event Action<ManagedMcpDashboardState, Ui4ServiceAction>? ActionRequested;
    internal event Action? RefreshRequested;
    internal event Action<ManagedMcpDashboardState>? DetailOpened;

    internal void ApplySnapshot(ControlCenterDashboardSnapshot snapshot, DateTimeOffset at)
    {
        Model.ApplySnapshot(snapshot, at, ControlCenterLifecycleService.IsManuallyStopped);
        if (IsDetailOpen && _detail.Model.Source is { } selected)
        {
            var updated = snapshot.Mcps.FirstOrDefault(s =>
                string.Equals(s.Registration.Id, selected.Registration.Id, StringComparison.OrdinalIgnoreCase));
            if (updated is not null)
                _detail.Model.ApplyState(updated, at,
                    ControlCenterLifecycleService.IsManuallyStopped(updated.Registration));
        }
    }

    internal void OpenDetail(ManagedMcpDashboardState state)
    {
        _detail.Open(state);
        ListRegion.Visibility = Visibility.Collapsed;
        DetailHost.Visibility = Visibility.Visible;
        DetailOpened?.Invoke(state);
    }

    internal void BackToList()
    {
        _detail.Close();
        DetailHost.Visibility = Visibility.Collapsed;
        ListRegion.Visibility = Visibility.Visible;
    }

    internal void FocusSearch()
    {
        if (IsDetailOpen) return;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();

    private void OnRowActionRequested(ServiceRowViewModel row, Ui4ServiceAction action)
    {
        if (action == Ui4ServiceAction.Details)
            OpenDetail(row.Source);
        else
        {
            OpenDetail(row.Source);
            ActionRequested?.Invoke(row.Source, action);
        }
    }

    private void OnListKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ServiceList.SelectedItem is ServiceRowViewModel selected)
        {
            OpenDetail(selected.Source);
            e.Handled = true;
        }
    }

    private void OnListMouseUp(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? element = e.OriginalSource as DependencyObject;
             element is not null; element = ParentOf(element))
        {
            if (element is System.Windows.Controls.Primitives.ButtonBase)
                return;
            if (element is System.Windows.Controls.ListBoxItem item &&
                item.DataContext is ServiceRowViewModel row)
            {
                OpenDetail(row.Source);
                e.Handled = true;
                return;
            }
        }
    }

    private static DependencyObject? ParentOf(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}
