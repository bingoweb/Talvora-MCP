using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Media;
using Talvora.Shared;
using FormsCursor = System.Windows.Forms.Cursor;
using FormsPoint = System.Drawing.Point;
using FormsRectangle = System.Drawing.Rectangle;
using FormsScreen = System.Windows.Forms.Screen;

namespace Talvora.Tray;

internal sealed partial class DesktopProgressNotificationPresenter : IDisposable
{
    private const int MaximumVisibleCardsPerLane = 1;
    private const int MaximumPlacementDocumentBytes = 16 * 1024;
    private const int ScreenMarginPixels = 18;
    private const int CardGapPixels = 10;
    private const int PlacementDocumentVersion = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private sealed record DesktopProgressPlacementDocument(
        int Version,
        bool IsPinned,
        int LeftPixels,
        int TopPixels)
    {
        public bool HasUserSize { get; init; }

        public double? WidthDip { get; init; }

        public double? HeightDip { get; init; }
    }

    private static readonly JsonSerializerOptions PlacementJsonOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

    private static string PlacementPath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "ControlCenter",
            "desktop-progress-placement.json");

    private readonly ControlCenterApplication _application;
    private readonly string _placementPath;
    private readonly Dictionary<string, DesktopProgressNotificationWindow> _windows =
        new(StringComparer.Ordinal);
    private readonly DesktopProgressPresentationState _presentationState =
        new();
    private Task _placementSaveTask = Task.CompletedTask;
    private bool _isPinned;
    private int _anchorLeftPixels;
    private int _anchorTopPixels;
    private bool _hasPreferredSize;
    private double _preferredWidthDip;
    private double _preferredHeightDip;
    private string? _automaticScreenDeviceName;
    private bool _disposed;

    public DesktopProgressNotificationPresenter(
        ControlCenterApplication application,
        string? placementPath = null)
    {
        _application = application;
        _placementPath = string.IsNullOrWhiteSpace(placementPath)
            ? PlacementPath
            : Path.GetFullPath(placementPath);
        RestorePlacement();
    }

    public void Publish(DesktopProgressMessage message)
    {
        if (_application.Dispatcher.HasShutdownStarted ||
            _application.Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (!_application.Dispatcher.CheckAccess())
        {
            try
            {
                _application.Dispatcher.BeginInvoke(
                    () => Publish(message));
            }
            catch (InvalidOperationException)
            {
            }
            return;
        }

        if (_disposed)
        {
            return;
        }

        try
        {
            PublishCore(message);
        }
        catch (Exception ex)
        {
            TrayLog.Write(
                $"Desktop progress notification publish failed. Operation={message.OperationId}",
                ex);

            if (_windows.Remove(
                    message.OperationId,
                    out var failedWindow))
            {
                try
                {
                    failedWindow.Close();
                }
                catch
                {
                }
            }
        }
    }

    private void PublishCore(DesktopProgressMessage message)
    {
        if (_presentationState.Evaluate(message) !=
            DesktopProgressPresentationDecision.Accept)
        {
            return;
        }

        if (!_windows.TryGetValue(
                message.OperationId,
                out var window))
        {
            _automaticScreenDeviceName ??=
                FormsScreen.FromPoint(
                    FormsCursor.Position).DeviceName;
            window = new DesktopProgressNotificationWindow();
            if (_hasPreferredSize)
            {
                window.ApplyPreferredSize(
                    _preferredWidthDip,
                    _preferredHeightDip);
            }
            window.SetPinned(_isPinned);
            window.UserMoveCompleted +=
                (_, _) => HandleUserMoveCompleted(window);
            window.UserResizeCompleted +=
                (_, _) => HandleUserResizeCompleted(window);
            window.PinStateChangeRequested +=
                pinned => HandlePinStateChangeRequested(window, pinned);
            window.Closed += (_, _) =>
            {
                if (window.WasManuallyClosed)
                {
                    _presentationState.MarkManualDismissed(
                        message.OperationId,
                        window.LastLane,
                        window.IsTerminal);
                }

                _windows.Remove(message.OperationId);
                if (_windows.Count == 0 && !_isPinned)
                {
                    _automaticScreenDeviceName = null;
                }
                Reposition();
            };
            _windows[message.OperationId] = window;
        }

        window.Update(message);

        if (!window.IsVisible)
        {
            window.Show();
        }

        TrimVisibleCards();
        Reposition();
        CaptureAutomaticPreferredSize(window);
    }

    private void TrimVisibleCards()
    {
        TrimVisibleCards(
            DesktopProgressLane.Worklog);
        TrimVisibleCards(
            DesktopProgressLane.Alert);
    }

    private void TrimVisibleCards(
        DesktopProgressLane lane)
    {
        while (_windows.Values.Count(
                   window => window.LastLane == lane) >
               MaximumVisibleCardsPerLane)
        {
            var oldest =
                _windows.Values
                    .Where(window => window.LastLane == lane)
                    .OrderByDescending(static window => window.IsTerminal)
                    .ThenBy(static window => window.LastUpdatedUtc)
                    .First();
            oldest.Close();
        }
    }

    private void Reposition()
    {
        if (_disposed || _windows.Count == 0)
        {
            return;
        }

        var ordered =
            _windows.Values
                .Where(static window => window.IsVisible)
                .OrderByDescending(static window => window.LastUpdatedUtc)
                .ToArray();

        if (ordered.Length == 0)
        {
            return;
        }

        foreach (var window in ordered)
        {
            window.UpdateLayout();
        }

        if (_isPinned &&
            TryGetPinnedScreen(out var pinnedScreen))
        {
            ApplyHeightConstraint(ordered, pinnedScreen);
            UpdateLayouts(ordered);
            PositionPinned(ordered, pinnedScreen);
            return;
        }

        if (_isPinned)
        {
            _isPinned = false;
            _automaticScreenDeviceName =
                FormsScreen.FromPoint(
                    FormsCursor.Position).DeviceName;
            SetPinnedStateForAllWindows();
            QueuePlacementSave();
        }

        var automaticScreen = ResolveAutomaticScreen();
        ApplyHeightConstraint(ordered, automaticScreen);
        UpdateLayouts(ordered);
        PositionAutomatically(ordered, automaticScreen);
    }

    private static void UpdateLayouts(
        IReadOnlyList<DesktopProgressNotificationWindow> windows)
    {
        foreach (var window in windows)
        {
            window.UpdateLayout();
        }
    }

    private static void PositionAutomatically(
        IReadOnlyList<DesktopProgressNotificationWindow> ordered,
        FormsScreen screen)
    {
        var workArea = screen.WorkingArea;
        var right = workArea.Right - ScreenMarginPixels;
        var bottom = workArea.Bottom - ScreenMarginPixels;

        foreach (var window in ordered)
        {
            if (!TryGetWindowRectangle(window, out var rectangle))
            {
                continue;
            }

            var left =
                Math.Max(
                    workArea.Left,
                    right - rectangle.Width);
            var top =
                Math.Max(
                    workArea.Top,
                    bottom - rectangle.Height);

            MoveWindow(window, left, top);
            bottom = top - CardGapPixels;
        }
    }

    private void PositionPinned(
        IReadOnlyList<DesktopProgressNotificationWindow> ordered,
        FormsScreen screen)
    {
        var workArea = screen.WorkingArea;
        if (!TryGetWindowRectangle(
                ordered[0],
                out var primaryRectangle))
        {
            return;
        }

        var left =
            ClampWindowCoordinate(
                _anchorLeftPixels,
                workArea.Left,
                workArea.Right,
                primaryRectangle.Width);
        var top =
            ClampWindowCoordinate(
                _anchorTopPixels,
                workArea.Top,
                workArea.Bottom,
                primaryRectangle.Height);

        MoveWindow(ordered[0], left, top);

        var anchorChanged =
            left != _anchorLeftPixels ||
            top != _anchorTopPixels;
        _anchorLeftPixels = left;
        _anchorTopPixels = top;

        var topOfUpperStack = top;
        var bottomOfLowerStack =
            top + primaryRectangle.Height;

        for (var index = 1; index < ordered.Count; index++)
        {
            var window = ordered[index];
            if (!TryGetWindowRectangle(window, out var rectangle))
            {
                continue;
            }

            var stackedLeft =
                ClampWindowCoordinate(
                    left,
                    workArea.Left,
                    workArea.Right,
                    rectangle.Width);
            var candidateAbove =
                topOfUpperStack -
                CardGapPixels -
                rectangle.Height;

            int stackedTop;
            if (candidateAbove >= workArea.Top)
            {
                stackedTop = candidateAbove;
                topOfUpperStack = stackedTop;
            }
            else
            {
                stackedTop =
                    ClampWindowCoordinate(
                        bottomOfLowerStack + CardGapPixels,
                        workArea.Top,
                        workArea.Bottom,
                        rectangle.Height);
                bottomOfLowerStack =
                    stackedTop + rectangle.Height;
            }

            MoveWindow(window, stackedLeft, stackedTop);
        }

        if (anchorChanged)
        {
            QueuePlacementSave();
        }
    }

    private void HandleUserMoveCompleted(
        DesktopProgressNotificationWindow window)
    {
        if (_disposed ||
            !TryGetWindowRectangle(window, out var rectangle))
        {
            return;
        }

        window.MarkInteracted();
        _isPinned = true;
        _anchorLeftPixels = rectangle.Left;
        _anchorTopPixels = rectangle.Top;
        SetPinnedStateForAllWindows();
        QueuePlacementSave();
        Reposition();
    }

    private void HandleUserResizeCompleted(
        DesktopProgressNotificationWindow window)
    {
        if (_disposed ||
            !TryGetWindowRectangle(window, out var rectangle) ||
            !double.IsFinite(window.ActualWidth) ||
            !double.IsFinite(window.ActualHeight) ||
            window.ActualWidth <= 0 ||
            window.ActualHeight <= 0)
        {
            return;
        }

        _isPinned = true;
        _anchorLeftPixels = rectangle.Left;
        _anchorTopPixels = rectangle.Top;
        _preferredWidthDip = Math.Clamp(
            window.ActualWidth,
            window.MinWidth,
            window.MaxWidth);
        _preferredHeightDip = Math.Clamp(
            window.ActualHeight,
            window.MinHeight,
            window.MaxHeight);
        _hasPreferredSize = true;
        window.MarkInteracted();
        SetPinnedStateForAllWindows();
        QueuePlacementSave();
        Reposition();
    }

    private void HandlePinStateChangeRequested(
        DesktopProgressNotificationWindow window,
        bool pinned)
    {
        if (_disposed)
        {
            return;
        }

        if (pinned)
        {
            if (!TryGetWindowRectangle(window, out var rectangle))
            {
                return;
            }

            window.MarkInteracted();
            _anchorLeftPixels = rectangle.Left;
            _anchorTopPixels = rectangle.Top;
        }
        else
        {
            _automaticScreenDeviceName =
                FormsScreen.FromPoint(
                    FormsCursor.Position).DeviceName;
        }

        _isPinned = pinned;
        SetPinnedStateForAllWindows();
        QueuePlacementSave();
        Reposition();
    }

    private void CaptureAutomaticPreferredSize(
        DesktopProgressNotificationWindow window)
    {
        if (_disposed)
        {
            return;
        }

        if (!window.AutomaticSizePersistenceRequested)
        {
            return;
        }

        window.UpdateLayout();
        if (!double.IsFinite(window.ActualWidth) ||
            !double.IsFinite(window.ActualHeight) ||
            window.ActualWidth <= 0 ||
            window.ActualHeight <= 0)
        {
            return;
        }

        _preferredWidthDip = Math.Clamp(
            window.ActualWidth,
            window.MinWidth,
            window.MaxWidth);
        _preferredHeightDip = Math.Clamp(
            window.ActualHeight,
            window.MinHeight,
            window.MaxHeight);
        _hasPreferredSize = true;
        window.MarkAutomaticSizePersistenceHandled();
        QueuePlacementSave();
    }

    private void SetPinnedStateForAllWindows()
    {
        foreach (var window in _windows.Values)
        {
            window.SetPinned(_isPinned);
        }
    }

    private bool TryGetPinnedScreen(out FormsScreen screen)
    {
        var point =
            new FormsPoint(
                _anchorLeftPixels,
                _anchorTopPixels);

        foreach (var candidate in FormsScreen.AllScreens)
        {
            if (candidate.WorkingArea.Contains(point))
            {
                screen = candidate;
                return true;
            }
        }

        screen = FormsScreen.PrimaryScreen ??
            FormsScreen.FromPoint(FormsCursor.Position);
        return false;
    }

    private FormsScreen ResolveAutomaticScreen()
    {
        if (!string.IsNullOrWhiteSpace(
                _automaticScreenDeviceName))
        {
            var existing = FormsScreen.AllScreens.FirstOrDefault(
                candidate => string.Equals(
                    candidate.DeviceName,
                    _automaticScreenDeviceName,
                    StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }
        }

        var resolved = FormsScreen.FromPoint(
            FormsCursor.Position);
        _automaticScreenDeviceName = resolved.DeviceName;
        return resolved;
    }

    private static void ApplyHeightConstraint(
        IReadOnlyList<DesktopProgressNotificationWindow> windows,
        FormsScreen screen)
    {
        var visibleCount = Math.Max(1, windows.Count);
        var stackGapPixels =
            CardGapPixels * Math.Max(0, visibleCount - 1);
        var usablePixels = Math.Max(
            180,
            screen.WorkingArea.Height -
            (ScreenMarginPixels * 2) -
            stackGapPixels);
        var perWindowPixelBudget =
            Math.Max(180.0, usablePixels / (double)visibleCount);

        foreach (var window in windows)
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var scaleY = Math.Max(0.5, dpi.DpiScaleY);
            var scaleX = Math.Max(0.5, dpi.DpiScaleX);
            var availableDip =
                perWindowPixelBudget / scaleY;
            var maximumHeightDip = Math.Max(
                150,
                Math.Min(900, availableDip));
            window.MinHeight = Math.Min(window.MinHeight, maximumHeightDip);
            window.MaxHeight = maximumHeightDip;
            var availableWidthDip =
                Math.Max(
                    360,
                    (screen.WorkingArea.Width -
                     (ScreenMarginPixels * 2.0)) / scaleX);
            window.MaxWidth = Math.Max(
                360,
                Math.Min(1200, availableWidthDip));
        }
    }

    private void RestorePlacement()
    {
        var path = _placementPath;
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var saved =
                JsonFileStore.ReadBounded<DesktopProgressPlacementDocument>(
                    path,
                    MaximumPlacementDocumentBytes,
                    PlacementJsonOptions);
            if (saved.Version is < 1 or > PlacementDocumentVersion)
            {
                return;
            }

            _isPinned = saved.IsPinned;
            _anchorLeftPixels = saved.LeftPixels;
            _anchorTopPixels = saved.TopPixels;
            if (saved.Version >= 2 &&
                saved.HasUserSize &&
                saved.WidthDip is { } savedWidth &&
                saved.HeightDip is { } savedHeight &&
                double.IsFinite(savedWidth) &&
                double.IsFinite(savedHeight) &&
                savedWidth > 0 &&
                savedHeight > 0)
            {
                _hasPreferredSize = true;
                _preferredWidthDip = savedWidth;
                _preferredHeightDip = savedHeight;
            }

            if (_isPinned &&
                !FormsScreen.AllScreens.Any(
                    screen =>
                        screen.WorkingArea.Contains(
                            new FormsPoint(
                                _anchorLeftPixels,
                                _anchorTopPixels))))
            {
                _isPinned = false;
                QueuePlacementSave();
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException)
        {
            TrayLog.Write(
                "Desktop progress placement could not be restored.",
                ex);
        }
    }

    private void QueuePlacementSave()
    {
        var snapshot =
            new DesktopProgressPlacementDocument(
                PlacementDocumentVersion,
                _isPinned,
                _anchorLeftPixels,
                _anchorTopPixels)
            {
                HasUserSize = _hasPreferredSize,
                WidthDip = _hasPreferredSize
                    ? _preferredWidthDip
                    : null,
                HeightDip = _hasPreferredSize
                    ? _preferredHeightDip
                    : null,
            };

        _placementSaveTask =
            _placementSaveTask
                .ContinueWith(
                    _ => SavePlacementAsync(
                        _placementPath,
                        snapshot),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default)
                .Unwrap();
    }

    private static async Task SavePlacementAsync(
        string path,
        DesktopProgressPlacementDocument snapshot)
    {
        try
        {
            await JsonFileStore.WriteAsync(
                    path,
                    snapshot,
                    PlacementJsonOptions,
                    createBackup: false,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            TrayLog.Write(
                "Desktop progress placement could not be saved.",
                ex);
        }
    }

    private static int ClampWindowCoordinate(
        int value,
        int workAreaStart,
        int workAreaEnd,
        int windowSize)
    {
        var maximum =
            Math.Max(
                workAreaStart,
                workAreaEnd - Math.Max(1, windowSize));
        return Math.Clamp(
            value,
            workAreaStart,
            maximum);
    }

    private static bool TryGetWindowRectangle(
        DesktopProgressNotificationWindow window,
        out FormsRectangle rectangle)
    {
        rectangle = FormsRectangle.Empty;
        var handle =
            new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero ||
            !GetWindowRect(handle, out var nativeRectangle))
        {
            return false;
        }

        rectangle =
            FormsRectangle.FromLTRB(
                nativeRectangle.Left,
                nativeRectangle.Top,
                nativeRectangle.Right,
                nativeRectangle.Bottom);
        return rectangle.Width > 0 &&
            rectangle.Height > 0;
    }

    private static void MoveWindow(
        DesktopProgressNotificationWindow window,
        int left,
        int top)
    {
        var handle =
            new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!SetWindowPos(
                handle,
                IntPtr.Zero,
                left,
                top,
                0,
                0,
                SwpNoSize |
                SwpNoZOrder |
                SwpNoActivate |
                SwpNoOwnerZOrder))
        {
            TrayLog.Write(
                $"Desktop progress window could not be positioned. Win32Error={Marshal.GetLastPInvokeError()}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var window in _windows.Values.ToArray())
        {
            window.Close();
        }

        _windows.Clear();

        try
        {
            if (!_placementSaveTask.Wait(
                    TimeSpan.FromSeconds(2)))
            {
                TrayLog.Write(
                    "Desktop progress placement save exceeded the shutdown wait budget.");
            }
        }
        catch (Exception ex)
        {
            TrayLog.Write(
                "Desktop progress placement save did not finish cleanly during shutdown.",
                ex);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport(
        "user32.dll",
        EntryPoint = "GetWindowRect",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(
        IntPtr windowHandle,
        out NativeRectangle rectangle);

    [LibraryImport(
        "user32.dll",
        EntryPoint = "SetWindowPos",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
