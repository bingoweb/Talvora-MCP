using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Talvora.Tray;

internal sealed partial class ControlCenterWindow
{
    private sealed record WindowPlacementDocument(
        double Left,
        double Top,
        double Width,
        double Height,
        bool Maximized);

    private static readonly JsonSerializerOptions WindowPlacementJsonOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

    private static string WindowPlacementPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "ControlCenter",
            "window-placement.json");

    internal static bool HasSavedWindowPlacement =>
        File.Exists(WindowPlacementPath);

    private void InitializeWindowPlacementPersistence()
    {
        _windowPlacementSaveTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(450),
            IsEnabled = false,
        };

        _windowPlacementSaveTimer.Tick += async (_, _) =>
        {
            _windowPlacementSaveTimer.Stop();
            await SaveWindowPlacementAsync();
        };
    }

    private void RestoreWindowPlacement()
    {
        _restoringWindowPlacement = true;
        try
        {
            var path = WindowPlacementPath;
            if (!File.Exists(path))
            {
                return;
            }

            WindowPlacementDocument? saved;
            try
            {
                saved = JsonSerializer.Deserialize<WindowPlacementDocument>(
                    File.ReadAllText(path),
                    WindowPlacementJsonOptions);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                JsonException)
            {
                TrayLog.Write("Control Center window placement could not be read", ex);
                return;
            }

            if (saved is null ||
                !IsFinite(saved.Left) ||
                !IsFinite(saved.Top) ||
                !IsFinite(saved.Width) ||
                !IsFinite(saved.Height))
            {
                return;
            }

            Width = Math.Clamp(
                saved.Width,
                MinWidth,
                Math.Max(MinWidth, SystemParameters.VirtualScreenWidth));
            Height = Math.Clamp(
                saved.Height,
                MinHeight,
                Math.Max(MinHeight, SystemParameters.VirtualScreenHeight));

            var candidate = new Rect(saved.Left, saved.Top, Width, Height);
            if (IsPlacementVisible(candidate))
            {
                Left = saved.Left;
                Top = saved.Top;
            }
            else
            {
                CenterOnPrimaryWorkingArea();
            }

            WindowStartupLocation = WindowStartupLocation.Manual;

            if (saved.Maximized)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    () => WindowState = WindowState.Maximized);
            }
        }
        finally
        {
            _restoringWindowPlacement = false;
        }
    }

    private void ScheduleWindowPlacementSave()
    {
        if (_smokeMode ||
            _restoringWindowPlacement ||
            !IsLoaded ||
            WindowState == WindowState.Minimized ||
            _applicationExitRequested)
        {
            return;
        }

        _windowPlacementSaveTimer.Stop();
        _windowPlacementSaveTimer.Start();
    }

    private async Task SaveWindowPlacementAsync()
    {
        if (_smokeMode ||
            _restoringWindowPlacement ||
            WindowState == WindowState.Minimized)
        {
            return;
        }

        try
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;

            if (!IsFinite(bounds.Left) ||
                !IsFinite(bounds.Top) ||
                !IsFinite(bounds.Width) ||
                !IsFinite(bounds.Height) ||
                bounds.Width < MinWidth ||
                bounds.Height < MinHeight)
            {
                return;
            }

            var placement = new WindowPlacementDocument(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                WindowState == WindowState.Maximized);

            await Talvora.Shared.JsonFileStore.WriteAsync(
                WindowPlacementPath,
                placement,
                WindowPlacementJsonOptions,
                createBackup: false,
                CancellationToken.None);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            TrayLog.Write("Control Center window placement could not be saved", ex);
        }
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsPlacementVisible(Rect placement)
    {
        var virtualDesktop = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        var intersection = Rect.Intersect(placement, virtualDesktop);
        return intersection.Width >= 120 && intersection.Height >= 80;
    }

    private void CenterOnPrimaryWorkingArea()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
    }

    private void UpdateHeaderLayout()
    {
        if (_headerGrid is null ||
            _headerStatusCard is null ||
            _headerStatusStack is null)
        {
            return;
        }

        var compact = ActualWidth > 0 && ActualWidth < 900;

        if (compact)
        {
            Grid.SetRow(_headerStatusCard, 1);
            Grid.SetColumn(_headerStatusCard, 0);
            Grid.SetColumnSpan(_headerStatusCard, 2);
            _headerStatusCard.Margin = new Thickness(0, 16, 0, 0);
            _headerStatusCard.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            _headerStatusStack.MinWidth = 0;

            if (_dashboardSectionMeta is not null)
            {
                _dashboardSectionMeta.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            Grid.SetRow(_headerStatusCard, 0);
            Grid.SetColumn(_headerStatusCard, 1);
            Grid.SetColumnSpan(_headerStatusCard, 1);
            _headerStatusCard.Margin = new Thickness(24, 0, 0, 0);
            _headerStatusCard.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            _headerStatusStack.MinWidth = 250;

            if (_dashboardSectionMeta is not null)
            {
                _dashboardSectionMeta.Visibility = Visibility.Visible;
            }
        }
    }

    private void UpdateWindowStateVisuals()
    {
        if (_windowTitleBar is null)
        {
            return;
        }

        _windowTitleBar.CanMaximize = ResizeMode is ResizeMode.CanResize
            or ResizeMode.CanResizeWithGrip;
    }

    private async void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F5 ||
            (e.Key == Key.R && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            e.Handled = true;
            await RefreshDashboardAsync();
            return;
        }

        if (e.Key == Key.F &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            _dashboardScroller.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            _searchBox.Focus();
            _searchBox.SelectAll();
            return;
        }

        if ((e.Key == Key.Escape ||
             (e.Key == Key.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))) &&
            _detailScroller.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            ShowDashboard();
            return;
        }

        if (e.Key == Key.Escape &&
            _dashboardScroller.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            Hide();
        }
    }
}