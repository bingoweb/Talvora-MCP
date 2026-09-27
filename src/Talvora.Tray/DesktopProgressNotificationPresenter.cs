using System.Windows.Media;
using Talvora.Shared;
using FormsCursor = System.Windows.Forms.Cursor;
using FormsScreen = System.Windows.Forms.Screen;

namespace Talvora.Tray;

internal sealed class DesktopProgressNotificationPresenter : IDisposable
{
    private const int MaximumVisibleCards = 4;
    private const double ScreenMargin = 18;
    private const double CardGap = 10;

    private readonly ControlCenterApplication _application;
    private readonly Dictionary<string, DesktopProgressNotificationWindow> _windows =
        new(StringComparer.Ordinal);
    private bool _disposed;

    public DesktopProgressNotificationPresenter(
        ControlCenterApplication application)
    {
        _application = application;
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

        if (!_windows.TryGetValue(
                message.OperationId,
                out var window))
        {
            window = new DesktopProgressNotificationWindow();
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
                    .OrderBy(static window => window.LastUpdatedUtc)
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

        var screen =
            FormsScreen.FromPoint(FormsCursor.Position);
        var workArea = screen.WorkingArea;
        var ordered =
            _windows.Values
                .Where(static window => window.IsVisible)
                .OrderByDescending(static window => window.LastUpdatedUtc)
                .ToArray();

        var bottom = double.NaN;
        var right = double.NaN;

        foreach (var window in ordered)
        {
            window.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(window);

            if (double.IsNaN(bottom))
            {
                bottom =
                    (workArea.Bottom / dpi.DpiScaleY) -
                    ScreenMargin;
                right =
                    (workArea.Right / dpi.DpiScaleX) -
                    ScreenMargin;
            }

            var height =
                Math.Max(window.ActualHeight, window.DesiredSize.Height);
            window.Left = right - window.Width;
            window.Top = bottom - height;
            bottom = window.Top - CardGap;
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
    }
}
