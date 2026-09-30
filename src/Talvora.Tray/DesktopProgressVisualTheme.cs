using System.Windows;
using Talvora.Shared;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Application = System.Windows.Application;
using SystemColors = System.Windows.SystemColors;
using WpfBrush = System.Windows.Media.Brush;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace Talvora.Tray;

internal static class DesktopProgressVisualTheme
{
    internal sealed record StatusVisual(
        string Label,
        SymbolRegular Symbol,
        string BrushResourceKey);

    internal static readonly WpfFontFamily UiFont =
        new("Segoe UI Variable Text, Segoe UI");

    internal static readonly WpfFontFamily TerminalFont =
        new("Cascadia Mono, Consolas");

    internal static StatusVisual Describe(
        DesktopProgressKind kind) =>
        kind switch
        {
            DesktopProgressKind.Started =>
                new(
                    "BAŞLADI",
                    SymbolRegular.Play20,
                    "Talvora.Notification.Trace"),
            DesktopProgressKind.Running =>
                new(
                    "ÇALIŞIYOR",
                    SymbolRegular.ArrowSync20,
                    "Talvora.Notification.Trace"),
            DesktopProgressKind.Completed =>
                new(
                    "TAMAM",
                    SymbolRegular.Checkmark20,
                    "Talvora.Notification.Success"),
            DesktopProgressKind.Failed =>
                new(
                    "HATA",
                    SymbolRegular.ErrorCircle20,
                    "Talvora.Notification.Danger"),
            DesktopProgressKind.Cancelled =>
                new(
                    "DURDU",
                    SymbolRegular.Stop20,
                    "Talvora.Notification.Warning"),
            DesktopProgressKind.Warning =>
                new(
                    "UYARI",
                    SymbolRegular.Warning20,
                    "Talvora.Notification.Warning"),
            DesktopProgressKind.Info =>
                new(
                    "BİLGİ",
                    SymbolRegular.Info20,
                    "Talvora.Notification.Info"),
            _ =>
                new(
                    "DURUM",
                    SymbolRegular.Info20,
                    "Talvora.Notification.Info"),
        };

    internal static WpfBrush SurfaceBrush =>
        ResolveBrush(
            "Talvora.Notification.Surface",
            SystemColors.WindowBrush);

    internal static WpfBrush RaisedSurfaceBrush =>
        ResolveBrush(
            "Talvora.Notification.SurfaceRaised",
            SystemColors.ControlBrush);

    internal static WpfBrush TerminalBrush =>
        ResolveBrush(
            "Talvora.Notification.Terminal",
            SystemColors.WindowBrush);

    internal static WpfBrush BorderBrush =>
        ResolveBrush(
            "Talvora.Notification.Border",
            SystemColors.WindowTextBrush);

    internal static WpfBrush StrongBorderBrush =>
        ResolveBrush(
            "Talvora.Notification.BorderStrong",
            SystemColors.WindowTextBrush);

    internal static WpfBrush TraceBrush =>
        ResolveBrush(
            "Talvora.Notification.Trace",
            SystemColors.HighlightBrush);

    internal static WpfBrush TraceSoftBrush =>
        ResolveBrush(
            "Talvora.Notification.TraceSoft",
            SystemColors.ControlBrush);

    internal static WpfBrush PrimaryTextBrush =>
        ResolveBrush(
            "Talvora.Notification.TextPrimary",
            SystemColors.WindowTextBrush);

    internal static WpfBrush SecondaryTextBrush =>
        ResolveBrush(
            "Talvora.Notification.TextSecondary",
            SystemColors.WindowTextBrush);

    internal static WpfBrush TertiaryTextBrush =>
        ResolveBrush(
            "Talvora.Notification.TextTertiary",
            SystemColors.GrayTextBrush);

    internal static WpfBrush TerminalTextBrush =>
        ResolveBrush(
            "Talvora.Notification.TerminalText",
            SystemColors.WindowTextBrush);

    internal static WpfBrush TerminalMutedBrush =>
        ResolveBrush(
            "Talvora.Notification.TerminalMuted",
            SystemColors.GrayTextBrush);

    internal static WpfBrush GetStatusBrush(
        DesktopProgressKind kind)
    {
        if (SystemParameters.HighContrast)
        {
            return kind == DesktopProgressKind.Failed
                ? SystemColors.WindowTextBrush
                : SystemColors.HighlightBrush;
        }

        var visual = Describe(kind);
        return ResolveBrush(
            visual.BrushResourceKey,
            SystemColors.HighlightBrush);
    }

    private static WpfBrush ResolveBrush(
        string resourceKey,
        WpfBrush fallback)
    {
        if (SystemParameters.HighContrast)
        {
            return fallback;
        }

        return Application.Current?.TryFindResource(resourceKey)
            as WpfBrush ?? fallback;
    }
}
