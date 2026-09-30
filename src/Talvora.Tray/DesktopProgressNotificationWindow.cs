using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Talvora.Shared;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Brushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace Talvora.Tray;

internal sealed partial class DesktopProgressNotificationWindow : Window
{
    private static readonly TimeSpan StartedLifetime =
        TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RunningLifetime =
        TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CompletedLifetime =
        TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FailureLifetime =
        TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ActiveStaleThreshold =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StaleCheckInterval =
        TimeSpan.FromSeconds(10);

    private Border _root = null!;
    private Border _accent = null!;
    private Border _stateBadge = null!;
    private SymbolIcon _stateIcon = null!;
    private TextBlock _stateBadgeText = null!;
    private TextBlock _traceLabel = null!;
    private TextBlock _title = null!;
    private TextBlock _message = null!;
    private TextBlock _meta = null!;
    private Border _diffPanel = null!;
    private TextBlock _diffHeader = null!;
    private WpfTextBox _codePreview = null!;
    private WpfButton _pinButton = null!;
    private WpfButton _closeButton = null!;
    private WpfButton _copyDiffButton = null!;
    private DispatcherTimer _dismissTimer = null!;
    private DispatcherTimer _staleTimer = null!;
    private readonly TranslateTransform _translateTransform = new();
    private TimeSpan _dismissAfter = StartedLifetime;
    private bool _isPointerOver;
    private bool _isPinned;
    private bool _isClosing;
    private bool _manualClose;
    private bool _isStale;
    private DateTimeOffset _lastPayloadUpdatedUtc;
    private DesktopProgressMessage? _lastMessage;

    public DesktopProgressNotificationWindow()
    {
        ConfigureWindowShell();
        BuildVisualTree();
        ConfigureTimersAndInteraction();
        ApplyEnvironmentPalette();
        SystemParameters.StaticPropertyChanged +=
            OnSystemParametersChanged;
    }
    public event EventHandler? UserMoveCompleted;

    public event Action<bool>? PinStateChangeRequested;

    public string OperationId { get; private set; } = string.Empty;

    public DateTimeOffset LastUpdatedUtc { get; private set; }

    public DesktopProgressKind LastKind { get; private set; }

    public DesktopProgressLane LastLane { get; private set; } =
        DesktopProgressLane.Worklog;

    public bool WasManuallyClosed => _manualClose;

    public bool IsTerminal =>
        LastKind is
            DesktopProgressKind.Completed or
            DesktopProgressKind.Failed or
            DesktopProgressKind.Cancelled or
            DesktopProgressKind.Info or
            DesktopProgressKind.Warning;

    public void Update(DesktopProgressMessage message)
    {
        CancelPendingAutomaticClose();

        OperationId = message.OperationId;
        LastUpdatedUtc = DateTimeOffset.UtcNow;
        _lastPayloadUpdatedUtc = LastUpdatedUtc;
        LastKind = message.Kind;
        LastLane = message.Lane;
        _isStale = false;
        _lastMessage = message;
        _title.Text = message.Title;
        _message.Text = message.Message;
        UpdateEvidence(message);
        ApplyStatusVisual(message);

        _dismissAfter =
            message.Kind switch
            {
                DesktopProgressKind.Started => StartedLifetime,
                DesktopProgressKind.Running => RunningLifetime,
                DesktopProgressKind.Completed => CompletedLifetime,
                DesktopProgressKind.Failed => FailureLifetime,
                DesktopProgressKind.Cancelled => FailureLifetime,
                DesktopProgressKind.Warning => FailureLifetime,
                DesktopProgressKind.Info => CompletedLifetime,
                _ => StartedLifetime,
            };
        RestartDismissTimer();
    }

    private void CheckForStaleWork()
    {
        if (_isClosing ||
            IsTerminal ||
            _lastPayloadUpdatedUtc == default)
        {
            return;
        }

        var staleFor =
            DateTimeOffset.UtcNow - _lastPayloadUpdatedUtc;
        if (staleFor < ActiveStaleThreshold ||
            _isStale)
        {
            return;
        }

        _isStale = true;
        _title.Text = "Yeni durum bekliyorum";
        _message.Text =
            $"Bu işten {FormatElapsed(staleFor.TotalSeconds)} süredir yeni bilgi gelmedi.\n\n" +
            $"Son gerçek güncelleme: {_lastPayloadUpdatedUtc.ToLocalTime():HH:mm:ss}. " +
            "Yeni veri gelir gelmez bu kart otomatik olarak devam edecek.";
        ApplyStaleVisual();
    }

    public void SetPinned(bool pinned)
    {
        _isPinned = pinned;
        _pinButton.Content = new SymbolIcon
        {
            Symbol = pinned
                ? SymbolRegular.PinOff20
                : SymbolRegular.Pin20,
            FontSize = 16,
        };
        _pinButton.ToolTip = pinned
            ? "Otomatik ekran konumuna dön"
            : "Kartın mevcut konumunu sabitle";
        _pinButton.Foreground = pinned
            ? DesktopProgressVisualTheme.TraceBrush
            : DesktopProgressVisualTheme.SecondaryTextBrush;
        _pinButton.Background = pinned
            ? DesktopProgressVisualTheme.TraceSoftBrush
            : Brushes.Transparent;
        _pinButton.BorderBrush = pinned
            ? DesktopProgressVisualTheme.TraceBrush
            : DesktopProgressVisualTheme.BorderBrush;
        AutomationProperties.SetName(
            _pinButton,
            pinned
                ? "Otomatik konuma dön"
                : "Kart konumunu sabitle");
    }

    public void MarkInteracted()
    {
        LastUpdatedUtc = DateTimeOffset.UtcNow;
    }

    private void OnCardMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_isClosing ||
            e.ChangedButton != MouseButton.Left ||
            e.LeftButton != MouseButtonState.Pressed ||
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
            TrayLog.Write(
                "Desktop progress card drag could not start.",
                ex);
        }
    }

    private static bool IsInteractiveElement(
        DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is WpfButtonBase or WpfScrollBar or WpfTextBoxBase)
            {
                return true;
            }

            current =
                VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void AnimateIn()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            _translateTransform.Y = 0;
            return;
        }

        Opacity = 1;
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(
                fromValue: 0,
                toValue: 1,
                duration: TimeSpan.FromMilliseconds(180))
            {
                EasingFunction =
                    new QuadraticEase
                    {
                        EasingMode = EasingMode.EaseOut,
                    },
                FillBehavior = FillBehavior.Stop,
            });

        _translateTransform.Y = 0;
        _translateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(
                fromValue: 14,
                toValue: 0,
                duration: TimeSpan.FromMilliseconds(220))
            {
                EasingFunction =
                    new QuadraticEase
                    {
                        EasingMode = EasingMode.EaseOut,
                    },
                FillBehavior = FillBehavior.Stop,
            });
    }

    private void BeginClose(bool userInitiated = false)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _manualClose = userInitiated;
        _dismissTimer.Stop();
        if (!SystemParameters.ClientAreaAnimation)
        {
            Close();
            return;
        }

        var animation = new DoubleAnimation(
            fromValue: Opacity,
            toValue: 0,
            duration: TimeSpan.FromMilliseconds(160))
        {
            EasingFunction =
                new QuadraticEase
                {
                    EasingMode = EasingMode.EaseIn,
                },
        };
        animation.Completed += (_, _) =>
        {
            if (_isClosing)
            {
                Close();
            }
        };
        BeginAnimation(OpacityProperty, animation);
    }

    private void CancelPendingAutomaticClose()
    {
        if (!_isClosing || _manualClose)
        {
            return;
        }

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        _isClosing = false;
    }

    private void RestartDismissTimer()
    {
        _dismissTimer.Stop();
        if (_isPointerOver ||
            _isClosing ||
            !IsTerminal)
        {
            return;
        }

        _dismissTimer.Interval = _dismissAfter;
        _dismissTimer.Start();
    }

    private static string FormatElapsed(double elapsedSeconds)
    {
        var elapsed =
            TimeSpan.FromSeconds(Math.Max(0, elapsedSeconds));
        if (elapsed.TotalMinutes >= 1)
        {
            return $"{(int)elapsed.TotalMinutes} dk {elapsed.Seconds} sn";
        }

        return $"{Math.Max(1, (int)elapsed.TotalSeconds)} sn";
    }

    private void UpdateEvidence(DesktopProgressMessage message)
    {
        var evidence = message.Evidence;
        var codePreview = evidence?.CodePreview ?? string.Empty;
        if (string.IsNullOrWhiteSpace(codePreview))
        {
            _diffPanel.Visibility = Visibility.Collapsed;
            SetTextIfChanged(_codePreview, string.Empty);
            return;
        }

        _diffPanel.Visibility = Visibility.Visible;
        SetTextIfChanged(
            _codePreview,
            codePreview);
    }

    private static void SetTextIfChanged(
        WpfTextBox target,
        string value)
    {
        if (!string.Equals(
                target.Text,
                value,
                StringComparison.Ordinal))
        {
            target.Text = value;
        }
    }
}
