using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using System.Windows.Input;
using Talvora.Tray.Controls;
using Talvora.Tray.ViewModels;
using Talvora.Tray.Views;

namespace Talvora.Tray;

// UI2 keeps the existing page builders and lifecycle handlers as a temporary adapter until UI3-UI8.
internal sealed partial class ShellWindow
{
    private ShellViewModel? _shellModel;
    private WorklogHistoryView? _historyView;
    private bool _syncingShellNavigation;
    private bool _syncingShellTheme;
    private System.Diagnostics.Stopwatch? _shellFirstPaint;
    internal double? FirstPaintMilliseconds { get; private set; }

    internal ShellViewModel ShellModel => _shellModel
        ?? throw new InvalidOperationException("The shell has not been initialized.");

    private void InitializeShell(Grid legacy)
    {
        foreach (UIElement child in legacy.Children)
        {
            if (Grid.GetRow(child) is 0 or 3 || Grid.GetColumn(child) == 0 && Grid.GetRow(child) == 1)
                child.Visibility = Visibility.Collapsed;
        }
        legacy.ColumnDefinitions[0].Width = new GridLength(0);
        legacy.RowDefinitions[0].Height = new GridLength(0);
        legacy.RowDefinitions[3].Height = new GridLength(0);
        legacy.DataContext = null;
        LegacyContentHost.Content = legacy;
        ShellRoot.Background = BackgroundBrush;
        _windowTitleBar = ShellTitleBar;
        _shellModel = new ShellViewModel(RefreshDashboardAsync);
        DataContext = _shellModel;
        _shellModel.PropertyChanged += OnShellModelChanged;
        _shellModel.UpdateViewportWidth(Width);
        UpdateShellRailWidth();
        SynchronizeShellThemeChoice();
        PreviewKeyDown += OnShellPreviewKeyDown;
        SizeChanged += (_, _) => _shellModel.UpdateViewportWidth(ActualWidth);
        Loaded += (_, _) => ApplyShellRoute();
        ContentRendered += (_, _) =>
        {
            if (_shellFirstPaint?.IsRunning == true)
            {
                _shellFirstPaint.Stop();
                FirstPaintMilliseconds = _shellFirstPaint.Elapsed.TotalMilliseconds;
                TrayLog.Write($"Shell first paint: {FirstPaintMilliseconds.Value:0.0} ms.");
            }
        };
        UpdateShellCommands();
    }

    private void OnShellModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.SelectedIndex)) ApplyShellRoute();
        if (e.PropertyName == nameof(ShellViewModel.IsRailExpanded)) UpdateShellRailWidth();
    }

    private void UpdateShellRailWidth()
    {
        if (_shellModel is not null)
            RailColumn.Width = new GridLength(_shellModel.IsRailExpanded ? 232 : 72);
    }

    private void OnRailChecked(object sender, RoutedEventArgs e)
    {
        if (!_syncingShellNavigation && _shellModel is not null && sender is NavRailItem item)
            _shellModel.SelectPage(int.Parse((string)item.Tag, System.Globalization.CultureInfo.InvariantCulture));
    }

    private void ApplyShellRoute()
    {
        if (_shellModel is null) return;
        _syncingShellNavigation = true;
        var items = new[] { NavOverview, NavServices, NavWorklog, NavChain, NavEvents, NavSettings };
        for (var index = 0; index < items.Length; index++)
            items[index].IsChecked = index == _shellModel.SelectedIndex;
        _syncingShellNavigation = false;
        var selected = _shellModel.SelectedIndex;
        var placeholder = selected == 3;
        LegacyContentHost.Visibility = selected is 2 or 3 ? Visibility.Collapsed : Visibility.Visible;
        HistoryContentHost.Visibility = selected == 2 ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderCard.Visibility = placeholder ? Visibility.Visible : Visibility.Collapsed;
        if (selected == 2)
        {
            _historyView ??= new WorklogHistoryView(new WorklogHistoryStore());
            HistoryContentHost.Content = _historyView;
            _historyView.Reload();
            return;
        }
        if (placeholder)
        {
            PlaceholderMessage.Text = "Bu sayfa hazırlanıyor. Bağlantıların durumu şimdilik Hizmetler sayfasında görünüyor.";
            PlaceholderServices.Visibility = Visibility.Visible;
            return;
        }

        switch (selected)
        {
            case 0: _navOverviewButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); break;
            case 1: _navServicesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); break;
            case 4: _navEventsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); break;
            case 5:
                ShowDashboard();
                if (_setupCard.Visibility == Visibility.Visible) _setupCard.BringIntoView();
                break;
        }
    }

    private void OnPlaceholderServices(object sender, RoutedEventArgs e) => _shellModel?.SelectPage(1);

    internal void OpenWorklogSession(string sessionId)
    {
        if (_shellModel?.SelectedIndex != 2) _shellModel?.SelectPage(2);
        else _historyView?.Reload();
        _historyView?.Model.SelectSession(sessionId);
    }

    private void OnOpenPalette(object sender, RoutedEventArgs e)
    {
        if (_shellModel is null) return;
        if (Palette.IsOpen) Palette.IsOpen = false;
        else
        {
            UpdateShellCommands();
            Palette.Open(PaletteButton, _shellModel, ActualHeight);
        }
    }

    private void UpdateShellCommands()
    {
        if (_shellModel is null) return;
        var commands = new List<ShellCommand>
        {
            new("Yenile", RefreshDashboardAsync),
            new("Ham günlüğü aç", () =>
            {
                _shellModel.SelectPage(4);
                ApplyShellRoute();
                _eventsExpander.IsExpanded = true;
                _rawLogExpander.IsExpanded = true;
                _rawLogExpander.BringIntoView();
                return Task.CompletedTask;
            }),
        };
        if (_snapshot is { } snapshot)
        {
            _shellModel.AttentionCount = snapshot.AttentionCount + snapshot.OfflineCount;
            foreach (var state in snapshot.Mcps)
            {
                commands.Add(new ShellCommand(state.Registration.DisplayName + " — ayrıntılar", async () =>
                {
                    _shellModel.SelectPage(1);
                    ApplyShellRoute();
                    await ShowDetailAsync(state);
                }));
            }
            var admin = snapshot.Mcps.FirstOrDefault(state => state.Registration.Id == "talvora-admin");
            if (admin is not null)
                commands.Add(new ShellCommand("Talvora Admin'i yeniden başlat", async () =>
                {
                    _shellModel.SelectPage(1);
                    ApplyShellRoute();
                    await ShowDetailAsync(admin);
                    await ExecuteDetailLifecycleOperationAsync(ManagedMcpLifecycleOperation.Restart);
                }));
        }
        _shellModel.SetServiceCommands(commands);
    }

    internal bool HandleShellNavigationKey(Key key, ModifierKeys modifiers)
    {
        if (_shellModel is null || modifiers != ModifierKeys.Control || key < Key.D1 || key > Key.D5)
            return false;
        return _shellModel.NavigateShortcut((int)key - (int)Key.D1 + 1);
    }

    private async void OnShellPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_shellModel is null) return;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (HandleShellNavigationKey(e.Key, Keyboard.Modifiers))
            {
                e.Handled = true;
                return;
            }
            if (e.Key == Key.K)
            {
                OnOpenPalette(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F)
            {
                if (_shellModel.SelectedIndex == 4) { _eventSearchBox.Focus(); _eventSearchBox.SelectAll(); }
                else if (_shellModel.SelectedIndex == 2) _historyView?.FocusSearch();
                else if (_shellModel.SelectedIndex is 0 or 1) { _searchBox.Focus(); _searchBox.SelectAll(); }
                e.Handled = true;
                return;
            }
        }
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            await _shellModel.RefreshCommand.ExecuteAsync(null);
        }
        else if (e.Key == Key.Escape && Palette.IsOpen)
        {
            Palette.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt &&
                 _detailScroller.Visibility == Visibility.Visible)
        {
            ShowDashboard();
            _shellModel.SelectPage(1);
            e.Handled = true;
        }
    }

    internal void SynchronizeShellThemeChoice()
    {
        if (ThemeChoice is null) return;
        _syncingShellTheme = true;
        ThemeChoice.SelectedIndex = (int)TalvoraThemeManager.Preference;
        _syncingShellTheme = false;
    }

    private async void OnShellThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingShellTheme || _shellModel is null || ThemeChoice.SelectedIndex < 0) return;
        await TalvoraThemeManager.SetPreferenceAsync((TalvoraThemePreference)ThemeChoice.SelectedIndex);
    }
}
