using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Talvora.Tray.Worklog;

/// <summary>One entry of the status bar's window list (DESIGN-WORKLOG §7b "Window list", D109v).</summary>
internal sealed record WorklogWindowEntry(
    int Index,
    string SessionId,
    string Text,
    bool IsShown,
    int Rank,
    string AutomationName,
    string? HelpText);

/// <summary>The entries the bar may draw (shown one plus up to three others), the real session count and the spoken list.</summary>
internal sealed record WorklogWindowList(
    IReadOnlyList<WorklogWindowEntry> Entries,
    int TotalSessions,
    string AutomationName)
{
    /// <summary>Everything the view draws; an identical list is not rebuilt.</summary>
    public string Signature =>
        string.Join(
            "\u001e",
            Entries.Select(static entry => string.Join(
                "\u001f",
                entry.Index,
                entry.SessionId,
                entry.Text,
                entry.IsShown,
                entry.Rank,
                entry.AutomationName,
                entry.HelpText ?? "\u0000")).Prepend(
                $"{TotalSessions}\u001f{AutomationName}"));
}

/// <summary>What a card window needs from the tray: the open sessions and the switch (D109).</summary>
internal interface IWorklogSessionSource
{
    IReadOnlyList<WorklogSessionTab> GetSessionTabs(DateTimeOffset nowUtc, string? shownSessionId);

    bool ShowSession(string sessionId);
}

/// <summary>
/// Lays the window list out left to right: as many entries as fit, the lowest ranks first, then <c>+k</c> for the
/// hidden ones. Children are never changed during measure or arrange; hidden children get an empty rectangle.
/// </summary>
internal sealed class WorklogWindowListPanel : Panel
{
    private const double MoreGap = 4;

    public static readonly DependencyProperty RankProperty = DependencyProperty.RegisterAttached(
        "Rank",
        typeof(int),
        typeof(WorklogWindowListPanel),
        new FrameworkPropertyMetadata(int.MaxValue, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty TotalCountProperty = DependencyProperty.Register(
        nameof(TotalCount),
        typeof(int),
        typeof(WorklogWindowListPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MoreOpacityProperty = DependencyProperty.Register(
        nameof(MoreOpacity),
        typeof(double),
        typeof(WorklogWindowListPanel),
        new FrameworkPropertyMetadata(0.7, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly HashSet<UIElement> visibleChildren = [];
    private int hiddenCount;
    private string moreText = string.Empty;
    private double moreX;

    public static int GetRank(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)element.GetValue(RankProperty);
    }

    public static void SetRank(DependencyObject element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(RankProperty, value);
    }

    /// <summary>All sessions the tray keeps open; the part that does not fit is counted as <c>+k</c>.</summary>
    public int TotalCount
    {
        get => (int)GetValue(TotalCountProperty);
        set => SetValue(TotalCountProperty, value);
    }

    /// <summary>Opacity of the <c>+k</c> text (1.0 in high contrast).</summary>
    public double MoreOpacity
    {
        get => (double)GetValue(MoreOpacityProperty);
        set => SetValue(MoreOpacityProperty, value);
    }

    internal int VisibleCount => visibleChildren.Count;

    internal int HiddenCount => hiddenCount;

    internal bool IsVisibleChild(UIElement child) => visibleChildren.Contains(child);

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToArray();
        var childConstraint = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in children)
        {
            child.Measure(childConstraint);
        }

        var ranked = children
            .Select(static (child, index) => (Child: child, Index: index))
            .OrderBy(static item => GetRank(item.Child))
            .ThenBy(static item => item.Index)
            .Select(static item => item.Child)
            .ToArray();

        var chosen = ranked.Length == 0 ? 0 : 1;
        var chosenWidth = ranked.Length == 0 ? 0 : ranked[0].DesiredSize.Width;
        var chosenMoreText = string.Empty;
        var chosenMoreWidth = 0d;
        for (var count = ranked.Length; count >= 1; count--)
        {
            var width = 0d;
            for (var index = 0; index < count; index++)
            {
                width += ranked[index].DesiredSize.Width;
            }

            var hidden = Math.Max(0, TotalCount - count);
            var text = hidden > 0 ? "+" + hidden.ToString(CultureInfo.InvariantCulture) : string.Empty;
            var more = hidden > 0 ? MoreGap + MeasureMore(text) : 0;
            if (count == 1 || width + more <= availableSize.Width)
            {
                chosen = count;
                chosenWidth = width;
                chosenMoreText = text;
                chosenMoreWidth = more;
                break;
            }
        }

        visibleChildren.Clear();
        for (var index = 0; index < chosen; index++)
        {
            visibleChildren.Add(ranked[index]);
        }

        hiddenCount = Math.Max(0, TotalCount - chosen);
        if (!string.Equals(moreText, chosenMoreText, StringComparison.Ordinal))
        {
            moreText = chosenMoreText;
            InvalidateVisual();
        }

        var height = children.Where(visibleChildren.Contains).Select(static child => child.DesiredSize.Height)
            .DefaultIfEmpty(0).Max();
        var desiredWidth = chosenWidth + chosenMoreWidth;
        return new Size(
            double.IsInfinity(availableSize.Width) ? desiredWidth : Math.Min(desiredWidth, availableSize.Width),
            double.IsInfinity(availableSize.Height) ? height : Math.Min(height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        foreach (UIElement child in InternalChildren)
        {
            if (!visibleChildren.Contains(child))
            {
                child.Arrange(new Rect());
                continue;
            }

            var size = child.DesiredSize;
            child.Arrange(new Rect(x, Math.Max(0, (finalSize.Height - size.Height) / 2), size.Width, size.Height));
            x += size.Width;
        }

        var newMoreX = x + MoreGap;
        if (Math.Abs(newMoreX - moreX) > 0.001)
        {
            moreX = newMoreX;
            InvalidateVisual();
        }

        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (hiddenCount <= 0 || moreText.Length == 0)
        {
            return;
        }

        var text = CreateMoreText(moreText);
        drawingContext.PushOpacity(MoreOpacity);
        drawingContext.DrawText(text, new Point(moreX, Math.Max(0, (RenderSize.Height - text.Height) / 2)));
        drawingContext.Pop();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new WorklogWindowListPeer(this);

    private double MeasureMore(string text) => CreateMoreText(text).WidthIncludingTrailingWhitespace;

    private FormattedText CreateMoreText(string text) =>
        new(
            text,
            CultureInfo.GetCultureInfo("tr-TR"),
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(
                TextElement.GetFontFamily(this),
                TextElement.GetFontStyle(this),
                TextElement.GetFontWeight(this),
                TextElement.GetFontStretch(this)),
            TextElement.GetFontSize(this),
            TextElement.GetForeground(this),
            new NumberSubstitution(),
            TextFormattingMode.Display,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private sealed class WorklogWindowListPeer(WorklogWindowListPanel owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => "WorklogWindowList";
    }
}
