using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Talvora.Tray.ViewModels;
using Brush = System.Windows.Media.Brush;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Talvora.Tray.Controls;

/// <summary>One socket on the switchboard: a managed service and, when it has one, its outside line.</summary>
internal sealed record SwitchboardSocket(
    string Id,
    string Name,
    string? Note,
    ComponentVisualState State,
    string? Line,
    string? OutsideTitle,
    string? OutsideSub,
    long? Calls,
    long? Failed,
    DateTimeOffset? LastCallAtUtc,
    DateTimeOffset StateSinceUtc,
    ManagedMcpDashboardState Source);

/// <summary>
/// SN3/SN4 (DESIGN-SANTRAL §4–§5): the Santral switchboard. Everything drawn is a measurement: lamps are health
/// states, a cable is a live connection, a light is one finished tool call (SN1), the reconnect timer counts from
/// the observed state change. Animation runs only while something real is moving.
/// </summary>
internal sealed class Switchboard : Canvas
{
    private static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan PlugInDuration = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan DropDuration = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan SwingDuration = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan SeatDuration = TimeSpan.FromMilliseconds(600);
    private const int MaxPulsesPerLinePerSecond = 12;
    private const int MaxCachedTexts = 512;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // SN9: four type faces (Bahnschrift normal/condensed, Cascadia Mono; each normal/semibold), built once.
    private static readonly Typeface[] Faces = BuildFaces();
    private static readonly Brush LampHighlight = Frozen(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)));
    private static readonly Transform CableShadowOffset = Frozen(new TranslateTransform(3, 5));
    private static readonly Transform CableShineOffset = Frozen(new TranslateTransform(-1.5, -1.5));

    // Bound to Sn.Panel by resource reference: a theme swap changes it and re-renders the board.
    public static readonly DependencyProperty ThemeTokenProperty = DependencyProperty.Register(
        nameof(ThemeToken), typeof(object), typeof(Switchboard),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<Pulse> _pulses = [];
    private readonly Dictionary<string, (long Second, int Count)> _rate = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Motion> _motion = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _frame;
    private readonly DispatcherTimer _second;
    private IReadOnlyList<SwitchboardSocket> _sockets = [];
    private bool _firstApply = true;

    // SN9: brushes, pens and text layouts are built once per theme and DPI instead of on every frame, and
    // everything that does not move (grain, divider, section labels, jacks with their names) is one frozen layer.
    private readonly Dictionary<string, Brush> _brushes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Pen> _pens = new(StringComparer.Ordinal);
    private readonly Dictionary<Color, Brush> _halos = new();
    private readonly Dictionary<(string Text, double Size, int Face, Brush Brush), FormattedText> _texts = new();
    private object? _cacheToken;
    private double _cacheDpi;
    private DrawingGroup? _staticLayer;
    private string? _staticKey;

    public Switchboard()
    {
        SetResourceReference(ThemeTokenProperty, "Sn.Panel");
        ClipToBounds = true;
        Focusable = false;
        _frame = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _frame.Tick += (_, _) => OnFrame();
        _second = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _second.Tick += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, _) => UpdateTimers();
        Unloaded += (_, _) => { _frame.Stop(); _second.Stop(); };
        Loaded += (_, _) => UpdateTimers();
        SizeChanged += (_, _) => LayoutHotspots();
    }

    public object? ThemeToken
    {
        get => GetValue(ThemeTokenProperty);
        set => SetValue(ThemeTokenProperty, value);
    }

    public event Action<ManagedMcpDashboardState>? SocketActivated;

    public IReadOnlyList<SwitchboardSocket> Sockets => _sockets;
    public int ActivePulseCount => _pulses.Count;

    internal static bool MotionAllowed =>
        SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast &&
        SettingsPreferencesStore.Current.Motion != SettingsMotionPreference.Reduced;

    public void Apply(IReadOnlyList<SwitchboardSocket> sockets)
    {
        ArgumentNullException.ThrowIfNull(sockets);
        var now = _clock.Elapsed;
        foreach (var socket in sockets)
        {
            var plugged = IsPlugged(socket.State);
            if (!_motion.TryGetValue(socket.Id, out var motion))
            {
                // First paint: cables plug in one after another (DESIGN-SANTRAL §5 "Plug-in").
                var index = _motion.Count;
                _motion[socket.Id] = new Motion
                {
                    Plugged = plugged,
                    ChangedAt = _firstApply ? now + TimeSpan.FromMilliseconds(90 * index) : now,
                    Kind = plugged ? MotionKind.PlugIn : MotionKind.Still,
                };
                continue;
            }
            if (motion.Plugged == plugged) continue;
            motion.Kind = plugged ? (motion.Kind is MotionKind.Drop or MotionKind.Hanging ? MotionKind.Seat : MotionKind.PlugIn)
                : MotionKind.Drop;
            motion.Plugged = plugged;
            motion.ChangedAt = now;
        }
        _firstApply = false;
        _sockets = sockets;
        if (!MotionAllowed)
            foreach (var motion in _motion.Values) motion.ChangedAt = TimeSpan.MinValue;
        UpdateHotspots();
        InvalidateVisual();
        UpdateTimers();
    }

    /// <summary>Adds one light per finished call (SN1 feed); extra calls beyond the cap only raise the counter.</summary>
    public void AddCalls(IEnumerable<(string Line, bool Ok)> calls)
    {
        if (!MotionAllowed) return;
        var now = _clock.Elapsed;
        var second = (long)now.TotalSeconds;
        foreach (var (line, ok) in calls)
        {
            var socket = _sockets.FirstOrDefault(item => item.Line == line && item.OutsideTitle is not null);
            if (socket is null || !IsPlugged(socket.State)) continue;
            var (lastSecond, count) = _rate.TryGetValue(line, out var rate) ? rate : (second, 0);
            if (lastSecond != second) count = 0;
            if (count >= MaxPulsesPerLinePerSecond) continue;
            _rate[line] = (second, count + 1);
            _pulses.Add(new Pulse(socket.Id, now + TimeSpan.FromMilliseconds(60 * count), ok));
        }
        UpdateTimers();
    }

    /// <summary>Smoke and screenshots: show every cable in its final, real position at once.</summary>
    internal void Settle()
    {
        foreach (var motion in _motion.Values) { motion.ChangedAt = TimeSpan.MinValue; motion.Kind = motion.Plugged ? MotionKind.Still : MotionKind.Hanging; }
        _pulses.Clear();
        InvalidateVisual();
        UpdateTimers();
    }

    private static bool IsPlugged(ComponentVisualState state) =>
        state is ComponentVisualState.Ready or ComponentVisualState.Attention or ComponentVisualState.Repairing;

    private void UpdateTimers()
    {
        var visible = IsVisible && IsLoaded;
        var now = _clock.Elapsed;
        var fast = MotionAllowed && (_pulses.Count > 0 ||
            _motion.Values.Any(motion => motion.Kind != MotionKind.Still && motion.Kind != MotionKind.Hanging &&
                now - motion.ChangedAt < MotionLength(motion.Kind)));
        // A 1.6 s breath needs ten frames a second, not sixty (idle CPU, DESIGN-SANTRAL §5).
        var breathing = MotionAllowed &&
            _sockets.Any(socket => socket.State is ComponentVisualState.Attention or ComponentVisualState.Repairing);
        var moving = fast || breathing;
        _frame.Interval = TimeSpan.FromMilliseconds(fast ? 16 : 100);
        if (visible && moving) { if (!_frame.IsEnabled) _frame.Start(); }
        else _frame.Stop();
        // The reconnect timer and the "son çağrı" text need one repaint per second, nothing more.
        var ticking = _sockets.Any(socket => socket.State is not ComponentVisualState.Ready and not ComponentVisualState.Neutral);
        if (visible && ticking && !moving) { if (!_second.IsEnabled) _second.Start(); }
        else _second.Stop();
    }

    private static TimeSpan MotionLength(MotionKind kind) => kind switch
    {
        MotionKind.PlugIn => PlugInDuration + TimeSpan.FromMilliseconds(200),
        MotionKind.Drop => DropDuration + SwingDuration,
        MotionKind.Seat => SeatDuration + TimeSpan.FromMilliseconds(200),
        _ => TimeSpan.Zero,
    };

    private void OnFrame()
    {
        var now = _clock.Elapsed;
        _pulses.RemoveAll(pulse => now - pulse.Start > PulseDuration);
        foreach (var motion in _motion.Values)
            if (motion.Kind == MotionKind.Drop && now - motion.ChangedAt > DropDuration + SwingDuration)
                motion.Kind = MotionKind.Hanging;
            else if (motion.Kind is MotionKind.PlugIn or MotionKind.Seat && now - motion.ChangedAt > MotionLength(motion.Kind))
                motion.Kind = MotionKind.Still;
        InvalidateVisual();
        UpdateTimers();
    }

    // ---------- geometry ----------

    private sealed record Place(SwitchboardSocket Socket, Point SocketCenter, Point? OutsideCenter);

    private IReadOnlyList<Place> Arrange()
    {
        var width = Math.Max(ActualWidth, 640);
        var height = Math.Max(ActualHeight, 360);
        var left = 190.0;
        var right = width - 130.0;
        var socketY = height - 112;
        var outsideY = 104.0;
        var n = _sockets.Count;
        var places = new List<Place>(n);
        // Outside lines in exchange order: tunnelled lines first, local clients last, so cables cross like a real board.
        var outside = _sockets.Where(socket => socket.OutsideTitle is not null)
            .OrderBy(socket => socket.Line switch { "dev" => 0, "admin" => 1, null => 2, "core" => 3, _ => 4 })
            .Select(socket => socket.Id).ToList();
        for (var i = 0; i < n; i++)
        {
            var socket = _sockets[i];
            var x = n == 1 ? (left + right) / 2 : left + i * (right - left) / (n - 1);
            Point? o = null;
            var j = outside.IndexOf(socket.Id);
            if (j >= 0)
            {
                var m = outside.Count;
                var ox = m == 1 ? (left + right) / 2 : left + 70 + j * (right - left - 140) / (m - 1);
                o = new Point(ox, outsideY);
            }
            places.Add(new Place(socket, new Point(x, socketY), o));
        }
        return places;
    }

    private static (Point A, Point C1, Point C2, Point B) Cable(Point socket, Point end, bool seated)
    {
        var a = new Point(socket.X, socket.Y - 30);
        if (seated)
        {
            var b = new Point(end.X, end.Y + 30);
            var sag = 46 + Math.Abs(a.X - b.X) * 0.06;
            return (a, new Point(a.X, Lerp(a.Y, b.Y, 0.32) + sag), new Point(b.X, Lerp(a.Y, b.Y, 0.72) + sag * 0.35), b);
        }
        // A dropped plug hangs beside its socket; the cable rises a little and falls back.
        return (a, new Point(a.X, a.Y - 120), new Point(end.X, end.Y - 110), end);
    }

    private static Point Bezier((Point A, Point C1, Point C2, Point B) c, double t)
    {
        var u = 1 - t;
        return new Point(
            u * u * u * c.A.X + 3 * u * u * t * c.C1.X + 3 * u * t * t * c.C2.X + t * t * t * c.B.X,
            u * u * u * c.A.Y + 3 * u * u * t * c.C1.Y + 3 * u * t * t * c.C2.Y + t * t * t * c.B.Y);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static double Ease(double t) { t = Math.Clamp(t, 0, 1); return 1 - Math.Pow(1 - t, 3); }
    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    private Point PlugEnd(Place place, Motion? motion, TimeSpan now, out bool seated, out double drawn)
    {
        drawn = 1;
        var outside = place.OutsideCenter!.Value;
        var seatedEnd = new Point(outside.X, outside.Y + 30);
        var hang = new Point(place.SocketCenter.X - 54, place.SocketCenter.Y - 64);
        seated = motion?.Plugged ?? IsPlugged(place.Socket.State);
        if (motion is null || motion.ChangedAt == TimeSpan.MinValue) return seated ? seatedEnd : hang;
        var t = (now - motion.ChangedAt).TotalMilliseconds;
        switch (motion.Kind)
        {
            case MotionKind.PlugIn:
                drawn = Ease(t / PlugInDuration.TotalMilliseconds);
                return seatedEnd;
            case MotionKind.Drop:
                {
                    seated = false;
                    var fall = Ease(t / DropDuration.TotalMilliseconds);
                    var s = Math.Max(0, t - DropDuration.TotalMilliseconds) / 1000.0;
                    var swing = t < DropDuration.TotalMilliseconds ? 0 : 70 * Math.Exp(-1.1 * s) * Math.Sin(6.2 * s);
                    return new Point(Lerp(seatedEnd.X, hang.X, fall) + swing, Lerp(seatedEnd.Y, hang.Y, fall) - 10 * Math.Abs(swing) / 70);
                }
            case MotionKind.Seat:
                {
                    var k = Smooth(t / SeatDuration.TotalMilliseconds);
                    seated = k >= 1;
                    return new Point(Lerp(hang.X, seatedEnd.X, k), Lerp(hang.Y, seatedEnd.Y, k) - 60 * Math.Sin(Math.PI * k));
                }
            default:
                return seated ? seatedEnd : hang;
        }
    }

    // ---------- caches (SN9) ----------

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Typeface[] BuildFaces()
    {
        var bahnschrift = new FontFamily("Bahnschrift");
        var mono = new FontFamily("Cascadia Mono, Consolas");
        var faces = new Typeface[8];
        for (var i = 0; i < faces.Length; i++)
        {
            var weight = (i & 2) != 0 ? FontWeights.SemiBold : FontWeights.Normal;
            faces[i] = (i & 4) != 0
                ? new Typeface(mono, FontStyles.Normal, weight, FontStretches.Normal)
                : new Typeface(bahnschrift, FontStyles.Normal, weight, (i & 1) != 0 ? FontStretches.Condensed : FontStretches.Normal);
        }
        return faces;
    }

    /// <summary>Drops every cached brush, pen, text and the static layer when the theme or the DPI changes.</summary>
    private void EnsureCaches(double dpi)
    {
        if (ReferenceEquals(_cacheToken, ThemeToken) && _cacheDpi == dpi) return;
        _cacheToken = ThemeToken;
        _cacheDpi = dpi;
        _brushes.Clear();
        _pens.Clear();
        _halos.Clear();
        _texts.Clear();
        _staticLayer = null;
        _staticKey = null;
    }

    private Brush Res(string key, Color fallback)
    {
        if (_brushes.TryGetValue(key, out var cached)) return cached;
        var brush = TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
        // A frozen copy: shared theme brushes are never frozen in place, and frozen brushes draw without change tracking.
        if (!brush.IsFrozen)
        {
            brush = brush.CloneCurrentValue();
            if (brush.CanFreeze) brush.Freeze();
        }
        _brushes[key] = brush;
        return brush;
    }

    private Pen CachedPen(string name, Func<Pen> create)
    {
        if (_pens.TryGetValue(name, out var pen)) return pen;
        pen = create();
        if (pen.CanFreeze) pen.Freeze();
        _pens[name] = pen;
        return pen;
    }

    private Brush JackRing()
    {
        if (_brushes.TryGetValue("jack-ring", out var ring)) return ring;
        var brass = Res("Sn.Brass", Color.FromRgb(0xB0, 0x8D, 0x57));
        var light = Res("Sn.BrassLight", Color.FromRgb(0xE2, 0xC6, 0x8F));
        var dark = Res("Sn.BrassDark", Color.FromRgb(0x7A, 0x5E, 0x33));
        ring = Frozen(new RadialGradientBrush(
            [new GradientStop(((SolidColorBrush)light).Color, 0), new GradientStop(((SolidColorBrush)brass).Color, 0.6),
             new GradientStop(((SolidColorBrush)dark).Color, 1)])
        { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.5, 0.5) });
        _brushes["jack-ring"] = ring;
        return ring;
    }

    private Brush PlugMetal()
    {
        if (_brushes.TryGetValue("plug-metal", out var metal)) return metal;
        var dark = ((SolidColorBrush)Res("Sn.BrassDark", Colors.SaddleBrown)).Color;
        var light = ((SolidColorBrush)Res("Sn.BrassLight", Colors.Wheat)).Color;
        metal = Frozen(new LinearGradientBrush(
            [new GradientStop(dark, 0), new GradientStop(light, 0.45), new GradientStop(dark, 1)], 0));
        _brushes["plug-metal"] = metal;
        return metal;
    }

    // ---------- drawing ----------

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var now = _clock.Elapsed;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        EnsureCaches(dpi);
        var ink = Res("Sn.Ink", Colors.Black);
        var muted = Res("Sn.InkMuted", Colors.DimGray);
        var faint = Res("Sn.InkFaint", Colors.Gray);
        var places = Arrange();
        var socketY = places.Count > 0 ? places[0].SocketCenter.Y : ActualHeight - 112;

        // SN9: grain, divider, section labels and jacks come from one cached layer; only what moves is drawn below.
        DrawStaticLayer(dc, places, socketY, dpi, ink, muted);

        // Cables (behind lamps and words, above jacks).
        var cables = new Dictionary<string, (Point A, Point C1, Point C2, Point B)>(StringComparer.Ordinal);
        foreach (var place in places)
        {
            if (place.OutsideCenter is null || place.Socket.State == ComponentVisualState.Neutral) continue;
            _motion.TryGetValue(place.Socket.Id, out var motion);
            var end = PlugEnd(place, motion, now, out var seated, out var drawn);
            var cable = Cable(place.SocketCenter, end, seated || motion?.Kind == MotionKind.PlugIn);
            DrawCable(dc, cable, drawn);
            DrawPlug(dc, cable.A, Math.PI / 2);
            if (drawn >= 1)
            {
                var tip = cable.B;
                var before = Bezier(cable, 0.97);
                DrawPlug(dc, tip, Math.Atan2(tip.Y - before.Y, tip.X - before.X));
            }
            if (seated && motion?.Kind != MotionKind.Seat) cables[place.Socket.Id] = cable;
        }

        // Lights: one per finished call, travelling from the outside line into the socket.
        var pulseBrush = Res("Sn.Pulse", Colors.LightYellow);
        var failBrush = Res("Sn.Lamp.Offline", Colors.IndianRed);
        foreach (var pulse in _pulses)
        {
            if (!cables.TryGetValue(pulse.SocketId, out var cable) || now < pulse.Start) continue;
            var t = Smooth((now - pulse.Start).TotalMilliseconds / PulseDuration.TotalMilliseconds);
            var p = Bezier(cable, 1 - t);
            // SN8: a failed call travels in the error colour and ends in a short spark at the socket.
            var brush = pulse.Ok ? pulseBrush : failBrush;
            dc.PushOpacity(0.35);
            dc.DrawEllipse(brush, null, p, 9, 9);
            dc.Pop();
            dc.DrawEllipse(brush, null, p, 4.5, 4.5);
            if (!pulse.Ok && t > 0.82)
            {
                var spark = (t - 0.82) / 0.18;
                var pen = CachedPen("spark", () => new Pen(failBrush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
                dc.PushOpacity(1 - spark);
                for (var k = 0; k < 6; k++)
                {
                    var a = k * Math.PI / 3 + 0.4;
                    var r1 = 6 + 10 * spark;
                    dc.DrawLine(pen, new Point(p.X + Math.Cos(a) * r1, p.Y + Math.Sin(a) * r1),
                        new Point(p.X + Math.Cos(a) * (r1 + 6), p.Y + Math.Sin(a) * (r1 + 6)));
                }
                dc.Pop();
            }
        }

        // Lamps, words, counters and timers.
        foreach (var place in places)
        {
            var socket = place.Socket;
            var (lampKey, wordKey, word) = Lamp(socket.State);
            var breathing = MotionAllowed && socket.State is ComponentVisualState.Attention or ComponentVisualState.Repairing
                ? 0.78 + 0.22 * Math.Sin(now.TotalSeconds / 1.6 * Math.PI * 2) : 1.0;
            DrawLamp(dc, place.SocketCenter, Res(lampKey, Colors.Green), lampKey != "Sn.Lamp.Off", breathing);
            Text(dc, word, place.SocketCenter.X + 82, place.SocketCenter.Y - 10, 15, Res(wordKey, Colors.Green), dpi, semibold: true, condensed: true);
            if (place.OutsideCenter is not { } outside) continue;
            var outsideLit = socket.State is ComponentVisualState.Ready or ComponentVisualState.Attention or ComponentVisualState.Repairing;
            DrawLamp(dc, outside, Res(outsideLit ? lampKey : "Sn.Lamp.Off", Colors.Gray), outsideLit, breathing);
            if (socket.State is ComponentVisualState.Offline)
            {
                var elapsed = DateTimeOffset.UtcNow - socket.StateSinceUtc;
                if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
                // The cable is gone, so the space under the jack carries the measured reconnect timer. SN9: it sits
                // below the neighbours' third text line ("son …" under a failure count), so the two never meet.
                Text(dc, "yeniden bağlanıyor", outside.X, outside.Y + 70, 13, muted, dpi, mono: true, center: true);
                Text(dc, elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss", Turkish) : elapsed.ToString(@"mm\:ss", Turkish),
                    outside.X, outside.Y + 88, 19, ink, dpi, mono: true, semibold: true, center: true);
                continue;
            }
            Text(dc, outsideLit ? word : "kapalı", outside.X + 82, outside.Y - 14, 15,
                Res(outsideLit ? wordKey : "Sn.Word.Off", Colors.Gray), dpi, semibold: true, condensed: true);
            // Gitea's calls never pass through Talvora: say so instead of drawing a number.
            var calls = socket.Line is null ? "ölçülmüyor"
                : socket.Calls is { } count ? count.ToString("N0", Turkish) + " çağrı" : "veri yok";
            Text(dc, calls, outside.X + 82, outside.Y + 6, 13, socket.Calls is null ? faint : muted, dpi, mono: true);
            // SN8: measured failures on their own line (outside lines sit too close for one long line).
            var nextLine = outside.Y + 24;
            if (socket.Failed is > 0 and var failed)
            {
                Text(dc, failed.ToString("N0", Turkish) + " hata", outside.X + 82, nextLine - 1, 13,
                    Res("Sn.Word.Offline", Colors.IndianRed), dpi, mono: true, semibold: true);
                nextLine += 18;
            }
            if (socket.LastCallAtUtc is { } last)
                Text(dc, "son " + last.ToLocalTime().ToString("HH:mm:ss", Turkish), outside.X + 82, nextLine, 12, faint, dpi, mono: true);
        }
    }

    /// <summary>SN9: draws the parts that only change with the size, the theme or the socket names, once.</summary>
    private void DrawStaticLayer(DrawingContext dc, IReadOnlyList<Place> places, double socketY, double dpi,
        Brush ink, Brush muted)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"{ActualWidth:0.##}x{ActualHeight:0.##}") +
            string.Concat(places.Select(place => "|" + string.Join("\u001f", place.Socket.Id, place.Socket.Name,
                place.Socket.Note, place.Socket.OutsideTitle, place.Socket.OutsideSub,
                place.Socket.State == ComponentVisualState.Neutral ? "optional" : "-")));
        if (_staticLayer is null || !string.Equals(_staticKey, key, StringComparison.Ordinal))
        {
            var layer = new DrawingGroup();
            using (var context = layer.Open())
            {
                // Brushed-metal grain: one hairline every 3 px, drawn as a tiled brush (cheap, theme-aware).
                context.DrawRectangle(Grain(ink), null, new Rect(0, 0, ActualWidth, ActualHeight));
                var rule = Res("Sn.Rule", Colors.Gray);
                var divider = CachedPen("divider", () => new Pen(rule, 1) { DashStyle = new DashStyle([2, 5], 0) });
                context.DrawLine(divider, new Point(24, (104 + socketY) / 2), new Point(ActualWidth - 24, (104 + socketY) / 2));
                Text(context, "DIŞ HATLAR", 24, 92, 14, muted, dpi, semibold: true, condensed: true);
                Text(context, "BU BİLGİSAYAR", 24, socketY - 12, 14, muted, dpi, semibold: true, condensed: true);
                foreach (var place in places)
                {
                    if (place.OutsideCenter is { } outside)
                        DrawJack(context, outside, place.Socket.OutsideTitle!, place.Socket.OutsideSub, top: true, ink, muted, dpi, optional: false);
                    DrawJack(context, place.SocketCenter, place.Socket.Name, place.Socket.Note, top: false, ink, muted, dpi,
                        optional: place.Socket.State == ComponentVisualState.Neutral);
                }
            }
            if (layer.CanFreeze) layer.Freeze();
            _staticLayer = layer;
            _staticKey = key;
        }
        dc.DrawDrawing(_staticLayer);
    }

    private static (string Lamp, string Word, string Text) Lamp(ComponentVisualState state) => state switch
    {
        ComponentVisualState.Ready => ("Sn.Lamp.Ready", "Sn.Word.Ready", "hazır"),
        ComponentVisualState.Attention => ("Sn.Lamp.Attention", "Sn.Word.Attention", "dikkat"),
        ComponentVisualState.Repairing => ("Sn.Lamp.Attention", "Sn.Word.Attention", "onarılıyor"),
        ComponentVisualState.Offline => ("Sn.Lamp.Offline", "Sn.Word.Offline", "ulaşılamıyor"),
        _ => ("Sn.Lamp.Off", "Sn.Word.Off", "kapalı"),
    };

    private Brush? _grain;
    private object? _grainToken;

    private Brush Grain(Brush ink)
    {
        if (_grain is not null && ReferenceEquals(_grainToken, ThemeToken)) return _grain;
        var color = ink is SolidColorBrush solid ? solid.Color : Colors.Black;
        var line = new GeometryDrawing(new SolidColorBrush(Color.FromArgb(9, color.R, color.G, color.B)), null,
            new RectangleGeometry(new Rect(0, 0, 4, 1)));
        var gap = new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 4, 3)));
        var group = new DrawingGroup();
        group.Children.Add(gap);
        group.Children.Add(line);
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 4, 3),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        _grain = brush;
        _grainToken = ThemeToken;
        return brush;
    }

    private void DrawJack(DrawingContext dc, Point c, string title, string? sub, bool top, Brush ink, Brush muted,
        double dpi, bool optional)
    {
        dc.DrawEllipse(Res("Sn.Shadow", Color.FromArgb(0x2E, 0x28, 0x19, 0x0A)), null, new Point(c.X + 3, c.Y + 4), 30, 30);
        dc.DrawEllipse(JackRing(), null, c, 28, 28);
        dc.DrawEllipse(Res("Sn.Socket", Colors.Black), null, c, 11, 11);
        if (optional)
            dc.DrawEllipse(null, CachedPen("optional", () => new Pen(muted, 1.4) { DashStyle = new DashStyle([2.5, 3], 0) }), c, 38, 38);
        var titleY = top ? c.Y - 78 : c.Y + 40;
        Text(dc, title, c.X, titleY, 22, ink, dpi, semibold: true, condensed: true, center: true);
        if (!string.IsNullOrWhiteSpace(sub))
            Text(dc, sub, c.X, titleY + 26, 15, muted, dpi, condensed: true, center: true);
        // Lamp socket (the lamp itself is drawn later, above cables).
        dc.DrawEllipse(Res("Sn.Cable", Colors.Black), null, new Point(c.X + 58, c.Y), 12, 12);
    }

    private void DrawLamp(DrawingContext dc, Point jack, Brush colour, bool lit, double intensity)
    {
        var p = new Point(jack.X + 58, jack.Y);
        if (!lit)
        {
            dc.DrawEllipse(Res("Sn.Lamp.Off", Colors.Gray), null, p, 9, 9);
            return;
        }
        var c = ((SolidColorBrush)colour).Color;
        if (!_halos.TryGetValue(c, out var halo))
        {
            halo = Frozen(new RadialGradientBrush(Color.FromArgb(150, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B)));
            _halos[c] = halo;
        }
        // SN9: one cached halo per colour; the breath only changes the opacity of halo, lamp and highlight.
        dc.PushOpacity(intensity);
        dc.DrawEllipse(halo, null, p, 30, 30);
        dc.DrawEllipse(colour, null, p, 9, 9);
        dc.DrawEllipse(LampHighlight, null, new Point(p.X - 3, p.Y - 3), 2.6, 2.6);
        dc.Pop();
    }

    private void DrawCable(DrawingContext dc, (Point A, Point C1, Point C2, Point B) c, double drawn)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = c.A, IsClosed = false };
        if (drawn >= 1)
            figure.Segments.Add(new BezierSegment(c.C1, c.C2, c.B, true));
        else
        {
            var steps = Math.Max(2, (int)(40 * drawn));
            for (var i = 1; i <= steps; i++) figure.Segments.Add(new LineSegment(Bezier(c, drawn * i / steps), true));
        }
        geometry.Figures.Add(figure);
        var shadow = CachedPen("cable-shadow", () => new Pen(Res("Sn.Shadow", Color.FromArgb(0x2E, 0x28, 0x19, 0x0A)), 13)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var body = CachedPen("cable-body", () => new Pen(Res("Sn.Cable", Colors.Black), 9)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var shine = CachedPen("cable-shine", () => new Pen(Res("Sn.CableHighlight", Colors.White), 2)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        dc.PushTransform(CableShadowOffset);
        dc.DrawGeometry(null, shadow, geometry);
        dc.Pop();
        dc.DrawGeometry(null, body, geometry);
        dc.PushTransform(CableShineOffset);
        dc.DrawGeometry(null, shine, geometry);
        dc.Pop();
    }

    private void DrawPlug(DrawingContext dc, Point tip, double angle)
    {
        dc.PushTransform(new TranslateTransform(tip.X, tip.Y));
        dc.PushTransform(new RotateTransform((angle + Math.PI / 2) * 180 / Math.PI));
        dc.DrawRectangle(Res("Sn.Cable", Colors.Black), null, new Rect(-8, 0, 16, 22));
        var metal = PlugMetal();
        dc.DrawRectangle(metal, null, new Rect(-7, -16, 14, 17));
        dc.DrawRectangle(metal, null, new Rect(-3.5, -27, 7, 12));
        dc.Pop();
        dc.Pop();
    }

    private double Text(DrawingContext dc, string text, double x, double y, double size, Brush brush, double dpi,
        bool semibold = false, bool condensed = false, bool mono = false, bool center = false)
    {
        var face = (mono ? 4 : 0) | (semibold ? 2 : 0) | (condensed && !mono ? 1 : 0);
        var key = (text, size, face, brush);
        if (!_texts.TryGetValue(key, out var formatted))
        {
            // Counters and clocks change every second, so the cache stays bounded.
            if (_texts.Count >= MaxCachedTexts) _texts.Clear();
            formatted = new FormattedText(text, Turkish, System.Windows.FlowDirection.LeftToRight, Faces[face], size, brush, dpi);
            _texts[key] = formatted;
        }
        dc.DrawText(formatted, new Point(center ? x - formatted.Width / 2 : x, y));
        return formatted.WidthIncludingTrailingWhitespace;
    }

    // ---------- interaction and accessibility ----------

    /// <summary>
    /// SN9: the 1 s traffic poll calls <see cref="Apply"/>; while the set of sockets stays the same, the hotspots
    /// are updated in place so keyboard focus and the hover ring survive it. They are rebuilt only when it changes.
    /// </summary>
    private void UpdateHotspots()
    {
        var same = Children.Count == _sockets.Count &&
            Children.OfType<SwitchboardHotspot>().Select(spot => spot.Socket?.Id)
                .SequenceEqual(_sockets.Select(socket => (string?)socket.Id), StringComparer.Ordinal);
        if (!same)
        {
            Children.Clear();
            for (var i = 0; i < _sockets.Count; i++)
            {
                var spot = new SwitchboardHotspot();
                spot.Click += (sender, _) =>
                {
                    if (sender is SwitchboardHotspot { Socket: { } socket }) SocketActivated?.Invoke(socket.Source);
                };
                Children.Add(spot);
            }
        }
        for (var i = 0; i < _sockets.Count; i++)
        {
            var socket = _sockets[i];
            var spot = (SwitchboardHotspot)Children[i];
            spot.Socket = socket;
            var word = Lamp(socket.State).Text;
            var calls = socket.Calls is { } count ? $", {count.ToString("N0", Turkish)} çağrı" : string.Empty;
            var failed = socket.Failed is > 0 and var failures ? $", {failures.ToString("N0", Turkish)} hata" : string.Empty;
            AutomationProperties.SetName(spot, $"{socket.Name}, {word}{calls}{failed}");
        }
        LayoutHotspots();
    }

    private void LayoutHotspots()
    {
        if (Children.Count == 0) return;
        var places = Arrange();
        for (var i = 0; i < Children.Count && i < places.Count; i++)
        {
            var spot = (FrameworkElement)Children[i];
            spot.Width = 150;
            spot.Height = 110;
            SetLeft(spot, places[i].SocketCenter.X - 54);
            SetTop(spot, places[i].SocketCenter.Y - 40);
        }
    }

    private enum MotionKind { Still, PlugIn, Drop, Hanging, Seat }

    private sealed class Motion
    {
        public bool Plugged;
        public TimeSpan ChangedAt;
        public MotionKind Kind;
    }

    private readonly record struct Pulse(string SocketId, TimeSpan Start, bool Ok);
}

/// <summary>Invisible, keyboard-reachable target over one socket; draws only the focus ring.</summary>
internal sealed class SwitchboardHotspot : ButtonBase
{
    // SN9: settable, so a traffic poll can refresh the socket without replacing the hotspot.
    public SwitchboardSocket? Socket { get; set; }

    public SwitchboardHotspot()
    {
        Focusable = true;
        IsTabStop = true;
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        IsKeyboardFocusedChanged += (_, _) => InvalidateVisual();
        MouseEnter += (_, _) => InvalidateVisual();
        MouseLeave += (_, _) => InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        // SN8: hover answers with a brass ring around the socket (socket centre is at 54, 40 in this box).
        if (IsMouseOver && !IsKeyboardFocused)
            dc.DrawEllipse(null, new Pen(TryFindResource("Sn.Brass") as Brush ?? Brushes.Goldenrod, 2),
                new Point(54, 40), 37, 37);
        if (IsKeyboardFocused)
            dc.DrawRoundedRectangle(null, new Pen(TryFindResource("Sn.Focus") as Brush ?? Brushes.Black, 2),
                new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), 6, 6);
    }
}
