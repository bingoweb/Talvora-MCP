using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using Talvora.Shared;
using FormsCursor = System.Windows.Forms.Cursor;
using FormsPoint = System.Drawing.Point;
using FormsRectangle = System.Drawing.Rectangle;
using FormsScreen = System.Windows.Forms.Screen;

namespace Talvora.Tray;

internal sealed partial class DesktopProgressNotificationPresenter : IDisposable
{
    private const int MaximumVisibleCards = 1;
    private const int ScreenMarginPixels = 18;
    private const int CardGapPixels = 10;
    private const int PlacementDocumentVersion = 1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private sealed record DesktopProgressPlacementDocument(
        int Version,
        bool IsPinned,
        int LeftPixels,
        int TopPixels);

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
    private readonly Dictionary<string, DesktopProgressNotificationWindow> _windows =
        new(StringComparer.Ordinal);
    private Task _placementSaveTask = Task.CompletedTask;
    private bool _isPinned;
    private int _anchorLeftPixels;
    private int _anchorTopPixels;
    private bool _disposed;

    public DesktopProgressNotificationPresenter(
        ControlCenterApplication application)
    {
        _application = application;
        RestorePlacement();
    }

    public void Publish(DesktopProgressMessage message)
    {
        if (!_application.Dispatcher.CheckAccess())
        {
            _application.Dispatcher.BeginInvoke(
                () => Publish(message));
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
        if (!_windows.TryGetValue(
                message.OperationId,
                out var window))
        {
            window = new DesktopProgressNotificationWindow();
            window.SetPinned(_isPinned);
            window.UserMoveCompleted +=
                (_, _) => HandleUserMoveCompleted(window);
            window.PinStateChangeRequested +=
                pinned => HandlePinStateChangeRequested(window, pinned);
            window.Closed += (_, _) =>
            {
                _windows.Remove(message.OperationId);
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
    }

    private void TrimVisibleCards()
    {
        while (_windows.Count > MaximumVisibleCards)
        {
            var oldest =
                _windows.Values
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
            PositionPinned(ordered, pinnedScreen);
            return;
        }

        if (_isPinned)
        {
            _isPinned = false;
            SetPinnedStateForAllWindows();
            QueuePlacementSave();
        }

        PositionAutomatically(ordered);
    }

    private static void PositionAutomatically(
        IReadOnlyList<DesktopProgressNotificationWindow> ordered)
    {
        var workArea =
            FormsScreen.FromPoint(FormsCursor.Position).WorkingArea;
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

        _isPinned = pinned;
        SetPinnedStateForAllWindows();
        QueuePlacementSave();
        Reposition();
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

    private void RestorePlacement()
    {
        var path = PlacementPath;
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var saved =
                JsonSerializer.Deserialize<DesktopProgressPlacementDocument>(
                    File.ReadAllText(path),
                    PlacementJsonOptions);
            if (saved is null ||
                saved.Version != PlacementDocumentVersion)
            {
                return;
            }

            _isPinned = saved.IsPinned;
            _anchorLeftPixels = saved.LeftPixels;
            _anchorTopPixels = saved.TopPixels;

            if (_isPinned &&
                !FormsScreen.AllScreens.Any(
                    screen =>
                        screen.WorkingArea.Contains(
                            new FormsPoint(
                                _anchorLeftPixels,
                                _anchorTopPixels))))
            {
                _isPinned = false;
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
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
                _anchorTopPixels);

        _placementSaveTask =
            _placementSaveTask
                .ContinueWith(
                    _ => SavePlacementAsync(snapshot),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default)
                .Unwrap();
    }

    private static async Task SavePlacementAsync(
        DesktopProgressPlacementDocument snapshot)
    {
        try
        {
            await JsonFileStore.WriteAsync(
                    PlacementPath,
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
            _placementSaveTask
                .GetAwaiter()
                .GetResult();
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
