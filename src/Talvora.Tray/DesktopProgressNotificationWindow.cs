using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Talvora.Shared;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;

namespace Talvora.Tray;

internal sealed class DesktopProgressNotificationWindow : Window
{
    private static readonly TimeSpan ActiveLifetime =
        TimeSpan.FromSeconds(28);
    private static readonly TimeSpan CompletedLifetime =
        TimeSpan.FromSeconds(11);
    private static readonly TimeSpan FailureLifetime =
        TimeSpan.FromSeconds(18);

    private readonly Border _accent;
    private readonly TextBlock _title;
    private readonly TextBlock _message;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _dismissTimer;
    private TimeSpan _dismissAfter = ActiveLifetime;

    public DesktopProgressNotificationWindow()
    {
        Width = 470;
        MaxHeight = 340;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;

        var root = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background =
                new SolidColorBrush(WpfColor.FromArgb(248, 24, 27, 35)),
            BorderBrush =
                new SolidColorBrush(WpfColor.FromArgb(72, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
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
        };
        content.Children.Add(_title);

        _message = new TextBlock
        {
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(222, 226, 234)),
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
        };

        var scroll = new ScrollViewer
        {
            Content = _message,
            MaxHeight = 190,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        Grid.SetRow(scroll, 1);
        content.Children.Add(scroll);

        _status = new TextBlock
        {
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(147, 156, 171)),
            FontSize = 11,
            Margin = new Thickness(0, 9, 0, 0),
        };
        Grid.SetRow(_status, 2);
        content.Children.Add(_status);

        grid.Children.Add(content);

        var closeButton = new WpfButton
        {
            Content = "×",
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(12, -5, -5, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
            Foreground =
                new SolidColorBrush(WpfColor.FromRgb(171, 178, 190)),
            FontSize = 18,
            Cursor = WpfCursors.Hand,
            Focusable = false,
        };
        closeButton.Click += (_, _) => Close();
        Grid.SetColumn(closeButton, 3);
        grid.Children.Add(closeButton);

        root.Child = grid;
        Content = root;

        _dismissTimer = new DispatcherTimer
        {
            Interval = _dismissAfter,
        };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            Close();
        };

        MouseEnter += (_, _) => _dismissTimer.Stop();
        MouseLeave += (_, _) => RestartDismissTimer();
    }

    public string OperationId { get; private set; } = string.Empty;

    public DateTimeOffset LastUpdatedUtc { get; private set; }

    public void Update(DesktopProgressMessage message)
    {
        OperationId = message.OperationId;
        LastUpdatedUtc = DateTimeOffset.UtcNow;
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
                _ => "Talvora",
            };

        _accent.Background =
            new SolidColorBrush(
                message.Kind switch
                {
                    DesktopProgressKind.Completed =>
                        WpfColor.FromRgb(59, 201, 123),
                    DesktopProgressKind.Failed =>
                        WpfColor.FromRgb(244, 92, 92),
                    DesktopProgressKind.Cancelled =>
                        WpfColor.FromRgb(244, 177, 72),
                    _ =>
                        WpfColor.FromRgb(72, 151, 255),
                });

        _dismissAfter =
            message.Kind switch
            {
                DesktopProgressKind.Completed => CompletedLifetime,
                DesktopProgressKind.Failed => FailureLifetime,
                DesktopProgressKind.Cancelled => FailureLifetime,
                _ => ActiveLifetime,
            };
        RestartDismissTimer();
    }

    private void RestartDismissTimer()
    {
        _dismissTimer.Stop();
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
