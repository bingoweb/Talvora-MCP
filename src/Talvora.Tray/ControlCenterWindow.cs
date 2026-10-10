using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
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
    // Stitch-derived "Kontrol Merkezi Desktop" palette: calm light workspace.
    private static readonly SolidColorBrush BackgroundBrush = CreateBrush(247, 249, 255);
    private static readonly SolidColorBrush SurfaceBrush = CreateBrush(255, 255, 255);
    private static readonly SolidColorBrush RaisedSurfaceBrush = CreateBrush(240, 244, 251);
    private static readonly SolidColorBrush HoverSurfaceBrush = CreateBrush(235, 240, 252);
    private static readonly SolidColorBrush CardBorderBrush = CreateBrush(229, 233, 240);
    private static readonly SolidColorBrush StrongBorderBrush = CreateBrush(199, 208, 223);
    private static readonly SolidColorBrush PrimaryTextBrush = CreateBrush(23, 35, 51);
    private static readonly SolidColorBrush SecondaryTextBrush = CreateBrush(87, 101, 121);
    private static readonly SolidColorBrush TertiaryTextBrush = CreateBrush(117, 131, 152);
    private static readonly SolidColorBrush AccentBrush = CreateBrush(56, 78, 192);
    private static readonly SolidColorBrush AccentSoftBrush = CreateBrush(234, 240, 255);
    private static readonly SolidColorBrush ReadyBrush = CreateBrush(22, 118, 83);
    private static readonly SolidColorBrush AttentionBrush = CreateBrush(151, 99, 19);
    private static readonly SolidColorBrush OfflineBrush = CreateBrush(190, 63, 82);
    private static readonly SolidColorBrush CheckingBrush = CreateBrush(79, 103, 207);
    private static readonly SolidColorBrush ReadySoftBrush = CreateBrush(230, 246, 237);
    private static readonly SolidColorBrush AttentionSoftBrush = CreateBrush(255, 242, 217);
    private static readonly SolidColorBrush OfflineSoftBrush = CreateBrush(252, 232, 237);
    private static readonly SolidColorBrush CheckingSoftBrush = CreateBrush(235, 240, 255);
    private static readonly SolidColorBrush IdleBrush = CreateBrush(99, 112, 131);
    private static readonly SolidColorBrush IdleSoftBrush = CreateBrush(241, 243, 247);
    private static readonly SolidColorBrush SidebarBrush = CreateBrush(16, 24, 39);
    private static readonly SolidColorBrush SidebarSecondaryBrush = CreateBrush(174, 189, 210);
    private static readonly SolidColorBrush SidebarAccentBrush = CreateBrush(170, 189, 255);
    private static readonly SolidColorBrush SidebarHoverBrush = CreateBrush(32, 43, 64);

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
    private Grid _dashboardControlsGrid = null!;
    private StackPanel _dashboardSearchStack = null!;
    private UiTextBox _searchBox = null!;
    private ComboBox _filterBox = null!;
    private DispatcherTimer _dashboardFilterDebounceTimer = null!;
    private UiButton _refreshButton = null!;
    private StackPanel _cardsPanel = null!;
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
    private DateTimeOffset? _lastSuccessfulDashboardRefresh;
    private int _dashboardRefreshPending;
    private bool _detailOperationInProgress;
    private bool _applicationExitRequested;

    public ControlCenterWindow(bool smokeMode = false)
    {
        _smokeMode = smokeMode;
        Title = "Talvora Yönetim Merkezi";
        Width = 1220;
        Height = 790;
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
            UpdateOverviewMetricsLayout();
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
            DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(8),
            IsEnabled = false,
        };
        _refreshTimer.Tick += OnRefreshTimerTick;

        _dashboardFilterDebounceTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(180),
            IsEnabled = false,
        };
        _dashboardFilterDebounceTimer.Tick += OnDashboardFilterDebounceTick;

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

    public async Task PrepareForApplicationExitAsync()
    {
        _applicationExitRequested = true;
        _refreshTimer.Stop();
        _windowPlacementSaveTimer.Stop();
        _rawLogRefreshTimer?.Stop();
        _dashboardFilterDebounceTimer.Stop();

        try
        {
            await SaveWindowPlacementAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException ex)
        {
            TrayLog.Write("Control Center window placement save timed out during shutdown", ex);
        }

        DetachControlCenterTimers();
        _lifetimeCts.Cancel();
    }

    internal void ReportRecoverableUiFailure()
    {
        if (_healthSummaryText is null)
        {
            return;
        }

        _healthSummaryText.Text = "Arayüz hatası kurtarıldı";
        _healthSummaryText.Foreground = AttentionBrush;
        _healthSummaryDot.Background = AttentionBrush;
        _technicalSummaryText.Text =
            "Control Center çalışmaya devam ediyor. Durum otomatik olarak yenilenecek.";
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshDashboardAsync();
    }

    private void DetachControlCenterTimers()
    {
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _dashboardFilterDebounceTimer.Tick -= OnDashboardFilterDebounceTick;
        _windowPlacementSaveTimer.Tick -= OnWindowPlacementSaveTimerTick;
        if (_rawLogRefreshTimer is not null)
        {
            _rawLogRefreshTimer.Tick -= OnRawLogRefreshTimerTick;
        }
    }

    private UIElement BuildContent()
    {
        var root = new Grid
        {
            Background = BackgroundBrush,
        };
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(226),
        });
        root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _windowTitleBar = BuildWindowTitleBar();
        Grid.SetColumnSpan(_windowTitleBar, 2);
        root.Children.Add(_windowTitleBar);

        var header = BuildHeader();
        Grid.SetRow(header, 1);
        Grid.SetColumn(header, 1);
        root.Children.Add(header);

        var sidebar = BuildNavigationSidebar();
        Grid.SetRow(sidebar, 1);
        Grid.SetRowSpan(sidebar, 3);
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

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
        Grid.SetColumn(bodyHost, 1);
        root.Children.Add(bodyHost);

        var footer = new Border
        {
            Padding = new Thickness(28, 10, 28, 10),
            Background = SurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new TextBlock
            {
                Text = "Talvora  /  Yerel MCP yönetimi  •  Windows",
                Foreground = TertiaryTextBrush,
                FontSize = 10.5,
            },
        };
        Grid.SetRow(footer, 3);
        Grid.SetColumn(footer, 1);
        root.Children.Add(footer);

        return root;
    }

    private TitleBar BuildWindowTitleBar()
    {
        var titleBar = new TitleBar
        {
            Title = "Talvora  /  Kontrol Merkezi",
            Height = 46,
            Padding = new Thickness(18, 0, 0, 0),
            Background = SurfaceBrush,
            Foreground = SecondaryTextBrush,
            ButtonsForeground = PrimaryTextBrush,
            ButtonsBackground = SurfaceBrush,
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
            Text = "YEREL / WINDOWS",
            Foreground = AccentBrush,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Kontrol Merkezi",
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "MCP hizmetleri, bağlantılar ve sistem olayları tek bir çalışma alanında.",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
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
            Text = "Bileşen bilgileri hazırlanıyor",
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
            Padding = new Thickness(16, 13, 16, 13),
            Background = RaisedSurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            Child = statusGrid,
        };

        Grid.SetColumn(_headerStatusCard, 1);
        _headerGrid.Children.Add(_headerStatusCard);

        return new Border
        {
            Padding = new Thickness(28, 24, 28, 20),
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _headerGrid,
        };
    }

    private UIElement BuildDashboard()
    {
        var content = new StackPanel
        {
            Margin = new Thickness(28, 24, 28, 32),
        };

        _setupCard = BuildSetupCard();
        content.Children.Add(_setupCard);
        content.Children.Add(BuildOverviewMetrics());

        var sectionHeader = new Grid
        {
            Margin = new Thickness(0, 0, 0, 16),
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
            Text = "Yönetilen MCP hizmetleri",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
        });
        sectionTitleStack.Children.Add(new TextBlock
        {
            Text = "Gerçek durumları görüntüleyin, gerektiğinde hizmetleri tek tıkla yönetin.",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 12.5,
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

        _dashboardControlsGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 20),
        };
        _dashboardControlsGrid.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });
        _dashboardControlsGrid.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });
        _dashboardControlsGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        _dashboardControlsGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });
        _dashboardControlsGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        _dashboardSearchStack = new StackPanel();

        _searchBox = new UiTextBox
        {
            MinWidth = 0,
            Style = FindStyle("TalvoraSearchBoxStyle"),
            PlaceholderText = "Bileşen ara",
            ClearButtonEnabled = true,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Search20 },
            ToolTip = "Ad veya açıklamaya göre bileşen ara",
        };
        AutomationProperties.SetName(
            _searchBox,
            "Yönetilen bileşenlerde ara");
        _searchBox.TextChanged += (_, _) => ScheduleDashboardFilter();
        _dashboardSearchStack.Children.Add(_searchBox);
        _dashboardControlsGrid.Children.Add(_dashboardSearchStack);

        _filterBox = new ComboBox
        {
            Width = 184,
            Height = 44,
            Margin = new Thickness(12, 0, 0, 0),
            SelectedIndex = 0,
            Visibility = Visibility.Collapsed,
            Style = FindStyle("TalvoraFilterComboBoxStyle"),
        };
        AutomationProperties.SetName(
            _filterBox,
            "Bileşen durum filtresi");
        _filterBox.Items.Add("Tümü");
        _filterBox.Items.Add("Dikkat isteyenler");
        _filterBox.Items.Add("Hazır");
        _filterBox.SelectionChanged += (_, _) => ApplyDashboardFilter();
        Grid.SetColumn(_filterBox, 1);
        _dashboardControlsGrid.Children.Add(_filterBox);

        _refreshButton = new UiButton
        {
            Content = "Yenile",
            Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowClockwise20 },
            MinWidth = 104,
            Margin = new Thickness(12, 0, 0, 0),
            Style = FindStyle("TalvoraSecondaryButtonStyle"),
            Cursor = WpfCursors.Hand,
        };
        AutomationProperties.SetName(
            _refreshButton,
            "Bileşen durumlarını yenile");
        _refreshButton.Click += async (_, _) => await RefreshDashboardAsync();
        Grid.SetColumn(_refreshButton, 2);
        _dashboardControlsGrid.Children.Add(_refreshButton);


        content.Children.Add(_dashboardControlsGrid);

        _loadingState = new Border
        {
            Style = FindStyle("TalvoraSubtleCardStyle"),
            Child = new TextBlock
            {
                Text = "Bileşen durumları kontrol ediliyor...",
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

        _cardsPanel = new StackPanel();
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
            Interlocked.Exchange(
                ref _dashboardRefreshPending,
                1);
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
            await RefreshEmbeddedMcpIntegrationsAsync(_lifetimeCts.Token);
            _lastSuccessfulDashboardRefresh = DateTimeOffset.Now;

            _healthSummaryText.Text = snapshot.HealthSummary;
            _healthSummaryText.Foreground = GetSummaryBrush(snapshot);
            _healthSummaryDot.Background = GetSummaryBrush(snapshot);
            _headerStatusCard.Background = GetSummarySoftBrush(snapshot);
            _headerStatusCard.BorderBrush = CardBorderBrush;
            _technicalSummaryText.Text = snapshot.TechnicalSummary;
            RefreshOverviewMetrics(snapshot);
            _dashboardSectionMeta.Text =
                $"{snapshot.Mcps.Count} bileşen • {snapshot.ReadyCount} hazır";
            _lastRefreshText.Text =
                $"Son kontrol {_lastSuccessfulDashboardRefresh.Value:HH:mm:ss}";
            _filterBox.Visibility = snapshot.Mcps.Count >= 4
                ? Visibility.Visible
                : Visibility.Collapsed;

            var dashboardVisible =
                _dashboardScroller.Visibility == Visibility.Visible;
            if (dashboardVisible)
            {
                ApplyDashboardFilter();
                RefreshSetupCard();
                await RefreshEventsPanelAsync();
            }

            if (_selectedMcp is not null &&
                _detailScroller.Visibility == Visibility.Visible)
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
            var hasLastKnownSnapshot =
                _snapshot is not null;

            _healthSummaryText.Text = "Canlı durum alınamadı";
            _sidebarHealthText.Text = "Canlı durum alınamadı";
            _sidebarHealthDot.Background = OfflineBrush;
            _healthSummaryText.Foreground = OfflineBrush;
            _healthSummaryDot.Background = OfflineBrush;
            _headerStatusCard.Background = OfflineSoftBrush;
            _headerStatusCard.BorderBrush = OfflineBrush;
            _technicalSummaryText.Text = hasLastKnownSnapshot
                ? "Son bilinen durum gösteriliyor • otomatik yeniden denenecek."
                : "Bileşen bilgileri şu anda alınamıyor • otomatik yeniden denenecek.";
            _lastRefreshText.Text = _lastSuccessfulDashboardRefresh is { } lastSuccessful
                ? $"Son başarılı kontrol {lastSuccessful:HH:mm:ss} • güncel değil"
                : string.Empty;

            if (hasLastKnownSnapshot)
            {
                if (_dashboardScroller.Visibility == Visibility.Visible)
                {
                    ApplyDashboardFilter();
                    RefreshSetupCard();
                }
            }
            else
            {
                _cardsPanel.Children.Clear();
                _emptyState.Text = "Bileşen bilgileri şu anda alınamıyor.";
                _emptyState.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _loadingState.Visibility = Visibility.Collapsed;
            _refreshButton.IsEnabled = true;
            _refreshGate.Release();

            if (Interlocked.Exchange(
                    ref _dashboardRefreshPending,
                    0) != 0 &&
                IsVisible &&
                !_lifetimeCts.IsCancellationRequested)
            {
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    () => _ = RefreshDashboardAsync());
            }
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
                1 => state.Health != ControlCenterHealthState.Ready &&
                     !ManagedMcpAggregateHealthPolicy.IsIgnored(
                         state.Registration,
                         state.Health,
                         ManagedMcpSessionState.IsManuallyStopped(
                             state.Registration.Id)),
                2 => state.Health == ControlCenterHealthState.Ready,
                _ => true,
            })
            .ToList();

        _cardsPanel.Children.Clear();
        var talvoraFamily = visible
            .Where(state => IsTalvoraFamily(state.Registration.Id))
            .OrderBy(state => GetTalvoraRoleOrder(state.Registration.Id))
            .ToList();
        var otherMcps = visible
            .Where(state => !IsTalvoraFamily(state.Registration.Id))
            .ToList();
        if (talvoraFamily.Count > 0)
        {
            _cardsPanel.Children.Add(CreateDashboardSection(
                "Talvora",
                "Bir çekirdek servis ve onu kullanan Dev/Admin ChatGPT bağlantıları.",
                talvoraFamily));
        }
        if (otherMcps.Count > 0)
        {
            _cardsPanel.Children.Add(CreateDashboardSection(
                "Diğer MCP'ler",
                "Talvora'dan bağımsız yönetilen yerel MCP ve entegrasyonlar.",
                otherMcps));
        }
        AppendEmbeddedMcpIntegrationSection(query, selectedFilter);

        _emptyState.Text = _snapshot.Mcps.Count == 0
            ? "Henüz yönetilen bir bileşen bulunamadı."
            : "Aramanızla eşleşen bileşen yok.";
        _emptyState.Visibility = visible.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateCardWidths();
    }

    private StackPanel CreateDashboardSection(
        string title,
        string description,
        IReadOnlyList<ManagedMcpDashboardState> states)
    {
        var section = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 14),
        };
        var header = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 12),
        };
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        });
        header.Children.Add(new TextBlock
        {
            Text = description,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = TertiaryTextBrush,
            FontSize = 11.5,
        });
        section.Children.Add(header);
        var cards = new StackPanel();
        foreach (var state in states)
        {
            cards.Children.Add(CreateMcpCard(state));
        }
        section.Children.Add(cards);
        return section;
    }
    private static bool IsTalvoraFamily(string id) =>
        string.Equals(id, "talvora", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id, "talvora-dev", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id, "talvora-admin", StringComparison.OrdinalIgnoreCase);
    private static int GetTalvoraRoleOrder(string id) =>
        id.ToLowerInvariant() switch
        {
            "talvora" => 0,
            "talvora-dev" => 1,
            "talvora-admin" => 2,
            _ => 3,
        };
    private void ScheduleDashboardFilter()
    {
        _dashboardFilterDebounceTimer.Stop();
        _dashboardFilterDebounceTimer.Start();
    }

    private void OnDashboardFilterDebounceTick(object? sender, EventArgs e)
    {
        _dashboardFilterDebounceTimer.Stop();
        ApplyDashboardFilter();
    }

    private Border CreateMcpCard(ManagedMcpDashboardState state)
    {
        var intentionallyInactive =
            state.Health != ControlCenterHealthState.Ready &&
            ManagedMcpAggregateHealthPolicy.IsIgnored(
                state.Registration,
                state.Health,
                ManagedMcpSessionState.IsManuallyStopped(
                    state.Registration.Id));
        var cardStroke =
            intentionallyInactive ||
            state.Health == ControlCenterHealthState.Ready
                ? CardBorderBrush
                : GetHealthSoftBrush(state.Health);

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
        var roleLabel = GetMcpRoleLabel(state.Registration.Id);
        if (roleLabel is not null)
        {
            titleStack.Children.Add(new TextBlock
            {
                Text = roleLabel,
                Margin = new Thickness(0, 0, 0, 3),
                Foreground = AccentBrush,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
            });
        }
        titleStack.Children.Add(new TextBlock
        {
            Text = state.Registration.DisplayName,
            FontSize = 16.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = state.Registration.Description,
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 38,
        });
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(titleStack);

        var statusPill = CreateStatusPill(
            state.Health,
            state.StatusText,
            intentionallyInactive);
        Grid.SetColumn(statusPill, 2);
        header.Children.Add(statusPill);

        root.Children.Add(header);

        var detail = new TextBlock
        {
            Text = state.Detail,
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = SecondaryTextBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 38,
        };
        Grid.SetRow(detail, 1);
        root.Children.Add(detail);

        var actionRow = new Grid
        {
            Margin = new Thickness(0, 12, 0, 0),
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
            MinHeight = 36,
            Padding = new Thickness(12, 6, 12, 6),
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
            Margin = new Thickness(0, 0, 0, 12),
            MinHeight = 145,
            Style = FindStyle("TalvoraCardStyle"),
            BorderBrush = cardStroke,
            Child = root,
            Tag = state.Registration.Id,
            Cursor = WpfCursors.Hand,
            Focusable = true,
        };
        KeyboardNavigation.SetIsTabStop(card, true);
        AutomationProperties.SetName(
            card,
            $"{state.Registration.DisplayName}: {state.StatusText}");
        AutomationProperties.SetHelpText(
            card,
            "Ayrıntıları açmak için Enter veya Boşluk tuşuna basın.");
        card.MouseEnter += (_, _) =>
        {
            card.Background = HoverSurfaceBrush;
            card.BorderBrush = StrongBorderBrush;
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = SurfaceBrush;
            if (!card.IsKeyboardFocusWithin)
            {
                card.BorderBrush = cardStroke;
            }
        };
        card.GotKeyboardFocus += (_, _) =>
        {
            card.BorderBrush = StrongBorderBrush;
        };
        card.LostKeyboardFocus += (_, _) =>
        {
            if (!card.IsMouseOver)
            {
                card.BorderBrush = cardStroke;
            }
        };
        card.KeyDown += async (_, e) =>
        {
            if (IsInsideButton(e.OriginalSource as DependencyObject) ||
                e.Key is not (Key.Enter or Key.Space))
            {
                return;
            }

            e.Handled = true;
            await ShowDetailAsync(state);
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

    private static string? GetMcpRoleLabel(string id) =>
        id.ToLowerInvariant() switch
        {
            "talvora" => "ÇEKİRDEK SERVİS",
            "talvora-dev" or "talvora-admin" => "CHATGPT BAĞLANTISI",
            _ => null,
        };
    private UIElement CreateLogo(Talvora.Shared.ManagedMcpRegistration registration)
    {
        var source = McpLogoResolver.TryResolve(registration.Id);
        if (source is not null)
        {
            return new Border
            {
                Width = 46,
                Height = 46,
                Padding = new Thickness(7),
                Background = IsTalvoraFamily(registration.Id)
                    ? AccentSoftBrush
                    : RaisedSurfaceBrush,
                BorderBrush = IsTalvoraFamily(registration.Id)
                    ? AccentSoftBrush
                    : CardBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
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
            Width = 46,
            Height = 46,
            Background = IsTalvoraFamily(registration.Id)
                ? AccentSoftBrush
                : RaisedSurfaceBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(fallbackText) ? "M" : fallbackText,
                HorizontalAlignment = HAlign.Center,
                VerticalAlignment = VAlign.Center,
                Foreground = AccentBrush,
                FontSize = 18.5,
                FontWeight = FontWeights.SemiBold,
            },
        };
    }

    private void UpdateCardWidths()
    {
        if (_cardsPanel is null)
        {
            return;
        }

        // Full-width rows eliminate the empty gutters of the old card grid.
        foreach (var section in _cardsPanel.Children.OfType<StackPanel>())
        {
            foreach (var panel in section.Children.OfType<StackPanel>())
            {
                foreach (var item in panel.Children.OfType<Border>())
                {
                    item.Width = double.NaN;
                    item.HorizontalAlignment = HAlign.Stretch;
                }
            }
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

    private static SolidColorBrush GetSummarySoftBrush(
        ControlCenterDashboardSnapshot snapshot)
    {
        if (snapshot.OfflineCount > 0)
        {
            return OfflineSoftBrush;
        }

        if (snapshot.AttentionCount > 0)
        {
            return AttentionSoftBrush;
        }

        return ReadySoftBrush;
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
        string text,
        bool intentionallyInactive = false)
    {
        var foreground = intentionallyInactive
            ? IdleBrush
            : GetHealthBrush(health);
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
            Padding = new Thickness(10, 6, 10, 6),
            Background = intentionallyInactive
                ? IdleSoftBrush
                : GetHealthSoftBrush(health),
            CornerRadius = new CornerRadius(11),
            HorizontalAlignment = HAlign.Right,
            VerticalAlignment = VAlign.Top,
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
            _ = RefreshDashboardAsync();
            UpdateRawLogTimerState();
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