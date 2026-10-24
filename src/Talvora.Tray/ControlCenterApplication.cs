using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using WpfApplication = System.Windows.Application;

namespace Talvora.Tray;

internal sealed class ControlCenterApplication : WpfApplication
{
    private readonly bool _smokeTest;
    private ShellWindow? _window;
    private bool _exitRequested;

    public ControlCenterApplication(bool smokeTest = false)
    {
        _smokeTest = smokeTest;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        TalvoraThemeManager.Initialize(this, smokeTest);

        Resources.MergedDictionaries.Add(
            new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/Talvora.Tray;component/ControlCenterTheme.xaml",
                    UriKind.Absolute),
            });

        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    public void ShowControlCenter()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(ShowControlCenter);
            return;
        }

        if (_exitRequested)
        {
            return;
        }

        if (_window is null)
        {
            var window = new ShellWindow(_smokeTest);
            _window = window;
            window.Loaded += (_, _) => TalvoraThemeManager.Watch(window);
            window.Closed += (_, _) =>
            {
                TalvoraThemeManager.UnWatch(window);
                if (_window == window) _window = null;
            };
            MainWindow = window;
        }

        _window.BringToForeground();
    }

    public void ExitFromTray()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(ExitFromTray);
            return;
        }

        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        _ = ExitFromTrayCoreAsync();
    }

    private async Task ExitFromTrayCoreAsync()
    {
        try
        {
            if (_window is not null)
            {
                await _window.PrepareForApplicationExitAsync();
                _window.Close();
                _window = null;
            }
        }
        catch (Exception ex)
        {
            TrayLog.Write("Control Center graceful shutdown failed", ex);
        }
        finally
        {
            Shutdown();
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        TrayLog.Write("Control Center dispatcher failure", e.Exception);
        if (IsFatalUiException(e.Exception))
        {
            return;
        }

        e.Handled = true;
        _window?.ReportRecoverableUiFailure();
    }

    private static bool IsFatalUiException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;
}