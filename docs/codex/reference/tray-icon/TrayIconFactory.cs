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
    // Altıgen-T (Claude, 2026-10-24): the mark is vector art drawn at the tray's exact size in the taskbar
    // theme's colours (TrayIconArt); the executable icon is no longer shrunk into the tray.
    public static Icon CreateStatusIcon(
        TalvoraConnectionState state,
        TrayIconAppearance appearance)
    {
        using var bitmap = TrayIconArt.Render(
            state switch
            {
                TalvoraConnectionState.Ready => TrayIconState.Ready,
                TalvoraConnectionState.LocalOnly => TrayIconState.Attention,
                _ => TrayIconState.Offline,
            },
            appearance);

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
