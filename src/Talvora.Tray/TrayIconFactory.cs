using System.Diagnostics;
using Talvora.Shared;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Tray;

internal static class TrayIconFactory
{
    public static Icon CreateStatusIcon(
        TalvoraConnectionState state,
        Color statusColor)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        using var applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (applicationIcon is not null)
        {
            using var brandBitmap = applicationIcon.ToBitmap();
            graphics.DrawImage(brandBitmap, new Rectangle(0, 0, 32, 32));
        }
        else
        {
            DrawBrandFallback(graphics);
        }

        DrawStatusBadge(graphics, state, statusColor);

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            _ = NativeUser32.DestroyIcon(handle);
        }
    }

    private static void DrawBrandFallback(Graphics graphics)
    {
        using var background = new SolidBrush(Color.FromArgb(9, 30, 49));
        using var border = new Pen(Color.FromArgb(0, 229, 255), 1.4f);
        using var brandPen = new Pen(Color.FromArgb(44, 160, 255), 2.6f)
        {
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        using var tBrush = new SolidBrush(Color.FromArgb(68, 220, 255));

        graphics.FillEllipse(background, 1, 1, 30, 30);
        graphics.DrawEllipse(border, 1.5f, 1.5f, 29, 29);

        var hex = new[]
        {
            new PointF(16, 5),
            new PointF(24, 10),
            new PointF(24, 20),
            new PointF(16, 27),
            new PointF(8, 20),
            new PointF(8, 10),
            new PointF(16, 5),
        };
        graphics.DrawLines(brandPen, hex);

        var t = new[]
        {
            new PointF(10, 12),
            new PointF(22, 12),
            new PointF(20, 15),
            new PointF(18, 16),
            new PointF(18, 24),
            new PointF(16, 26),
            new PointF(14, 24),
            new PointF(14, 16),
            new PointF(12, 15),
        };
        graphics.FillPolygon(tBrush, t);
    }

    private static void DrawStatusBadge(
        Graphics graphics,
        TalvoraConnectionState state,
        Color statusColor)
    {
        const float x = 20.5f;
        const float y = 20.5f;
        const float size = 10.5f;

        using var halo = new SolidBrush(Color.FromArgb(235, 5, 18, 31));
        using var fill = new SolidBrush(statusColor);
        using var outline = new Pen(Color.FromArgb(245, 235, 246, 255), 1.0f);
        graphics.FillEllipse(halo, x - 1.8f, y - 1.8f, size + 3.6f, size + 3.6f);
        graphics.FillEllipse(fill, x, y, size, size);
        graphics.DrawEllipse(outline, x, y, size, size);

        using var glyph = new Pen(Color.White, 1.35f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };

        switch (state)
        {
            case TalvoraConnectionState.Ready:
                graphics.DrawLines(glyph, new[]
                {
                    new PointF(23.1f, 26.0f),
                    new PointF(24.9f, 27.6f),
                    new PointF(28.5f, 23.8f),
                });
                break;

            case TalvoraConnectionState.LocalOnly:
                graphics.DrawLine(glyph, 23.4f, 25.8f, 28.4f, 25.8f);
                break;

            default:
                graphics.DrawLine(glyph, 23.8f, 23.8f, 28.1f, 28.1f);
                graphics.DrawLine(glyph, 28.1f, 23.8f, 23.8f, 28.1f);
                break;
        }
    }
    public static Icon CreateGiteaStatusIcon(GiteaConnectionState state)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        using var background = new SolidBrush(Color.FromArgb(56, 108, 73));
        using var border = new Pen(Color.FromArgb(183, 226, 194), 1.2f);
        graphics.FillEllipse(background, 1.5f, 1.5f, 29, 29);
        graphics.DrawEllipse(border, 1.5f, 1.5f, 29, 29);

        using var branchPen = new Pen(Color.White, 2.1f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        using var nodeBrush = new SolidBrush(Color.White);

        graphics.DrawLine(branchPen, 10.5f, 9.5f, 10.5f, 22.0f);
        graphics.DrawBezier(
            branchPen,
            10.5f, 15.0f,
            15.0f, 15.0f,
            17.0f, 11.0f,
            21.0f, 11.0f);

        graphics.FillEllipse(nodeBrush, 7.7f, 6.7f, 5.6f, 5.6f);
        graphics.FillEllipse(nodeBrush, 7.7f, 19.2f, 5.6f, 5.6f);
        graphics.FillEllipse(nodeBrush, 18.2f, 8.2f, 5.6f, 5.6f);

        var statusColor = state switch
        {
            GiteaConnectionState.Running => Color.FromArgb(72, 214, 116),
            GiteaConnectionState.Degraded => Color.FromArgb(244, 180, 58),
            GiteaConnectionState.Restarting => Color.FromArgb(244, 180, 58),
            _ => Color.FromArgb(230, 74, 74),
        };

        using var halo = new SolidBrush(Color.FromArgb(238, 8, 23, 16));
        using var statusBrush = new SolidBrush(statusColor);
        using var statusOutline = new Pen(Color.White, 1.0f);
        graphics.FillEllipse(halo, 20.0f, 20.0f, 12.5f, 12.5f);
        graphics.FillEllipse(statusBrush, 21.7f, 21.7f, 9.2f, 9.2f);
        graphics.DrawEllipse(statusOutline, 21.7f, 21.7f, 9.2f, 9.2f);

        using var glyphPen = new Pen(Color.White, 1.25f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };

        switch (state)
        {
            case GiteaConnectionState.Running:
                graphics.DrawLines(glyphPen, new[]
                {
                    new PointF(23.5f, 26.2f),
                    new PointF(25.1f, 27.6f),
                    new PointF(29.1f, 23.9f),
                });
                break;

            case GiteaConnectionState.Degraded:
                graphics.DrawLine(glyphPen, 26.3f, 23.7f, 26.3f, 27.1f);
                graphics.FillEllipse(nodeBrush, 25.7f, 28.2f, 1.2f, 1.2f);
                break;

            case GiteaConnectionState.Restarting:
                graphics.DrawArc(glyphPen, 23.2f, 23.2f, 6.2f, 6.2f, 35, 285);
                break;

            default:
                graphics.DrawLine(glyphPen, 24.0f, 24.0f, 28.7f, 28.7f);
                graphics.DrawLine(glyphPen, 28.7f, 24.0f, 24.0f, 28.7f);
                break;
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            _ = NativeUser32.DestroyIcon(handle);
        }
    }

}
