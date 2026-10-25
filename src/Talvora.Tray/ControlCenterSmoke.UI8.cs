using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Brush = System.Windows.Media.Brush;
using SystemColors = System.Windows.SystemColors;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    private static void AssertTitleBarAutomationNames(ShellWindow window)
    {
        window.ShellTitleBar.ApplyTemplate();
        CheckTitleBarButtons(window, "Ekranı Kapla");
        var initialState = window.WindowState;
        try
        {
            window.WindowState = WindowState.Maximized;
            window.UpdateLayout();
            CheckTitleBarButtons(window, "Önceki Boyutuna Getir");
        }
        finally
        {
            window.WindowState = initialState;
            window.UpdateLayout();
        }
    }

    private static void CheckTitleBarButtons(ShellWindow window, string maximizeName)
    {
        foreach (var (automationId, expected) in new[]
        {
            ("TitleBarMinimizeButton", "Simge Durumuna Küçült"),
            ("TitleBarMaximizeButton", maximizeName),
            ("TitleBarCloseButton", "Kapat"),
        })
        {
            var button = EnumerateVisualElements(window.ShellTitleBar)
                .OfType<ButtonBase>()
                .FirstOrDefault(part => AutomationProperties.GetAutomationId(part) == automationId)
                ?? throw new InvalidOperationException($"Title bar button {automationId} was not rendered.");
            if (AutomationProperties.GetName(button) != expected)
                throw new InvalidOperationException($"Title bar button {automationId} has no approved UIA name.");
        }
    }

    private static void AssertUi8AccessibilityAudit(ShellWindow window)
    {
        var named = new List<string>();
        foreach (var element in EnumerateVisualElements(window))
        {
            if (!element.IsVisible || !element.IsEnabled || !element.Focusable)
                continue;
            // WPF's internal scroll repeat buttons are not keyboard targets (P-41).
            if (element is RepeatButton) continue;
            var id = AutomationProperties.GetAutomationId(element);
            if (element.TemplatedParent is not null && !id.StartsWith("TitleBar", StringComparison.Ordinal))
                continue;
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(element);
            if (peer is null || !peer.IsControlElement() || peer.IsOffscreen()) continue;
            if (peer.GetAutomationControlType() is not (
                System.Windows.Automation.Peers.AutomationControlType.Button or
                System.Windows.Automation.Peers.AutomationControlType.Edit or
                System.Windows.Automation.Peers.AutomationControlType.ComboBox or
                System.Windows.Automation.Peers.AutomationControlType.CheckBox or
                System.Windows.Automation.Peers.AutomationControlType.RadioButton or
                System.Windows.Automation.Peers.AutomationControlType.ListItem or
                System.Windows.Automation.Peers.AutomationControlType.Hyperlink or
                System.Windows.Automation.Peers.AutomationControlType.MenuItem or
                System.Windows.Automation.Peers.AutomationControlType.Slider))
                continue;
            var name = peer.GetName();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException($"UI8 UIA name missing: {peer.GetClassName()} ({peer.GetAutomationControlType()}).");
            named.Add($"{peer.GetAutomationControlType()}={name}");
        }
        if (named.Count < 10)
            throw new InvalidOperationException("UI8 UIA audit found fewer than ten keyboard controls.");
        TrayLog.Write($"UI8 UIA audit: {named.Count} named keyboard controls; {string.Join("; ", named)}");
    }

    private static void AssertHighContrastTokenMapping(ControlCenterApplication application)
    {
        var wasHighContrast = SystemParameters.HighContrast;
        try
        {
            TalvoraThemeManager.SetHighContrastTokens(application, true);
            var expected = new (Brush Brush, string[] Keys)[]
            {
                (SystemColors.WindowBrush, ["Tv.Bg", "Tv.Surface", "Tv.SurfaceSunken", "Tv.Ready.Soft", "Tv.Attention.Soft", "Tv.Offline.Soft", "Tv.Neutral.Soft", "Tv.Repairing.Soft"]),
                (SystemColors.ControlBrush, ["Tv.SurfaceRaised", "Tv.AccentSoft"]),
                (SystemColors.WindowTextBrush, ["Tv.Border", "Tv.BorderStrong", "Tv.Text", "Tv.TextMuted", "Tv.Ready", "Tv.Attention", "Tv.Offline", "Tv.Neutral", "Tv.Repairing", "Tv.Aurora"]),
                (SystemColors.GrayTextBrush, ["Tv.TextFaint"]),
                (SystemColors.HighlightBrush, ["Tv.Accent"]),
                (SystemColors.HighlightTextBrush, ["Tv.AccentInk"]),
            };
            foreach (var (brush, keys) in expected)
                foreach (var key in keys)
                    if (!ReferenceEquals(application.TryFindResource(key), brush))
                        throw new InvalidOperationException($"High contrast token {key} does not use its approved SystemColors brush.");
            TalvoraThemeManager.SetHighContrastTokens(application, false);
            foreach (var (brush, keys) in expected)
                foreach (var key in keys)
                    if (ReferenceEquals(application.TryFindResource(key), brush) ||
                        application.TryFindResource(key) is null)
                        throw new InvalidOperationException($"Theme token {key} was not restored after high contrast.");
        }
        finally
        {
            TalvoraThemeManager.SetHighContrastTokens(application, wasHighContrast);
        }
    }
}
