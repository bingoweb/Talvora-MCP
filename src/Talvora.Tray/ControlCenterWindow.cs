using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using UiPasswordBox = Wpf.Ui.Controls.PasswordBox;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Talvora.Tray;

/// <summary>
/// The Control Center window: lifecycle, refresh loop and window placement. Every page is a XAML view hosted by the
/// shell (UI2–UI8); the former imperative dashboard, detail, sidebar and header builders were removed in UI9.
/// </summary>
internal sealed partial class ShellWindow : FluentWindow
{
    // The shell root paints Tv.Bg at 0.85 opacity over Mica on Windows 11 (§4); the instance refreshes in place.
    private static readonly SolidColorBrush BackgroundBrush = ThemeBrush("Tv.Bg");

    private static SolidColorBrush ThemeBrush(string key) =>
        (System.Windows.Application.Current.TryFindResource(key) as SolidColorBrush)?.CloneCurrentValue()
        ?? throw new InvalidOperationException($"Control Center theme token {key} is not available.");

    internal static void RefreshThemePalette(System.Windows.Application application)
    {
        if (application.TryFindResource("Tv.Bg") is not SolidColorBrush brush)
            throw new InvalidOperationException("Control Center theme token Tv.Bg is not available.");
        BackgroundBrush.Color = brush.Color;
        BackgroundBrush.Opacity = TalvoraThemeManager.GetBackdrop() == WindowBackdropType.Mica ? 0.85 : 1;
        if (application.MainWindow is ShellWindow shell) shell.SynchronizeShellThemeChoice();
    }

    private readonly DispatcherTimer _refreshTimer;
    private readonly bool _smokeMode;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _windowPlacementSaveGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();

    private TitleBar _windowTitleBar = null!;
    private Talvora.Tray.Controls.StepperCard _setupCard = null!;
    private TextBlock _setupDetailText = null!;
    private UiTextBox _setupDevTunnelIdBox = null!;
    private UiTextBox _setupAdminTunnelIdBox = null!;
    private UiPasswordBox _setupAdminKeyBox = null!;
    private UiButton _setupActionButton = null!;
    private ControlCenterSetupState? _setupState;
    private DispatcherTimer _windowPlacementSaveTimer = null!;
    private bool _restoringWindowPlacement;
    private long _windowPlacementSaveVersion;

    private ControlCenterDashboardSnapshot? _snapshot;
    private int _dashboardRefreshPending;
    private bool _applicationExitRequested;

    public ShellWindow(bool smokeMode = false)
    {
        _smokeMode = smokeMode;
        SettingsPreferencesStore.Initialize(smokeMode);
        Title = "Talvora Kontrol Merkezi";
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
        WindowBackdropType = TalvoraThemeManager.GetBackdrop();
        ExtendsContentIntoTitleBar = true;
        ResizeMode = ResizeMode.CanResize;
        Background = BackgroundBrush;
        SetResourceReference(ForegroundProperty, "Tv.Text");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        _setupCard = BuildSetupCard();
        InitializeComponent();
        InitializeShell();
        InitializeWindowPlacementPersistence();

        Closing += OnClosing;
        IsVisibleChanged += OnIsVisibleChanged;
        SizeChanged += (_, _) => ScheduleWindowPlacementSave();
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
        Loaded += (_, _) => UpdateWindowStateVisuals();

        _refreshTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(8),
            IsEnabled = false,
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    public void BringToForeground()
    {
        if (!IsVisible)
        {
            if (_smokeMode && FirstPaintMilliseconds is null) _shellFirstPaint = System.Diagnostics.Stopwatch.StartNew();
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

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = RefreshDashboardAsync()));
    }

    public async Task PrepareForApplicationExitAsync()
    {
        _applicationExitRequested = true;
        _refreshTimer.Stop();
        _windowPlacementSaveTimer.Stop();

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

    // A recovered dispatcher failure: the XAML views show live state again on the next refresh.
    internal void ReportRecoverableUiFailure()
    {
        if (!_applicationExitRequested && IsVisible)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = RefreshDashboardAsync()));
        }
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshDashboardAsync();
    }

    private void DetachControlCenterTimers()
    {
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _windowPlacementSaveTimer.Tick -= OnWindowPlacementSaveTimerTick;
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

        try
        {
            var snapshot = await ControlCenterDashboardService.GetSnapshotAsync(
                _lifetimeCts.Token);
            _snapshot = snapshot;
            UpdateUi3Snapshot(snapshot);
            UpdateUi4Snapshot(snapshot);
            UpdateUi5Snapshot(snapshot);
            RefreshSetupCard();
            UpdateShellCommands();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Control Center dashboard refresh failed", ex);
            if (_snapshot is null) _overviewView?.Model.MarkUnavailable();
        }
        finally
        {
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

    // Smoke: the live snapshot loads and a real service detail opens and closes through the UI4 views.
    internal async Task RunSmokeScenarioAsync()
    {
        var dashboardDeadline = DateTime.UtcNow.AddSeconds(15);
        while ((_snapshot is null || _snapshot.Mcps.Count == 0) &&
               DateTime.UtcNow < dashboardDeadline)
        {
            await RefreshDashboardAsync();
            if (_snapshot is not null && _snapshot.Mcps.Count > 0)
            {
                break;
            }

            await Task.Delay(250);
        }
        if (_snapshot is null || _snapshot.Mcps.Count == 0)
        {
            throw new InvalidOperationException(
                "Control Center dashboard did not load any managed MCP.");
        }

        OpenUi4Detail(_snapshot.Mcps[0]);
        UpdateLayout();
        if (_servicesView is not { IsDetailOpen: true } services)
        {
            throw new InvalidOperationException(
                "Control Center service detail did not open.");
        }

        services.BackToList();
        if (services.IsDetailOpen)
        {
            throw new InvalidOperationException(
                "Control Center could not return to the service list.");
        }
        ShellModel.SelectPage(0);
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
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = RefreshDashboardAsync()));
        }
        else
        {
            _refreshTimer.Stop();
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
}
