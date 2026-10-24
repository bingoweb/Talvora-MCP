using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
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

                SaveVisualSnapshotIfRequested(window);
                AssertFluentResources(application);
                AssertWindowChrome(window);
                AssertWindowHasVisibleContent(window);
                AssertWindowDpi(window);
                AssertRenderedSurfaceIsNotBlank(window);
                AssertStitchNavigation(window);

                // UI1: swap the running window's real palette and restore it, without reopening.
                var initialPreference = TalvoraThemeManager.Preference;
                var contentRoot = (Grid)window.Content;
                var initialBackground = ((SolidColorBrush)contentRoot.Background).Color;
                var alternatePreference = initialBackground ==
                    System.Windows.Media.Color.FromRgb(0x0A, 0x0E, 0x15)
                    ? TalvoraThemePreference.Gun : TalvoraThemePreference.Gece;
                await TalvoraThemeManager.SetPreferenceAsync(alternatePreference, persist: true);
                if (((SolidColorBrush)contentRoot.Background).Color == initialBackground)
                    throw new InvalidOperationException("Control Center theme did not change live.");
                var smokePreferencePath = TalvoraThemeManager.SmokePreferencePath
                    ?? throw new InvalidOperationException("Theme smoke has no isolated preference path.");
                try
                {
                    if (!File.Exists(smokePreferencePath))
                        throw new InvalidOperationException("Theme preference was not persisted.");
                    using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(smokePreferencePath));
                    var name = saved.RootElement.GetProperty("preference").GetString();
                    if (name != alternatePreference.ToString())
                        throw new InvalidOperationException("Persisted theme preference does not match the selected theme.");
                }
                finally
                {
                    var folder = Path.GetDirectoryName(smokePreferencePath)!;
                    if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                }
                await TalvoraThemeManager.SetPreferenceAsync(initialPreference, persist: false);
                if (((SolidColorBrush)contentRoot.Background).Color != initialBackground)
                    throw new InvalidOperationException("Control Center theme did not restore live.");

                await window.RunSmokeScenarioAsync();
                SaveVisualSnapshotIfRequested(window, "loaded");
                AssertDashboardStructure(window);
                AssertRenderedSurfaceIsNotBlank(window);
                AssertCompactDashboardLayout(window);

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
            "TalvoraSidebarButtonStyle",
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

    private static void AssertStitchNavigation(ControlCenterWindow window)
    {
        foreach (var name in new[] { "Genel Bakış", "MCP Hizmetleri", "Olaylar" })
        {
            var link = FindVisualChildByAutomationName(window, name);
            AssertAutomationElement(link);
            if (!link.IsEnabled || link.ActualWidth < 80 || link.ActualHeight < 30)
            {
                throw new InvalidOperationException(
                    $"Stitch navigation item '{name}' is not usable.");
            }
        }
    }

    private static void SaveVisualSnapshotIfRequested(
        ControlCenterWindow window,
        string? variant = null)
    {
        var path = Environment.GetEnvironmentVariable(
            "TALVORA_CONTROL_CENTER_PREVIEW_PNG");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Round(window.ActualWidth)),
            Math.Max(1, (int)Math.Round(window.ActualHeight)),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var output = Path.GetFullPath(path);
        if (!string.IsNullOrWhiteSpace(variant))
        {
            output = Path.Combine(
                Path.GetDirectoryName(output)!,
                Path.GetFileNameWithoutExtension(output) +
                "-" + variant +
                Path.GetExtension(output));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var file = File.Create(output);
        encoder.Save(file);
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

    private static void AssertDashboardStructure(ControlCenterWindow window)
    {
        window.UpdateLayout();
        var root = (Grid)window.Content;
        var header = root.Children.OfType<Border>().Single(element =>
            Grid.GetRow(element) == 1 && Grid.GetColumn(element) == 1);
        var statusCard = ((Grid)header.Child).Children.OfType<Border>().Single();
        var status = FindVisualChild<System.Windows.Controls.TextBlock>(statusCard)
            ?? throw new InvalidOperationException(
                "Kontrol Merkezi durum başlığı görüntülenmedi.");
        AssertAutomationElement(status);

        var serviceRows = EnumerateVisualElements(window)
            .OfType<Border>()
            .Where(element => element.Tag is string &&
                !string.IsNullOrWhiteSpace(AutomationProperties.GetName(element)))
            .ToArray();
        if (serviceRows.Length == 0)
        {
            throw new InvalidOperationException(
                "Kontrol Merkezi hiçbir hizmet satırı görüntülemedi.");
        }
        AssertAutomationElement(serviceRows[0]);

        foreach (var element in EnumerateVisualElements(window))
        {
            // Inspect application controls, not WPF template geometry such as zero-length scroll tracks.
            if (!element.IsVisible || element.TemplatedParent is not null)
            {
                continue;
            }
            var peer = UIElementAutomationPeer.CreatePeerForElement(element);
            if (peer is not null && peer.IsControlElement() && !peer.IsOffscreen())
            {
                AssertAutomationBounds(peer);
            }
        }
    }

    private static void AssertAutomationElement(FrameworkElement element)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(element)
            ?? new FrameworkElementAutomationPeer(element);
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0 ||
            string.IsNullOrWhiteSpace(peer.GetName()))
        {
            throw new InvalidOperationException(
                "Kontrol Merkezi öğesinin erişilebilir adı veya görünür boyutu eksik.");
        }
        AssertAutomationBounds(peer);
    }

    private static void AssertAutomationBounds(AutomationPeer peer)
    {
        var bounds = peer.GetBoundingRectangle();
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException(
                $"Kontrol Merkezi öğesinin boyutu sıfır: {peer.GetName()} ({peer.GetClassName()}, {peer.GetAutomationControlType()}, {bounds}).");
        }
    }

    private static IEnumerable<FrameworkElement> EnumerateVisualElements(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element)
            {
                yield return element;
            }
            foreach (var descendant in EnumerateVisualElements(child))
            {
                yield return descendant;
            }
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

    private static void AssertCompactDashboardLayout(
        ControlCenterWindow window)
    {
        var originalWidth = window.Width;
        try
        {
            window.Width = window.MinWidth;
            window.UpdateLayout();

            var search = FindVisualChildByAutomationName(
                window,
                "Yönetilen bileşenlerde ara");
            var refresh = FindVisualChildByAutomationName(
                window,
                "Bileşen durumlarını yenile");

            AssertElementFitsHorizontally(window, search);
            AssertElementFitsHorizontally(window, refresh);

            var searchOrigin = search
                .TransformToAncestor(window)
                .Transform(new System.Windows.Point(0, 0));
            var refreshOrigin = refresh
                .TransformToAncestor(window)
                .Transform(new System.Windows.Point(0, 0));
            if (searchOrigin.Y >= refreshOrigin.Y)
            {
                throw new InvalidOperationException(
                    "Compact dashboard toolbar did not move its actions below the search field.");
            }

            AssertRenderedSurfaceIsNotBlank(window);
        }
        finally
        {
            window.Width = originalWidth;
            window.UpdateLayout();
        }
    }

    private static FrameworkElement FindVisualChildByAutomationName(
        DependencyObject root,
        string automationName)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element &&
                string.Equals(
                    AutomationProperties.GetName(element),
                    automationName,
                    StringComparison.Ordinal))
            {
                return element;
            }

            try
            {
                return FindVisualChildByAutomationName(
                    child,
                    automationName);
            }
            catch (InvalidOperationException)
            {
            }
        }

        throw new InvalidOperationException(
            $"Control Center element with automation name '{automationName}' was not rendered.");
    }

    private static void AssertElementFitsHorizontally(
        ControlCenterWindow window,
        FrameworkElement element)
    {
        var origin = element
            .TransformToAncestor(window)
            .Transform(new System.Windows.Point(0, 0));
        if (origin.X < -1 ||
            origin.X + element.ActualWidth > window.ActualWidth + 1)
        {
            throw new InvalidOperationException(
                $"Control Center compact layout clips '{AutomationProperties.GetName(element)}'.");
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
        double sum = 0;
        double sumSquares = 0;

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

                var intensity = (red + green + blue) / 3.0;
                sum += intensity;
                sumSquares += intensity * intensity;
            }
        }

        if (sampled == 0)
        {
            throw new InvalidOperationException(
                "Control Center render produced no visible pixels.");
        }

        var mean = sum / sampled;
        var variance = Math.Max(0, sumSquares / sampled - mean * mean);
        if (variance <= 25)
        {
            throw new InvalidOperationException(
                $"Kontrol Merkezi görüntüsü tek renk veya boş. Piksel varyansı={variance:F2}.");
        }
    }
}