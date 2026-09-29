using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using WpfComboBox = System.Windows.Controls.ComboBox;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace Talvora.Tray;

internal sealed partial class ControlCenterWindow
{
    private readonly SemaphoreSlim _memoryRefreshGate = new(1, 1);
    private ScrollViewer _memoryScroller = null!;
    private UiTextBox _memorySearchBox = null!;
    private UiTextBox _memoryProjectBox = null!;
    private WpfComboBox _memoryScopeBox = null!;
    private WpfComboBox _memoryCategoryBox = null!;
    private WpfComboBox _memoryDateBox = null!;
    private UiButton _memoryRefreshButton = null!;
    private UiButton _memoryReembedButton = null!;
    private UiButton _memoryBackupButton = null!;
    private UiButton _memoryRestoreButton = null!;
    private TextBlock _memoryDbHealthText = null!;
    private TextBlock _memoryEmbeddingText = null!;
    private TextBlock _memoryCountsText = null!;
    private TextBlock _memoryResultMetaText = null!;
    private TextBlock _memoryStatusText = null!;
    private StackPanel _memoryResultsPanel = null!;
    private Border _memoryLoadingState = null!;
    private Border _memoryEmptyState = null!;
    private DispatcherTimer _memorySearchDebounceTimer = null!;
    private long _memoryRefreshVersion;

    private UIElement BuildMemoryView()
    {
        var content = new StackPanel
        {
            Margin = new Thickness(32, 26, 32, 36),
        };

        var backButton = new UiButton
        {
            Content = "Ana görünüm",
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowLeft20 },
            MinWidth = 124,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Style = FindStyle("TalvoraGhostButtonStyle"),
        };
        backButton.Click += (_, _) => ShowDashboard();
        content.Children.Add(backButton);

        var hero = new Grid
        {
            Margin = new Thickness(0, 20, 0, 20),
        };
        hero.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        hero.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "Talvora Hafıza",
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Kalıcı kararları, tercihleri, öğrenilen çözümleri ve proje bağlamını inceleyin.",
            Margin = new Thickness(0, 8, 24, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
        });
        hero.Children.Add(titleStack);

        var maintenanceActions = new WrapPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };

        _memoryReembedButton = new UiButton
        {
            Content = "Embeddingleri tamamla",
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowSync20 },
            MinWidth = 168,
            Margin = new Thickness(0, 0, 8, 8),
            Style = FindStyle("TalvoraSecondaryButtonStyle"),
        };
        _memoryReembedButton.Click += async (_, _) => await ReembedVisibleProjectAsync();
        maintenanceActions.Children.Add(_memoryReembedButton);

        _memoryBackupButton = new UiButton
        {
            Content = "Yedek al",
            Icon = new SymbolIcon { Symbol = SymbolRegular.DocumentText20 },
            MinWidth = 112,
            Margin = new Thickness(0, 0, 8, 8),
            Style = FindStyle("TalvoraSecondaryButtonStyle"),
        };
        _memoryBackupButton.Click += async (_, _) => await BackupMemoryAsync();
        maintenanceActions.Children.Add(_memoryBackupButton);

        _memoryRestoreButton = new UiButton
        {
            Content = "Geri yükleme hazırla",
            Icon = new SymbolIcon { Symbol = SymbolRegular.History20 },
            MinWidth = 164,
            Margin = new Thickness(0, 0, 0, 8),
            Style = FindStyle("TalvoraSecondaryButtonStyle"),
        };
        _memoryRestoreButton.Click += async (_, _) => await StageMemoryRestoreAsync();
        maintenanceActions.Children.Add(_memoryRestoreButton);

        Grid.SetColumn(maintenanceActions, 1);
        hero.Children.Add(maintenanceActions);
        content.Children.Add(hero);

        var healthPanel = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 22),
        };
        healthPanel.Children.Add(
            BuildMemoryHealthCard(
                "Veritabanı",
                "Kontrol ediliyor...",
                out _memoryDbHealthText));
        healthPanel.Children.Add(
            BuildMemoryHealthCard(
                "Semantic indeks",
                "Kontrol ediliyor...",
                out _memoryEmbeddingText));
        healthPanel.Children.Add(
            BuildMemoryHealthCard(
                "Kayıtlar",
                "Kontrol ediliyor...",
                out _memoryCountsText));
        content.Children.Add(healthPanel);

        var filterCard = new Border
        {
            Style = FindStyle("TalvoraCardStyle"),
            Margin = new Thickness(0, 0, 0, 20),
        };
        var filterBody = new StackPanel();

        var searchRow = new Grid();
        searchRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(0.75, GridUnitType.Star),
        });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        _memorySearchBox = new UiTextBox
        {
            MinWidth = 180,
            Style = FindStyle("TalvoraSearchBoxStyle"),
            PlaceholderText = "Hafızada anlam veya metin ara",
            ClearButtonEnabled = true,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Search20 },
        };
        AutomationProperties.SetName(
            _memorySearchBox,
            "Talvora hafızasında ara");
        _memorySearchBox.TextChanged += (_, _) => ScheduleMemoryRefresh();
        searchRow.Children.Add(_memorySearchBox);

        _memoryProjectBox = new UiTextBox
        {
            MinWidth = 160,
            Margin = new Thickness(12, 0, 0, 0),
            Style = FindStyle("TalvoraSearchBoxStyle"),
            PlaceholderText = "Proje yolu / kimliği",
            ClearButtonEnabled = true,
        };
        AutomationProperties.SetName(
            _memoryProjectBox,
            "Hafıza proje filtresi");
        _memoryProjectBox.TextChanged += (_, _) => ScheduleMemoryRefresh();
        Grid.SetColumn(_memoryProjectBox, 1);
        searchRow.Children.Add(_memoryProjectBox);

        _memoryRefreshButton = new UiButton
        {
            Content = "Yenile",
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowClockwise20 },
            MinWidth = 104,
            Margin = new Thickness(12, 0, 0, 0),
            Style = FindStyle("TalvoraSecondaryButtonStyle"),
        };
        AutomationProperties.SetName(
            _memoryRefreshButton,
            "Hafıza görünümünü yenile");
        _memoryRefreshButton.Click += async (_, _) => await RefreshMemoryAsync();
        Grid.SetColumn(_memoryRefreshButton, 2);
        searchRow.Children.Add(_memoryRefreshButton);
        filterBody.Children.Add(searchRow);

        var optionRow = new WrapPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
        };
        _memoryScopeBox = BuildMemoryFilterCombo(
            ["Tüm kapsamlar", "global", "user", "project", "session"],
            width: 160);
        _memoryCategoryBox = BuildMemoryFilterCombo(
            [
                "Tüm kategoriler",
                "decision",
                "preference",
                "lesson",
                "error",
                "solution",
                "architecture",
                "workflow",
                "todo",
                "fact",
            ],
            width: 184);
        _memoryDateBox = BuildMemoryFilterCombo(
            ["Tüm tarihler", "Son 24 saat", "Son 7 gün", "Son 30 gün", "Son 180 gün"],
            width: 160);
        _memoryCategoryBox.Margin = new Thickness(10, 0, 0, 0);
        _memoryDateBox.Margin = new Thickness(10, 0, 0, 0);
        _memoryScopeBox.SelectionChanged += (_, _) => ScheduleMemoryRefresh(immediate: true);
        _memoryCategoryBox.SelectionChanged += (_, _) => ScheduleMemoryRefresh(immediate: true);
        _memoryDateBox.SelectionChanged += (_, _) => ScheduleMemoryRefresh(immediate: true);
        optionRow.Children.Add(_memoryScopeBox);
        optionRow.Children.Add(_memoryCategoryBox);
        optionRow.Children.Add(_memoryDateBox);
        filterBody.Children.Add(optionRow);

        _memoryStatusText = new TextBlock
        {
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = TertiaryTextBrush,
            FontSize = 11,
            Text = "Hafıza yerel Talvora MCP üzerinden okunur; ham SQLite dosyasına UI erişmez.",
            TextWrapping = TextWrapping.Wrap,
        };
        filterBody.Children.Add(_memoryStatusText);
        filterCard.Child = filterBody;
        content.Children.Add(filterCard);

        var resultHeader = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12),
        };
        resultHeader.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        resultHeader.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });
        resultHeader.Children.Add(new TextBlock
        {
            Text = "Hafıza kayıtları",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
        });
        _memoryResultMetaText = new TextBlock
        {
            Foreground = TertiaryTextBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_memoryResultMetaText, 1);
        resultHeader.Children.Add(_memoryResultMetaText);
        content.Children.Add(resultHeader);

        _memoryLoadingState = new Border
        {
            Style = FindStyle("TalvoraSubtleCardStyle"),
            Child = new TextBlock
            {
                Text = "Hafıza okunuyor...",
                Foreground = SecondaryTextBrush,
                FontSize = 13,
            },
        };
        content.Children.Add(_memoryLoadingState);

        _memoryEmptyState = new Border
        {
            Style = FindStyle("TalvoraSubtleCardStyle"),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "Bu filtrelerle eşleşen aktif hafıza kaydı yok.",
                Foreground = SecondaryTextBrush,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
            },
        };
        content.Children.Add(_memoryEmptyState);

        _memoryResultsPanel = new StackPanel();
        content.Children.Add(_memoryResultsPanel);

        _memorySearchDebounceTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(350),
            IsEnabled = false,
        };
        _memorySearchDebounceTimer.Tick += OnMemorySearchDebounceTick;

        return content;
    }

    private WpfComboBox BuildMemoryFilterCombo(
        IEnumerable<string> items,
        double width)
    {
        var combo = new WpfComboBox
        {
            Width = width,
            Height = 40,
            SelectedIndex = 0,
            Style = FindStyle("TalvoraFilterComboBoxStyle"),
        };
        foreach (var item in items)
        {
            combo.Items.Add(item);
        }
        return combo;
    }

    private Border BuildMemoryHealthCard(
        string label,
        string initialValue,
        out TextBlock valueText)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = TertiaryTextBrush,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
        });
        valueText = new TextBlock
        {
            Text = initialValue,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = PrimaryTextBrush,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        body.Children.Add(valueText);
        return new Border
        {
            Width = 330,
            MinHeight = 78,
            Margin = new Thickness(0, 0, 12, 10),
            Style = FindStyle("TalvoraSubtleCardStyle"),
            Child = body,
        };
    }

    private async Task ShowMemoryInspectorAsync()
    {
        _selectedMcp = null;
        _refreshTimer.Stop();
        _dashboardScroller.Visibility = Visibility.Collapsed;
        _detailScroller.Visibility = Visibility.Collapsed;
        _memoryScroller.Visibility = Visibility.Visible;
        _memoryScroller.ScrollToTop();
        UpdateRawLogTimerState();
        await RefreshMemoryAsync();
    }

    private void ScheduleMemoryRefresh(bool immediate = false)
    {
        if (_memoryScroller?.Visibility != Visibility.Visible ||
            _lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        Interlocked.Increment(ref _memoryRefreshVersion);
        _memorySearchDebounceTimer.Stop();
        if (immediate)
        {
            _ = RefreshMemoryAsync();
            return;
        }
        _memorySearchDebounceTimer.Start();
    }

    private async void OnMemorySearchDebounceTick(object? sender, EventArgs e)
    {
        _memorySearchDebounceTimer.Stop();
        await RefreshMemoryAsync();
    }

    private async Task RefreshMemoryAsync()
    {
        if (_lifetimeCts.IsCancellationRequested ||
            _memoryScroller.Visibility != Visibility.Visible)
        {
            return;
        }

        var requestVersion = Interlocked.Increment(ref _memoryRefreshVersion);
        if (!await _memoryRefreshGate.WaitAsync(0))
        {
            return;
        }

        _memoryRefreshButton.IsEnabled = false;
        _memoryReembedButton.IsEnabled = false;
        _memoryLoadingState.Visibility = Visibility.Visible;
        _memoryEmptyState.Visibility = Visibility.Collapsed;
        _memoryStatusText.Text = "Hafıza ve semantic indeks kontrol ediliyor...";
        _memoryStatusText.Foreground = SecondaryTextBrush;

        try
        {
            var query = _memorySearchBox.Text.Trim();
            var project = EmptyMemoryFilter(_memoryProjectBox.Text);
            var scope = _memoryScopeBox.SelectedIndex <= 0
                ? null
                : _memoryScopeBox.SelectedItem?.ToString();
            var category = _memoryCategoryBox.SelectedIndex <= 0
                ? null
                : _memoryCategoryBox.SelectedItem?.ToString();
            var updatedAfterUtc = GetMemoryUpdatedAfterUtc();

            var diagnosticsTask =
                ControlCenterMemoryService.DiagnosticsAsync(_lifetimeCts.Token);
            var embeddingTask =
                ControlCenterMemoryService.EmbeddingStatusAsync(
                    project,
                    _lifetimeCts.Token);
            var restoreStatusTask =
                ControlCenterMemoryService.RestoreStatusAsync(
                    _lifetimeCts.Token);

            IReadOnlyList<ControlCenterMemorySearchHit> searchHits = [];
            IReadOnlyList<ControlCenterMemoryItem> items;
            if (string.IsNullOrWhiteSpace(query))
            {
                var listTask = ControlCenterMemoryService.ListAsync(
                    scope,
                    project,
                    category,
                    updatedAfterUtc,
                    null,
                    150,
                    _lifetimeCts.Token);
                await Task.WhenAll(
                    diagnosticsTask,
                    embeddingTask,
                    restoreStatusTask,
                    listTask);
                items = (await listTask).Items;
            }
            else
            {
                var searchTask = ControlCenterMemoryService.SearchAsync(
                    query,
                    scope,
                    project,
                    category,
                    100,
                    _lifetimeCts.Token);
                await Task.WhenAll(
                    diagnosticsTask,
                    embeddingTask,
                    restoreStatusTask,
                    searchTask);
                searchHits = (await searchTask).Items;
                items = searchHits
                    .Select(hit => hit.Item)
                    .Where(item =>
                        updatedAfterUtc is null ||
                        item.UpdatedAtUtc >= updatedAfterUtc.Value)
                    .ToArray();
            }

            if (requestVersion != Volatile.Read(ref _memoryRefreshVersion))
            {
                return;
            }

            var diagnostics = await diagnosticsTask;
            var embedding = await embeddingTask;
            var restoreStatus = await restoreStatusTask;
            UpdateMemoryHealth(
                diagnostics,
                embedding,
                restoreStatus);
            RenderMemoryResults(items, searchHits);
            _memoryResultMetaText.Text =
                $"{items.Count} kayıt • {DateTime.Now:HH:mm:ss}";
            _memoryStatusText.Text = string.IsNullOrWhiteSpace(query)
                ? "En son güncellenen aktif kayıtlar gösteriliyor."
                : "Sonuçlar FTS5 + semantic hybrid arama ile sıralandı.";
            _memoryStatusText.Foreground = TertiaryTextBrush;
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (
            ex is TimeoutException or
            InvalidOperationException or
            System.IO.InvalidDataException or
            HttpRequestException)
        {
            TrayLog.Write("Control Center memory refresh failed", ex);
            _memoryResultsPanel.Children.Clear();
            _memoryEmptyState.Visibility = Visibility.Visible;
            if (_memoryEmptyState.Child is TextBlock emptyText)
            {
                emptyText.Text =
                    "Hafıza şu anda okunamıyor. Talvora servisi hazır olduğunda Yenile ile tekrar deneyin.";
            }
            _memoryStatusText.Text =
                ControlCenterUserMessage.ForOperation(ex, "Hafıza yenileme");
            _memoryStatusText.Foreground = OfflineBrush;
        }
        finally
        {
            _memoryLoadingState.Visibility = Visibility.Collapsed;
            _memoryRefreshButton.IsEnabled = true;
            _memoryReembedButton.IsEnabled = true;
            _memoryRefreshGate.Release();

            if (requestVersion != Volatile.Read(ref _memoryRefreshVersion) &&
                _memoryScroller.Visibility == Visibility.Visible &&
                !_lifetimeCts.IsCancellationRequested)
            {
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    () => _ = RefreshMemoryAsync());
            }
        }
    }

    private void UpdateMemoryHealth(
        ControlCenterMemoryDiagnosticsResult diagnostics,
        ControlCenterMemoryEmbeddingStatusResult embedding,
        ControlCenterMemoryRestoreStatusResult? restoreStatus = null)
    {
        _memoryDbHealthText.Text =
            $"{FormatBytes(diagnostics.DatabaseBytes)} • {diagnostics.Integrity}" +
            (restoreStatus?.Pending == true
                ? " • geri yükleme bekliyor"
                : string.Empty);
        _memoryDbHealthText.Foreground =
            restoreStatus?.Pending == true
                ? AttentionBrush
                : string.Equals(
                diagnostics.Integrity,
                "ok",
                StringComparison.OrdinalIgnoreCase)
                ? ReadyBrush
                : OfflineBrush;

        _memoryEmbeddingText.Text = embedding.Available
            ? embedding.MissingOrStaleEmbeddings == 0
                ? $"Hazır • {embedding.CurrentEmbeddings}/{embedding.ActiveMemories}"
                : $"{embedding.MissingOrStaleEmbeddings} kayıt güncellenecek"
            : "Semantic model kullanılamıyor • FTS5 aktif";
        _memoryEmbeddingText.Foreground = embedding.Available
            ? embedding.MissingOrStaleEmbeddings == 0
                ? ReadyBrush
                : AttentionBrush
            : AttentionBrush;

        _memoryCountsText.Text =
            $"{diagnostics.ActiveCount} aktif • {diagnostics.ExpiredCount} süresi dolmuş • " +
            $"{diagnostics.SupersededCount} eski";
        _memoryCountsText.Foreground = PrimaryTextBrush;
    }

    private void RenderMemoryResults(
        IReadOnlyList<ControlCenterMemoryItem> items,
        IReadOnlyList<ControlCenterMemorySearchHit> searchHits)
    {
        _memoryResultsPanel.Children.Clear();
        _memoryEmptyState.Visibility = items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (items.Count == 0)
        {
            return;
        }

        var scores = searchHits.ToDictionary(
            hit => hit.Item.Id,
            StringComparer.Ordinal);
        foreach (var item in items)
        {
            scores.TryGetValue(item.Id, out var score);
            _memoryResultsPanel.Children.Add(
                CreateMemoryCard(item, score));
        }
    }

    private UIElement CreateMemoryCard(
        ControlCenterMemoryItem item,
        ControlCenterMemorySearchHit? score)
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = PrimaryTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        header.Children.Add(new TextBlock
        {
            Text =
                $"{GetMemoryCategoryLabel(item.Category)} • {GetMemoryScopeLabel(item.Scope)}" +
                (string.IsNullOrWhiteSpace(item.Source)
                    ? string.Empty
                    : $" • {item.Source}"),
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 11,
        });

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = item.Content,
            Foreground = PrimaryTextBrush,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });

        var meta = new TextBlock
        {
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = TertiaryTextBrush,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Text =
                $"Güven {item.Confidence:P0} • Önem {item.Importance:P0} • " +
                $"Saklama {item.RetentionClass} • Güncellendi {item.UpdatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}",
        };
        body.Children.Add(meta);

        if (!string.IsNullOrWhiteSpace(item.Project))
        {
            body.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = TertiaryTextBrush,
                FontSize = 10,
                Text = $"Proje: {item.Project}",
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (!string.IsNullOrWhiteSpace(item.SourceReference))
        {
            body.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = TertiaryTextBrush,
                FontSize = 10,
                Text = $"Kaynak: {item.SourceReference}",
                TextWrapping = TextWrapping.Wrap,
            });
        }

        body.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = TertiaryTextBrush,
            FontSize = 10,
            Text = $"Kimlik: {item.Id}",
            TextWrapping = TextWrapping.Wrap,
        });

        if (score is not null)
        {
            body.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = TertiaryTextBrush,
                FontSize = 10,
                Text =
                    $"Teknik skor • semantic {FormatScore(score.SemanticScore)} • hybrid {FormatScore(score.HybridScore)}",
            });
        }

        body.Children.Add(BuildMemoryActionBar(item));

        return new CardExpander
        {
            Margin = new Thickness(0, 0, 0, 12),
            Header = header,
            IsExpanded = false,
            Background = SurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            ContentPadding = new Thickness(20, 10, 20, 20),
            Content = body,
        };
    }

    private async Task ReembedVisibleProjectAsync()
    {
        if (_lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        _memoryReembedButton.IsEnabled = false;
        _memoryStatusText.Text = "Eksik veya eski embeddingler güncelleniyor...";
        try
        {
            var result = await ControlCenterMemoryService.ReembedAsync(
                EmptyMemoryFilter(_memoryProjectBox.Text),
                batchSize: 100,
                _lifetimeCts.Token);
            _memoryStatusText.Text =
                $"{result.Embedded} embedding güncellendi • {result.Remaining} kaldı • {result.Failed} hata.";
            _memoryStatusText.Foreground =
                result.Failed == 0 ? ReadyBrush : AttentionBrush;
            await RefreshMemoryAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (
            ex is TimeoutException or
            InvalidOperationException or
            System.IO.InvalidDataException or
            HttpRequestException)
        {
            TrayLog.Write("Control Center memory re-embed failed", ex);
            _memoryStatusText.Text =
                ControlCenterUserMessage.ForOperation(ex, "Embedding güncelleme");
            _memoryStatusText.Foreground = OfflineBrush;
        }
        finally
        {
            _memoryReembedButton.IsEnabled = true;
        }
    }

    private DateTimeOffset? GetMemoryUpdatedAfterUtc() =>
        _memoryDateBox.SelectedIndex switch
        {
            1 => DateTimeOffset.UtcNow.AddDays(-1),
            2 => DateTimeOffset.UtcNow.AddDays(-7),
            3 => DateTimeOffset.UtcNow.AddDays(-30),
            4 => DateTimeOffset.UtcNow.AddDays(-180),
            _ => null,
        };

    private static string? EmptyMemoryFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetMemoryCategoryLabel(string category) =>
        category switch
        {
            "decision" => "Karar",
            "preference" => "Tercih",
            "lesson" => "Öğrenilen çözüm",
            "error" => "Hata",
            "solution" => "Çözüm",
            "architecture" => "Mimari",
            "workflow" => "İş akışı",
            "todo" => "Görev",
            "fact" => "Bilgi",
            _ => category,
        };

    private static string GetMemoryScopeLabel(string scope) =>
        scope switch
        {
            "global" => "Genel",
            "user" => "Kullanıcı",
            "project" => "Proje",
            "session" => "Oturum",
            _ => scope,
        };

    private static string FormatScore(double? value) =>
        value is null
            ? "—"
            : value.Value.ToString("0.000", CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        if (bytes < 1024L * 1024L)
        {
            return $"{bytes / 1024d:0.0} KB";
        }
        if (bytes < 1024L * 1024L * 1024L)
        {
            return $"{bytes / (1024d * 1024d):0.0} MB";
        }
        return $"{bytes / (1024d * 1024d * 1024d):0.00} GB";
    }

    internal Task RunMemoryInspectorVisualSmokeScenarioAsync()
    {
        _refreshTimer.Stop();
        _dashboardScroller.Visibility = Visibility.Collapsed;
        _detailScroller.Visibility = Visibility.Collapsed;
        _memoryScroller.Visibility = Visibility.Visible;
        _memoryLoadingState.Visibility = Visibility.Collapsed;

        var sample = new ControlCenterMemoryItem(
            "memory-smoke-001", "project", @"C:\Smoke\Talvora", null,
            "decision", "Memory Inspector görsel doğrulama",
            "Bu kayıt yalnız Control Center görsel smoke senaryosunda kart, metadata ve teknik detayların render edilmesini doğrular.",
            0.9, 0.95, "smoke", "control-center-visual",
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow,
            null, null, "durable", "control-center-memory-smoke");
        RenderMemoryResults(
            [sample],
            [new ControlCenterMemorySearchHit(sample, 0.91, 0.75, 0.72, 0.91)]);
        UpdateMemoryHealth(
            new ControlCenterMemoryDiagnosticsResult(
                @"C:\ProgramData\Talvora\memory\talvora-memory.db",
                1_572_864, "ok", 12, 10, 1, 1, 0),
            new ControlCenterMemoryEmbeddingStatusResult(
                true,
                "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2",
                "q8-smoke", 384, null, 10, 10, 0));
        _memoryResultMetaText.Text = "1 kayıt • görsel smoke";
        _memoryStatusText.Text = "Memory Inspector görsel doğrulama modu.";
        _memoryStatusText.Foreground = ReadyBrush;
        UpdateLayout();

        if (_memoryScroller.Visibility != Visibility.Visible ||
            _memoryResultsPanel.Children.Count != 1 ||
            string.IsNullOrWhiteSpace(_memoryDbHealthText.Text) ||
            string.IsNullOrWhiteSpace(_memoryEmbeddingText.Text))
        {
            throw new InvalidOperationException(
                "Memory Inspector visual tree did not initialize.");
        }

        return Task.CompletedTask;
    }

    internal void EndMemoryInspectorVisualSmokeScenario()
    {
        _memoryResultsPanel.Children.Clear();
        ShowDashboard();
    }
}
