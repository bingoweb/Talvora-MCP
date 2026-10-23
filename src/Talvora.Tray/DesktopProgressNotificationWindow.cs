using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Talvora.Shared;
using Talvora.Tray.Worklog;
using WpfClipboard = System.Windows.Clipboard;

namespace Talvora.Tray;

/// <summary>
/// Desktop worklog card window (DESIGN-WORKLOG §3, §3.1, §5, §7 "Phosphor").
/// Hosts <see cref="WorklogCardView"/>; owns placement interaction, lifetimes and stale detection.
/// </summary>
internal sealed class DesktopProgressNotificationWindow : Window
{
    private static readonly TimeSpan SucceededLifetime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan FailedLifetime = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan CancelledLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StaleCheckInterval = TimeSpan.FromSeconds(5);
    private const double DefaultWidth = 460;
    private const double DefaultCodeMaxHeight = 420;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;

    private readonly WorklogCardView view;
    private readonly WorklogCardViewModel model = new();
    private readonly DispatcherTimer dismissTimer;
    private readonly DispatcherTimer staleTimer;
    private readonly TranslateTransform enterShift = new();
    private DesktopWorklogFrame? lastFrame;
    private DateTimeOffset? lastCodeChangedUtc;
    private DateTimeOffset lastFrameReceivedUtc;
    private TimeSpan dismissAfter = SucceededLifetime;
    private bool isPointerOver;
    private bool isPinned;
    private bool isClosing;
    private bool manualClose;
    private bool isStale;
    private HwndSource? windowSource;
    private double resizeStartWidth;
    private double resizeStartHeight;

    public DesktopProgressNotificationWindow()
    {
        Width = DefaultWidth;
        MinWidth = 380;
        MaxWidth = 720;
        MinHeight = 120;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = false;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x09, 0x0E, 0x0C));
        Opacity = 0;
        RenderTransform = enterShift;
        Title = "Talvora çalışma günlüğü";
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            UseAeroCaptionButtons = false,
        });
        AutomationProperties.SetName(this, "Talvora çalışma günlüğü");

        view = new WorklogCardView { DataContext = model, CodeMaxHeight = DefaultCodeMaxHeight };
        view.PinRequested += (_, _) =>
        {
            if (!isClosing)
            {
                PinStateChangeRequested?.Invoke(!isPinned);
            }
        };
        view.CloseRequested += (_, _) => BeginClose(userInitiated: true);
        view.CopyRequested += (_, _) => CopyCode();
        view.PreviewMouseLeftButtonDown += OnCardMouseLeftButtonDown;
        Content = view;

        dismissTimer = new DispatcherTimer { Interval = dismissAfter };
        dismissTimer.Tick += (_, _) =>
        {
            dismissTimer.Stop();
            BeginClose();
        };
        staleTimer = new DispatcherTimer { Interval = StaleCheckInterval };
        staleTimer.Tick += (_, _) => CheckForStaleWork();
        staleTimer.Start();

        MouseEnter += (_, _) =>
        {
            isPointerOver = true;
            dismissTimer.Stop();
        };
        MouseLeave += (_, _) =>
        {
            isPointerOver = false;
            RestartDismissTimer();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                BeginClose(userInitiated: true);
                e.Handled = true;
            }
        };
        SourceInitialized += (_, _) => AttachNativeWindowHook();
        Loaded += (_, _) => AnimateIn();
        Closed += (_, _) =>
        {
            dismissTimer.Stop();
            staleTimer.Stop();
            DetachNativeWindowHook();
        };
    }

    public event EventHandler? UserMoveCompleted;

    public event EventHandler? UserResizeCompleted;

    public event Action<bool>? PinStateChangeRequested;

    public string SessionId { get; private set; } = string.Empty;

    public DateTimeOffset LastUpdatedUtc { get; private set; }

    public DesktopProgressLane LastLane { get; private set; } = DesktopProgressLane.Worklog;

    public bool WasManuallyClosed => manualClose;

    public bool IsTerminal => lastFrame?.Current.State is
        WorklogStepState.Succeeded or WorklogStepState.Failed or WorklogStepState.Cancelled;

    /// <summary>Shows a real frame. The only entry point for content.</summary>
    public void Update(DesktopWorklogFrame frame, DateTimeOffset? codeChangedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        CancelPendingAutomaticClose();
        lastFrame = frame;
        lastFrameReceivedUtc = DateTimeOffset.UtcNow;
        LastUpdatedUtc = lastFrameReceivedUtc;
        SessionId = frame.SessionId;
        LastLane = frame.Lane;
        isStale = false;
        lastCodeChangedUtc = codeChangedUtc;
        WorklogCardMapper.Apply(model, frame, Environment.MachineName, stale: false, codeChangedUtc);
        AutomationProperties.SetName(this, $"Talvora çalışma günlüğü: {frame.Current.Title}");

        dismissAfter = frame.Current.State switch
        {
            WorklogStepState.Failed => FailedLifetime,
            WorklogStepState.Cancelled => CancelledLifetime,
            _ => SucceededLifetime,
        };
        RestartDismissTimer();
    }

    /// <summary>User-chosen width; the user's height becomes the code terminal's maximum (§3.1).</summary>
    public void ApplyPreferredSize(double widthDip, double heightDip)
    {
        if (!double.IsFinite(widthDip) || !double.IsFinite(heightDip) || widthDip <= 0 || heightDip <= 0)
        {
            return;
        }

        SizeToContent = SizeToContent.Height;
        Width = Math.Clamp(widthDip, MinWidth, MaxWidth);
        view.CodeMaxHeight = Math.Clamp(heightDip - 240, 160, 900);
    }

    public void SetPinned(bool pinned)
    {
        isPinned = pinned;
        view.SetPinned(pinned);
    }

    public void MarkInteracted() => LastUpdatedUtc = DateTimeOffset.UtcNow;

    private void CheckForStaleWork()
    {
        if (isClosing || isStale || lastFrame is null || IsTerminal)
        {
            return;
        }

        var budget = TimeSpan.FromSeconds(Math.Max(10, lastFrame.ExpectedNextUpdateSeconds) * 2);
        if (DateTimeOffset.UtcNow - lastFrameReceivedUtc < budget)
        {
            return;
        }

        isStale = true;
        WorklogCardMapper.Apply(model, lastFrame, Environment.MachineName, stale: true, lastCodeChangedUtc);
    }

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (isClosing || e.ChangedButton != MouseButton.Left || e.LeftButton != MouseButtonState.Pressed ||
            IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
            MarkInteracted();
            UserMoveCompleted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        catch (InvalidOperationException ex)
        {
            TrayLog.Write("Desktop worklog card drag could not start.", ex);
        }
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        for (var current = source; current is not null; current = GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.ScrollBar)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);

    private void CopyCode()
    {
        var text = string.Join(
            Environment.NewLine,
            model.CodeLines.Select(line => line.Kind switch
            {
                DiffLineKind.File => "+++ " + line.Text,
                DiffLineKind.Hunk or DiffLineKind.Note => line.Text,
                DiffLineKind.Added => "+" + line.Text,
                DiffLineKind.Removed => "-" + line.Text,
                _ => " " + line.Text,
            }));
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            WpfClipboard.SetText(text);
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            TrayLog.Write("Desktop worklog code could not be copied.", ex);
        }
    }

    private void AnimateIn()
    {
        Opacity = 1;
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop,
        });
        enterShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop,
        });
    }

    private void BeginClose(bool userInitiated = false)
    {
        if (isClosing)
        {
            return;
        }

        isClosing = true;
        manualClose = userInitiated;
        dismissTimer.Stop();
        if (!SystemParameters.ClientAreaAnimation)
        {
            Close();
            return;
        }

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) =>
        {
            if (isClosing)
            {
                Close();
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void CancelPendingAutomaticClose()
    {
        if (!isClosing || manualClose)
        {
            return;
        }

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        isClosing = false;
    }

    private void RestartDismissTimer()
    {
        dismissTimer.Stop();
        if (isPointerOver || isClosing || !IsTerminal)
        {
            return;
        }

        dismissTimer.Interval = dismissAfter;
        dismissTimer.Start();
    }

    private void AttachNativeWindowHook()
    {
        if (windowSource is not null)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            windowSource = HwndSource.FromHwnd(handle);
            windowSource?.AddHook(WindowMessageHook);
        }
    }

    private void DetachNativeWindowHook()
    {
        windowSource?.RemoveHook(WindowMessageHook);
        windowSource = null;
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmEnterSizeMove)
        {
            resizeStartWidth = ActualWidth;
            resizeStartHeight = ActualHeight;
        }
        else if (message == WmExitSizeMove &&
                 (Math.Abs(ActualWidth - resizeStartWidth) > 0.5 || Math.Abs(ActualHeight - resizeStartHeight) > 0.5))
        {
            UserResizeCompleted?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }
}
