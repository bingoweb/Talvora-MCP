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
using FontFamily = System.Windows.Media.FontFamily;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace Talvora.Tray;

internal sealed class DesktopProgressNotificationWindow : Window
{
    private static readonly TimeSpan StartedLifetime =
        TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RunningLifetime =
        TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CompletedLifetime =
        TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FailureLifetime =
        TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ActiveStaleThreshold =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StaleCheckInterval =
        TimeSpan.FromSeconds(10);

    private readonly Border _accent;
    private readonly TextBlock _title;
    private readonly TextBlock _message;
    private readonly TextBlock _status;
    private readonly Expander _evidenceExpander;
    private readonly TextBlock _evidenceSummary;
    private readonly TextBlock _evidenceFiles;
    private readonly WpfTextBox _codePreview;
    private readonly WpfButton _pinButton;
    private readonly DispatcherTimer _dismissTimer;
    private readonly DispatcherTimer _staleTimer;
    private readonly TranslateTransform _translateTransform = new();
    private TimeSpan _dismissAfter = StartedLifetime;
    private bool _isPointerOver;
    private bool _isPinned;
    private bool _isClosing;
    private bool _manualClose;
    private bool _isStale;

    public DesktopProgressNotificationWindow()
    {
        Width = 620;
        MaxHeight = 760;
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
            CornerRadius = new CornerRadius(10),
            Background =
                new SolidColorBrush(WpfColor.FromArgb(252, 3, 8, 6)),
            BorderBrush =
                new SolidColorBrush(WpfColor.FromArgb(168, 36, 255, 111)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Cursor = WpfCursors.SizeAll,
            ToolTip =
                "Canlı Talvora çalışma günlüğü. Teknik kanıt yalnız gerçek çalışma verisinden üretilir.",
            Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 2,
                Opacity = 0.56,
                Color = WpfColor.FromRgb(20, 255, 96),
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
            CornerRadius = new CornerRadius(2),
            Background =
                new SolidColorBrush(WpfColor.FromRgb(38, 255, 112)),
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
        content.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(content, 2);

        var traceLabel = new TextBlock
        {
            Text = "TALVORA // LIVE TRACE",
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(68, 255, 132)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = WpfCursors.SizeAll,
        };
        Grid.SetRow(traceLabel, 0);
        content.Children.Add(traceLabel);

        _title = new TextBlock
        {
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(232, 255, 239)),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = WpfCursors.SizeAll,
        };
        Grid.SetRow(_title, 1);
        content.Children.Add(_title);

        _message = new TextBlock
        {
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(201, 230, 208)),
            FontSize = 13,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            Cursor = WpfCursors.Arrow,
        };

        var scroll = new ScrollViewer
        {
            Content = _message,
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Cursor = WpfCursors.Arrow,
        };
        Grid.SetRow(scroll, 2);
        content.Children.Add(scroll);

        _evidenceSummary = new TextBlock
        {
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(86, 255, 144)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };

        _evidenceFiles = new TextBlock
        {
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(151, 206, 166)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.5,
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };

        _codePreview = new WpfTextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 220,
            Margin = new Thickness(0, 9, 0, 0),
            Padding = new Thickness(9),
            Background =
                new SolidColorBrush(WpfColor.FromRgb(1, 12, 7)),
            BorderBrush =
                new SolidColorBrush(WpfColor.FromArgb(112, 43, 255, 118)),
            BorderThickness = new Thickness(1),
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(108, 255, 154)),
            SelectionBrush =
                new SolidColorBrush(WpfColor.FromArgb(96, 58, 255, 127)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.5,
            Cursor = WpfCursors.IBeam,
        };

        var evidenceStack = new StackPanel();
        evidenceStack.Children.Add(_evidenceSummary);
        evidenceStack.Children.Add(_evidenceFiles);
        evidenceStack.Children.Add(_codePreview);

        var evidenceBorder = new Border
        {
            Background =
                new SolidColorBrush(WpfColor.FromArgb(190, 1, 16, 9)),
            BorderBrush =
                new SolidColorBrush(WpfColor.FromArgb(72, 43, 255, 118)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 5, 0, 0),
            Child = evidenceStack,
        };

        _evidenceExpander = new Expander
        {
            Header = "KANIT // GERÇEK VERİ",
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(74, 255, 135)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Content = evidenceBorder,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 10, 0, 0),
            Cursor = WpfCursors.Arrow,
        };
        Grid.SetRow(_evidenceExpander, 3);
        content.Children.Add(_evidenceExpander);

        _status = new TextBlock
        {
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(80, 201, 113)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10.5,
            Margin = new Thickness(0, 10, 0, 0),
            Cursor = WpfCursors.SizeAll,
        };
        Grid.SetRow(_status, 4);
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
                new SolidColorBrush(WpfColor.FromArgb(82, 43, 255, 118)),
            BorderThickness = new Thickness(1),
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(114, 221, 143)),
            FontFamily = new FontFamily("Consolas"),
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
                new SolidColorBrush(WpfColor.FromRgb(114, 221, 143)),
            FontFamily = new FontFamily("Consolas"),
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

        _staleTimer = new DispatcherTimer
        {
            Interval = StaleCheckInterval,
        };
        _staleTimer.Tick += (_, _) =>
            CheckForStaleWork();
        _staleTimer.Start();

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
        Closed += (_, _) =>
        {
            _dismissTimer.Stop();
            _staleTimer.Stop();
        };
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
        _isStale = false;
        _title.Text = message.Title;
        _message.Text = message.Message;
        UpdateEvidence(message);
        var localTime = message.TimestampUtc.ToLocalTime();
        _status.Text =
            message.Kind switch
            {
                DesktopProgressKind.Started =>
                    $"● LIVE  //  {localTime:HH:mm:ss}",
                DesktopProgressKind.Running =>
                    $"● LIVE  //  {FormatElapsed(message.ElapsedSeconds)}  //  {localTime:HH:mm:ss}",
                DesktopProgressKind.Completed =>
                    $"✓ TAMAM  //  {FormatElapsed(message.ElapsedSeconds)}  //  {localTime:HH:mm:ss}",
                DesktopProgressKind.Failed =>
                    $"✕ HATA  //  {FormatElapsed(message.ElapsedSeconds)}  //  {localTime:HH:mm:ss}",
                DesktopProgressKind.Cancelled =>
                    $"■ DURDU  //  {FormatElapsed(message.ElapsedSeconds)}  //  {localTime:HH:mm:ss}",
                DesktopProgressKind.Warning =>
                    $"! UYARI  //  {localTime:HH:mm:ss}",
                DesktopProgressKind.Info =>
                    $"i BİLGİ  //  {localTime:HH:mm:ss}",
                _ => "Talvora",
            };

        _accent.Background =
            new SolidColorBrush(
                message.Kind switch
                {
                    DesktopProgressKind.Completed =>
                        WpfColor.FromRgb(38, 255, 112),
                    DesktopProgressKind.Running =>
                        WpfColor.FromRgb(38, 255, 112),
                    DesktopProgressKind.Failed =>
                        WpfColor.FromRgb(255, 67, 88),
                    DesktopProgressKind.Cancelled =>
                        WpfColor.FromRgb(244, 177, 72),
                    DesktopProgressKind.Warning =>
                        WpfColor.FromRgb(244, 177, 72),
                    DesktopProgressKind.Info =>
                        WpfColor.FromRgb(55, 225, 124),
                    _ =>
                        WpfColor.FromRgb(38, 255, 112),
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

    private void CheckForStaleWork()
    {
        if (_isClosing ||
            IsTerminal ||
            LastUpdatedUtc == default)
        {
            return;
        }

        var staleFor =
            DateTimeOffset.UtcNow - LastUpdatedUtc;
        if (staleFor < ActiveStaleThreshold ||
            _isStale)
        {
            return;
        }

        _isStale = true;
        _title.Text = "Yeni durum bekliyorum";
        _message.Text =
            $"Bu işten {FormatElapsed(staleFor.TotalSeconds)} süredir yeni bilgi gelmedi.\n\n" +
            $"Son gerçek güncelleme: {LastUpdatedUtc.ToLocalTime():HH:mm:ss}. " +
            "Yeni veri gelir gelmez bu kart otomatik olarak devam edecek.";
        _status.Text =
            $"◌ BEKLİYOR  //  SON VERİ {LastUpdatedUtc.ToLocalTime():HH:mm:ss}";
        _accent.Background =
            new SolidColorBrush(
                WpfColor.FromRgb(244, 177, 72));
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
                    ? WpfColor.FromRgb(83, 255, 139)
                    : WpfColor.FromRgb(114, 221, 143));
        _pinButton.Background =
            new SolidColorBrush(
                pinned
                    ? WpfColor.FromArgb(34, 43, 255, 118)
                    : WpfColor.FromArgb(0, 0, 0, 0));
        _pinButton.BorderBrush =
            new SolidColorBrush(
                pinned
                    ? WpfColor.FromArgb(112, 43, 255, 118)
                    : WpfColor.FromArgb(82, 43, 255, 118));
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
        if (evidence is null)
        {
            _evidenceExpander.Visibility = Visibility.Collapsed;
            SetTextIfChanged(_evidenceSummary, string.Empty);
            SetTextIfChanged(_evidenceFiles, string.Empty);
            SetTextIfChanged(_codePreview, string.Empty);
            return;
        }

        _evidenceExpander.Visibility = Visibility.Visible;
        var evidenceSummary =
            string.Join(
                "  //  ",
                new[]
                {
                    evidence.Summary,
                    evidence.Result,
                }
                .Where(static value =>
                    !string.IsNullOrWhiteSpace(value)));
        SetTextIfChanged(
            _evidenceSummary,
            evidenceSummary);

        var evidenceFiles =
            evidence.Files is { Count: > 0 }
                ? string.Join(
                    Environment.NewLine,
                    evidence.Files.Select(
                        static path => $"› {path}"))
                : string.Empty;
        SetTextIfChanged(
            _evidenceFiles,
            evidenceFiles);

        var codePreview =
            evidence.CodePreview ?? string.Empty;
        SetTextIfChanged(
            _codePreview,
            codePreview);
        _codePreview.Visibility =
            string.IsNullOrWhiteSpace(codePreview)
                ? Visibility.Collapsed
                : Visibility.Visible;

        if (message.Kind == DesktopProgressKind.Failed ||
            (message.Kind == DesktopProgressKind.Completed &&
             !string.IsNullOrWhiteSpace(codePreview)))
        {
            _evidenceExpander.IsExpanded = true;
        }
    }

    private static void SetTextIfChanged(
        TextBlock target,
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
