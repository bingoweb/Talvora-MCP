using System.Windows;
using System.Windows.Media;
using Talvora.Tray.ViewModels;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Talvora.Tray.Controls;

/// <summary>Approved segmented Nabız glyph, DESIGN-CONTROL-CENTER §7.3.</summary>
internal sealed class PulseRing : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<ComponentVisualState>), typeof(PulseRing),
        new FrameworkPropertyMetadata(Array.Empty<ComponentVisualState>(),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ComponentVisualState> Segments
    {
        get => (IReadOnlyList<ComponentVisualState>)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public int ReadyCount => Segments.Count(s => s == ComponentVisualState.Ready);
    public int TotalCount => Segments.Count;
    public bool IsAllReady => Segments.Count > 0 && Segments.All(s => s == ComponentVisualState.Ready);

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0 || Segments.Count == 0) return;
        const double thickness = 8, gapDegrees = 6;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = size / 2 - thickness;
        var allReady = Segments.All(s => s == ComponentVisualState.Ready);
        var sweep = 360.0 / Segments.Count;

        for (var i = 0; i < Segments.Count; i++)
        {
            var start = -90 + i * sweep + gapDegrees / 2;
            var end = start + sweep - gapDegrees;
            var brush = allReady ? (Brush)FindResource("Tv.Aurora") : BrushFor(Segments[i]);
            var pen = new System.Windows.Media.Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, Arc(center, radius, start, end));
        }
    }

    private Brush BrushFor(ComponentVisualState state) => (Brush)FindResource(state switch
    {
        ComponentVisualState.Ready => "Tv.Ready",
        ComponentVisualState.Attention => "Tv.Attention",
        ComponentVisualState.Offline => "Tv.Offline",
        ComponentVisualState.Repairing => "Tv.Repairing",
        _ => "Tv.Neutral",
    });

    private static Geometry Arc(Point c, double r, double startDeg, double endDeg)
    {
        static Point P(Point c, double r, double deg) =>
            new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
        var figure = new PathFigure { StartPoint = P(c, r, startDeg), IsClosed = false };
        figure.Segments.Add(new ArcSegment(P(c, r, endDeg), new Size(r, r), 0,
            isLargeArc: endDeg - startDeg > 180, SweepDirection.Clockwise, isStroked: true));
        var g = new PathGeometry([figure]);
        g.Freeze();
        return g;
    }
}
