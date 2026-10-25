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

internal static partial class ControlCenterSmoke
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
                if (application.MainWindow is not ShellWindow window)
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
                AssertHighContrastTokenMapping(application);
                AssertWindowChrome(window);
                AssertTitleBarAutomationNames(window);
                AssertUi8AccessibilityAudit(window);
                AssertWindowHasVisibleContent(window);
                AssertWindowDpi(window);
                AssertRenderedSurfaceIsNotBlank(window);
                AssertStitchNavigation(window);
                AssertUi3VisualFixture(window, "initial");
                AssertUi4VisualFixture(window, "initial");
                await AssertUi5VisualFixtureAsync(window, "initial");
                AssertUi6VisualFixture(window, "initial");

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
                AssertUi3VisualFixture(window, "alternate");
                AssertUi4VisualFixture(window, "alternate");
                await AssertUi5VisualFixtureAsync(window, "alternate");
                AssertUi6VisualFixture(window, "alternate");
                window.ShellModel.SelectPage(2);
                AssertWorklogHistorySelection(window, "worklog-session-alternate");
                window.ShellModel.SelectPage(0);
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
                await AssertUi4LifecycleSmokeAsync(window);
                SaveVisualSnapshotIfRequested(window, "loaded");
                if (SystemParameters.HighContrast) SaveVisualSnapshotIfRequested(window, "hc");
                window.ShellModel.SelectPage(5);
                AssertUi6VisualFixture(window, "loaded");
                window.ShellModel.SelectPage(0);
                await MeasureUi3IdleCpuAsync(window);

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

    private static void AssertStitchNavigation(ShellWindow window)
    {
        var names = new[] { "Genel bakış", "Hizmetler", "Çalışma günlüğü", "Bağlantı zinciri", "Olaylar", "Ayarlar ve kurulum" };
        foreach (var name in names)
        {
            var link = FindVisualChildByAutomationName(window, name);
            AssertAutomationElement(link);
            if (!link.IsEnabled || link.ActualWidth < 32 || link.ActualHeight < 32)
                throw new InvalidOperationException($"Shell navigation item '{name}' is not usable.");
        }
        for (var index = 0; index < 5; index++)
        {
            if (!window.HandleShellNavigationKey((System.Windows.Input.Key)((int)System.Windows.Input.Key.D1 + index),
                    System.Windows.Input.ModifierKeys.Control))
                throw new InvalidOperationException("Shell navigation shortcut was not handled.");
            window.UpdateLayout();
            if (window.ShellModel.SelectedIndex != index)
                throw new InvalidOperationException("Shell shortcut selected a different route.");
            if (index == 0)
            {
                if (window.OverviewContentHost.Visibility != Visibility.Visible ||
                    window.OverviewContentHost.Content is not Views.OverviewView ||
                    window.LegacyContentHost.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("UI3 overview did not open its view.");
            }
            else if (index == 1)
            {
                if (window.ServicesContentHost.Visibility != Visibility.Visible ||
                    window.ServicesContentHost.Content is not Views.ServicesView ||
                    window.LegacyContentHost.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("UI4 services route did not open its view.");
            }
            else if (index == 2)
            {
                if (window.HistoryContentHost.Visibility != Visibility.Visible ||
                    window.HistoryContentHost.Content is not Views.WorklogHistoryView ||
                    window.PlaceholderCard.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("The worklog history route did not open its view.");
                SaveVisualSnapshotIfRequested(window, "worklog");
                AssertWorklogHistorySelection(window);
            }
            else if (index == 3)
            {
                if (window.ChainContentHost.Visibility != Visibility.Visible ||
                    window.ChainContentHost.Content is not Views.ChainView ||
                    window.PlaceholderCard.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("UI3 chain did not replace its placeholder.");
                SaveVisualSnapshotIfRequested(window, "chain");
            }
        }
        window.ShellModel.SelectPage(1);
        if (window.ShellModel.SelectedIndex != 1)
            throw new InvalidOperationException("Service navigation failed after the chain.");
        window.ShellModel.SelectPage(0);
        window.PaletteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.Palette.Child.UpdateLayout();
        if (!window.Palette.IsOpen || window.Palette.Results.Items.Count < 6)
            throw new InvalidOperationException("The command palette must open with every destination.");
        var backgroundClick = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent };
        window.Palette.Results.RaiseEvent(backgroundClick);
        if (!window.Palette.IsOpen || window.ShellModel.SelectedIndex != 0)
            throw new InvalidOperationException("Clicking palette background must not execute a command.");
        var popupRoot = PresentationSource.FromVisual((Visual)window.Palette.Child)?.RootVisual as FrameworkElement;
        SaveVisualSnapshotIfRequested(window, "palette", popupRoot ?? (FrameworkElement)window.Palette.Child);
        window.ShellModel.PaletteQuery = "hzmt";
        window.Palette.Results.SelectedIndex = 0;
        var enter = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual((Visual)window.Palette.Child), Environment.TickCount, System.Windows.Input.Key.Enter)
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
        window.Palette.Child.RaiseEvent(enter);
        if (window.Palette.IsOpen || window.ShellModel.SelectedIndex != 1)
            throw new InvalidOperationException("Enter must execute the selected palette destination and close it.");
        window.PaletteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual((Visual)window.Palette.Child), Environment.TickCount, System.Windows.Input.Key.Escape)
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
        window.Palette.Child.RaiseEvent(escape);
        if (window.Palette.IsOpen) throw new InvalidOperationException("Esc must close the palette.");
        if (window.FirstPaintMilliseconds is null)
            throw new InvalidOperationException("The shell first-paint measurement was not recorded.");
        window.ShellModel.SelectPage(0);
        window.UpdateLayout();
    }

    private static void AssertWorklogHistorySelection(
        ShellWindow window, string snapshot = "worklog-session")
    {
        var directory = Path.Combine(Path.GetTempPath(), "talvora-n7-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previous = window.HistoryContentHost.Content;
        try
        {
            var store = new WorklogHistoryStore(Path.Combine(directory, "worklog-sessions.jsonl"));
            var now = DateTimeOffset.UtcNow;
            foreach (var id in new[] { "n7-first", "n7-second" })
            {
                var frame = new Talvora.Shared.DesktopWorklogFrame(
                    id, 2, now,
                    new Talvora.Shared.WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"),
                    new Talvora.Shared.WorklogStep(Talvora.Shared.WorklogStepCategory.SourceEdit,
                        Talvora.Shared.WorklogStepState.Succeeded, "Düzenleme tamamlandı.",
                        "1 dosya değişti", null, 3),
                    [new Talvora.Shared.WorklogTimelineEntry(now.AddSeconds(-3),
                        Talvora.Shared.WorklogStepState.Running, "Düzenleme başladı."),
                     new Talvora.Shared.WorklogTimelineEntry(now,
                        Talvora.Shared.WorklogStepState.Succeeded, "Düzenleme tamamlandı.")],
                    new Talvora.Shared.WorklogTotals(2, 3, 1,
                        Talvora.Shared.WorklogCheckState.Succeeded, Talvora.Shared.WorklogCheckState.Succeeded),
                    null, 15);
                store.AppendTerminal(new Talvora.Shared.DesktopProgressMessage(
                    id, "talvora_apply_patch", "Düzenleme tamamlandı.", "",
                    Talvora.Shared.DesktopProgressKind.Completed, now, 3,
                    new Talvora.Shared.DesktopProgressEvidence(Summary: "1 dosya değişti",
                        Files: ["src/Talvora.Tray/WorklogHistoryStore.cs"]),
                    WorklogFrame: frame, SessionClosed: true));
            }
            var page = new Views.WorklogHistoryView(store);
            page.Reload();
            window.HistoryContentHost.Content = page;
            window.UpdateLayout();
            var rows = FindVisualChild<System.Windows.Controls.ListBox>(page)
                ?? throw new InvalidOperationException("Worklog history session list is missing.");
            var matching = page.Model.Sessions.Single(session => session.SessionId == "n7-second");
            rows.SelectedItem = matching; // The same selection binding a clicked card uses.
            if (page.Model.SelectedSession?.SessionId != "n7-second" ||
                page.Model.SelectedSession?.Timeline.Count != 2)
                throw new InvalidOperationException("A clicked worklog card opened another session.");
            SaveVisualSnapshotIfRequested(window, snapshot);
        }
        finally
        {
            window.HistoryContentHost.Content = previous;
            window.UpdateLayout();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void SaveVisualSnapshotIfRequested(
        ShellWindow window,
        string? variant = null,
        FrameworkElement? surface = null)
    {
        var path = Environment.GetEnvironmentVariable(
            "TALVORA_CONTROL_CENTER_PREVIEW_PNG");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var visual = surface ?? window;
        visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Round(visual.ActualWidth)),
            Math.Max(1, (int)Math.Round(visual.ActualHeight)),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
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

    private static void AssertWindowChrome(ShellWindow window)
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

    private static void AssertDashboardStructure(ShellWindow window)
    {
        window.UpdateLayout();
        var root = (Grid)window.LegacyContentHost.Content;
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
                try { AssertAutomationBounds(peer); }
                catch (InvalidOperationException exception)
                {
                    var field = typeof(ShellWindow).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .FirstOrDefault(candidate => ReferenceEquals(candidate.GetValue(window), element));
                    var parent = VisualTreeHelper.GetParent(element);
                    var parentName = parent is null ? string.Empty : AutomationProperties.GetName(parent);
                    throw new InvalidOperationException($"Shell automation bounds: field={field?.Name}, name={element.Name}, type={element.GetType().Name}, parent={parent?.GetType().Name}/{parentName}, text='{(element as System.Windows.Controls.TextBlock)?.Text}'.", exception);
                }
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

    private static void AssertWindowDpi(ShellWindow window)
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
        ShellWindow window)
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
        ShellWindow window,
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

    private static void AssertWindowHasVisibleContent(ShellWindow window)
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
        ShellWindow window)
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