using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Talvora.Shared;

namespace Talvora.Tray;

internal sealed partial class ShellWindow
{
    private const int MaximumWindowPlacementBytes =
        64 * 1024;

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

        _windowPlacementSaveTimer.Tick += OnWindowPlacementSaveTimerTick;
    }

    private async void OnWindowPlacementSaveTimerTick(object? sender, EventArgs e)
    {
        _windowPlacementSaveTimer.Stop();
        await SaveWindowPlacementAsync();
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
                saved = JsonFileStore.ReadBounded<WindowPlacementDocument>(
                    path,
                    MaximumWindowPlacementBytes,
                    WindowPlacementJsonOptions);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
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

            var saveVersion = Interlocked.Increment(
                ref _windowPlacementSaveVersion);

            await _windowPlacementSaveGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (saveVersion != Volatile.Read(
                        ref _windowPlacementSaveVersion))
                {
                    return;
                }

                await Talvora.Shared.JsonFileStore.WriteAsync(
                    WindowPlacementPath,
                    placement,
                    WindowPlacementJsonOptions,
                    createBackup: false,
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _windowPlacementSaveGate.Release();
            }
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

    private void UpdateWindowStateVisuals()
    {
        if (_windowTitleBar is null)
        {
            return;
        }

        _windowTitleBar.CanMaximize = ResizeMode is ResizeMode.CanResize
            or ResizeMode.CanResizeWithGrip;
    }
}
