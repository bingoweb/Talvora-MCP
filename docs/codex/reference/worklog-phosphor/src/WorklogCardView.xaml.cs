using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Talvora.Tray.Worklog;

/// <summary>
/// Desktop worklog card view ("tmux", DESIGN-WORKLOG §7b). Layout is XAML; this file drives motion, the live session
/// clock and events. The clock only advances from a measured value while the session is running (never estimated).
/// </summary>
public partial class WorklogCardView : System.Windows.Controls.UserControl
{
    private static readonly Duration RowIn = TimeSpan.FromMilliseconds(220);

    private readonly DispatcherTimer clockTimer;
    private readonly DispatcherTimer typeTimer;
    private readonly Storyboard cursorBlink;
    private readonly Storyboard waitPulse;
    private bool highContrastActive;
    private WorklogCardViewModel? subscribed;
    private WorklogVisualState? lastState;
    private string typedMain = string.Empty;
    private string typedDetail = string.Empty;
    private int typedCount;
    private int typeStep;
    private string? firstRowKey;

    public WorklogCardView()
    {
        InitializeComponent();

        clockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        clockTimer.Tick += (_, _) => UpdateClock();

        // §7b "type": 18 ms per tick, at most 20 ticks (≤ 360 ms) whatever the title length.
        typeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(18) };
        typeTimer.Tick += (_, _) => TypeTick();

        cursorBlink = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1060) };
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(530))));
        Storyboard.SetTarget(blink, TypingCursor);
        Storyboard.SetTargetProperty(blink, new PropertyPath("(TextElement.Foreground).(Brush.Opacity)"));
        cursorBlink.Children.Add(blink);

        // §7b "wait": three dots breathe every 1.2 s while waiting.
        waitPulse = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var breathe = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(600))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(breathe, WaitDots);
        Storyboard.SetTargetProperty(breathe, new PropertyPath(OpacityProperty));
        waitPulse.Children.Add(breathe);

        ApplyContrast(SystemParameters.HighContrast);
        DataContextChanged += (_, _) => Subscribe(DataContext as WorklogCardViewModel);
        IsVisibleChanged += (_, _) => UpdateMotion();
        Unloaded += (_, _) => StopMotion();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Unloaded += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }

    public event EventHandler? PinRequested;

    public event EventHandler? CloseRequested;

    public event EventHandler? DetailsRequested;

    public event EventHandler? CopyRequested;

    /// <summary>The user asked to copy the "ayrıntılar" rows (N6).</summary>
    public event EventHandler? CopyDetailsRequested;

    /// <summary>Maximum height of the code terminal (set by the window from the work area / user size).</summary>
    public double CodeMaxHeight
    {
        get => CodeScroll.MaxHeight;
        set => CodeScroll.MaxHeight = value;
    }

    private bool MotionAllowed =>
        subscribed?.AnimationsEnabled != false &&
        SystemParameters.ClientAreaAnimation &&
        !highContrastActive &&
        IsVisible;

    /// <summary>
    /// High contrast: system colours, no glow, no scanlines, no tinted rows, no motion. Called automatically;
    /// the preview harness may force it.
    /// </summary>
    internal void ApplyContrast(bool highContrast)
    {
        highContrastActive = highContrast;
        string[] surfaces = ["Wl.Bg", "Wl.Surface", "Wl.Raised", "Wl.Terminal",
            "Wl.Status.Running", "Wl.Status.Succeeded", "Wl.Status.Failed", "Wl.Status.Waiting"];
        string[] texts = ["Wl.Text", "Wl.Muted", "Wl.Green", "Wl.Cyan", "Wl.Amber", "Wl.Red", "Wl.Border", "Wl.BorderStrong",
            "Wl.StatusText", "Wl.StatusMuted"];
        string[] tints = ["Wl.AddedBg", "Wl.RemovedBg", "Wl.HunkBg", "Wl.AddedEmphasisBg", "Wl.RemovedEmphasisBg"];
        foreach (var key in surfaces.Concat(texts).Concat(tints).Append("Wl.Faint"))
        {
            Resources.Remove(key);
        }

        if (highContrast)
        {
            foreach (var key in surfaces)
            {
                Resources[key] = System.Windows.SystemColors.WindowBrush;
            }

            foreach (var key in texts)
            {
                Resources[key] = System.Windows.SystemColors.WindowTextBrush;
            }

            foreach (var key in tints)
            {
                Resources[key] = System.Windows.Media.Brushes.Transparent;
            }

            Resources["Wl.Faint"] = System.Windows.SystemColors.GrayTextBrush;
        }

        Scanlines.Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Effect = highContrast
            ? null
            : new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Color.FromRgb(0x39, 0xFF, 0x88),
                BlurRadius = 9,
                ShadowDepth = 0,
                Opacity = 0.28,
            };
        UpdateMotion();
    }

    /// <summary>Set by the window: pinned shows the "unpin" glyph.</summary>
    public void SetPinned(bool pinned)
    {
        PinGlyph.Text = pinned ? "" : "";
        var label = pinned ? "Otomatik konuma dön" : "Konumu sabitle";
        PinButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(PinButton, label);
    }

    private void Subscribe(WorklogCardViewModel? model)
    {
        if (subscribed is not null)
        {
            subscribed.PropertyChanged -= OnModelChanged;
            subscribed.Timeline.CollectionChanged -= OnTimelineChanged;
        }

        subscribed = model;
        lastState = model?.State;
        firstRowKey = null;
        if (subscribed is not null)
        {
            subscribed.PropertyChanged += OnModelChanged;
            subscribed.Timeline.CollectionChanged += OnTimelineChanged;
            firstRowKey = RowKey(subscribed);
        }

        ShowTitle(animate: false);
        UpdateClock();
        UpdateMotion();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorklogCardViewModel.Title):
                ShowTitle(animate: true);
                break;
            case nameof(WorklogCardViewModel.State):
                OnStateChanged();
                UpdateClock();
                UpdateMotion();
                break;
            case nameof(WorklogCardViewModel.AnimationsEnabled):
                UpdateMotion();
                break;
            case nameof(WorklogCardViewModel.SessionSeconds) or nameof(WorklogCardViewModel.ClockAnchorUtc) or
                nameof(WorklogCardViewModel.Steps) or nameof(WorklogCardViewModel.FilesChanged):
                UpdateClock();
                break;
        }
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            ApplyContrast(SystemParameters.HighContrast);
        }
        else if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
        {
            UpdateMotion();
        }
    }

    /// <summary>"mm:ss · n adım · k dosya" from the measured session time (§7b).</summary>
    private void UpdateClock() =>
        StatusStats.Text = subscribed?.StatusStats(DateTimeOffset.UtcNow) ?? string.Empty;

    // §7b "type": a new title types in; the first render and reduced motion show it at once.
    private void ShowTitle(bool animate)
    {
        typeTimer.Stop();
        typedMain = subscribed?.TitleMain ?? string.Empty;
        typedDetail = subscribed?.TitleDetail ?? string.Empty;
        var total = typedMain.Length + typedDetail.Length;
        if (!animate || !MotionAllowed || total == 0)
        {
            TitleMainRun.Text = typedMain;
            TitleDetailRun.Text = typedDetail;
            return;
        }

        typedCount = 0;
        typeStep = Math.Max(1, (int)Math.Ceiling(total / 20.0));
        TitleMainRun.Text = string.Empty;
        TitleDetailRun.Text = string.Empty;
        typeTimer.Start();
    }

    private void TypeTick()
    {
        typedCount = Math.Min(typedCount + typeStep, typedMain.Length + typedDetail.Length);
        TitleMainRun.Text = typedMain[..Math.Min(typedCount, typedMain.Length)];
        TitleDetailRun.Text = typedCount > typedMain.Length ? typedDetail[..(typedCount - typedMain.Length)] : string.Empty;
        if (typedCount >= typedMain.Length + typedDetail.Length)
        {
            typeTimer.Stop();
        }
    }

    // §7b "row-in": the newest history row slides down 8 px and fades in.
    private void OnTimelineChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (subscribed is null)
        {
            return;
        }

        var key = RowKey(subscribed);
        if (key is null || key == firstRowKey)
        {
            firstRowKey = key;
            return;
        }

        var animate = firstRowKey is not null && MotionAllowed;
        firstRowKey = key;
        if (!animate)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (History.ItemContainerGenerator.ContainerFromIndex(0) is not FrameworkElement row)
            {
                return;
            }

            var shift = new TranslateTransform(0, -8);
            row.RenderTransform = shift;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, RowIn) { EasingFunction = ease });
            row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, RowIn) { EasingFunction = ease });
        });
    }

    private static string? RowKey(WorklogCardViewModel model) =>
        model.Timeline.Count == 0 ? null : model.Timeline[0].Time + "\u001f" + model.Timeline[0].Label;

    // §7b "ok" and "fail": one moment on the transition into a final state; nothing repeats.
    private void OnStateChanged()
    {
        var state = subscribed?.State;
        var previous = lastState;
        lastState = state;
        if (state == previous || !MotionAllowed)
        {
            return;
        }

        if (state == WorklogVisualState.Succeeded)
        {
            var width = Math.Max(StatusBar.ActualWidth, 200);
            SweepBand.Opacity = 1;
            var sweep = new DoubleAnimation(-SweepBand.Width, width, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            sweep.Completed += (_, _) => SweepBand.Opacity = 0;
            SweepShift.BeginAnimation(TranslateTransform.XProperty, sweep);
        }
        else if (state == WorklogVisualState.Failed)
        {
            var jitter = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(120) };
            jitter.KeyFrames.Add(new DiscreteDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            jitter.KeyFrames.Add(new DiscreteDoubleKeyFrame(-2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(30))));
            jitter.KeyFrames.Add(new DiscreteDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
            jitter.KeyFrames.Add(new DiscreteDoubleKeyFrame(-2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            jitter.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            CardShift.BeginAnimation(TranslateTransform.XProperty, jitter);
            StatusBar.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(260)));
        }
    }

    private void UpdateMotion()
    {
        var model = subscribed;
        var running = model?.IsRunning == true;
        var waiting = model?.State == WorklogVisualState.Waiting;

        TypingCursor.Text = running && !highContrastActive ? "▍" : string.Empty;
        PromptMark.SetResourceReference(
            System.Windows.Documents.TextElement.ForegroundProperty,
            model?.State switch
            {
                WorklogVisualState.Succeeded => "Wl.Green",
                WorklogVisualState.Failed => "Wl.Red",
                WorklogVisualState.Cancelled or WorklogVisualState.Waiting => "Wl.Amber",
                _ => "Wl.Cyan",
            });
        WaitDots.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;

        // The clock is information, not decoration: it ticks whenever a running session is visible.
        if (running && IsVisible)
        {
            clockTimer.Start();
        }
        else
        {
            clockTimer.Stop();
        }

        if (running && MotionAllowed)
        {
            cursorBlink.Begin(this, true);
        }
        else
        {
            cursorBlink.Stop(this);
        }

        if (waiting && MotionAllowed)
        {
            waitPulse.Begin(this, true);
        }
        else
        {
            waitPulse.Stop(this);
            WaitDots.Opacity = 1;
        }
    }

    private void StopMotion()
    {
        clockTimer.Stop();
        typeTimer.Stop();
        cursorBlink.Stop(this);
        waitPulse.Stop(this);
    }

    private void OnPinClick(object sender, RoutedEventArgs e) => PinRequested?.Invoke(this, EventArgs.Empty);

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnDetailsClick(object sender, RoutedEventArgs e) => DetailsRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopyDetailsClick(object sender, RoutedEventArgs e) => CopyDetailsRequested?.Invoke(this, EventArgs.Empty);
}

/// <summary>null or empty string → Collapsed; anything else → Visible.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || (value is string text && string.IsNullOrWhiteSpace(text))
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
