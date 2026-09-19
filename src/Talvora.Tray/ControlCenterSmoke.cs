using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace Talvora.Tray;

internal static class ControlCenterSmoke
{
    public static int Run()
    {
        var result = 1;
        var application = new ControlCenterApplication(smokeTest: true);
        var timer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            (_, _) => { },
            application.Dispatcher);

        timer.Tick += async (_, _) =>
        {
            timer.Stop();

            try
            {
                if (application.MainWindow is not ControlCenterWindow window)
                {
                    throw new InvalidOperationException(
                        "Control Center main window was not created.");
                }

                if (!window.IsVisible)
                {
                    throw new InvalidOperationException(
                        "Control Center window did not become visible.");
                }

                AssertFluentResources(application);
                AssertWindowChrome(window);
                AssertWindowHasVisibleContent(window);
                AssertWindowDpi(window);
                AssertRenderedSurfaceIsNotBlank(window);

                await window.RunSmokeScenarioAsync();

                window.Close();
                if (window.IsVisible)
                {
                    throw new InvalidOperationException(
                        "Closing the Control Center should hide the window.");
                }

                application.ShowControlCenter();
                if (!window.IsVisible)
                {
                    throw new InvalidOperationException(
                        "Hidden Control Center window could not be shown again.");
                }

                result = 0;
                TrayLog.Write("Control Center visual smoke succeeded.");
            }
            catch (Exception ex)
            {
                TrayLog.Write("Control Center smoke failed", ex);
                result = 1;
            }
            finally
            {
                application.ExitFromTray();
            }
        };

        application.Dispatcher.BeginInvoke(() =>
        {
            application.ShowControlCenter();
            timer.Start();
        });

        _ = application.Run();
        return result;
    }

    private static void AssertFluentResources(ControlCenterApplication application)
    {
        var requiredResources = new[]
        {
            "TextFillColorPrimaryBrush",
            "TalvoraCardStyle",
            "TalvoraSecondaryButtonStyle",
            "TalvoraSearchBoxStyle",
        };

        foreach (var key in requiredResources)
        {
            if (application.TryFindResource(key) is null)
            {
                throw new InvalidOperationException(
                    $"Fluent resource '{key}' is not loaded.");
            }
        }
    }

    private static void AssertWindowChrome(ControlCenterWindow window)
    {
        var titleBar = FindVisualChild<TitleBar>(window)
            ?? throw new InvalidOperationException(
                "Control Center title bar was not rendered.");

        if (!window.ExtendsContentIntoTitleBar ||
            !titleBar.ShowClose ||
            !titleBar.ShowMinimize ||
            !titleBar.ShowMaximize ||
            !titleBar.CanMaximize)
        {
            throw new InvalidOperationException(
                "Control Center window chrome is incomplete.");
        }

        if (titleBar.ActualHeight < 30)
        {
            throw new InvalidOperationException(
                "Control Center title bar does not have a usable drag surface.");
        }
    }

    private static void AssertWindowDpi(ControlCenterWindow window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        if (dpi.DpiScaleX <= 0 ||
            dpi.DpiScaleY <= 0 ||
            dpi.PixelsPerDip <= 0)
        {
            throw new InvalidOperationException(
                "Control Center DPI information is invalid.");
        }
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
            {
                return typed;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void AssertWindowHasVisibleContent(ControlCenterWindow window)
    {
        window.UpdateLayout();

        if (window.Content is not Grid root || root.Children.Count < 2)
        {
            throw new InvalidOperationException(
                "Control Center visual tree is empty.");
        }

        if (window.ActualWidth < 400 || window.ActualHeight < 300)
        {
            throw new InvalidOperationException(
                "Control Center did not receive a usable render size.");
        }
    }

    private static void AssertRenderedSurfaceIsNotBlank(
        ControlCenterWindow window)
    {
        var width = Math.Max(1, (int)Math.Round(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Round(window.ActualHeight));

        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);

        bitmap.Render(window);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        long sampled = 0;
        long nearWhite = 0;
        long dark = 0;

        var step = Math.Max(1, Math.Min(width, height) / 160);
        for (var y = 0; y < height; y += step)
        {
            for (var x = 0; x < width; x += step)
            {
                var index = (y * stride) + (x * 4);
                var blue = pixels[index];
                var green = pixels[index + 1];
                var red = pixels[index + 2];
                var alpha = pixels[index + 3];

                if (alpha == 0)
                {
                    continue;
                }

                sampled++;

                if (red >= 245 && green >= 245 && blue >= 245)
                {
                    nearWhite++;
                }

                if (red <= 70 && green <= 80 && blue <= 95)
                {
                    dark++;
                }
            }
        }

        if (sampled == 0)
        {
            throw new InvalidOperationException(
                "Control Center render produced no visible pixels.");
        }

        var whiteRatio = nearWhite / (double)sampled;
        var darkRatio = dark / (double)sampled;

        if (whiteRatio > 0.70 || darkRatio < 0.20)
        {
            throw new InvalidOperationException(
                $"Control Center render looks blank/light. WhiteRatio={whiteRatio:P1}; DarkRatio={darkRatio:P1}.");
        }
    }
}