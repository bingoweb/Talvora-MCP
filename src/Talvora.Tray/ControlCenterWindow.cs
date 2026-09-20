using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using MediaColor = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using ComboBox = System.Windows.Controls.ComboBox;
using Image = System.Windows.Controls.Image;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using UiPasswordBox = Wpf.Ui.Controls.PasswordBox;
using HAlign = System.Windows.HorizontalAlignment;
using VAlign = System.Windows.VerticalAlignment;
using WpfCursors = System.Windows.Input.Cursors;

namespace Talvora.Tray;

internal sealed partial class ControlCenterWindow : FluentWindow
{
    private static readonly SolidColorBrush BackgroundBrush = CreateBrush(16, 18, 22);
    private static readonly SolidColorBrush SurfaceBrush = CreateBrush(22, 25, 31);
    private static readonly SolidColorBrush RaisedSurfaceBrush = CreateBrush(27, 31, 39);
    private static readonly SolidColorBrush HoverSurfaceBrush = CreateBrush(32, 37, 46);
    private static readonly SolidColorBrush CardBorderBrush = CreateBrush(41, 47, 57);
    private static readonly SolidColorBrush StrongBorderBrush = CreateBrush(52, 60, 72);
    private static readonly SolidColorBrush PrimaryTextBrush = CreateBrush(245, 247, 250);
    private static readonly SolidColorBrush SecondaryTextBrush = CreateBrush(162, 173, 189);
    private static readonly SolidColorBrush TertiaryTextBrush = CreateBrush(117, 128, 145);
    private static readonly SolidColorBrush AccentBrush = CreateBrush(117, 167, 255);
    private static readonly SolidColorBrush ReadyBrush = CreateBrush(72, 199, 142);
    private static readonly SolidColorBrush AttentionBrush = CreateBrush(240, 184, 90);
    private static readonly SolidColorBrush OfflineBrush = CreateBrush(235, 104, 119);
    private static readonly SolidColorBrush CheckingBrush = CreateBrush(117, 167, 255);
    private static readonly SolidColorBrush ReadySoftBrush = CreateBrush(34, 72, 199, 142);
    private static readonly SolidColorBrush AttentionSoftBrush = CreateBrush(36, 240, 184, 90);
    private static readonly SolidColorBrush OfflineSoftBrush = CreateBrush(36, 235, 104, 119);
    private static readonly SolidColorBrush CheckingSoftBrush = CreateBrush(34, 117, 167, 255);

    private readonly DispatcherTimer _refreshTimer;
    private readonly bool _smokeMode;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _windowPlacementSaveGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();

    private TitleBar _windowTitleBar = null!;
    private Grid _headerGrid = null!;
    private StackPanel _headerStatusStack = null!;
    private Border _headerStatusCard = null!;
    private TextBlock _dashboardSectionMeta = null!;
    private Border _setupCard = null!;
    private TextBlock _setupTitleText = null!;
    private TextBlock _setupDetailText = null!;
    private UiTextBox _setupDevTunnelIdBox = null!;
    private UiTextBox _setupAdminTunnelIdBox = null!;
    private UiPasswordBox _setupAdminKeyBox = null!;
    private UiButton _setupActionButton = null!;
    private ControlCenterSetupState? _setupState;
    private DispatcherTimer _windowPlacementSaveTimer = null!;
    private bool _restoringWindowPlacement;
    private long _windowPlacementSaveVersion;
    private TextBlock _healthSummaryText = null!;
    private Border _healthSummaryDot = null!;
    private TextBlock _technicalSummaryText = null!;
    private TextBlock _lastRefreshText = null!;
    private UiTextBox _searchBox = null!;
    private ComboBox _filterBox = null!;
    private UiButton _refreshButton = null!;
    private WrapPanel _cardsPanel = null!;
    private Border _loadingState = null!;
    private TextBlock _emptyState = null!;
    private ScrollViewer _dashboardScroller = null!;
    private ScrollViewer _detailScroller = null!;
    private TextBlock _detailTitle = null!;
    private TextBlock _detailDescription = null!;
    private TextBlock _detailStatus = null!;
    private TextBlock _detailVersion = null!;
    private TextBlock _detailEndpoint = null!;
    private TextBlock _detailTransport = null!;
    private TextBlock _detailBrowserChannel = null!;
    private TextBlock _detailProfileMode = null!;
    private TextBlock _detailProfilePath = null!;
    private TextBlock _detailRuntimeStatePath = null!;
    private TextBlock _detailBrowserSmokeStatePath = null!;
    private TextBlock _detailTunnel = null!;
    private TextBlock _detailTunnelId = null!;
    private TextBlock _detailTunnelConfig = null!;
    private TextBlock _detailTunnelStateRoot = null!;
    private TextBlock _detailTechnicalComponents = null!;
    private StackPanel _componentList = null!;
    private StackPanel _detailRecentEventsList = null!;
    private UiButton _detailPrimaryActionButton = null!;
    private UiButton _detailStartButton = null!;
    private UiButton _detailStopButton = null!;
    private UiButton _detailRestartButton = null!;
    private Border _detailOperationBanner = null!;
    private TextBlock _detailOperationText = null!;
    private CardExpander _technicalDetailsExpander = null!;

    private ControlCenterDashboardSnapshot? _snapshot;
    private ManagedMcpDashboardState? _selectedMcp;
    private bool _detailOperationInProgress;
    private bool _applicationExitRequested;

    public ControlCenterWindow(bool smokeMode = false)
    {
        _smokeMode = smokeMode;
        Title = "Talvora Yönetim Merkezi";
        Width = 1180;
        Height = 760;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = smokeMode
            ? WindowStartupLocation.Manual
            : WindowStartupLocation.CenterScreen;

        if (smokeMode)
        {
            Left = -32_000;
            Top = -32_000;
            ShowInTaskbar = false;
            ShowActivated = false;
        }
        WindowCornerPreference = WindowCornerPreference.Round;
        WindowBackdropType = WindowBackdropType.None;
        ExtendsContentIntoTitleBar = true;
        ResizeMode = ResizeMode.CanResize;
        Background = BackgroundBrush;
        Foreground = PrimaryTextBrush;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Content = BuildContent();
        InitializeWindowPlacementPersistence();

        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        IsVisibleChanged += OnIsVisibleChanged;
        SizeChanged += (_, _) =>
        {
            UpdateCardWidths();
            UpdateHeaderLayout();
            ScheduleWindowPlacementSave();
        };
        StateChanged += (_, _) =>
        {
            UpdateWindowStateVisuals();
            ScheduleWindowPlacementSave();
        };
        SourceInitialized += (_, _) =>
        {
            if (!smokeMode)
            {
                RestoreWindowPlacement();
            }
        };
        LocationChanged += (_, _) => ScheduleWindowPlacementSave();
        Loaded += (_, _) =>
        {
            UpdateHeaderLayout();
            UpdateWindowStateVisuals();
        };

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(8),
            DispatcherPriority.Background,
            async (_, _) => await RefreshDashboardAsync(),
            Dispatcher)
        {
            IsEnabled = false,
        };

    }

    public void BringToForeground()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        _ = RefreshDashboardAsync();
    }

    public void PrepareForApplicationExit()
    {
        _windowPlacementSaveTimer.Stop();
        _rawLogRefreshTimer?.Stop();
        SaveWindowPlacementAsync().GetAwaiter().GetResult();
        _applicationExitRequested = true;
        _refreshTimer.Stop();
        _lifetimeCts.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _windowTitleBar = BuildWindowTitleBar();
        root.Children.Add(_windowTitleBar);

        var header = BuildHeader();
        Grid.SetRow(header, 1);
        root.Children.Add(header);

        var bodyHost = new Grid();

        _dashboardScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildDashboard(),
        };
        bodyHost.Children.Add(_dashboardScroller);

        _detailScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed,
            Content = BuildDetailView(),
        };
        bodyHost.Children.Add(_detailScroller);

        Grid.SetRow(bodyHost, 2);
        root.Children.Add(bodyHost);

        var footer = new Border
        {
            Padding = new Thickness(28, 12, 28, 12),
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new TextBlock
            {
                Text = "Talvora  •  Yerel MCP yönetimi",
                Foreground = TertiaryTextBrush,
                FontSize = 11,
            },
        };
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private TitleBar BuildWindowTitleBar()
    {
        var titleBar = new TitleBar
        {
            Title = "Talvora Yönetim Merkezi",
            Height = 44,
            Padding = new Thickness(14, 0, 0, 0),
            Background = BackgroundBrush,
            Foreground = SecondaryTextBrush,
            ButtonsForeground = PrimaryTextBrush,
            ButtonsBackground = HoverSurfaceBrush,
            ShowMinimize = true,
            ShowMaximize = true,
            ShowClose = true,
            ShowHelp = false,
            CanMaximize = true,
            ForceShutdown = false,
            CloseWindowByDoubleClickOnIcon = false,
        };

        titleBar.Icon = new SymbolIcon
        {
            Symbol = SymbolRegular.Server20,
            Foreground = AccentBrush,
        };

        return titleBar;
    }

    private UIElement BuildHeader()
    {
        _headerGrid = new Grid();
        _headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        var titleStack = new StackPanel
        {
            VerticalAlignment = VAlign.Center,
        };
        titleStack.Children.Add(new TextBlock
        {
            Text = "TALVORA",
            Foreground = AccentBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Yönetim Merkezi",
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Yerel MCP servisleri, tüneller ve durumlar tek görünümde.",
            Margin = new Thickness(0, 7, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 14,
        });
        _headerGrid.Children.Add(titleStack);

        var statusGrid = new Grid();
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        _healthSummaryDot = new Border
        {
            Width = 9,
            Height = 9,
            Margin = new Thickness(0, 5, 11, 0),
            VerticalAlignment = VAlign.Top,
            Background = CheckingBrush,
            CornerRadius = new CornerRadius(5),
        };
        statusGrid.Children.Add(_healthSummaryDot);

        _headerStatusStack = new StackPanel
        {
            MinWidth = 250,
        };

        _healthSummaryText = new TextBlock
        {
            Text = "Durum kontrol ediliyor...",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
        };
        _headerStatusStack.Children.Add(_healthSummaryText);

        _technicalSummaryText = new TextBlock
        {
            Text = "MCP bilgileri hazırlanıyor",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 12,
        };
        _headerStatusStack.Children.Add(_technicalSummaryText);

        _lastRefreshText = new TextBlock
        {
            Text = string.Empty,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = TertiaryTextBrush,
            FontSize = 11,
        };
        _headerStatusStack.Children.Add(_lastRefreshText);

        Grid.SetColumn(_headerStatusStack, 1);
        statusGrid.Children.Add(_headerStatusStack);

        _headerStatusCard = new Border
        {
            Padding = new Thickness(14, 11, 14, 11),
            Background = RaisedSurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = statusGrid,
        };

        Grid.SetColumn(_headerStatusCard, 1);
        _headerGrid.Children.Add(_headerStatusCard);

        return new Border
        {
            Padding = new Thickness(28, 22, 28, 18),
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _headerGrid,
        };
    }

    private UIElement BuildDashboard()
    {
        var content = new StackPanel
        {
            Margin = new Thickness(28, 24, 28, 28),
        };

        _setupCard = BuildSetupCard();
        content.Children.Add(_setupCard);

        var sectionHeader = new Grid
        {
            Margin = new Thickness(0, 0, 0, 14),
        };
        sectionHeader.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        sectionHeader.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        var sectionTitleStack = new StackPanel();
        sectionTitleStack.Children.Add(new TextBlock
        {
            Text = "Yönetilen MCP'ler",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
        });
        sectionTitleStack.Children.Add(new TextBlock
        {
            Text = "Servis, MCP sunucusu ve tünel zincirlerini tek yerden yönetin.",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 12,
        });
        sectionHeader.Children.Add(sectionTitleStack);

        _dashboardSectionMeta = new TextBlock
        {
            Text = "Durumlar hazırlanıyor",
            VerticalAlignment = VAlign.Center,
            Foreground = TertiaryTextBrush,
            FontSize = 11,
        };
        Grid.SetColumn(_dashboardSectionMeta, 1);
        sectionHeader.Children.Add(_dashboardSectionMeta);
        content.Children.Add(sectionHeader);

        var controlsGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 18),
        };
        controlsGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        controlsGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });
        controlsGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        var searchStack = new StackPanel();

        _searchBox = new UiTextBox
        {
            MinWidth = 300,
            Style = FindStyle("TalvoraSearchBoxStyle"),
            PlaceholderText = "MCP ara",
            ClearButtonEnabled = true,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Search20 },
            ToolTip = "Ad veya açıklamaya göre MCP ara",
        };
        _searchBox.TextChanged += (_, _) => ApplyDashboardFilter();
        searchStack.Children.Add(_searchBox);
        controlsGrid.Children.Add(searchStack);

        _filterBox = new ComboBox
        {
            Width = 184,
            Height = 44,
            Margin = new Thickness(12, 0, 0, 0),
            SelectedIndex = 0,
            Visibility = Visibility.Collapsed,
            Style = FindStyle("TalvoraFilterComboBoxStyle"),
        };
        _filterBox.Items.Add("Tümü");
        _filterBox.Items.Add("Dikkat isteyenler");
        _filterBox.Items.Add("Hazır");
        _filterBox.SelectionChanged += (_, _) => ApplyDashboardFilter();
        Grid.SetColumn(_filterBox, 1);
        controlsGrid.Children.Add(_filterBox);

        _refreshButton = new UiButton
        {
            Content = "Yenile",
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowClockwise20 },
            MinWidth = 104,
            Margin = new Thickness(12, 0, 0, 0),
            Style = FindStyle("TalvoraSecondaryButtonStyle"),
            Cursor = WpfCursors.Hand,
        };
        _refreshButton.Click += async (_, _) => await RefreshDashboardAsync();
        Grid.SetColumn(_refreshButton, 2);
        controlsGrid.Children.Add(_refreshButton);

        content.Children.Add(controlsGrid);

        _loadingState = new Border
        {
            Style = FindStyle("TalvoraSubtleCardStyle"),
            Child = new TextBlock
            {
                Text = "MCP durumları kontrol ediliyor...",
                Foreground = SecondaryTextBrush,
                FontSize = 14,
            },
        };
        content.Children.Add(_loadingState);

        _emptyState = new TextBlock
        {
            Text = string.Empty,
            Margin = new Thickness(0, 24, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        content.Children.Add(_emptyState);

        _cardsPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
        };
        content.Children.Add(_cardsPanel);

        content.Children.Add(BuildEventsPanel());

        return content;
    }

    private async Task RefreshDashboardAsync()
    {
        if (_lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        if (!await _refreshGate.WaitAsync(0))
        {
            return;
        }

        _refreshButton.IsEnabled = false;
        if (_snapshot is null)
        {
            _loadingState.Visibility = Visibility.Visible;
        }

        try
        {
            var snapshot = await ControlCenterDashboardService.GetSnapshotAsync(
                _lifetimeCts.Token);
            _snapshot = snapshot;

            _healthSummaryText.Text = snapshot.HealthSummary;
            _healthSummaryText.Foreground = GetSummaryBrush(snapshot);
            _healthSummaryDot.Background = GetSummaryBrush(snapshot);
            _technicalSummaryText.Text = snapshot.TechnicalSummary;
            _dashboardSectionMeta.Text =
                $"{snapshot.Mcps.Count} MCP • {snapshot.ReadyCount} hazır";
            _lastRefreshText.Text = $"Son kontrol {DateTime.Now:HH:mm:ss}";
            _filterBox.Visibility = snapshot.Mcps.Count >= 4
                ? Visibility.Visible
                : Visibility.Collapsed;

            ApplyDashboardFilter();
            RefreshSetupCard();
            RefreshEventsPanel();

            if (_selectedMcp is not null)
            {
                var refreshedSelection = snapshot.Mcps.FirstOrDefault(state =>
                    string.Equals(
                        state.Registration.Id,
                        _selectedMcp.Registration.Id,
                        StringComparison.OrdinalIgnoreCase));

                if (refreshedSelection is not null)
                {
                    _selectedMcp = refreshedSelection;
                    UpdateDetailSummary(refreshedSelection);
                    UpdateDetailActionState(refreshedSelection);
                    await RefreshDetailAsync();
                }
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Control Center dashboard refresh failed", ex);
            _healthSummaryText.Text = "Durum alınamadı";
            _healthSummaryText.Foreground = OfflineBrush;
            _healthSummaryDot.Background = OfflineBrush;
            _technicalSummaryText.Text = "Otomatik olarak yeniden denenecek.";
            _lastRefreshText.Text = string.Empty;
            _cardsPanel.Children.Clear();
            _emptyState.Text = "MCP bilgileri şu anda alınamıyor.";
            _emptyState.Visibility = Visibility.Visible;
        }
        finally
        {
            _loadingState.Visibility = Visibility.Collapsed;
            _refreshButton.IsEnabled = true;
            _refreshGate.Release();
        }
    }

    private void ApplyDashboardFilter()
    {
        if (_snapshot is null || _cardsPanel is null)
        {
            return;
        }

        var query = _searchBox.Text.Trim();
        var selectedFilter = _filterBox.SelectedIndex;

        var visible = _snapshot.Mcps
            .Where(state =>
                string.IsNullOrWhiteSpace(query) ||
                state.Registration.DisplayName.Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase) ||
                state.Registration.Description.Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase))
            .Where(state => selectedFilter switch
            {
                1 => state.Health != ControlCenterHealthState.Ready,
                2 => state.Health == ControlCenterHealthState.Ready,
                _ => true,
            })
            .ToList();

        _cardsPanel.Children.Clear();
        foreach (var state in visible)
        {
            _cardsPanel.Children.Add(CreateMcpCard(state));
        }

        _emptyState.Text = _snapshot.Mcps.Count == 0
            ? "Henüz yönetilen bir MCP bulunamadı."
            : "Aramanızla eşleşen MCP yok.";
        _emptyState.Visibility = visible.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateCardWidths();
    }

    private Border CreateMcpCard(ManagedMcpDashboardState state)
    {
        var statusBrush = GetHealthBrush(state.Health);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var logo = CreateLogo(state.Registration);
        header.Children.Add(logo);

        var titleStack = new StackPanel
        {
            Margin = new Thickness(14, 0, 12, 0),
            VerticalAlignment = VAlign.Center,
        };
        titleStack.Children.Add(new TextBlock
        {
            Text = state.Registration.DisplayName,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = state.Registration.Description,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 38,
        });
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(titleStack);

        var statusPill = CreateStatusPill(state.Health, state.StatusText);
        Grid.SetColumn(statusPill, 2);
        header.Children.Add(statusPill);

        root.Children.Add(header);

        var detail = new TextBlock
        {
            Text = state.Detail,
            Margin = new Thickness(0, 16, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 38,
        };
        Grid.SetRow(detail, 1);
        root.Children.Add(detail);

        var actionRow = new Grid
        {
            Margin = new Thickness(0, 14, 0, 0),
        };
        actionRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        actionRow.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        var hint = CreateIconLabel(
            SymbolRegular.ChevronRight20,
            "Ayrıntılar",
            TertiaryTextBrush,
            12);
        hint.VerticalAlignment = VAlign.Center;
        actionRow.Children.Add(hint);

        var contextualAction = GetCardContextAction(state);
        var actionButton = new UiButton
        {
            Content = contextualAction.Text,
            Icon = new SymbolIcon { Symbol = contextualAction.Icon },
            MinHeight = 32,
            Padding = new Thickness(11, 4, 11, 4),
            Style = FindStyle(
                contextualAction.Primary
                    ? "TalvoraPrimaryButtonStyle"
                    : "TalvoraSecondaryButtonStyle"),
            Cursor = WpfCursors.Hand,
        };
        actionButton.Click += async (_, _) =>
            await ExecuteCardContextActionAsync(state, actionButton);
        Grid.SetColumn(actionButton, 1);
        actionRow.Children.Add(actionButton);

        Grid.SetRow(actionRow, 2);
        root.Children.Add(actionRow);

        var card = new Border
        {
            Margin = new Thickness(0, 0, 16, 16),
            MinHeight = 176,
            Style = FindStyle("TalvoraCardStyle"),
            Child = root,
            Tag = state.Registration.Id,
            Cursor = WpfCursors.Hand,
        };
        card.MouseEnter += (_, _) =>
        {
            card.Background = HoverSurfaceBrush;
            card.BorderBrush = StrongBorderBrush;
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = SurfaceBrush;
            card.BorderBrush = CardBorderBrush;
        };
        card.MouseLeftButtonUp += async (_, e) =>
        {
            if (IsInsideButton(e.OriginalSource as DependencyObject))
            {
                return;
            }

            await ShowDetailAsync(state);
        };

        return card;
    }

    private UIElement CreateLogo(Talvora.Shared.ManagedMcpRegistration registration)
    {
        var source = McpLogoResolver.TryResolve(registration.Id);
        if (source is not null)
        {
            return new Border
            {
                Width = 44,
                Height = 44,
                Padding = new Thickness(6),
                Background = RaisedSurfaceBrush,
                CornerRadius = new CornerRadius(10),
                Child = new Image
                {
                    Source = source,
                    Stretch = Stretch.Uniform,
                },
            };
        }

        var fallbackText = registration.DisplayName
            .Trim()
            .FirstOrDefault()
            .ToString()
            .ToUpperInvariant();

        return new Border
        {
            Width = 44,
            Height = 44,
            Background = RaisedSurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(fallbackText) ? "M" : fallbackText,
                HorizontalAlignment = HAlign.Center,
                VerticalAlignment = VAlign.Center,
                Foreground = AccentBrush,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
            },
        };
    }

    private void UpdateCardWidths()
    {
        if (_cardsPanel is null || _cardsPanel.ActualWidth <= 0)
        {
            return;
        }

        var available = Math.Max(320, _cardsPanel.ActualWidth);
        var columns = available >= 1040
            ? 3
            : available >= 680
                ? 2
                : 1;
        var gaps = 16 * (columns - 1);
        var cardWidth = Math.Max(
            300,
            Math.Floor((available - gaps) / columns) -
            (columns == 1 ? 0 : 1));

        foreach (var child in _cardsPanel.Children.OfType<Border>())
        {
            child.Width = cardWidth;
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static SolidColorBrush GetSummaryBrush(
        ControlCenterDashboardSnapshot snapshot)
    {
        if (snapshot.OfflineCount > 0)
        {
            return OfflineBrush;
        }

        if (snapshot.AttentionCount > 0)
        {
            return AttentionBrush;
        }

        return ReadyBrush;
    }

    private static SolidColorBrush GetHealthBrush(
        ControlCenterHealthState health) =>
        health switch
        {
            ControlCenterHealthState.Ready => ReadyBrush,
            ControlCenterHealthState.Attention => AttentionBrush,
            ControlCenterHealthState.Offline => OfflineBrush,
            _ => CheckingBrush,
        };

    private static SolidColorBrush GetHealthSoftBrush(
        ControlCenterHealthState health) =>
        health switch
        {
            ControlCenterHealthState.Ready => ReadySoftBrush,
            ControlCenterHealthState.Attention => AttentionSoftBrush,
            ControlCenterHealthState.Offline => OfflineSoftBrush,
            _ => CheckingSoftBrush,
        };

    private static Border CreateStatusPill(
        ControlCenterHealthState health,
        string text)
    {
        var foreground = GetHealthBrush(health);
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VAlign.Center,
        };
        panel.Children.Add(new Border
        {
            Width = 7,
            Height = 7,
            Margin = new Thickness(0, 1, 7, 0),
            Background = foreground,
            CornerRadius = new CornerRadius(4),
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        });

        return new Border
        {
            Padding = new Thickness(9, 5, 9, 5),
            Background = GetHealthSoftBrush(health),
            CornerRadius = new CornerRadius(9),
            Child = panel,
        };
    }

    private Style FindStyle(string key) =>
        (Style)FindResource(key);

    private static StackPanel CreateIconLabel(
        SymbolRegular symbol,
        string text,
        System.Windows.Media.Brush? foreground = null,
        double fontSize = 12)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VAlign.Center,
        };

        panel.Children.Add(new SymbolIcon
        {
            Symbol = symbol,
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VAlign.Center,
            Foreground = foreground ?? PrimaryTextBrush,
        });

        panel.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VAlign.Center,
            Foreground = foreground ?? PrimaryTextBrush,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
        });

        return panel;
    }

    private void OnIsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (_applicationExitRequested)
        {
            return;
        }

        if (IsVisible)
        {
            _refreshTimer.Start();
            UpdateRawLogTimerState();
            _ = RefreshDashboardAsync();
        }
        else
        {
            _refreshTimer.Stop();
            UpdateRawLogTimerState();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_applicationExitRequested)
        {
            return;
        }

        ScheduleWindowPlacementSave();
        e.Cancel = true;
        Hide();
    }

    private static SolidColorBrush CreateBrush(
        byte red,
        byte green,
        byte blue)
    {
        var brush = new SolidColorBrush(
            MediaColor.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateBrush(
        byte alpha,
        byte red,
        byte green,
        byte blue)
    {
        var brush = new SolidColorBrush(
            MediaColor.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }
}