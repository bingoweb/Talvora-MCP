using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Talvora.Tray.Worklog;

/// <summary>Desktop worklog card view ("Phosphor"). Layout is defined in XAML; this file only drives motion and events.</summary>
public partial class WorklogCardView : System.Windows.Controls.UserControl
{
    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly DispatcherTimer spinnerTimer;
    private readonly Storyboard cursorBlink;
    private readonly Storyboard liveLineSweep;
    private int spinnerIndex;
    private bool highContrastActive;
    private WorklogCardViewModel? subscribed;

    public WorklogCardView()
    {
        InitializeComponent();

        spinnerTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(90) };
        spinnerTimer.Tick += (_, _) =>
        {
            spinnerIndex = (spinnerIndex + 1) % SpinnerFrames.Length;
            Spinner.Text = SpinnerFrames[spinnerIndex];
        };

        cursorBlink = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1060) };
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(530))));
        Storyboard.SetTarget(blink, TypingCursor);
        Storyboard.SetTargetProperty(blink, new PropertyPath("(TextElement.Foreground).(Brush.Opacity)"));
        cursorBlink.Children.Add(blink);

        liveLineSweep = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var sweep = new DoubleAnimation(-1, 1, TimeSpan.FromMilliseconds(1600));
        Storyboard.SetTarget(sweep, LiveLine);
        Storyboard.SetTargetProperty(sweep, new PropertyPath("(Shape.Fill).(Brush.RelativeTransform).(TranslateTransform.X)"));
        liveLineSweep.Children.Add(sweep);

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

    /// <summary>
    /// High contrast: system colours, no glow, no scanlines, no tinted rows. Called automatically;
    /// the preview harness may force it.
    /// </summary>
    internal void ApplyContrast(bool highContrast)
    {
        highContrastActive = highContrast;
        string[] surfaces = ["Wl.Bg", "Wl.Surface", "Wl.Raised", "Wl.Terminal"];
        string[] texts = ["Wl.Text", "Wl.Muted", "Wl.Green", "Wl.Cyan", "Wl.Amber", "Wl.Red", "Wl.Border", "Wl.BorderStrong"];
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
        StateLine.Opacity = highContrast ? 0 : 0.55;
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
        }

        subscribed = model;
        if (subscribed is not null)
        {
            subscribed.PropertyChanged += OnModelChanged;
        }

        UpdateMotion();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorklogCardViewModel.State) or nameof(WorklogCardViewModel.AnimationsEnabled))
        {
            UpdateMotion();
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

    private void UpdateMotion()
    {
        var running = subscribed?.IsRunning == true;
        var motionAllowed = subscribed?.AnimationsEnabled != false &&
            SystemParameters.ClientAreaAnimation &&
            !highContrastActive &&
            IsVisible;

        TypingCursor.Text = running && !highContrastActive ? "▍" : string.Empty;
        PromptMark.SetResourceReference(
            System.Windows.Documents.TextElement.ForegroundProperty,
            subscribed?.State switch
            {
                WorklogVisualState.Succeeded => "Wl.Green",
                WorklogVisualState.Failed => "Wl.Red",
                WorklogVisualState.Cancelled or WorklogVisualState.Waiting => "Wl.Amber",
                _ => "Wl.Cyan",
            });

        if (running && motionAllowed)
        {
            spinnerTimer.Start();
            cursorBlink.Begin(this, true);
            liveLineSweep.Begin(this, true);
        }
        else
        {
            StopMotion();
            Spinner.Text = SpinnerFrames[0];
        }
    }

    private void StopMotion()
    {
        spinnerTimer.Stop();
        cursorBlink.Stop(this);
        liveLineSweep.Stop(this);
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
