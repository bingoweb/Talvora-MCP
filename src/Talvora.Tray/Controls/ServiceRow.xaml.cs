using System.Windows;
using Talvora.Tray.ViewModels;

namespace Talvora.Tray.Controls;

internal enum Ui4ServiceAction { Details, Start, Stop, Restart, Repair }

internal sealed partial class ServiceRow : System.Windows.Controls.UserControl
{
    internal event Action<ServiceRowViewModel, Ui4ServiceAction>? ActionRequested;

    public ServiceRow() => InitializeComponent();

    private void OnMore(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MoreButton;
            menu.IsOpen = true;
        }
        e.Handled = true;
    }

    private void Request(Ui4ServiceAction action)
    {
        if (DataContext is ServiceRowViewModel row) ActionRequested?.Invoke(row, action);
    }
    private void OnDetails(object sender, RoutedEventArgs e) => Request(Ui4ServiceAction.Details);
    private void OnStart(object sender, RoutedEventArgs e) => Request(Ui4ServiceAction.Start);
    private void OnStop(object sender, RoutedEventArgs e) => Request(Ui4ServiceAction.Stop);
    private void OnRestart(object sender, RoutedEventArgs e) => Request(Ui4ServiceAction.Restart);
    private void OnRepair(object sender, RoutedEventArgs e) => Request(Ui4ServiceAction.Repair);
}
