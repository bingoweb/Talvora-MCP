using System.Drawing;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>
    /// TI1: the tray mark is drawn at the exact tray size on a transparent background; Ready shows no badge;
    /// Attention and Offline add their badge in a cut-out that never touches the T; colours follow the taskbar.
    /// </summary>
    private static void AssertTrayIconArt()
    {
        foreach (var size in new[] { 16, 20, 24, 32 })
        {
            foreach (var light in new[] { false, true })
            {
                var appearance = new TrayIconAppearance(size, light, HighContrast: false);
                using var ready = TrayIconArt.Render(TrayIconState.Ready, appearance);
                using var attention = TrayIconArt.Render(TrayIconState.Attention, appearance);
                using var offline = TrayIconArt.Render(TrayIconState.Offline, appearance);
                var scale = size / 16f;
                Point At(float x, float y) => new((int)(x * scale), (int)(y * scale));

                foreach (var icon in new[] { ready, attention, offline })
                {
                    if (icon.Width != size || icon.Height != size)
                        throw new InvalidOperationException($"Tray icon must be exactly {size} px.");
                    if (icon.GetPixel(0, 0).A != 0 || icon.GetPixel(size - 1, 0).A != 0)
                        throw new InvalidOperationException("Tray icon background must stay transparent.");
                    var stem = At(8.0f, 10.5f);
                    if (icon.GetPixel(stem.X, stem.Y).A < 250)
                        throw new InvalidOperationException("The T stem must stay intact in every state.");
                }

                var badge = At(13.4f, 14.2f);
                if (ready.GetPixel(badge.X, badge.Y).A != 0)
                    throw new InvalidOperationException("Ready must not show a badge.");
                var expectedAttention = light ? Color.FromArgb(0x9A, 0x5B, 0x00) : Color.FromArgb(0xF5, 0xB8, 0x4B);
                var expectedOffline = light ? Color.FromArgb(0xC2, 0x32, 0x4A) : Color.FromArgb(0xFF, 0x6B, 0x81);
                AssertColour(attention.GetPixel(badge.X, badge.Y), expectedAttention, $"Attention badge ({size}, light={light})");
                AssertColour(offline.GetPixel(badge.X, badge.Y), expectedOffline, $"Offline badge ({size}, light={light})");
            }
        }

        using var darkHighContrast = TrayIconArt.Render(TrayIconState.Ready, new TrayIconAppearance(32, false, HighContrast: true));
        AssertColour(darkHighContrast.GetPixel(16, 21), Color.White, "High-contrast mark on a dark taskbar");
        using var dark = TrayIconArt.Render(TrayIconState.Ready, new TrayIconAppearance(32, false, false));
        using var light32 = TrayIconArt.Render(TrayIconState.Ready, new TrayIconAppearance(32, true, false));
        AssertColour(dark.GetPixel(16, 21), Color.FromArgb(0xE9, 0xEE, 0xF6), "T on a dark taskbar");
        AssertColour(light32.GetPixel(16, 21), Color.FromArgb(0x0E, 0x16, 0x25), "T on a light taskbar");

        static void AssertColour(Color actual, Color expected, string what)
        {
            if (actual.A < 250 || Math.Abs(actual.R - expected.R) > 6 || Math.Abs(actual.G - expected.G) > 6 || Math.Abs(actual.B - expected.B) > 6)
                throw new InvalidOperationException($"{what}: expected #{expected.R:X2}{expected.G:X2}{expected.B:X2}, got #{actual.A:X2}{actual.R:X2}{actual.G:X2}{actual.B:X2}.");
        }
    }
}
