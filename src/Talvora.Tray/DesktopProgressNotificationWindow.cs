using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Talvora.Shared;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace Talvora.Tray;

internal sealed class DesktopProgressNotificationWindow : Window
{
    private static readonly TimeSpan StartedLifetime =
        Timeout.InfiniteTimeSpan;
    private static readonly TimeSpan RunningLifetime =
        Timeout.InfiniteTimeSpan;
    private static readonly TimeSpan CompletedLifetime =
        TimeSpan.FromMinutes(3);
    private static readonly TimeSpan FailureLifetime =
        TimeSpan.FromMinutes(5);

    private readonly Border _accent;
    private readonly TextBlock _title;
    private readonly TextBlock _message;
    private readonly TextBlock _status;
    private readonly WpfButton _pinButton;
    private readonly DispatcherTimer _dismissTimer;
    private readonly TranslateTransform _translateTransform = new();
    private TimeSpan _dismissAfter = StartedLifetime;
    private bool _isPointerOver;
    private bool _isPinned;
    private bool _isClosing;
    private bool _manualClose;

    public DesktopProgressNotificationWindow()
    {
        Width = 490;
        MaxHeight = 390;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        Opacity = 0;
        RenderTransform = _translateTransform;

        var root = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background =
                new SolidColorBrush(WpfColor.FromArgb(248, 24, 27, 35)),
            BorderBrush =
                new SolidColorBrush(WpfColor.FromArgb(72, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Cursor = WpfCursors.SizeAll,
            ToolTip =
                "Boş bir alandan sürükleyerek taşı. Sabitle düğmesi konumu korur; kart yine otomatik kapanır.",
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 4,
                Opacity = 0.42,
                Color = Colors.Black,
            },
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        _accent = new Border
        {
            CornerRadius = new CornerRadius(3),
            Background =
                new SolidColorBrush(WpfColor.FromRgb(72, 151, 255)),
        };
        Grid.SetColumn(_accent, 0);
        grid.Children.Add(_accent);

        var content = new Grid();
        content.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(content, 2);

        _title = new TextBlock
        {
            Foreground = WpfBrushes.White,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 7),
            Cursor = WpfCursors.SizeAll,
        };
        content.Children.Add(_title);

        _message = new TextBlock
        {
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(222, 226, 234)),
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Cursor = WpfCursors.Arrow,
        };

        var scroll = new ScrollViewer
        {
            Content = _message,
            MaxHeight = 230,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Cursor = WpfCursors.Arrow,
        };
        Grid.SetRow(scroll, 1);
        content.Children.Add(scroll);

        _status = new TextBlock
        {
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(147, 156, 171)),
            FontSize = 11,
            Margin = new Thickness(0, 9, 0, 0),
            Cursor = WpfCursors.SizeAll,
        };
        Grid.SetRow(_status, 2);
        content.Children.Add(_status);

        grid.Children.Add(content);

        var actions = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, -5, -5, 0),
        };

        _pinButton = new WpfButton
        {
            Content = "Sabitle",
            MinWidth = 54,
            Height = 28,
            Padding = new Thickness(8, 0, 8, 0),
            Background = WpfBrushes.Transparent,
            BorderBrush =
                new SolidColorBrush(WpfColor.FromArgb(52, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(171, 178, 190)),
            FontSize = 11,
            Cursor = WpfCursors.Hand,
            Focusable = false,
            ToolTip = "Kartın mevcut konumunu sabitle",
        };
        _pinButton.Click += (_, _) =>
        {
            if (!_isClosing)
            {
                PinStateChangeRequested?.Invoke(!_isPinned);
            }
        };
        actions.Children.Add(_pinButton);

        var closeButton = new WpfButton
        {
            Content = "×",
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(5, 0, 0, 0),
            Background = WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(171, 178, 190)),
            FontSize = 18,
            Cursor = WpfCursors.Hand,
            Focusable = false,
            ToolTip = "Bildirimi kapat",
        };
        closeButton.Click += (_, _) =>
            BeginClose(userInitiated: true);
        actions.Children.Add(closeButton);

        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        root.Child = grid;
        Content = root;

        _dismissTimer = new DispatcherTimer
        {
            Interval = _dismissAfter,
        };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            BeginClose();
        };

        MouseEnter += (_, _) =>
        {
            _isPointerOver = true;
            _dismissTimer.Stop();
        };
        MouseLeave += (_, _) =>
        {
            _isPointerOver = false;
            RestartDismissTimer();
        };
        root.PreviewMouseLeftButtonDown += OnCardMouseLeftButtonDown;
        Loaded += (_, _) => AnimateIn();
    }

    public event EventHandler? UserMoveCompleted;

    public event Action<bool>? PinStateChangeRequested;

    public string OperationId { get; private set; } = string.Empty;

    public DateTimeOffset LastUpdatedUtc { get; private set; }

    public DesktopProgressKind LastKind { get; private set; }

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
        LastKind = message.Kind;
        _title.Text = message.Title;
        _message.Text = message.Message;
        _status.Text =
            message.Kind switch
            {
                DesktopProgressKind.Started => "Başlatıldı",
                DesktopProgressKind.Running =>
                    $"Çalışıyor · {FormatElapsed(message.ElapsedSeconds)}",
                DesktopProgressKind.Completed =>
                    $"Tamamlandı · {FormatElapsed(message.ElapsedSeconds)}",
                DesktopProgressKind.Failed =>
                    $"Başarısız · {FormatElapsed(message.ElapsedSeconds)}",
                DesktopProgressKind.Cancelled =>
                    $"İptal edildi · {FormatElapsed(message.ElapsedSeconds)}",
                DesktopProgressKind.Warning => "Uyarı",
                DesktopProgressKind.Info => "Bilgi",
                _ => "Talvora",
            };

        _accent.Background =
            new SolidColorBrush(
                message.Kind switch
                {
                    DesktopProgressKind.Completed =>
                        WpfColor.FromRgb(59, 201, 123),
                    DesktopProgressKind.Running =>
                        WpfColor.FromRgb(82, 177, 255),
                    DesktopProgressKind.Failed =>
                        WpfColor.FromRgb(244, 92, 92),
                    DesktopProgressKind.Cancelled =>
                        WpfColor.FromRgb(244, 177, 72),
                    DesktopProgressKind.Warning =>
                        WpfColor.FromRgb(244, 177, 72),
                    DesktopProgressKind.Info =>
                        WpfColor.FromRgb(72, 151, 255),
                    _ =>
                        WpfColor.FromRgb(72, 151, 255),
                });

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

    public void SetPinned(bool pinned)
    {
        _isPinned = pinned;
        _pinButton.Content = pinned
            ? "Sabit"
            : "Sabitle";
        _pinButton.ToolTip = pinned
            ? "Otomatik ekran konumuna dön"
            : "Kartın mevcut konumunu sabitle";
        _pinButton.Foreground =
            new SolidColorBrush(
                pinned
                    ? WpfColor.FromRgb(129, 184, 255)
                    : WpfColor.FromRgb(171, 178, 190));
        _pinButton.Background =
            new SolidColorBrush(
                pinned
                    ? WpfColor.FromArgb(34, 72, 151, 255)
                    : WpfColor.FromArgb(0, 0, 0, 0));
        _pinButton.BorderBrush =
            new SolidColorBrush(
                pinned
                    ? WpfColor.FromArgb(112, 72, 151, 255)
                    : WpfColor.FromArgb(52, 255, 255, 255));
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
            if (current is WpfButtonBase or WpfScrollBar)
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
            _dismissAfter == Timeout.InfiniteTimeSpan)
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
}
