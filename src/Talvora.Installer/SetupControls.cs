using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace Talvora.Installer;

/// <summary>The window that paints the textured panel its children sit on.</summary>
internal interface ISetupSurface
{
    Bitmap? Backdrop { get; }
}

internal static class SetupSurface
{
    internal static void PaintBackdrop(Control control, PaintEventArgs e)
    {
        if (control.Parent is ISetupSurface { Backdrop: { } backdrop })
        {
            var g = e.Graphics;
            var interpolation = g.InterpolationMode;
            var offset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(
                backdrop,
                new Rectangle(0, 0, control.Width, control.Height),
                new Rectangle(control.Left, control.Top, control.Width, control.Height),
                GraphicsUnit.Pixel);
            g.InterpolationMode = interpolation;
            g.PixelOffsetMode = offset;
            return;
        }

        using var brush = new SolidBrush(control.Parent?.BackColor ?? SystemColors.Control);
        e.Graphics.FillRectangle(brush, control.ClientRectangle);
    }
}

internal enum CordStage
{
    Idle,
    Working,
    Done,
    Failed,
}

/// <summary>The word beside the lamp in each stage.</summary>
internal sealed record LampWords(string Idle, string Working, string Done, string Failed);

/// <summary>
/// KUR1: the setup's switchboard. The setup package's socket holds one end of a patch cord; the installer's
/// real progress carries the other plug to this computer's socket. When the install answers, the plug seats,
/// one light runs down the cord and the lamp turns green (DESIGN-SANTRAL §5 "Plug-in" and "Call").
/// Uninstalling runs it backwards. Reduced motion or high contrast: states change at once.
/// </summary>
internal sealed class SwitchboardView : Control
{
    private const int Samples = 120;
    private const double CompleteMs = 280;
    private const double SeatMs = 140;
    private const double PulseMs = 900;
    private const double LampMs = 120;
    private const double RetractMs = 450;
    private const double BreathMs = 1600;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly PointF[] _curve = new PointF[Samples + 1];
    private readonly float[] _lengths = new float[Samples + 1];
    private SetupPalette _palette = SetupPalette.Gun;
    private SetupFonts? _fonts;
    private float _scale = 1f;
    private bool _animate = true;
    private CordStage _stage = CordStage.Idle;
    private double _target;
    private double _shown;
    private double _stageFrom;
    private double _seat;
    private long _stageStart;
    private long _lastTick;
    private int _percent = -1;
    private float _radius;
    private float _seatTravel;
    private float _minimumLength;
    private PointF _left;
    private PointF _right;
    private PointF _lamp;
    private string _leftTitle = string.Empty;
    private string _leftDetail = string.Empty;
    private string _rightTitle = string.Empty;
    private string _rightDetail = string.Empty;
    private string _progressName = string.Empty;

    internal SwitchboardView(bool unplugging, LampWords words)
    {
        Unplugging = unplugging;
        Words = words;
        _shown = unplugging ? 1 : 0;
        _seat = unplugging ? 1 : 0;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.Transparent;
        TabStop = false;
        _timer.Tick += (_, _) => Advance();
    }

    internal bool Unplugging { get; }

    internal LampWords Words { get; }

    internal string ProgressName => _progressName;

    internal string? PercentText => _percent < 0 ? null : "%" + _percent.ToString(CultureInfo.InvariantCulture);

    internal void SetCaptions(
        string leftTitle, string leftDetail, string rightTitle, string rightDetail, string progressName)
    {
        _leftTitle = leftTitle;
        _leftDetail = leftDetail;
        _rightTitle = rightTitle;
        _rightDetail = rightDetail;
        _progressName = progressName;
        Invalidate();
    }

    internal void ApplyTheme(SetupPalette palette, SetupFonts fonts, float scale, bool animate)
    {
        _palette = palette;
        _fonts = fonts;
        _scale = scale;
        _animate = animate;
        Relayout();
        Invalidate();
    }

    internal void SetStage(CordStage stage)
    {
        if (stage == CordStage.Working && _stage != CordStage.Working)
        {
            _percent = 0;
            _target = 0;
        }

        _stage = stage;
        _stageFrom = _shown;
        _stageStart = _clock.ElapsedMilliseconds;
        _lastTick = _stageStart;
        if (stage == CordStage.Idle)
        {
            _shown = Unplugging ? 1 : 0;
            _seat = Unplugging ? 1 : 0;
        }

        Kick();
    }

    internal void SetProgress(double fraction)
    {
        _target = Math.Clamp(fraction, 0d, 1d);
        var percent = (int)Math.Round(_target * 100d);
        if (percent != _percent)
        {
            _percent = percent;
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }

        Kick();
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SwitchboardAccessible(this);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) => SetupSurface.PaintBackdrop(this, pevent);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_fonts is null || _lengths[Samples] <= 0f)
        {
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        DrawCaption(g, _leftTitle, _leftDetail, _left.X);
        DrawCaption(g, _rightTitle, _rightDetail, _right.X);
        SetupArt.DrawSocket(g, _left, _radius, _palette);
        SetupArt.DrawSocket(g, _right, _radius, _palette);

        var (glass, glow, word, wordColor) = LampNow();
        SetupArt.DrawLamp(g, _lamp, 7f * _scale, glass, glow, _palette);
        using (var format = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center })
        using (var brush = new SolidBrush(wordColor))
        {
            g.DrawString(word, _fonts.Word, brush, new PointF(_lamp.X + (16f * _scale), _lamp.Y - (0.5f * _scale)), format);
        }

        var plug = SetupArt.PlugLength(_scale);
        var reach = _minimumLength + ((_lengths[Samples] - _minimumLength) * (float)_shown);
        var cord = PointsUpTo(reach, out var tail, out var direction);
        if (_shown >= 0.999 && _seat > 0)
        {
            tail = new PointF(tail.X, tail.Y - (_seatTravel * (float)_seat));
            cord.Add(tail);
            direction = new PointF(0f, -1f);
        }

        SetupArt.DrawCable(g, [.. cord], _scale, _palette);
        SetupArt.DrawPlug(g, SeatedTip(_left), -90f, _scale, _palette);
        var tip = new PointF(tail.X + (direction.X * plug), tail.Y + (direction.Y * plug));
        var heading = MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;
        SetupArt.DrawPlug(g, tip, heading, _scale, _palette);

        var pulse = PulseAt();
        if (pulse is > 0 and < 1)
        {
            var (point, _) = PointAt((float)pulse * reach);
            SetupArt.DrawPulse(g, point, _scale, _palette);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private static double EaseOut(double t)
    {
        var x = Math.Clamp(t, 0d, 1d);
        return 1d - Math.Pow(1d - x, 3d);
    }

    private static double EaseInOut(double t)
    {
        var x = Math.Clamp(t, 0d, 1d);
        return x < 0.5 ? 4d * x * x * x : 1d - (Math.Pow((-2d * x) + 2d, 3d) / 2d);
    }

    private PointF SeatedTip(PointF socket) => new(socket.X, socket.Y + (_radius * 0.62f));

    private void DrawCaption(Graphics g, string title, string detail, float centerX)
    {
        if (_fonts is null)
        {
            return;
        }

        var tracking = 1.2f * _scale;
        var titleWidth = SetupArt.MeasureTracked(g, title, _fonts.Label, tracking);
        SetupArt.DrawTracked(g, title, _fonts.Label, _palette.Ink, centerX - (titleWidth / 2f), 10f * _scale, tracking);
        using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center };
        using var brush = new SolidBrush(_palette.InkMuted);
        g.DrawString(detail, _fonts.Mono, brush, new PointF(centerX, 33f * _scale), format);
    }

    private (Color Glass, double Glow, string Word, Color WordColor) LampNow()
    {
        var t = _clock.ElapsedMilliseconds - _stageStart;
        switch (_stage)
        {
            case CordStage.Working:
            {
                var breath = _animate ? 0.89 + (0.11 * Math.Cos(2d * Math.PI * t / BreathMs)) : 1d;
                return (SetupArt.Mix(_palette.LampOff, _palette.LampAttention, breath), breath,
                    Words.Working, _palette.WordAttention);
            }

            case CordStage.Done when !Unplugging:
            {
                var arrival = _animate ? CompleteMs + SeatMs + PulseMs : 0d;
                if (t < arrival)
                {
                    return (_palette.LampAttention, 1d, Words.Working, _palette.WordAttention);
                }

                var turn = _animate ? Math.Clamp((t - arrival) / LampMs, 0d, 1d) : 1d;
                return (SetupArt.Mix(_palette.LampAttention, _palette.LampReady, turn), 1d,
                    Words.Done, _palette.WordReady);
            }

            case CordStage.Done:
                return (_palette.LampOff, 0d, Words.Done, _palette.InkFaint);
            case CordStage.Failed:
                return (_palette.LampOffline, 1d, Words.Failed, _palette.WordOffline);
            default:
                return Unplugging
                    ? (_palette.LampReady, 1d, Words.Idle, _palette.WordReady)
                    : (_palette.LampOff, 0d, Words.Idle, _palette.InkFaint);
        }
    }

    private double PulseAt()
    {
        if (_stage != CordStage.Done || Unplugging || !_animate)
        {
            return -1d;
        }

        var t = _clock.ElapsedMilliseconds - _stageStart - CompleteMs - SeatMs;
        return t <= 0 ? -1d : t >= PulseMs ? 2d : EaseInOut(t / PulseMs);
    }

    private void Kick()
    {
        if (!_animate)
        {
            Settle();
            Invalidate();
            return;
        }

        if (!_timer.Enabled)
        {
            _lastTick = _clock.ElapsedMilliseconds;
            _timer.Start();
        }

        Invalidate();
    }

    private void Settle()
    {
        switch (_stage)
        {
            case CordStage.Working:
                _seat = 0;
                _shown = Unplugging ? 1d - _target : _target;
                break;
            case CordStage.Done:
                _shown = Unplugging ? 0d : 1d;
                _seat = Unplugging ? 0d : 1d;
                break;
            case CordStage.Failed:
                if (!Unplugging)
                {
                    _shown = 0d;
                }

                _seat = 0d;
                break;
            default:
                _shown = Unplugging ? 1d : 0d;
                _seat = Unplugging ? 1d : 0d;
                break;
        }
    }

    private void Advance()
    {
        var now = _clock.ElapsedMilliseconds;
        var step = Math.Min(64d, now - _lastTick);
        _lastTick = now;
        var t = now - _stageStart;
        var running = false;
        switch (_stage)
        {
            case CordStage.Working:
            {
                running = true;
                if (Unplugging && _seat > 0)
                {
                    _seat = 1d - EaseInOut(t / SeatMs);
                    _shown = 1d;
                    break;
                }

                var goal = Unplugging ? 1d - _target : _target;
                _shown = goal + ((_shown - goal) * Math.Exp(-step / 220d));
                break;
            }

            case CordStage.Done when !Unplugging:
                _shown = _stageFrom + ((1d - _stageFrom) * EaseOut(t / CompleteMs));
                _seat = EaseInOut((t - CompleteMs) / SeatMs);
                running = t < CompleteMs + SeatMs + PulseMs + LampMs;
                break;
            case CordStage.Done:
                _shown = _stageFrom * (1d - EaseOut(t / CompleteMs));
                _seat = 0d;
                running = t < CompleteMs;
                break;
            case CordStage.Failed when !Unplugging:
                _shown = _stageFrom * (1d - EaseInOut(t / RetractMs));
                _seat = 0d;
                running = t < RetractMs;
                break;
            default:
                Settle();
                break;
        }

        Invalidate();
        if (!running)
        {
            _timer.Stop();
        }
    }

    private void Relayout()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var s = _scale;
        _radius = 22f * s;
        var socketY = 84f * s;
        _left = new PointF(MathF.Round(Width * 0.22f), socketY);
        _right = new PointF(MathF.Round(Width * 0.62f), socketY);
        _lamp = new PointF(_right.X + _radius + (27f * s), socketY);
        _seatTravel = 10f * s;
        _minimumLength = 14f * s;
        var tailY = SeatedTip(_left).Y + SetupArt.PlugLength(s);
        var start = new PointF(_left.X, tailY);
        var end = new PointF(_right.X, tailY + _seatTravel);
        var sag = 70f * s;
        var c1 = new PointF(start.X, start.Y + sag);
        var c2 = new PointF(end.X, end.Y + sag);
        _lengths[0] = 0f;
        for (var i = 0; i <= Samples; i++)
        {
            var u = i / (float)Samples;
            var v = 1f - u;
            _curve[i] = new PointF(
                (v * v * v * start.X) + (3f * v * v * u * c1.X) + (3f * v * u * u * c2.X) + (u * u * u * end.X),
                (v * v * v * start.Y) + (3f * v * v * u * c1.Y) + (3f * v * u * u * c2.Y) + (u * u * u * end.Y));
            if (i > 0)
            {
                var dx = _curve[i].X - _curve[i - 1].X;
                var dy = _curve[i].Y - _curve[i - 1].Y;
                _lengths[i] = _lengths[i - 1] + MathF.Sqrt((dx * dx) + (dy * dy));
            }
        }
    }

    private (PointF Point, PointF Direction) PointAt(float length)
    {
        var target = Math.Clamp(length, 0f, _lengths[Samples]);
        var i = 1;
        while (i < Samples && _lengths[i] < target)
        {
            i++;
        }

        var segment = _lengths[i] - _lengths[i - 1];
        var k = segment <= 0f ? 0f : (target - _lengths[i - 1]) / segment;
        var a = _curve[i - 1];
        var b = _curve[i];
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var norm = MathF.Max(0.0001f, MathF.Sqrt((dx * dx) + (dy * dy)));
        return (new PointF(a.X + (dx * k), a.Y + (dy * k)), new PointF(dx / norm, dy / norm));
    }

    private List<PointF> PointsUpTo(float length, out PointF tail, out PointF direction)
    {
        var points = new List<PointF> { _curve[0] };
        for (var i = 1; i <= Samples && _lengths[i] < length; i++)
        {
            points.Add(_curve[i]);
        }

        (tail, direction) = PointAt(length);
        points.Add(tail);
        return points;
    }

    private sealed class SwitchboardAccessible(SwitchboardView owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.ProgressBar;

        public override string? Name => owner.ProgressName;

        public override string? Value => owner.PercentText;
    }
}

/// <summary>KUR1: the maker plate; screen readers hear <see cref="InstallerForm.DeveloperCredit"/>.</summary>
internal sealed class MakerPlate : Control
{
    private SetupPalette _palette = SetupPalette.Gun;
    private SetupFonts? _fonts;
    private float _scale = 1f;

    internal MakerPlate()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.Transparent;
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = InstallerForm.DeveloperCredit;
    }

    internal void ApplyTheme(SetupPalette palette, SetupFonts fonts, float scale)
    {
        _palette = palette;
        _fonts = fonts;
        _scale = scale;
        using var bitmap = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var plate = SetupArt.MeasurePlate(g, fonts, scale);
        Size = new Size((int)MathF.Ceiling(plate.Width) + 2, (int)MathF.Ceiling(plate.Height + (3f * scale)) + 1);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) => SetupSurface.PaintBackdrop(this, pevent);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_fonts is null)
        {
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        SetupArt.DrawPlate(g, new RectangleF(0.5f, 0.5f, Width - 2f, Height - (3f * _scale) - 1f), _scale, _palette, _fonts);
    }
}

/// <summary>KUR1: a Santral button: filled ink for the main action, an ink outline for the other.</summary>
internal sealed class SetupButton : Button
{
    private SetupPalette _palette = SetupPalette.Gun;
    private float _scale = 1f;
    private bool _hover;
    private bool _pressed;

    internal SetupButton(bool primary)
    {
        Primary = primary;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        UseMnemonic = false;
        Cursor = Cursors.Hand;
    }

    internal bool Primary { get; }

    internal float Inset => 4f * _scale;

    internal void ApplyTheme(SetupPalette palette, Font font, float scale)
    {
        _palette = palette;
        _scale = scale;
        Font = font;
        Invalidate();
    }

    internal int PreferredWidth()
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        return Math.Max((int)MathF.Round(108f * _scale), text.Width + (int)MathF.Round(48f * _scale));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) => SetupSurface.PaintBackdrop(this, pevent);

    protected override void OnPaint(PaintEventArgs pevent)
    {
        // ButtonBase is opaque, so the panel behind the rounded face is painted here.
        SetupSurface.PaintBackdrop(this, pevent);
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var inset = Inset;
        var face = new RectangleF(inset, inset, Width - (2f * inset) - 1f, Height - (2f * inset) - 1f);
        using var shape = SetupArt.RoundedRectangle(face, 4f * _scale);
        Color fill;
        Color text;
        Color? border = null;
        if (_palette.HighContrast)
        {
            fill = Primary ? SystemColors.Highlight : SystemColors.ButtonFace;
            text = Primary ? SystemColors.HighlightText : SystemColors.ControlText;
            border = SystemColors.ControlText;
            if (!Enabled)
            {
                fill = SystemColors.ButtonFace;
                text = SystemColors.GrayText;
            }
        }
        else if (Primary)
        {
            fill = !Enabled ? SetupArt.Alpha(_palette.Rule, 120)
                : _pressed ? SetupArt.Mix(_palette.Ink, _palette.InkMuted, 0.3)
                : _hover ? SetupArt.Mix(_palette.Ink, _palette.InkMuted, 0.16)
                : _palette.Ink;
            text = Enabled ? _palette.OnInk : _palette.InkFaint;
        }
        else
        {
            fill = _pressed ? _palette.PanelSunken : _hover ? _palette.PanelRaised : Color.Transparent;
            text = Enabled ? _palette.Ink : _palette.InkFaint;
            border = Enabled ? _palette.Ink : _palette.Rule;
        }

        if (fill.A > 0)
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, shape);
        }

        if (border is { } line)
        {
            using var pen = new Pen(line, 1.5f * _scale);
            g.DrawPath(pen, shape);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            Rectangle.Round(face),
            text,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding);

        if (Focused && ShowFocusCues)
        {
            using var ring = SetupArt.RoundedRectangle(RectangleF.Inflate(face, 2.5f * _scale, 2.5f * _scale), 6f * _scale);
            using var pen = new Pen(_palette.Focus, 2f * _scale);
            g.DrawPath(pen, ring);
        }
    }
}

/// <summary>KUR1: the uninstall window's one option, drawn in the same ink.</summary>
internal sealed class SetupCheckBox : CheckBox
{
    private SetupPalette _palette = SetupPalette.Gun;
    private float _scale = 1f;
    private bool _hover;

    internal SetupCheckBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        AutoSize = false;
        UseMnemonic = false;
        Cursor = Cursors.Hand;
    }

    internal void ApplyTheme(SetupPalette palette, Font font, float scale)
    {
        _palette = palette;
        _scale = scale;
        Font = font;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs eventargs)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(eventargs);
    }

    protected override void OnMouseLeave(EventArgs eventargs)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(eventargs);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) => SetupSurface.PaintBackdrop(this, pevent);

    protected override void OnPaint(PaintEventArgs pevent)
    {
        SetupSurface.PaintBackdrop(this, pevent);
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var size = 16f * _scale;
        var box = new RectangleF(3f * _scale, (Height - size) / 2f, size, size);
        using var shape = SetupArt.RoundedRectangle(box, 3f * _scale);
        using (var fill = new SolidBrush(_palette.HighContrast ? SystemColors.Window
                   : _hover ? _palette.PanelSunken : _palette.PanelRaised))
        {
            g.FillPath(fill, shape);
        }

        using (var border = new Pen(_palette.Ink, 1.5f * _scale))
        {
            g.DrawPath(border, shape);
        }

        if (Checked)
        {
            using var mark = new Pen(_palette.Ink, 2f * _scale)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            g.DrawLines(mark,
            [
                new PointF(box.Left + (size * 0.22f), box.Top + (size * 0.52f)),
                new PointF(box.Left + (size * 0.43f), box.Top + (size * 0.72f)),
                new PointF(box.Left + (size * 0.78f), box.Top + (size * 0.28f)),
            ]);
        }

        if (Focused && ShowFocusCues)
        {
            using var ring = SetupArt.RoundedRectangle(RectangleF.Inflate(box, 3f * _scale, 3f * _scale), 5f * _scale);
            using var pen = new Pen(_palette.Focus, 2f * _scale);
            g.DrawPath(pen, ring);
        }

        var textLeft = (int)MathF.Round(box.Right + (10f * _scale));
        TextRenderer.DrawText(
            g,
            Text,
            Font,
            new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height),
            _palette.Ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis);
    }
}
