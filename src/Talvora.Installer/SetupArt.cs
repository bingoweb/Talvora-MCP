using System.Drawing;
using System.Drawing.Drawing2D;

namespace Talvora.Installer;

/// <summary>
/// KUR1: the switchboard parts the setup window draws, in the Control Center's language
/// (brass sockets, black plugs and cable, glass lamps) plus the developer's maker plate.
/// </summary>
internal static class SetupArt
{
    internal const string PlateLabel = "GELİŞTİREN";
    internal const string PlateName = "TAYLAN SOYLU";

    private const float NeckLength = 12f;
    private const float CollarLength = 1.5f;
    private const float HandleLength = 18f;
    private static readonly Color CableHighlight = Color.FromArgb(255, 0xFF, 0xF0, 0xD2);

    /// <summary>Tip to cable joint of a plug, in logical pixels.</summary>
    internal static float PlugLength(float scale) => (NeckLength + CollarLength + HandleLength) * scale;

    internal static Color Mix(Color from, Color to, double amount)
    {
        var t = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            (int)Math.Round(from.A + ((to.A - from.A) * t)),
            (int)Math.Round(from.R + ((to.R - from.R) * t)),
            (int)Math.Round(from.G + ((to.G - from.G) * t)),
            (int)Math.Round(from.B + ((to.B - from.B) * t)));
    }

    internal static Color Alpha(Color color, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), color);

    internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        if (r < 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var d = r * 2f;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>The panel: fine horizontal brushed lines and a soft vignette; plain in high contrast.</summary>
    internal static Bitmap CreateBackdrop(Size size, SetupPalette palette, float scale)
    {
        var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
        using var g = Graphics.FromImage(bitmap);
        g.Clear(palette.Panel);
        if (palette.HighContrast)
        {
            return bitmap;
        }

        var period = Math.Max(2, (int)Math.Round(2f * scale));
        using (var line = new Pen(Alpha(palette.Ink, palette.Dark ? 9 : 7), 1f))
        {
            for (var y = 0; y < size.Height; y += period)
            {
                g.DrawLine(line, 0, y, size.Width, y);
            }
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        path.AddEllipse(-size.Width * 0.25f, -size.Height * 0.35f, size.Width * 1.5f, size.Height * 1.7f);
        using var vignette = new PathGradientBrush(path)
        {
            CenterColor = Alpha(Color.Black, 0),
            SurroundColors = [Alpha(Color.Black, palette.Dark ? 70 : 26)],
            Blend = new Blend(3) { Factors = [0f, 1f, 1f], Positions = [0f, 0.32f, 1f] },
        };
        g.FillRectangle(vignette, 0, 0, size.Width, size.Height);
        return bitmap;
    }

    internal static void DrawSocket(Graphics g, PointF center, float radius, SetupPalette palette)
    {
        var ring = new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        var holeRadius = radius * 0.42f;
        var hole = new RectangleF(center.X - holeRadius, center.Y - holeRadius, holeRadius * 2f, holeRadius * 2f);
        if (palette.HighContrast)
        {
            using var outline = new Pen(palette.Ink, Math.Max(1.5f, radius * 0.08f));
            g.DrawEllipse(outline, ring);
            using var dark = new SolidBrush(palette.Ink);
            g.FillEllipse(dark, hole);
            return;
        }

        using (var shadow = new SolidBrush(Alpha(Color.Black, palette.Dark ? 80 : 38)))
        {
            g.FillEllipse(shadow, ring.X + (radius * 0.05f), ring.Y + (radius * 0.16f), ring.Width, ring.Height);
        }

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(ring);
            using var brass = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(center.X - (radius * 0.34f), center.Y - (radius * 0.4f)),
                CenterColor = palette.BrassLight,
                SurroundColors = [palette.BrassDark],
                InterpolationColors = new ColorBlend(3)
                {
                    Colors = [palette.BrassDark, palette.Brass, palette.BrassLight],
                    Positions = [0f, 0.55f, 1f],
                },
            };
            g.FillPath(brass, path);
        }

        using (var edge = new Pen(Mix(palette.BrassDark, Color.Black, 0.3), Math.Max(1f, radius * 0.05f)))
        {
            g.DrawEllipse(edge, ring);
        }

        using (var lip = new SolidBrush(Mix(palette.BrassDark, Color.Black, 0.4)))
        {
            g.FillEllipse(lip, RectangleF.Inflate(hole, radius * 0.08f, radius * 0.08f));
        }

        using (var dark = new SolidBrush(palette.Socket))
        {
            g.FillEllipse(dark, hole);
        }
    }

    /// <summary>A plug whose tip is at <paramref name="tip"/> and points along <paramref name="directionDegrees"/>.</summary>
    internal static void DrawPlug(Graphics g, PointF tip, float directionDegrees, float scale, SetupPalette palette)
    {
        var state = g.Save();
        g.TranslateTransform(tip.X, tip.Y);
        g.RotateTransform(directionDegrees + 90f);
        var neckWidth = 8f * scale;
        var neck = new RectangleF(-neckWidth / 2f, 0f, neckWidth, NeckLength * scale);
        var collar = new RectangleF(-5.5f * scale, neck.Bottom - (0.5f * scale), 11f * scale, (CollarLength + 1f) * scale);
        var handleWidth = 14f * scale;
        var handle = new RectangleF(
            -handleWidth / 2f, (NeckLength + CollarLength) * scale, handleWidth, HandleLength * scale);
        if (palette.HighContrast)
        {
            using var fill = new SolidBrush(palette.Panel);
            using var outline = new Pen(palette.Ink, Math.Max(1f, 1.4f * scale));
            foreach (var part in new[] { neck, handle })
            {
                using var path = RoundedRectangle(part, 2f * scale);
                g.FillPath(fill, path);
                g.DrawPath(outline, path);
            }

            g.Restore(state);
            return;
        }

        using (var path = RoundedRectangle(neck, 2.2f * scale))
        using (var brass = new LinearGradientBrush(RectangleF.Inflate(neck, 0.5f, 0.5f), palette.BrassDark, palette.BrassDark, 0f))
        {
            brass.InterpolationColors = new ColorBlend(4)
            {
                Colors = [palette.BrassDark, palette.BrassLight, palette.Brass, palette.BrassDark],
                Positions = [0f, 0.32f, 0.64f, 1f],
            };
            g.FillPath(brass, path);
        }

        using (var groove = new Pen(Alpha(Mix(palette.BrassDark, Color.Black, 0.3), 210), Math.Max(1f, 0.9f * scale)))
        {
            g.DrawLine(groove, neck.Left + (0.6f * scale), 4f * scale, neck.Right - (0.6f * scale), 4f * scale);
        }

        using (var ring = new SolidBrush(palette.BrassDark))
        {
            g.FillRectangle(ring, collar);
        }

        using (var path = RoundedRectangle(handle, 2.6f * scale))
        using (var body = new LinearGradientBrush(RectangleF.Inflate(handle, 0.5f, 0.5f), palette.Cable, palette.Cable, 0f))
        {
            body.InterpolationColors = new ColorBlend(4)
            {
                Colors =
                [
                    Mix(palette.Cable, Color.Black, 0.25),
                    Mix(palette.Cable, CableHighlight, palette.Dark ? 0.12 : 0.2),
                    palette.Cable,
                    Mix(palette.Cable, Color.Black, 0.35),
                ],
                Positions = [0f, 0.3f, 0.62f, 1f],
            };
            g.FillPath(body, path);
        }

        g.Restore(state);
    }

    internal static void DrawCable(Graphics g, PointF[] points, float scale, SetupPalette palette)
    {
        if (points.Length < 2)
        {
            return;
        }

        using (var cable = new Pen(palette.HighContrast ? palette.Ink : palette.Cable, 7f * scale))
        {
            cable.StartCap = LineCap.Round;
            cable.EndCap = LineCap.Round;
            cable.LineJoin = LineJoin.Round;
            g.DrawLines(cable, points);
        }

        if (palette.HighContrast)
        {
            return;
        }

        var offset = 1.25f * scale;
        var shine = new PointF[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            shine[i] = new PointF(points[i].X - offset, points[i].Y - offset);
        }

        using var highlight = new Pen(Alpha(CableHighlight, palette.Dark ? 26 : 41), 1.6f * scale);
        highlight.StartCap = LineCap.Round;
        highlight.EndCap = LineCap.Round;
        highlight.LineJoin = LineJoin.Round;
        g.DrawLines(highlight, shine);
    }

    /// <summary>A lamp: unlit glass when <paramref name="glow"/> is 0, a lit lamp with its halo up to 1.</summary>
    internal static void DrawLamp(Graphics g, PointF center, float radius, Color glass, double glow, SetupPalette palette)
    {
        var lens = new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        if (palette.HighContrast)
        {
            using var fill = new SolidBrush(glow > 0 ? glass : palette.Panel);
            using var outline = new Pen(palette.Ink, Math.Max(1f, radius * 0.18f));
            g.FillEllipse(fill, lens);
            g.DrawEllipse(outline, lens);
            return;
        }

        if (glow > 0)
        {
            var haloRadius = radius * 2.7f;
            using var halo = new GraphicsPath();
            halo.AddEllipse(center.X - haloRadius, center.Y - haloRadius, haloRadius * 2f, haloRadius * 2f);
            using var light = new PathGradientBrush(halo)
            {
                CenterColor = Alpha(glass, (int)Math.Round(120 * glow)),
                SurroundColors = [Alpha(glass, 0)],
            };
            g.FillPath(light, halo);
        }

        var bezel = RectangleF.Inflate(lens, radius * 0.32f, radius * 0.32f);
        using (var ring = new SolidBrush(Mix(palette.Cable, Color.Black, 0.15)))
        {
            g.FillEllipse(ring, bezel);
        }

        using var lensPath = new GraphicsPath();
        lensPath.AddEllipse(lens);
        using var lensBrush = new PathGradientBrush(lensPath)
        {
            CenterPoint = new PointF(center.X - (radius * 0.3f), center.Y - (radius * 0.35f)),
            CenterColor = Mix(glass, Color.White, glow > 0 ? 0.45 : 0.16),
            SurroundColors = [Mix(glass, Color.Black, glow > 0 ? 0.08 : 0.22)],
        };
        g.FillPath(lensBrush, lensPath);
    }

    /// <summary>A light that travels down the cable: one measured call in the Control Center, the first answer here.</summary>
    internal static void DrawPulse(Graphics g, PointF center, float scale, SetupPalette palette)
    {
        var haloRadius = 10f * scale;
        using (var halo = new GraphicsPath())
        {
            halo.AddEllipse(center.X - haloRadius, center.Y - haloRadius, haloRadius * 2f, haloRadius * 2f);
            using var light = new PathGradientBrush(halo)
            {
                CenterColor = Alpha(palette.Pulse, 200),
                SurroundColors = [Alpha(palette.Pulse, 0)],
            };
            g.FillPath(light, halo);
        }

        var core = 3.2f * scale;
        using var dot = new SolidBrush(Mix(palette.Pulse, Color.White, 0.4));
        g.FillEllipse(dot, center.X - core, center.Y - core, core * 2f, core * 2f);
    }

    internal static float MeasureTracked(Graphics g, string text, Font font, float tracking)
    {
        using var format = TrackingFormat();
        var width = 0f;
        foreach (var character in text)
        {
            width += g.MeasureString(character.ToString(), font, PointF.Empty, format).Width + tracking;
        }

        return text.Length == 0 ? 0f : width - tracking;
    }

    /// <summary>Letter-spaced capitals, as the wordmark and socket labels use them.</summary>
    internal static float DrawTracked(Graphics g, string text, Font font, Color color, float x, float y, float tracking)
    {
        using var format = TrackingFormat();
        using var brush = new SolidBrush(color);
        var start = x;
        foreach (var character in text)
        {
            var glyph = character.ToString();
            g.DrawString(glyph, font, brush, x, y, format);
            x += g.MeasureString(glyph, font, PointF.Empty, format).Width + tracking;
        }

        return text.Length == 0 ? 0f : x - start - tracking;
    }

    internal static SizeF MeasurePlate(Graphics g, SetupFonts fonts, float scale)
    {
        var label = MeasureTracked(g, PlateLabel, fonts.PlateLabel, 1.3f * scale);
        var name = MeasureTracked(g, PlateName, fonts.PlateName, 1.7f * scale);
        return new SizeF((19f * scale) + label + (15f * scale) + name + (19f * scale), 26f * scale);
    }

    /// <summary>
    /// The developer's maker plate: a small riveted brass plate with engraved capitals, the way old
    /// switchboards carried their maker's name. Quiet on purpose (user request 2026-11-08: "çok abartmadan").
    /// </summary>
    internal static void DrawPlate(Graphics g, RectangleF bounds, float scale, SetupPalette palette, SetupFonts fonts)
    {
        using var shape = RoundedRectangle(bounds, 3f * scale);
        var labelWidth = MeasureTracked(g, PlateLabel, fonts.PlateLabel, 1.3f * scale);
        var midline = bounds.Top + (bounds.Height / 2f);
        var labelX = bounds.Left + (19f * scale);
        var nameX = labelX + labelWidth + (15f * scale);
        var labelY = midline - (fonts.PlateLabel.GetHeight(g) * 0.52f) + (1.5f * scale);
        var nameY = midline - (fonts.PlateName.GetHeight(g) * 0.53f) + (1.5f * scale);
        if (palette.HighContrast)
        {
            using var outline = new Pen(palette.Ink, Math.Max(1f, scale));
            g.DrawPath(outline, shape);
            DrawTracked(g, PlateLabel, fonts.PlateLabel, palette.Ink, labelX, labelY, 1.3f * scale);
            DrawTracked(g, PlateName, fonts.PlateName, palette.Ink, nameX, nameY, 1.7f * scale);
            return;
        }

        using (var drop = RoundedRectangle(new RectangleF(bounds.X, bounds.Y + (1.4f * scale), bounds.Width, bounds.Height), 3f * scale))
        using (var shadow = new SolidBrush(Alpha(Color.Black, palette.Dark ? 110 : 46)))
        {
            g.FillPath(shadow, drop);
        }

        using (var metal = new LinearGradientBrush(RectangleF.Inflate(bounds, 0f, 1f), palette.Brass, palette.Brass, 90f))
        {
            metal.InterpolationColors = new ColorBlend(3)
            {
                Colors =
                [
                    Mix(palette.BrassLight, palette.Brass, 0.35),
                    palette.Brass,
                    Mix(palette.Brass, palette.BrassDark, 0.55),
                ],
                Positions = [0f, 0.52f, 1f],
            };
            g.FillPath(metal, shape);
        }

        var state = g.Save();
        g.SetClip(shape, CombineMode.Intersect);
        using (var light = new Pen(Alpha(Color.White, 20), 1f))
        using (var dark = new Pen(Alpha(Color.Black, 12), 1f))
        {
            var step = Math.Max(1.5f, 1.5f * scale);
            var odd = false;
            for (var y = bounds.Top + 1f; y < bounds.Bottom; y += step)
            {
                g.DrawLine(odd ? dark : light, bounds.Left, y, bounds.Right, y);
                odd = !odd;
            }
        }

        g.Restore(state);
        using (var edge = new Pen(Mix(palette.BrassDark, Color.Black, 0.25), Math.Max(1f, 0.9f * scale)))
        {
            g.DrawPath(edge, shape);
        }

        using (var bevel = new Pen(Alpha(palette.BrassLight, 175), Math.Max(1f, 0.8f * scale)))
        {
            g.DrawLine(bevel, bounds.Left + (3f * scale), bounds.Top + (1.2f * scale),
                bounds.Right - (3f * scale), bounds.Top + (1.2f * scale));
        }

        DrawScrew(g, new PointF(bounds.Left + (8.5f * scale), midline), 2.7f * scale, 35f, palette);
        DrawScrew(g, new PointF(bounds.Right - (8.5f * scale), midline), 2.7f * scale, 118f, palette);

        DrawEngraved(g, PlateLabel, fonts.PlateLabel, labelX, labelY, 1.3f * scale, scale, palette);
        var dotRadius = 1.3f * scale;
        var dotX = labelX + labelWidth + (7.5f * scale);
        using (var shine = new SolidBrush(Alpha(palette.BrassLight, 150)))
        {
            g.FillEllipse(shine, dotX - dotRadius, midline - dotRadius + (0.8f * scale), dotRadius * 2f, dotRadius * 2f);
        }

        using (var dot = new SolidBrush(palette.Engraving))
        {
            g.FillEllipse(dot, dotX - dotRadius, midline - dotRadius, dotRadius * 2f, dotRadius * 2f);
        }

        DrawEngraved(g, PlateName, fonts.PlateName, nameX, nameY, 1.7f * scale, scale, palette);
    }

    private static void DrawEngraved(
        Graphics g, string text, Font font, float x, float y, float tracking, float scale, SetupPalette palette)
    {
        DrawTracked(g, text, font, Alpha(palette.BrassLight, 150), x, y + (0.85f * scale), tracking);
        DrawTracked(g, text, font, palette.Engraving, x, y, tracking);
    }

    private static void DrawScrew(Graphics g, PointF center, float radius, float slotDegrees, SetupPalette palette)
    {
        var head = new RectangleF(center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(head);
            using var metal = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(center.X - (radius * 0.35f), center.Y - (radius * 0.4f)),
                CenterColor = palette.BrassLight,
                SurroundColors = [palette.BrassDark],
            };
            g.FillPath(metal, path);
        }

        using (var rim = new Pen(Mix(palette.BrassDark, Color.Black, 0.35), Math.Max(0.8f, radius * 0.22f)))
        {
            g.DrawEllipse(rim, head);
        }

        var radians = slotDegrees * MathF.PI / 180f;
        var dx = MathF.Cos(radians) * radius * 0.78f;
        var dy = MathF.Sin(radians) * radius * 0.78f;
        using var slot = new Pen(Alpha(palette.Socket, 190), Math.Max(0.8f, radius * 0.32f));
        g.DrawLine(slot, center.X - dx, center.Y - dy, center.X + dx, center.Y + dy);
    }

    private static StringFormat TrackingFormat()
    {
        var format = new StringFormat(StringFormat.GenericTypographic);
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip;
        return format;
    }
}
