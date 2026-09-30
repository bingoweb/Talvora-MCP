using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;
using Talvora.Shared;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Clipboard = System.Windows.Clipboard;
using SystemColors = System.Windows.SystemColors;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Talvora.Tray;

internal sealed partial class DesktopProgressNotificationWindow
{
    private readonly DropShadowEffect _normalShadow = new()
    {
        BlurRadius = 18,
        ShadowDepth = 3,
        Direction = 270,
        Opacity = 0.28,
        Color = Colors.Black,
    };
    private bool _initialSizeLocked;

    private void ConfigureWindowShell()
    {
        Width = 500;
        MinWidth = 360;
        MaxWidth = 1200;
        MinHeight = 150;
        MaxHeight = 900;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = false;
        Background = DesktopProgressVisualTheme.SurfaceBrush;
        Opacity = 0;
        RenderTransform = _translateTransform;
        Title = "Talvora trace";

        WindowChrome.SetWindowChrome(
            this,
            new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(8),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                UseAeroCaptionButtons = false,
            });

        AutomationProperties.SetName(
            this,
            "Talvora çalışma bildirimi");
        AutomationProperties.SetHelpText(
            this,
            "Üstte kısa çalışma bilgisi, altta varsa gerçek kod veya diff önizlemesi gösterir.");
    }

    private void BuildVisualTree()
    {
        _root = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Cursor = WpfCursors.SizeAll,
            ToolTip =
                "Talvora çalışma kartı. Kenarlardan yeniden boyutlandırılabilir; teknik diff gerçek çalışma verisinden üretilir.",
        };
        AutomationProperties.SetName(
            _root,
            "Talvora çalışma kartı");

        var outer = new Grid();
        outer.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(3) });
        outer.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(10) });
        outer.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });

        _accent = new Border
        {
            CornerRadius = new CornerRadius(2),
        };
        Grid.SetColumn(_accent, 0);
        outer.Children.Add(_accent);

        var content = new Grid();
        for (var index = 0; index < 3; index++)
        {
            content.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });
        }
        _diffRow = new RowDefinition
        {
            Height = GridLength.Auto,
        };
        content.RowDefinitions.Add(_diffRow);
        Grid.SetColumn(content, 2);

        content.Children.Add(BuildHeader());

        var messageScroll = new ScrollViewer
        {
            MaxHeight = 125,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Margin = new Thickness(0, 9, 0, 0),
        };
        _message = new TextBlock
        {
            FontFamily = DesktopProgressVisualTheme.UiFont,
            FontSize = 12.5,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Cursor = WpfCursors.Arrow,
        };
        AutomationProperties.SetName(
            _message,
            "Çalışma açıklaması");
        AutomationProperties.SetLiveSetting(
            _message,
            AutomationLiveSetting.Polite);
        messageScroll.Content = _message;
        Grid.SetRow(messageScroll, 1);
        content.Children.Add(messageScroll);

        _meta = new TextBlock
        {
            FontFamily = DesktopProgressVisualTheme.TerminalFont,
            FontSize = 9.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        AutomationProperties.SetName(
            _meta,
            "Teknik işlem özeti");
        Grid.SetRow(_meta, 2);
        content.Children.Add(_meta);

        _diffPanel = BuildDiffPanel();
        Grid.SetRow(_diffPanel, 3);
        content.Children.Add(_diffPanel);

        outer.Children.Add(content);
        _root.Child = outer;
        Content = _root;
    }

    private UIElement BuildHeader()
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
        header.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        var traceRow = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _stateIcon = new SymbolIcon
        {
            Symbol = SymbolRegular.Play20,
            Width = 15,
            Height = 15,
            Margin = new Thickness(0, 0, 6, 0),
        };
        traceRow.Children.Add(_stateIcon);

        _traceLabel = new TextBlock
        {
            Text = "TALVORA // TRACE",
            FontFamily = DesktopProgressVisualTheme.TerminalFont,
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        traceRow.Children.Add(_traceLabel);

        _stateBadgeText = new TextBlock
        {
            Text = "BAŞLADI",
            FontFamily = DesktopProgressVisualTheme.TerminalFont,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
        };
        _stateBadge = new Border
        {
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            Child = _stateBadgeText,
        };
        traceRow.Children.Add(_stateBadge);
        left.Children.Add(traceRow);

        _title = new TextBlock
        {
            FontFamily = DesktopProgressVisualTheme.UiFont,
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0),
        };
        AutomationProperties.SetName(
            _title,
            "İşlem başlığı");
        left.Children.Add(_title);
        header.Children.Add(left);

        var actions = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, -2, -2, 0),
        };
        _pinButton = CreateIconButton(
            SymbolRegular.Pin20,
            "Kart konumunu sabitle",
            "Bildirimi bulunduğu ekranda ve konumda sabitler.");
        _pinButton.Click += (_, _) =>
        {
            if (!_isClosing)
            {
                PinStateChangeRequested?.Invoke(!_isPinned);
            }
        };
        actions.Children.Add(_pinButton);

        _closeButton = CreateIconButton(
            SymbolRegular.Dismiss20,
            "Bildirimi kapat",
            "Bu çalışma kartını kapatır.");
        _closeButton.Margin = new Thickness(3, 0, 0, 0);
        _closeButton.Click += (_, _) =>
            BeginClose(userInitiated: true);
        actions.Children.Add(_closeButton);

        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        return header;
    }

    private Border BuildDiffPanel()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        _diffContentRow = new RowDefinition
        {
            Height = GridLength.Auto,
        };
        grid.RowDefinitions.Add(_diffContentRow);

        var header = new Grid();
        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
        header.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        _diffHeader = new TextBlock
        {
            Text = "DIFF // GERÇEK DEĞİŞİKLİK",
            FontFamily = DesktopProgressVisualTheme.TerminalFont,
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(_diffHeader);

        _copyDiffButton = CreateIconButton(
            SymbolRegular.Copy20,
            "Diff'i kopyala",
            "Gösterilen gerçek kod veya diff önizlemesini panoya kopyalar.",
            compact: true);
        _copyDiffButton.Click += (_, _) => CopyDiff();
        Grid.SetColumn(_copyDiffButton, 1);
        header.Children.Add(_copyDiffButton);
        grid.Children.Add(header);

        _codePreview = new WpfTextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            AcceptsReturn = true,
            IsUndoEnabled = false,
            TextWrapping = TextWrapping.Wrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 64,
            MaxHeight = 190,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            FontFamily = DesktopProgressVisualTheme.TerminalFont,
            FontSize = 10,
            Cursor = WpfCursors.IBeam,
        };
        AutomationProperties.SetName(
            _codePreview,
            "Gerçek kod veya diff önizlemesi");
        Grid.SetRow(_codePreview, 1);
        grid.Children.Add(_codePreview);

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9),
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            Child = grid,
        };
    }

    private static WpfButton CreateIconButton(
        SymbolRegular symbol,
        string automationName,
        string helpText,
        bool compact = false)
    {
        var button = new WpfButton
        {
            Content = new SymbolIcon
            {
                Symbol = symbol,
                FontSize = compact ? 12.5 : 14,
            },
            Width = compact ? 23 : 26,
            Height = compact ? 22 : 26,
            Padding = new Thickness(0),
            Background = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand,
            Focusable = true,
            IsTabStop = true,
            Opacity = 0.78,
            ToolTip = helpText,
        };
        button.MouseEnter += (_, _) =>
            button.Opacity = 1;
        button.MouseLeave += (_, _) =>
            button.Opacity = 0.78;
        AutomationProperties.SetName(
            button,
            automationName);
        AutomationProperties.SetHelpText(
            button,
            helpText);
        return button;
    }

    private void ConfigureTimersAndInteraction()
    {
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
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                BeginClose(userInitiated: true);
                e.Handled = true;
            }
        };
        _root.PreviewMouseLeftButtonDown +=
            OnCardMouseLeftButtonDown;
        SourceInitialized += (_, _) =>
            AttachNativeWindowHook();
        Loaded += (_, _) =>
        {
            AnimateIn();
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(LockInitialSizeForResize));
        };
        Closed += (_, _) =>
        {
            _dismissTimer.Stop();
            _staleTimer.Stop();
            DetachNativeWindowHook();
            SystemParameters.StaticPropertyChanged -=
                OnSystemParametersChanged;
        };
    }

    private void LockInitialSizeForResize()
    {
        if (_initialSizeLocked)
        {
            return;
        }

        var renderedHeight = Math.Clamp(
            Math.Max(ActualHeight, MinHeight),
            MinHeight,
            MaxHeight);
        SizeToContent = SizeToContent.Manual;
        Height = renderedHeight;
        EnableResponsiveSizing();
        _initialSizeLocked = true;
    }

    private void EnableResponsiveSizing()
    {
        _diffRow.Height = new GridLength(
            1,
            GridUnitType.Star);
        _diffContentRow.Height = new GridLength(
            1,
            GridUnitType.Star);
        _codePreview.MaxHeight = double.PositiveInfinity;
    }

    private void OnSystemParametersChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.Equals(
                e.PropertyName,
                nameof(SystemParameters.HighContrast),
                StringComparison.Ordinal))
        {
            ApplyEnvironmentPalette();
        }
    }

    private void ApplyEnvironmentPalette()
    {
        Background =
            DesktopProgressVisualTheme.SurfaceBrush;
        _root.Background =
            DesktopProgressVisualTheme.SurfaceBrush;
        _root.BorderBrush =
            DesktopProgressVisualTheme.BorderBrush;
        _root.Effect = SystemParameters.HighContrast
            ? null
            : _normalShadow;

        _traceLabel.Foreground =
            DesktopProgressVisualTheme.TraceBrush;
        _title.Foreground =
            DesktopProgressVisualTheme.PrimaryTextBrush;
        _message.Foreground =
            DesktopProgressVisualTheme.SecondaryTextBrush;
        _meta.Foreground =
            DesktopProgressVisualTheme.TerminalMutedBrush;

        _diffPanel.Background =
            DesktopProgressVisualTheme.RaisedSurfaceBrush;
        _diffPanel.BorderBrush =
            DesktopProgressVisualTheme.BorderBrush;
        _diffHeader.Foreground =
            DesktopProgressVisualTheme.TraceBrush;
        _codePreview.Background =
            DesktopProgressVisualTheme.TerminalBrush;
        _codePreview.Foreground =
            DesktopProgressVisualTheme.TerminalTextBrush;
        _codePreview.BorderBrush =
            DesktopProgressVisualTheme.BorderBrush;
        _codePreview.SelectionBrush =
            DesktopProgressVisualTheme.TraceSoftBrush;

        _pinButton.Foreground =
            DesktopProgressVisualTheme.SecondaryTextBrush;
        _pinButton.BorderBrush =
            DesktopProgressVisualTheme.BorderBrush;
        _closeButton.Foreground =
            DesktopProgressVisualTheme.SecondaryTextBrush;
        _closeButton.BorderBrush =
            DesktopProgressVisualTheme.BorderBrush;
        _copyDiffButton.Foreground =
            DesktopProgressVisualTheme.TerminalMutedBrush;
        _copyDiffButton.BorderBrush =
            DesktopProgressVisualTheme.BorderBrush;

        if (_lastMessage is not null)
        {
            ApplyStatusVisual(_lastMessage);
        }
    }

    private void ApplyStatusVisual(
        DesktopProgressMessage message)
    {
        var visual =
            DesktopProgressVisualTheme.Describe(message.Kind);
        var statusBrush =
            DesktopProgressVisualTheme.GetStatusBrush(message.Kind);
        var localTime =
            message.TimestampUtc.ToLocalTime();

        _stateIcon.Symbol = visual.Symbol;
        _stateIcon.Foreground = statusBrush;
        _stateBadgeText.Text = visual.Label;
        _stateBadgeText.Foreground = statusBrush;
        _stateBadge.BorderBrush = statusBrush;
        _stateBadge.Background = SystemParameters.HighContrast
            ? SystemColors.ControlBrush
            : DesktopProgressVisualTheme.RaisedSurfaceBrush;
        _accent.Background = statusBrush;

        _meta.Text =
            $"{message.ToolName}  •  {FormatElapsed(message.ElapsedSeconds)}  •  {localTime:HH:mm:ss}";

        Title = message.Lane == DesktopProgressLane.Alert
            ? $"Talvora uyarısı — {visual.Label}"
            : $"Talvora çalışma günlüğü — {visual.Label}";
        AutomationProperties.SetName(
            this,
            $"Talvora {visual.Label.ToLowerInvariant()} bildirimi: {message.Title}");
    }

    private void ApplyStaleVisual()
    {
        var warningBrush =
            DesktopProgressVisualTheme.GetStatusBrush(
                DesktopProgressKind.Warning);
        _stateIcon.Symbol = SymbolRegular.Clock20;
        _stateIcon.Foreground = warningBrush;
        _stateBadgeText.Text = "BEKLİYOR";
        _stateBadgeText.Foreground = warningBrush;
        _stateBadge.BorderBrush = warningBrush;
        _accent.Background = warningBrush;

        if (_lastMessage is not null)
        {
            _meta.Text =
                $"{_lastMessage.ToolName}  •  son veri {_lastPayloadUpdatedUtc.ToLocalTime():HH:mm:ss}";
        }
    }

    private void CopyDiff()
    {
        if (string.IsNullOrWhiteSpace(
                _codePreview.Text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(_codePreview.Text);
        }
        catch (Exception ex) when (
            ex is ExternalException or
            InvalidOperationException)
        {
            TrayLog.Write(
                "Desktop progress diff could not be copied.",
                ex);
        }
    }
}
