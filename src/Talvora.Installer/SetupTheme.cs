using Microsoft.Win32;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Installer;

/// <summary>KUR1: the Santral colours of the setup window (docs/codex/DESIGN-SANTRAL.md §3).</summary>
internal sealed record SetupPalette(
    bool Dark,
    bool HighContrast,
    Color Panel,
    Color PanelRaised,
    Color PanelSunken,
    Color Ink,
    Color InkMuted,
    Color InkFaint,
    Color Rule,
    Color Brass,
    Color BrassLight,
    Color BrassDark,
    Color Socket,
    Color Cable,
    Color LampReady,
    Color LampAttention,
    Color LampOffline,
    Color LampOff,
    Color WordReady,
    Color WordAttention,
    Color WordOffline,
    Color Pulse,
    Color Focus,
    Color Engraving,
    Color OnInk)
{
    internal static SetupPalette Gun { get; } = new(
        Dark: false, HighContrast: false,
        Panel: Rgb(0xE7DFCB), PanelRaised: Rgb(0xEFE9DA), PanelSunken: Rgb(0xDCD3BD),
        Ink: Rgb(0x1F1C18), InkMuted: Rgb(0x5E574A), InkFaint: Rgb(0x6A6252), Rule: Rgb(0x8C826F),
        Brass: Rgb(0xB08D57), BrassLight: Rgb(0xE2C68F), BrassDark: Rgb(0x7A5E33),
        Socket: Rgb(0x151210), Cable: Rgb(0x2A2622),
        LampReady: Rgb(0x2F9E6B), LampAttention: Rgb(0xE39A1B), LampOffline: Rgb(0xC3412F), LampOff: Rgb(0x9A9184),
        WordReady: Rgb(0x1F6E4A), WordAttention: Rgb(0x9A5B00), WordOffline: Rgb(0xA3301F),
        Pulse: Rgb(0xF6E7B8), Focus: Rgb(0x1F1C18), Engraving: Rgb(0x2B2114), OnInk: Rgb(0xEFE9DA));

    internal static SetupPalette Gece { get; } = new(
        Dark: true, HighContrast: false,
        Panel: Rgb(0x1B1916), PanelRaised: Rgb(0x24211D), PanelSunken: Rgb(0x12110F),
        Ink: Rgb(0xEDE5D3), InkMuted: Rgb(0xB3A88F), InkFaint: Rgb(0x9C927C), Rule: Rgb(0x4A443A),
        Brass: Rgb(0xC9A56A), BrassLight: Rgb(0xEBD3A0), BrassDark: Rgb(0x8A6B3B),
        Socket: Rgb(0x050505), Cable: Rgb(0x0E0D0C),
        LampReady: Rgb(0x45C08A), LampAttention: Rgb(0xF2AE3A), LampOffline: Rgb(0xE2604C), LampOff: Rgb(0x5A544A),
        WordReady: Rgb(0x6FD4A6), WordAttention: Rgb(0xF5C46E), WordOffline: Rgb(0xF08A78),
        Pulse: Rgb(0xFFE9B0), Focus: Rgb(0xEDE5D3), Engraving: Rgb(0x2A1F10), OnInk: Rgb(0x1B1916));

    /// <summary>High contrast follows the user's system colours; no texture, brass or glow.</summary>
    internal static SetupPalette SystemHighContrast => new(
        Dark: false, HighContrast: true,
        Panel: SystemColors.Window, PanelRaised: SystemColors.Window, PanelSunken: SystemColors.Window,
        Ink: SystemColors.WindowText, InkMuted: SystemColors.WindowText, InkFaint: SystemColors.GrayText,
        Rule: SystemColors.WindowText,
        Brass: SystemColors.Window, BrassLight: SystemColors.Window, BrassDark: SystemColors.WindowText,
        Socket: SystemColors.WindowText, Cable: SystemColors.WindowText,
        LampReady: SystemColors.Highlight, LampAttention: SystemColors.Highlight,
        LampOffline: SystemColors.Highlight, LampOff: SystemColors.Window,
        WordReady: SystemColors.WindowText, WordAttention: SystemColors.WindowText,
        WordOffline: SystemColors.WindowText,
        Pulse: SystemColors.Highlight, Focus: SystemColors.WindowText, Engraving: SystemColors.WindowText,
        OnInk: SystemColors.HighlightText);

    private static Color Rgb(int rgb) =>
        Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}

/// <summary>KUR1: Bahnschrift for words, Cascadia Mono for numbers (Windows built-ins, no font files).</summary>
internal sealed class SetupFonts : IDisposable
{
    private const string HeadingFamily = "Bahnschrift SemiBold Condensed";

    private static readonly Lazy<HashSet<string>> InstalledFamilies = new(() =>
    {
        using var collection = new InstalledFontCollection();
        return collection.Families
            .Select(family => family.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    });

    internal SetupFonts(float scale)
    {
        Wordmark = Create(26f, scale, ("Bahnschrift Bold Condensed", FontStyle.Regular),
            ("Bahnschrift", FontStyle.Bold), ("Segoe UI Semibold", FontStyle.Regular));
        WordmarkLight = Create(26f, scale, ("Bahnschrift Light Condensed", FontStyle.Regular),
            ("Bahnschrift Light", FontStyle.Regular), ("Segoe UI Light", FontStyle.Regular));
        Title = Create(28f, scale, (HeadingFamily, FontStyle.Regular),
            ("Bahnschrift SemiBold", FontStyle.Regular), ("Segoe UI Semibold", FontStyle.Regular));
        Label = Create(16f, scale, (HeadingFamily, FontStyle.Regular),
            ("Bahnschrift SemiBold", FontStyle.Regular), ("Segoe UI Semibold", FontStyle.Regular));
        Word = Create(15f, scale, (HeadingFamily, FontStyle.Regular),
            ("Bahnschrift SemiBold", FontStyle.Regular), ("Segoe UI Semibold", FontStyle.Regular));
        Button = Create(16f, scale, (HeadingFamily, FontStyle.Regular),
            ("Bahnschrift SemiBold", FontStyle.Regular), ("Segoe UI Semibold", FontStyle.Regular));
        Body = Create(15f, scale, ("Bahnschrift", FontStyle.Regular), ("Segoe UI", FontStyle.Regular));
        Mono = Create(13f, scale, ("Cascadia Mono", FontStyle.Regular), ("Consolas", FontStyle.Regular));
        PlateLabel = Create(10f, scale, (HeadingFamily, FontStyle.Regular),
            ("Bahnschrift SemiBold", FontStyle.Regular), ("Segoe UI Semibold", FontStyle.Regular));
        PlateName = Create(12.5f, scale, ("Bahnschrift Bold Condensed", FontStyle.Regular),
            ("Bahnschrift", FontStyle.Bold), ("Segoe UI Semibold", FontStyle.Regular));
    }

    internal Font Wordmark { get; }
    internal Font WordmarkLight { get; }
    internal Font Title { get; }
    internal Font Label { get; }
    internal Font Word { get; }
    internal Font Button { get; }
    internal Font Body { get; }
    internal Font Mono { get; }
    internal Font PlateLabel { get; }
    internal Font PlateName { get; }

    public void Dispose()
    {
        foreach (var font in new[]
                 {
                     Wordmark, WordmarkLight, Title, Label, Word, Button, Body, Mono, PlateLabel, PlateName,
                 })
        {
            font.Dispose();
        }
    }

    private static Font Create(float pixels, float scale, params (string Family, FontStyle Style)[] candidates)
    {
        foreach (var (family, style) in candidates)
        {
            if (InstalledFamilies.Value.Contains(family))
            {
                return new Font(family, pixels * scale, style, GraphicsUnit.Pixel);
            }
        }

        return new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif,
            pixels * scale, FontStyle.Regular, GraphicsUnit.Pixel);
    }
}

/// <summary>KUR1: theme choice, reduced motion and the Windows 11 title bar colours.</summary>
internal static class SetupTheme
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private const uint SpiGetClientAreaAnimation = 0x1042;

    /// <summary>Set only by a design harness that renders both themes; the product never sets it.</summary>
    internal static SetupPalette? ForcedPalette { get; set; }

    internal static SetupPalette Resolve()
    {
        if (ForcedPalette is not null)
        {
            return ForcedPalette;
        }

        if (SystemInformation.HighContrast)
        {
            return SetupPalette.SystemHighContrast;
        }

        return PrefersDark() ? SetupPalette.Gece : SetupPalette.Gun;
    }

    /// <summary>The Control Center's own choice (Ayarlar → Görünüm) wins; "Sistem" follows Windows' app mode.</summary>
    internal static bool PrefersDark()
    {
        var preference = ReadControlCenterPreference();
        if (string.Equals(preference, "Gece", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(preference, "Gun", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool AnimationsEnabled()
    {
        if (SystemInformation.HighContrast)
        {
            return false;
        }

        var enabled = 1;
        return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref enabled, 0) || enabled != 0;
    }

    internal static void ApplyTitleBar(IntPtr handle, SetupPalette palette)
    {
        if (palette.HighContrast || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var dark = palette.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        var caption = ToColorRef(palette.Panel);
        _ = DwmSetWindowAttribute(handle, DwmCaptionColor, ref caption, sizeof(int));
        var text = ToColorRef(palette.Ink);
        _ = DwmSetWindowAttribute(handle, DwmTextColor, ref text, sizeof(int));
        var border = ToColorRef(palette.Rule);
        _ = DwmSetWindowAttribute(handle, DwmBorderColor, ref border, sizeof(int));
    }

    private static string? ReadControlCenterPreference()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Talvora", "ControlCenter", "theme-preference.json");
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > 4096)
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "preference", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref int value, uint update);
}
