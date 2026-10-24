using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Win32;

namespace Talvora.Tray;

/// <summary>State the tray icon shows. Ready stays quiet (no badge); the other two add a shaped badge.</summary>
public enum TrayIconState { Ready, Attention, Offline }

/// <summary>What the icon is drawn for: the tray's real pixel size, the taskbar theme and high contrast.</summary>
public readonly record struct TrayIconAppearance(int Size, bool LightTaskbar, bool HighContrast)
{
    public static TrayIconAppearance Current() => new(
        Math.Clamp(System.Windows.Forms.SystemInformation.SmallIconSize.Width, 16, 64),
        ReadLightTaskbar(),
        System.Windows.Forms.SystemInformation.HighContrast);

    private static bool ReadLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

/// <summary>
/// The Talvora tray mark ("Altıgen-T", Claude 2026-10-24): the brand's notched hexagon and T drawn as vector art at
/// the tray's exact pixel size on a transparent background, in the Control Center token colours of the taskbar theme.
/// Ready shows the bare mark; Attention adds an amber triangle, Offline a red circle, each in a transparent cut-out.
/// </summary>
public static class TrayIconArt
{
    // Gece / Gün tokens (DESIGN-CONTROL-CENTER §3.1, §3.2).
    private static readonly Color DarkRing = Color.FromArgb(0x5B, 0xD8, 0xFF);
    private static readonly Color DarkGlyph = Color.FromArgb(0xE9, 0xEE, 0xF6);
    private static readonly Color DarkAttention = Color.FromArgb(0xF5, 0xB8, 0x4B);
    private static readonly Color DarkOffline = Color.FromArgb(0xFF, 0x6B, 0x81);
    private static readonly Color LightRing = Color.FromArgb(0x07, 0x79, 0xA8);
    private static readonly Color LightGlyph = Color.FromArgb(0x0E, 0x16, 0x25);
    private static readonly Color LightAttention = Color.FromArgb(0x9A, 0x5B, 0x00);
    private static readonly Color LightOffline = Color.FromArgb(0xC2, 0x32, 0x4A);

    // Geometry on a 16-unit grid, scaled to the real size.
    private static readonly PointF[] Hexagon =
    [
        new(8.00f, 1.20f), new(14.06f, 4.70f), new(14.06f, 11.70f),
        new(8.00f, 15.20f), new(1.94f, 11.70f), new(1.94f, 4.70f),
    ];

    private static readonly PointF[] Tee =
    [
        new(4.30f, 5.00f), new(11.70f, 5.00f), new(10.70f, 7.30f), new(9.10f, 7.30f),
        new(9.10f, 11.90f), new(6.90f, 11.90f), new(6.90f, 7.30f), new(5.30f, 7.30f),
    ];

    private const float RingWidth = 1.55f;
    private const float NotchFraction = 0.10f;
    private static readonly PointF BadgeCentre = new(12.80f, 12.80f);
    private const float BadgeDiameter = 5.40f;
    private const float CutOutDiameter = 7.20f;

    public static Bitmap Render(TrayIconState state, TrayIconAppearance appearance)
    {
        var size = appearance.Size;
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.ScaleTransform(size / 16f, size / 16f);

        var (ring, glyph, attention, offline) = Palette(appearance);

        // Hexagon with the brand's notch at the top vertex.
        var top = Hexagon[0];
        PointF Along(PointF from, PointF to, float t) => new(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));
        var outline = new List<PointF> { Along(top, Hexagon[1], NotchFraction) };
        outline.AddRange(Hexagon[1..]);
        outline.Add(Along(Hexagon[5], top, 1f - NotchFraction));
        using (var ringPen = new Pen(ring, RingWidth) { LineJoin = LineJoin.Round, StartCap = LineCap.Flat, EndCap = LineCap.Flat })
        {
            graphics.DrawLines(ringPen, outline.ToArray());
        }

        using (var glyphBrush = new SolidBrush(glyph))
        {
            graphics.FillPolygon(glyphBrush, Tee);
        }

        if (state == TrayIconState.Ready)
        {
            return bitmap;
        }

        // Transparent cut-out so the badge never touches the mark, then the shaped badge.
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using (var clear = new SolidBrush(Color.Transparent))
        {
            graphics.FillEllipse(clear, BadgeCentre.X - (CutOutDiameter / 2), BadgeCentre.Y - (CutOutDiameter / 2), CutOutDiameter, CutOutDiameter);
        }

        graphics.CompositingMode = CompositingMode.SourceOver;
        if (state == TrayIconState.Attention)
        {
            var half = BadgeDiameter / 2;
            PointF[] triangle =
            [
                new(BadgeCentre.X, BadgeCentre.Y - half - 0.30f),
                new(BadgeCentre.X + half + 0.20f, BadgeCentre.Y + half - 0.10f),
                new(BadgeCentre.X - half - 0.20f, BadgeCentre.Y + half - 0.10f),
            ];
            using var brush = new SolidBrush(attention);
            using var path = new GraphicsPath();
            path.AddPolygon(triangle);
            using var softEdge = new Pen(attention, 0.80f) { LineJoin = LineJoin.Round };
            graphics.FillPath(brush, path);
            graphics.DrawPath(softEdge, path);
        }
        else
        {
            using var brush = new SolidBrush(offline);
            graphics.FillEllipse(brush, BadgeCentre.X - (BadgeDiameter / 2), BadgeCentre.Y - (BadgeDiameter / 2), BadgeDiameter, BadgeDiameter);
        }

        return bitmap;
    }

    private static (Color Ring, Color Glyph, Color Attention, Color Offline) Palette(TrayIconAppearance appearance)
    {
        if (appearance.HighContrast)
        {
            var mono = appearance.LightTaskbar ? Color.Black : Color.White;
            return (mono, mono,
                appearance.LightTaskbar ? LightAttention : DarkAttention,
                appearance.LightTaskbar ? LightOffline : DarkOffline);
        }

        return appearance.LightTaskbar
            ? (LightRing, LightGlyph, LightAttention, LightOffline)
            : (DarkRing, DarkGlyph, DarkAttention, DarkOffline);
    }

    /// <summary>
    /// Writes every state × taskbar theme × size (16, 20, 24, 32) as PNG, plus one review sheet that shows them
    /// on the Windows 11 taskbar colours at 8× (nearest neighbour). Returns the written paths.
    /// </summary>
    public static IReadOnlyList<string> RenderPreviews(string directory)
    {
        Directory.CreateDirectory(directory);
        var written = new List<string>();
        int[] sizes = [16, 20, 24, 32];
        var states = Enum.GetValues<TrayIconState>();
        const int zoom = 8;
        const int cell = (32 * zoom) + 24;
        using var sheet = new Bitmap(cell * sizes.Length, cell * states.Length * 2, PixelFormat.Format32bppArgb);
        using (var sheetGraphics = Graphics.FromImage(sheet))
        {
            sheetGraphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            sheetGraphics.PixelOffsetMode = PixelOffsetMode.Half;
            var row = 0;
            foreach (var light in new[] { false, true })
            {
                using var taskbar = new SolidBrush(light ? Color.FromArgb(0xF3, 0xF3, 0xF3) : Color.FromArgb(0x20, 0x20, 0x20));
                foreach (var state in states)
                {
                    sheetGraphics.FillRectangle(taskbar, 0, row * cell, sheet.Width, cell);
                    for (var column = 0; column < sizes.Length; column++)
                    {
                        var appearance = new TrayIconAppearance(sizes[column], light, HighContrast: false);
                        using var icon = Render(state, appearance);
                        var name = $"tray-{(light ? "light" : "dark")}-{state.ToString().ToLowerInvariant()}-{sizes[column]}.png";
                        var path = Path.Combine(directory, name);
                        icon.Save(path, ImageFormat.Png);
                        written.Add(path);
                        var scaled = sizes[column] * zoom;
                        sheetGraphics.DrawImage(icon, new Rectangle((column * cell) + 12, (row * cell) + 12, scaled, scaled));
                    }

                    row++;
                }
            }
        }

        var sheetPath = Path.Combine(directory, "tray-sheet.png");
        sheet.Save(sheetPath, ImageFormat.Png);
        written.Add(sheetPath);
        return written;
    }
}
