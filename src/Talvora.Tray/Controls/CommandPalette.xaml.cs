using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Talvora.Tray.ViewModels;

namespace Talvora.Tray.Controls;

internal partial class CommandPalette : Popup
{
    public CommandPalette() => InitializeComponent();

    public void Open(UIElement target, ShellViewModel model, double availableHeight)
    {
        DataContext = model;
        PlacementTarget = target;
        Width = target.RenderSize.Width + 48;
        Results.MaxHeight = Math.Max(32, availableHeight - 48 - 48 - 32);
        model.PaletteQuery = string.Empty;
        IsOpen = true;
    }

    private void OnPaletteOpened(object sender, EventArgs e)
    {
        Results.SelectedIndex = Results.Items.Count > 0 ? 0 : -1;
        _ = Dispatcher.BeginInvoke(() => { SearchBox.Focus(); SearchBox.SelectAll(); });
    }

    private void OnPaletteClosed(object sender, EventArgs e) => PlacementTarget?.Focus();

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (Results is not null) Results.SelectedIndex = Results.Items.Count > 0 ? 0 : -1;
    }

    private async void OnExecuteSelection(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            System.Windows.Controls.ItemsControl.ContainerFromElement(Results, source) is System.Windows.Controls.ListBoxItem)
            await ExecuteSelectionAsync();
    }

    private async void OnPaletteKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { IsOpen = false; e.Handled = true; return; }
        if (e.Key is Key.Down or Key.Up)
        {
            if (Results.Items.Count == 0) { e.Handled = true; return; }
            Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Math.Max(0, Results.Items.Count - 1));
            Results.ScrollIntoView(Results.SelectedItem);
            e.Handled = true;
        }
        if (e.Key == Key.Enter) { e.Handled = true; await ExecuteSelectionAsync(); }
    }

    private async Task ExecuteSelectionAsync()
    {
        if (Results.SelectedItem is not ShellCommand item) return;
        IsOpen = false;
        await ((IAsyncRelayCommand)item.Command).ExecuteAsync(null);
    }
}
