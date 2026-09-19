using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using ComboBox = System.Windows.Controls.ComboBox;
using MediaBrush = System.Windows.Media.Brush;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfCursors = System.Windows.Input.Cursors;

namespace Talvora.Tray;

internal sealed partial class ControlCenterWindow
{
    private CardExpander _eventsExpander = null!;
    private TextBlock _eventsSummaryText = null!;
    private UiTextBox _eventSearchBox = null!;
    private ComboBox _eventFilterBox = null!;
    private StackPanel _eventsList = null!;
    private CardExpander _rawLogExpander = null!;
    private UiTextBox _rawLogSearchBox = null!;
    private ComboBox _rawLogFilterBox = null!;
    private System.Windows.Controls.TextBox _rawLogTextBox = null!;
    private TextBlock _rawLogMetaText = null!;
    private UiButton _rawLogCopyButton = null!;
    private DispatcherTimer _rawLogRefreshTimer = null!;
    private IReadOnlyList<ControlCenterEventRecord> _recentEvents =
        Array.Empty<ControlCenterEventRecord>();
    private string _rawLogSourceText = string.Empty;

    private UIElement BuildEventsPanel()
    {
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        headerGrid.Children.Add(new SymbolIcon
        {
            Symbol = SymbolRegular.History20,
            Width = 20,
            Height = 20,
            Margin = new Thickness(0, 1, 10, 0),
            Foreground = AccentBrush,
            VerticalAlignment = VerticalAlignment.Top,
        });

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "Son olaylar",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
        });

        _eventsSummaryText = new TextBlock
        {
            Text = "Olay özeti hazırlanıyor...",
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 11,
        };
        titleStack.Children.Add(_eventsSummaryText);
        Grid.SetColumn(titleStack, 1);
        headerGrid.Children.Add(titleStack);

        var hint = new TextBlock
        {
            Text = "Ayrıntıları göster",
            Foreground = TertiaryTextBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(hint, 2);
        headerGrid.Children.Add(hint);

        var body = new StackPanel();

        var controls = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12),
        };
        controls.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        controls.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        _eventSearchBox = new UiTextBox
        {
            MinWidth = 220,
            Style = FindStyle("TalvoraSearchBoxStyle"),
            PlaceholderText = "Olaylarda ara",
            ClearButtonEnabled = true,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Search20 },
        };
        _eventSearchBox.TextChanged += (_, _) => RenderEvents();
        controls.Children.Add(_eventSearchBox);

        _eventFilterBox = new ComboBox
        {
            Width = 160,
            Height = 40,
            Margin = new Thickness(12, 0, 0, 0),
            Style = FindStyle("TalvoraFilterComboBoxStyle"),
            SelectedIndex = 0,
        };
        _eventFilterBox.Items.Add("Tümü");
        _eventFilterBox.Items.Add("Önemli");
        _eventFilterBox.Items.Add("Uyarılar");
        _eventFilterBox.Items.Add("Hatalar");
        _eventFilterBox.SelectionChanged += (_, _) => RenderEvents();
        Grid.SetColumn(_eventFilterBox, 1);
        controls.Children.Add(_eventFilterBox);
        body.Children.Add(controls);

        _eventsList = new StackPanel();
        body.Children.Add(_eventsList);

        _rawLogExpander = BuildRawLogExpander();
        body.Children.Add(_rawLogExpander);

        _eventsExpander = new CardExpander
        {
            Margin = new Thickness(0, 8, 0, 0),
            Header = headerGrid,
            IsExpanded = false,
            Background = SurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ContentPadding = new Thickness(18, 10, 18, 18),
            Content = body,
        };

        _eventsExpander.Expanded += (_, _) =>
        {
            RefreshEventsPanel();
            UpdateRawLogTimerState();
        };
        _eventsExpander.Collapsed += (_, _) =>
        {
            UpdateRawLogTimerState();
        };

        _rawLogRefreshTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(2),
            DispatcherPriority.Background,
            (_, _) => RefreshRawLogView(),
            Dispatcher)
        {
            IsEnabled = false,
        };

        return _eventsExpander;
    }

    private CardExpander BuildRawLogExpander()
    {
        var body = new StackPanel();

        var controls = new Grid
        {
            Margin = new Thickness(0, 0, 0, 10),
        };
        controls.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        controls.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });
        controls.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        _rawLogSearchBox = new UiTextBox
        {
            MinWidth = 220,
            Style = FindStyle("TalvoraSearchBoxStyle"),
            PlaceholderText = "Ham günlükte ara",
            ClearButtonEnabled = true,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Search20 },
        };
        _rawLogSearchBox.TextChanged += (_, _) => ApplyRawLogFilter();
        controls.Children.Add(_rawLogSearchBox);

        _rawLogFilterBox = new ComboBox
        {
            Width = 150,
            Height = 40,
            Margin = new Thickness(12, 0, 0, 0),
            Style = FindStyle("TalvoraFilterComboBoxStyle"),
            SelectedIndex = 0,
        };
        _rawLogFilterBox.Items.Add("Tümü");
        _rawLogFilterBox.Items.Add("Hatalar");
        _rawLogFilterBox.Items.Add("Kurtarma");
        _rawLogFilterBox.Items.Add("İşlemler");
        _rawLogFilterBox.SelectionChanged += (_, _) => ApplyRawLogFilter();
        Grid.SetColumn(_rawLogFilterBox, 1);
        controls.Children.Add(_rawLogFilterBox);

        _rawLogCopyButton = new UiButton
        {
            Content = "Kopyala",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Copy20 },
            MinWidth = 98,
            Margin = new Thickness(12, 0, 0, 0),
            Style = FindStyle("TalvoraSecondaryButtonStyle"),
            Cursor = WpfCursors.Hand,
            IsEnabled = false,
        };
        _rawLogCopyButton.Click += (_, _) => CopyVisibleRawLog();
        Grid.SetColumn(_rawLogCopyButton, 2);
        controls.Children.Add(_rawLogCopyButton);
        body.Children.Add(controls);

        _rawLogTextBox = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            IsUndoEnabled = false,
            MinHeight = 190,
            MaxHeight = 340,
            Padding = new Thickness(12),
            FontFamily = new WpfFontFamily("Cascadia Mono, Consolas"),
            FontSize = 11,
            Foreground = PrimaryTextBrush,
            Background = BackgroundBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        body.Children.Add(_rawLogTextBox);

        _rawLogMetaText = new TextBlock
        {
            Text = "Ham günlük yalnızca görüntülenir; kaynak metin değiştirilmez.",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = TertiaryTextBrush,
            FontSize = 10,
        };
        body.Children.Add(_rawLogMetaText);

        var expander = new CardExpander
        {
            Margin = new Thickness(0, 12, 0, 0),
            Header = "Canlı ham günlük",
            Icon = new SymbolIcon { Symbol = SymbolRegular.DocumentText20 },
            IsExpanded = false,
            Background = RaisedSurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ContentPadding = new Thickness(14, 10, 14, 14),
            Content = body,
        };

        expander.Expanded += (_, _) =>
        {
            RefreshRawLogView();
            UpdateRawLogTimerState();
        };
        expander.Collapsed += (_, _) =>
        {
            UpdateRawLogTimerState();
        };

        return expander;
    }

    private void RefreshEventsPanel()
    {
        if (_eventsSummaryText is null)
        {
            return;
        }

        _recentEvents = ControlCenterEventStore.ReadRecent(
            TimeSpan.FromHours(24),
            maxRecords: 100);

        var warningGroups = _recentEvents.Count(entry =>
            entry.Severity == ControlCenterEventSeverity.Warning);
        var errorGroups = _recentEvents.Count(entry =>
            entry.Severity == ControlCenterEventSeverity.Error);
        var important = warningGroups + errorGroups;

        _eventsSummaryText.Text = important == 0
            ? "Son 24 saatte önemli olay yok."
            : errorGroups > 0
                ? $"Son 24 saatte {important} önemli olay • {errorGroups} hata • {warningGroups} uyarı"
                : $"Son 24 saatte {important} önemli olay • {warningGroups} uyarı";

        _eventsSummaryText.Foreground = errorGroups > 0
            ? OfflineBrush
            : warningGroups > 0
                ? AttentionBrush
                : SecondaryTextBrush;

        if (_eventsExpander?.IsExpanded == true)
        {
            RenderEvents();
        }
    }

    private void RenderEvents()
    {
        if (_eventsList is null)
        {
            return;
        }

        var query = _eventSearchBox?.Text?.Trim() ?? string.Empty;
        var filterIndex = _eventFilterBox?.SelectedIndex ?? 0;

        var visible = _recentEvents
            .Where(entry =>
                string.IsNullOrWhiteSpace(query) ||
                entry.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Detail.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                (entry.McpId?.Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase) ?? false))
            .Where(entry => filterIndex switch
            {
                1 => entry.Severity >= ControlCenterEventSeverity.Warning,
                2 => entry.Severity == ControlCenterEventSeverity.Warning,
                3 => entry.Severity == ControlCenterEventSeverity.Error,
                _ => true,
            })
            .Take(30)
            .ToArray();

        _eventsList.Children.Clear();

        if (visible.Length == 0)
        {
            _eventsList.Children.Add(new TextBlock
            {
                Text = _recentEvents.Count == 0
                    ? "Son 24 saatte kaydedilmiş olay yok."
                    : "Arama veya filtreyle eşleşen olay yok.",
                Padding = new Thickness(4, 10, 4, 12),
                Foreground = SecondaryTextBrush,
                FontSize = 12,
            });
            return;
        }

        foreach (var entry in visible)
        {
            _eventsList.Children.Add(CreateEventRow(entry));
        }
    }

    private Border CreateEventRow(ControlCenterEventRecord entry)
    {
        var severityBrush = GetEventSeverityBrush(entry.Severity);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new SymbolIcon
        {
            Symbol = GetEventSeverityIcon(entry.Severity),
            Width = 19,
            Height = 19,
            Margin = new Thickness(0, 2, 12, 0),
            Foreground = severityBrush,
            VerticalAlignment = VerticalAlignment.Top,
        });

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = entry.Count > 1
                ? $"{entry.Title}  ×{entry.Count}"
                : entry.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            text.Children.Add(new TextBlock
            {
                Text = entry.Detail,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = SecondaryTextBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var time = new TextBlock
        {
            Text = FormatRelativeEventTime(entry.LastOccurredAtUtc),
            Margin = new Thickness(16, 1, 0, 0),
            Foreground = TertiaryTextBrush,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(time, 2);
        grid.Children.Add(time);

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10),
            Background = RaisedSurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = grid,
        };
    }

    private void RefreshRawLogView()
    {
        if (_rawLogTextBox is null ||
            _rawLogExpander?.IsExpanded != true ||
            !IsVisible)
        {
            return;
        }

        _rawLogSourceText = ControlCenterRawLogService.ReadTail();
        ApplyRawLogFilter();
    }

    private void ApplyRawLogFilter()
    {
        if (_rawLogTextBox is null)
        {
            return;
        }

        var query = _rawLogSearchBox?.Text?.Trim() ?? string.Empty;
        var filterIndex = _rawLogFilterBox?.SelectedIndex ?? 0;

        var allLines = _rawLogSourceText
            .Split(
                [Environment.NewLine],
                StringSplitOptions.RemoveEmptyEntries);

        var visible = allLines
            .Where(line =>
                string.IsNullOrWhiteSpace(query) ||
                line.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Where(line => MatchesRawLogFilter(line, filterIndex))
            .TakeLast(500)
            .ToArray();

        var nextText = string.Join(Environment.NewLine, visible);
        var wasAtEnd =
            _rawLogTextBox.CaretIndex >= Math.Max(0, _rawLogTextBox.Text.Length - 2);

        if (!string.Equals(
                _rawLogTextBox.Text,
                nextText,
                StringComparison.Ordinal))
        {
            _rawLogTextBox.Text = nextText;
            if (wasAtEnd || _rawLogTextBox.Text.Length == 0)
            {
                _rawLogTextBox.CaretIndex = _rawLogTextBox.Text.Length;
                _rawLogTextBox.ScrollToEnd();
            }
        }

        _rawLogCopyButton.IsEnabled = visible.Length > 0;
        _rawLogMetaText.Text =
            $"Gösterilen {visible.Length} / {allLines.Length} satır • son 512 KB • 2 sn canlı yenileme";
    }

    private void CopyVisibleRawLog()
    {
        if (string.IsNullOrWhiteSpace(_rawLogTextBox?.Text))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(_rawLogTextBox.Text);
            _rawLogMetaText.Text = "Görünen günlük satırları panoya kopyalandı.";
        }
        catch (Exception ex) when (
            ex is System.Runtime.InteropServices.COMException or
            InvalidOperationException)
        {
            _rawLogMetaText.Text = "Panoya kopyalama şu anda kullanılamıyor.";
            TrayLog.Write("Control Center raw log clipboard copy failed", ex);
        }
    }

    private void UpdateRawLogTimerState()
    {
        if (_rawLogRefreshTimer is null)
        {
            return;
        }

        var shouldRun =
            IsVisible &&
            _eventsExpander?.IsExpanded == true &&
            _rawLogExpander?.IsExpanded == true;

        if (shouldRun)
        {
            if (!_rawLogRefreshTimer.IsEnabled)
            {
                _rawLogRefreshTimer.Start();
            }
        }
        else
        {
            _rawLogRefreshTimer.Stop();
        }
    }

    private static bool MatchesRawLogFilter(string line, int filterIndex) =>
        filterIndex switch
        {
            1 =>
                line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("başarısız", StringComparison.CurrentCultureIgnoreCase),
            2 =>
                line.Contains("recovery", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("reconnect", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("tunnel", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("kurtarma", StringComparison.CurrentCultureIgnoreCase),
            3 =>
                line.Contains(" restart", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(" start", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(" stop", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("lifecycle", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("command", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };

    private static MediaBrush GetEventSeverityBrush(
        ControlCenterEventSeverity severity) =>
        severity switch
        {
            ControlCenterEventSeverity.Warning => AttentionBrush,
            ControlCenterEventSeverity.Error => OfflineBrush,
            _ => AccentBrush,
        };

    private static SymbolRegular GetEventSeverityIcon(
        ControlCenterEventSeverity severity) =>
        severity switch
        {
            ControlCenterEventSeverity.Warning => SymbolRegular.Warning20,
            ControlCenterEventSeverity.Error => SymbolRegular.ErrorCircle20,
            _ => SymbolRegular.Info20,
        };

    private static string FormatRelativeEventTime(DateTimeOffset occurredAtUtc)
    {
        var local = occurredAtUtc.ToLocalTime();
        var elapsed = DateTimeOffset.Now - local;

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "şimdi";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)} dk önce";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)} sa önce";
        }

        return local.ToString("dd.MM HH:mm");
    }
}