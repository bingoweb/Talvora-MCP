using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Talvora.Shared;

namespace Talvora.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string GiteaUrl = "http://127.0.0.1:3000/";

    private readonly ControlCenterApplication _controlCenterApplication;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;

    private readonly ToolStripMenuItem _talvoraStatusItem;
    private readonly ToolStripMenuItem _reconnectItem;
    private readonly ToolStripMenuItem _refreshTalvoraItem;

    private readonly ToolStripMenuItem _giteaStatusItem;
    private readonly ToolStripMenuItem _openGiteaItem;
    private readonly ToolStripMenuItem _restartGiteaItem;
    private readonly ToolStripMenuItem _refreshGiteaItem;

    private readonly System.Windows.Forms.Timer _talvoraTimer;
    private readonly System.Windows.Forms.Timer _giteaTimer;
    private readonly System.Windows.Forms.Timer _shutdownTimer;
    private readonly EventWaitHandle _shutdownEvent = new(
        initialState: false,
        EventResetMode.AutoReset,
        @"Global\Talvora.Tray.Shutdown");

    private readonly SemaphoreSlim _talvoraOperationGate = new(1, 1);
    private readonly SemaphoreSlim _giteaOperationGate = new(1, 1);

    private Icon? _statusIcon;
    private bool _disposed;
    private readonly ManagedMcpRecoveryState _talvoraRecoveryState = new("talvora");
    private readonly ManagedMcpRecoveryState _giteaRecoveryState = new("gitea");
    private ManagedMcpRegistration? _talvoraRegistration;
    private ManagedMcpRegistration? _giteaRegistration;

    private TalvoraStatus _talvoraStatus = new(
        TalvoraConnectionState.LocalOnly,
        "Talvora durumu kontrol ediliyor...",
        string.Empty);

    private GiteaStatus _giteaStatus = new(
        GiteaConnectionState.Restarting,
        "Gitea durumu kontrol ediliyor...",
        string.Empty);

    public TrayApplicationContext(ControlCenterApplication controlCenterApplication)
    {
        _controlCenterApplication = controlCenterApplication;

        _statusItem = new ToolStripMenuItem("Yönetilen MCP durumları kontrol ediliyor...")
        {
            Enabled = false,
        };

        var openControlCenterItem = new ToolStripMenuItem("Talvora Control Center'ı Aç");
        openControlCenterItem.Click += (_, _) =>
            _controlCenterApplication.ShowControlCenter();

        _talvoraStatusItem = new ToolStripMenuItem(_talvoraStatus.Summary)
        {
            Enabled = false,
        };
        _reconnectItem = new ToolStripMenuItem("ChatGPT Business'a yeniden bağlan");
        _reconnectItem.Click += async (_, _) => await ReconnectTalvoraAsync();
        _refreshTalvoraItem = new ToolStripMenuItem("Durumu yenile");
        _refreshTalvoraItem.Click += async (_, _) =>
            await RefreshTalvoraStatusAsync(showBalloon: true);

        var talvoraMenu = new ToolStripMenuItem("Talvora MCP");
        talvoraMenu.DropDownItems.Add(_talvoraStatusItem);
        talvoraMenu.DropDownItems.Add(new ToolStripSeparator());
        talvoraMenu.DropDownItems.Add(_reconnectItem);
        talvoraMenu.DropDownItems.Add(_refreshTalvoraItem);

        _giteaStatusItem = new ToolStripMenuItem(_giteaStatus.Summary)
        {
            Enabled = false,
        };
        _openGiteaItem = new ToolStripMenuItem("Gitea'yı Aç");
        _openGiteaItem.Click += (_, _) => OpenGitea();
        _restartGiteaItem = new ToolStripMenuItem("Gitea'yı Yeniden Başlat");
        _restartGiteaItem.Click += async (_, _) => await RestartGiteaAsync();
        _refreshGiteaItem = new ToolStripMenuItem("Durumu yenile");
        _refreshGiteaItem.Click += async (_, _) =>
            await RefreshGiteaStatusAsync(showBalloon: true);

        var giteaMenu = new ToolStripMenuItem("Gitea MCP");
        giteaMenu.DropDownItems.Add(_giteaStatusItem);
        giteaMenu.DropDownItems.Add(new ToolStripSeparator());
        giteaMenu.DropDownItems.Add(_openGiteaItem);
        giteaMenu.DropDownItems.Add(_restartGiteaItem);
        giteaMenu.DropDownItems.Add(_refreshGiteaItem);

        var exitItem = new ToolStripMenuItem("Talvora'dan Çık");
        exitItem.Click += (_, _) => ExitTray();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(openControlCenterItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(talvoraMenu);
        _menu.Items.Add(giteaMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Text = "Talvora",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) =>
            _controlCenterApplication.ShowControlCenter();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _controlCenterApplication.ShowControlCenter();
            }
        };

        UpdateAggregateTrayState();

        _talvoraTimer = new System.Windows.Forms.Timer
        {
            Interval = 5_000,
            Enabled = false,
        };
        _talvoraTimer.Tick += async (_, _) => await MaintainTalvoraConnectionAsync();

        _giteaTimer = new System.Windows.Forms.Timer
        {
            Interval = 10_000,
            Enabled = false,
        };
        _giteaTimer.Tick += async (_, _) =>
            await MaintainGiteaConnectionAsync();

        _shutdownTimer = new System.Windows.Forms.Timer
        {
            Interval = 250,
            Enabled = true,
        };
        _shutdownTimer.Tick += (_, _) =>
        {
            if (_shutdownEvent.WaitOne(0))
            {
                ExitTray();
            }
        };

        _ = InitializeManagedRegistryAndRecoveryAsync();

        TrayLog.Write("Single Talvora tray controller initialized.");
    }

    private async Task InitializeManagedRegistryAndRecoveryAsync()
    {
        try
        {
            var registry = await ManagedMcpRegistryCoordinator.LoadOrRecoverAsync(
                _lifetimeCts.Token);

            registry = await ManagedMcpTunnelProvisioningService.EnsureMissingTunnelsAsync(
                registry,
                _lifetimeCts.Token);

            _talvoraRegistration = registry.Mcps.FirstOrDefault(entry =>
                string.Equals(entry.Id, "talvora", StringComparison.OrdinalIgnoreCase));
            _giteaRegistration = registry.Mcps.FirstOrDefault(entry =>
                string.Equals(entry.Id, "gitea", StringComparison.OrdinalIgnoreCase));

            TrayLog.Write($"Managed MCP registry ready. Count={registry.Mcps.Count}");
            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Info,
                "system",
                "Yönetim Merkezi hazır",
                $"{registry.Mcps.Count} yönetilen MCP kaydı yüklendi.",
                dedupKey: "system:control-center-startup");

            var setupState = ControlCenterSetupService.Evaluate(registry);
            if (!ControlCenterWindow.HasSavedWindowPlacement ||
                !setupState.IsComplete)
            {
                _controlCenterApplication.ShowControlCenter();
                TrayLog.Write(
                    setupState.IsComplete
                        ? "Control Center opened for first-use dashboard."
                        : "Control Center opened for incomplete setup.");
            }

            // Startup recovery is deliberately ordered: Talvora provides the
            // LocalSystem broker used by Gitea lifecycle operations.
            await MaintainTalvoraConnectionAsync();
            await MaintainGiteaConnectionAsync();

            foreach (var registration in registry.Mcps.Where(entry =>
                         entry.AutoStart &&
                         !string.Equals(entry.Id, "talvora", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(entry.Id, "gitea", StringComparison.OrdinalIgnoreCase) &&
                         !ManagedMcpSessionState.IsManuallyStopped(entry.Id)))
            {
                try
                {
                    var result = await ControlCenterLifecycleService.StartAsync(
                        registration,
                        _lifetimeCts.Token);
                    TrayLog.Write(
                        $"Startup auto-start completed. MCP={registration.Id}");
                    ControlCenterEventStore.Record(
                        ControlCenterEventSeverity.Info,
                        "startup",
                        result.Summary,
                        result.Detail,
                        registration.Id,
                        $"startup:{registration.Id}:success");
                }
                catch (Exception ex)
                {
                    TrayLog.Write(
                        $"Startup auto-start failed. MCP={registration.Id}",
                        ex);
                    ControlCenterEventStore.Record(
                        ControlCenterEventSeverity.Warning,
                        "startup",
                        $"{registration.DisplayName} başlangıçta hazırlanamadı",
                        "Otomatik kurtarma sonraki sağlık döngüsünde yeniden deneyecek.",
                        registration.Id,
                        $"startup:{registration.Id}:failed");
                }
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Managed MCP registry/recovery initialization failed", ex);
            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Error,
                "startup",
                "Yönetim Merkezi başlangıç hazırlığı başarısız",
                "Yönetilen MCP kayıtları veya otomatik kurtarma hazırlanamadı.",
                dedupKey: "startup:registry-recovery-failed");
        }
        finally
        {
            if (!_disposed && !_lifetimeCts.IsCancellationRequested)
            {
                _talvoraTimer.Start();
                _giteaTimer.Start();
            }
        }
    }

    private async Task MaintainTalvoraConnectionAsync()
    {
        var lockTaken = false;
        try
        {
            lockTaken = await _talvoraOperationGate.WaitAsync(
                0,
                _lifetimeCts.Token);
            if (!lockTaken)
            {
                return;
            }

            if (ManagedMcpSessionState.IsManuallyStopped("talvora"))
            {
                _talvoraRecoveryState.ResetForManualAction();
                _talvoraTimer.Interval = 30_000;
                SetTalvoraStatus(new TalvoraStatus(
                    TalvoraConnectionState.Offline,
                    "Talvora elle durduruldu",
                    "Yönetim Merkezi üzerinden Başlat seçilene kadar otomatik kurtarma devre dışı."));
                return;
            }

            var status = await BusinessTunnelClient.GetStatusAsync(
                _lifetimeCts.Token);

            if (status.State == TalvoraConnectionState.Ready)
            {
                var recoveredSeriousIncident =
                    _talvoraRecoveryState.ResetHealthy();

                _talvoraTimer.Interval = 30_000;
                SetTalvoraStatus(status);

                if (recoveredSeriousIncident)
                {
                    NotifyRecoveryResolved(
                        "Talvora MCP",
                        "Servis ve güvenli MCP tüneli yeniden hazır.");
                }

                return;
            }

            SetTalvoraStatus(status);
            _talvoraTimer.Interval = 5_000;

            var nowUtc = DateTimeOffset.UtcNow;
            if (!_talvoraRecoveryState.CanAttempt(nowUtc))
            {
                return;
            }

            SetTalvoraActionsEnabled(false);

            try
            {
                if (status.State == TalvoraConnectionState.Offline)
                {
                    SetTalvoraStatus(new TalvoraStatus(
                        TalvoraConnectionState.LocalOnly,
                        "Talvora otomatik onarılıyor...",
                        "Yerel servis ve güvenli MCP tüneli yeniden hazırlanıyor."));

                    var registration = _talvoraRegistration ??
                        await ResolveManagedRegistrationAsync("talvora");

                    if (registration is null)
                    {
                        throw new InvalidOperationException(
                            "Talvora yönetim kaydı bulunamadı.");
                    }

                    await ControlCenterLifecycleService.StartAsync(
                        registration,
                        _lifetimeCts.Token);
                }
                else
                {
                    SetTalvoraStatus(new TalvoraStatus(
                        TalvoraConnectionState.LocalOnly,
                        "ChatGPT Business otomatik bağlanıyor...",
                        "Güvenli MCP tüneli yeniden hazırlanıyor."));

                    await BusinessTunnelClient.ReconnectAsync(
                        _lifetimeCts.Token);
                }

                status = await BusinessTunnelClient.GetStatusAsync(
                    _lifetimeCts.Token);

                if (status.State != TalvoraConnectionState.Ready)
                {
                    throw new InvalidOperationException(
                        "Otomatik kurtarma tamamlandı ancak Talvora henüz hazır değil.");
                }

                var recoveredSeriousIncident =
                    _talvoraRecoveryState.ResetHealthy();

                SetTalvoraStatus(status);
                _talvoraTimer.Interval = 30_000;

                var alias = BusinessTunnelClient.LoadConfig().Alias;
                TrayLog.Write(
                    $"Automatic Talvora recovery succeeded. Alias={alias}");
                ControlCenterEventStore.Record(
                    ControlCenterEventSeverity.Info,
                    "recovery",
                    "Talvora MCP otomatik olarak düzeltildi",
                    "Yerel servis ve güvenli MCP tüneli yeniden hazır.",
                    "talvora",
                    "recovery:talvora:success");

                if (recoveredSeriousIncident)
                {
                    NotifyRecoveryResolved(
                        "Talvora MCP",
                        "Servis ve güvenli MCP tüneli otomatik olarak düzeltildi.");
                }
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var failure = _talvoraRecoveryState.RegisterFailure(nowUtc);

                TrayLog.Write(
                    $"Automatic Talvora recovery failed. Attempt={failure.ConsecutiveFailures}; RetryIn={failure.RetryDelay.TotalSeconds:F0}s",
                    ex);
                ControlCenterEventStore.Record(
                    ControlCenterEventSeverity.Warning,
                    "recovery",
                    "Talvora otomatik kurtarma denemesi başarısız",
                    $"Deneme {failure.ConsecutiveFailures}; {failure.RetryDelay.TotalSeconds:F0} saniye sonra yeniden denenecek.",
                    "talvora",
                    "recovery:talvora:failed");

                var localHealthy =
                    await BusinessTunnelClient.IsLocalMcpHealthyAsync(
                        _lifetimeCts.Token);

                SetTalvoraStatus(new TalvoraStatus(
                    localHealthy
                        ? TalvoraConnectionState.LocalOnly
                        : TalvoraConnectionState.Offline,
                    localHealthy
                        ? "Talvora çalışıyor, Business bağlantısı bekleniyor"
                        : "Talvora servisi otomatik kurtarma bekliyor",
                    $"Otomatik yeniden deneme {failure.RetryDelay.TotalSeconds:F0} saniye sonra."));

                HandleRecoveryFailure(
                    _talvoraRecoveryState,
                    "Talvora MCP",
                    failure,
                    ex.Message);
            }
            finally
            {
                SetTalvoraActionsEnabled(true);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            var failure = _talvoraRecoveryState.RegisterFailure(
                DateTimeOffset.UtcNow);
            _talvoraTimer.Interval = 5_000;

            TrayLog.Write(
                $"Talvora connection maintenance failed. Attempt={failure.ConsecutiveFailures}; RetryIn={failure.RetryDelay.TotalSeconds:F0}s",
                ex);
            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Warning,
                "recovery",
                "Talvora bağlantı bakımı başarısız",
                $"Deneme {failure.ConsecutiveFailures}; {failure.RetryDelay.TotalSeconds:F0} saniye sonra yeniden denenecek.",
                "talvora",
                "recovery:talvora:maintenance-failed");

            SetTalvoraStatus(new TalvoraStatus(
                TalvoraConnectionState.LocalOnly,
                "Talvora bağlantısı hazırlanıyor",
                $"Otomatik yeniden deneme {failure.RetryDelay.TotalSeconds:F0} saniye sonra."));

            HandleRecoveryFailure(
                _talvoraRecoveryState,
                "Talvora MCP",
                failure,
                ex.Message);
        }
        finally
        {
            if (lockTaken)
            {
                _talvoraOperationGate.Release();
            }
        }
    }

    private async Task RefreshTalvoraStatusAsync(bool showBalloon)
    {
        var lockTaken = false;
        try
        {
            lockTaken = await _talvoraOperationGate.WaitAsync(
                0,
                _lifetimeCts.Token);
            if (!lockTaken)
            {
                return;
            }

            if (ManagedMcpSessionState.IsManuallyStopped("talvora"))
            {
                var stopped = new TalvoraStatus(
                    TalvoraConnectionState.Offline,
                    "Talvora elle durduruldu",
                    "Yönetim Merkezi üzerinden Başlat seçilene kadar otomatik kurtarma devre dışı.");
                SetTalvoraStatus(stopped);

                if (showBalloon)
                {
                    ShowBalloon(stopped.Summary, stopped.Detail, ToolTipIcon.Info);
                }

                return;
            }

            var status = await BusinessTunnelClient.GetStatusAsync(
                _lifetimeCts.Token);
            SetTalvoraStatus(status);

            if (showBalloon)
            {
                ShowBalloon(status.Summary, status.Detail);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Talvora status refresh failed", ex);
            SetTalvoraStatus(new TalvoraStatus(
                TalvoraConnectionState.Offline,
                "Talvora durumu alınamadı",
                ex.Message));

            if (showBalloon)
            {
                ShowBalloon("Talvora", ex.Message, ToolTipIcon.Error);
            }
        }
        finally
        {
            if (lockTaken)
            {
                _talvoraOperationGate.Release();
            }
        }
    }

    private async Task ReconnectTalvoraAsync()
    {
        var lockTaken = false;
        try
        {
            lockTaken = await _talvoraOperationGate.WaitAsync(
                0,
                _lifetimeCts.Token);
            if (!lockTaken)
            {
                return;
            }

            ManagedMcpSessionState.ClearManualStop("talvora");
            _talvoraRecoveryState.ResetForManualAction();

            SetTalvoraActionsEnabled(false);
            SetTalvoraStatus(new TalvoraStatus(
                TalvoraConnectionState.LocalOnly,
                "ChatGPT Business yeniden bağlanıyor...",
                "Tunnel hazırlanıyor."));

            await BusinessTunnelClient.ReconnectAsync(_lifetimeCts.Token);

            var status = await BusinessTunnelClient.GetStatusAsync(
                _lifetimeCts.Token);
            SetTalvoraStatus(status);

            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Info,
                "connection",
                "Talvora bağlantısı yenilendi",
                "ChatGPT Business güvenli MCP tüneli yeniden bağlandı.",
                "talvora",
                "connection:talvora:tray-reconnect-success");

            ShowBalloon(
                "Talvora bağlantısı hazır",
                "ChatGPT Business tunnel yeniden bağlandı.",
                ToolTipIcon.Info);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Reconnect failed", ex);
            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Error,
                "connection",
                "Talvora yeniden bağlanamadı",
                "Ayrıntılar ham günlükte bulunuyor.",
                "talvora",
                "connection:talvora:tray-reconnect-failed");
            var localHealthy =
                await BusinessTunnelClient.IsLocalMcpHealthyAsync(
                    _lifetimeCts.Token);

            SetTalvoraStatus(new TalvoraStatus(
                localHealthy
                    ? TalvoraConnectionState.LocalOnly
                    : TalvoraConnectionState.Offline,
                localHealthy
                    ? "Talvora çalışıyor, tunnel bağlı değil"
                    : "Talvora erişilemiyor",
                ex.Message));

            ShowBalloon(
                "Talvora yeniden bağlanamadı",
                ex.Message,
                ToolTipIcon.Error);
        }
        finally
        {
            SetTalvoraActionsEnabled(true);
            if (lockTaken)
            {
                _talvoraOperationGate.Release();
            }
        }
    }

    private async Task MaintainGiteaConnectionAsync()
    {
        var lockTaken = false;
        try
        {
            lockTaken = await _giteaOperationGate.WaitAsync(
                0,
                _lifetimeCts.Token);
            if (!lockTaken)
            {
                return;
            }

            if (ManagedMcpSessionState.IsManuallyStopped("gitea"))
            {
                _giteaRecoveryState.ResetForManualAction();
                _giteaTimer.Interval = 30_000;
                SetGiteaStatus(new GiteaStatus(
                    GiteaConnectionState.Offline,
                    "Gitea MCP elle durduruldu",
                    "Yönetim Merkezi üzerinden Başlat seçilene kadar otomatik kurtarma devre dışı."));
                return;
            }

            var status = await GiteaTrayClient.GetStatusAsync(
                _lifetimeCts.Token);

            if (status.State == GiteaConnectionState.Running)
            {
                var recoveredSeriousIncident =
                    _giteaRecoveryState.ResetHealthy();

                _giteaTimer.Interval = 30_000;
                SetGiteaStatus(status);

                if (recoveredSeriousIncident)
                {
                    NotifyRecoveryResolved(
                        "Gitea MCP",
                        "Gitea, Caddy, MCP sunucusu ve güvenli tünel yeniden hazır.");
                }

                return;
            }

            SetGiteaStatus(status);
            _giteaTimer.Interval = 10_000;

            var nowUtc = DateTimeOffset.UtcNow;
            if (!_giteaRecoveryState.CanAttempt(nowUtc))
            {
                return;
            }

            ManagedMcpSessionState.ClearManualStop("gitea");
            _giteaRecoveryState.ResetForManualAction();

            SetGiteaActionsEnabled(false);
            SetGiteaStatus(new GiteaStatus(
                GiteaConnectionState.Restarting,
                "Gitea MCP otomatik onarılıyor...",
                "Gitea, Caddy, MCP sunucusu ve güvenli tünel yeniden hazırlanıyor."));

            try
            {
                var registration = _giteaRegistration ??
                    await ResolveManagedRegistrationAsync("gitea");

                if (registration is null)
                {
                    throw new InvalidOperationException(
                        "Gitea MCP yönetim kaydı bulunamadı.");
                }

                var repaired = status.State == GiteaConnectionState.Offline
                    ? await ControlCenterLifecycleService.StartAsync(
                        registration,
                        _lifetimeCts.Token)
                    : await ControlCenterLifecycleService.RestartAsync(
                        registration,
                        _lifetimeCts.Token);

                _ = repaired;

                status = await GiteaTrayClient.GetStatusAsync(
                    _lifetimeCts.Token);

                if (status.State != GiteaConnectionState.Running)
                {
                    throw new InvalidOperationException(
                        $"Otomatik kurtarma tamamlandı ancak Gitea MCP hazır değil: {status.Detail}");
                }

                var recoveredSeriousIncident =
                    _giteaRecoveryState.ResetHealthy();

                SetGiteaStatus(status);
                _giteaTimer.Interval = 30_000;
                TrayLog.Write("Automatic Gitea MCP recovery succeeded.");
                ControlCenterEventStore.Record(
                    ControlCenterEventSeverity.Info,
                    "recovery",
                    "Gitea MCP otomatik olarak düzeltildi",
                    "Gitea, Caddy, MCP sunucusu ve güvenli tünel yeniden hazır.",
                    "gitea",
                    "recovery:gitea:success");

                if (recoveredSeriousIncident)
                {
                    NotifyRecoveryResolved(
                        "Gitea MCP",
                        "Gitea MCP zinciri otomatik olarak düzeltildi.");
                }
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
            {
                return;
            }
            catch (ManagedMcpOperationInProgressException)
            {
                SetGiteaStatus(new GiteaStatus(
                    GiteaConnectionState.Restarting,
                    "Gitea MCP üzerinde işlem sürüyor...",
                    "Kullanıcı tarafından başlatılmış yaşam döngüsü işlemi tamamlanıyor."));
                return;
            }
            catch (Exception ex)
            {
                var failure = _giteaRecoveryState.RegisterFailure(nowUtc);

                TrayLog.Write(
                    $"Automatic Gitea recovery failed. Attempt={failure.ConsecutiveFailures}; RetryIn={failure.RetryDelay.TotalSeconds:F0}s",
                    ex);
                ControlCenterEventStore.Record(
                    ControlCenterEventSeverity.Warning,
                    "recovery",
                    "Gitea MCP otomatik kurtarma denemesi başarısız",
                    $"Deneme {failure.ConsecutiveFailures}; {failure.RetryDelay.TotalSeconds:F0} saniye sonra yeniden denenecek.",
                    "gitea",
                    "recovery:gitea:failed");

                SetGiteaStatus(new GiteaStatus(
                    status.State == GiteaConnectionState.Offline
                        ? GiteaConnectionState.Offline
                        : GiteaConnectionState.Degraded,
                    "Gitea MCP otomatik kurtarma bekliyor",
                    $"Otomatik yeniden deneme {failure.RetryDelay.TotalSeconds:F0} saniye sonra."));

                HandleRecoveryFailure(
                    _giteaRecoveryState,
                    "Gitea MCP",
                    failure,
                    ex.Message);
            }
            finally
            {
                SetGiteaActionsEnabled(true);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            var failure = _giteaRecoveryState.RegisterFailure(
                DateTimeOffset.UtcNow);
            _giteaTimer.Interval = 10_000;

            TrayLog.Write(
                $"Gitea maintenance failed. Attempt={failure.ConsecutiveFailures}; RetryIn={failure.RetryDelay.TotalSeconds:F0}s",
                ex);
            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Warning,
                "recovery",
                "Gitea MCP sağlık bakımı başarısız",
                $"Deneme {failure.ConsecutiveFailures}; {failure.RetryDelay.TotalSeconds:F0} saniye sonra yeniden denenecek.",
                "gitea",
                "recovery:gitea:maintenance-failed");

            SetGiteaStatus(new GiteaStatus(
                GiteaConnectionState.Degraded,
                "Gitea MCP durumu hazırlanıyor",
                $"Otomatik yeniden deneme {failure.RetryDelay.TotalSeconds:F0} saniye sonra."));

            HandleRecoveryFailure(
                _giteaRecoveryState,
                "Gitea MCP",
                failure,
                ex.Message);
        }
        finally
        {
            if (lockTaken)
            {
                _giteaOperationGate.Release();
            }
        }
    }

    private async Task RefreshGiteaStatusAsync(bool showBalloon)
    {
        var lockTaken = false;
        try
        {
            lockTaken = await _giteaOperationGate.WaitAsync(
                0,
                _lifetimeCts.Token);
            if (!lockTaken)
            {
                return;
            }

            if (ManagedMcpSessionState.IsManuallyStopped("gitea"))
            {
                var stopped = new GiteaStatus(
                    GiteaConnectionState.Offline,
                    "Gitea MCP elle durduruldu",
                    "Yönetim Merkezi üzerinden Başlat seçilene kadar otomatik kurtarma devre dışı.");
                SetGiteaStatus(stopped);

                if (showBalloon)
                {
                    ShowBalloon(stopped.Summary, stopped.Detail, ToolTipIcon.Info);
                }

                return;
            }

            if (ManagedMcpSessionState.IsManuallyStopped("gitea"))
            {
                var stopped = new GiteaStatus(
                    GiteaConnectionState.Offline,
                    "Gitea MCP elle durduruldu",
                    "Yönetim Merkezi üzerinden Başlat seçilene kadar otomatik kurtarma devre dışı.");
                SetGiteaStatus(stopped);

                if (showBalloon)
                {
                    ShowBalloon(stopped.Summary, stopped.Detail, ToolTipIcon.Info);
                }

                return;
            }

            var status = await GiteaTrayClient.GetStatusAsync(
                _lifetimeCts.Token);
            SetGiteaStatus(status);

            if (showBalloon)
            {
                ShowBalloon(
                    status.Summary,
                    status.Detail,
                    status.State == GiteaConnectionState.Running
                        ? ToolTipIcon.Info
                        : ToolTipIcon.Warning);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Gitea status refresh failed", ex);
            SetGiteaStatus(new GiteaStatus(
                GiteaConnectionState.Offline,
                "Gitea durumu alınamadı",
                ex.Message));

            if (showBalloon)
            {
                ShowBalloon("Gitea", ex.Message, ToolTipIcon.Error);
            }
        }
        finally
        {
            if (lockTaken)
            {
                _giteaOperationGate.Release();
            }
        }
    }

    private async Task RestartGiteaAsync()
    {
        var lockTaken = false;
        try
        {
            lockTaken = await _giteaOperationGate.WaitAsync(
                0,
                _lifetimeCts.Token);
            if (!lockTaken)
            {
                return;
            }

            SetGiteaActionsEnabled(false);
            SetGiteaStatus(new GiteaStatus(
                GiteaConnectionState.Restarting,
                "Gitea yeniden başlatılıyor...",
                "Talvora MCP üzerinden servis zinciri yeniden hazırlanıyor."));

            var status = await GiteaTrayClient.RestartAsync(
                _lifetimeCts.Token);
            SetGiteaStatus(status);

            if (status.State == GiteaConnectionState.Running)
            {
                ShowBalloon(
                    "Gitea yeniden başlatıldı",
                    "Gitea MCP zinciri sağlıklı ve kullanıma hazır.",
                    ToolTipIcon.Info);
                TrayLog.Write("Gitea service restart succeeded.");
                ControlCenterEventStore.Record(
                    ControlCenterEventSeverity.Info,
                    "operation",
                    "Gitea MCP yeniden başlatıldı",
                    "Gitea MCP zinciri sağlıklı ve kullanıma hazır.",
                    "gitea",
                    "operation:gitea:tray-restart-success");
            }
            else
            {
                ControlCenterEventStore.Record(
                    ControlCenterEventSeverity.Warning,
                    "operation",
                    "Gitea yeniden başladı ancak hazır değil",
                    status.Detail,
                    "gitea",
                    "operation:gitea:tray-restart-degraded");
                ShowBalloon(
                    "Gitea yeniden başladı ancak hazır değil",
                    status.Detail,
                    ToolTipIcon.Warning);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Gitea service restart failed", ex);
            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Error,
                "operation",
                "Gitea MCP yeniden başlatılamadı",
                "Ayrıntılar ham günlükte bulunuyor.",
                "gitea",
                "operation:gitea:tray-restart-failed");
            SetGiteaStatus(new GiteaStatus(
                GiteaConnectionState.Offline,
                "Gitea yeniden başlatılamadı",
                ex.Message));
            ShowBalloon("Gitea", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            SetGiteaActionsEnabled(true);
            if (lockTaken)
            {
                _giteaOperationGate.Release();
            }
        }
    }

    private void SetTalvoraStatus(TalvoraStatus status)
    {
        if (_disposed)
        {
            return;
        }

        _talvoraStatus = status;
        _talvoraStatusItem.Text = status.Summary;
        UpdateAggregateTrayState();
    }

    private void SetGiteaStatus(GiteaStatus status)
    {
        if (_disposed)
        {
            return;
        }

        _giteaStatus = status;
        _giteaStatusItem.Text = status.Summary;
        UpdateAggregateTrayState();
    }

    private void UpdateAggregateTrayState()
    {
        if (_disposed)
        {
            return;
        }

        TalvoraConnectionState aggregateState;
        string summary;
        string detail;

        if (_talvoraStatus.State == TalvoraConnectionState.Offline ||
            _giteaStatus.State == GiteaConnectionState.Offline)
        {
            aggregateState = TalvoraConnectionState.Offline;
            summary = "Müdahale gerekiyor";
            detail = BuildAggregateDetail();
        }
        else if (_talvoraStatus.State == TalvoraConnectionState.Ready &&
                 _giteaStatus.State == GiteaConnectionState.Running)
        {
            aggregateState = TalvoraConnectionState.Ready;
            summary = "Her şey hazır";
            detail = "Talvora MCP ve Gitea MCP hazır.";
        }
        else
        {
            aggregateState = TalvoraConnectionState.LocalOnly;
            var attentionCount =
                (_talvoraStatus.State == TalvoraConnectionState.Ready ? 0 : 1) +
                (_giteaStatus.State == GiteaConnectionState.Running ? 0 : 1);
            summary = $"{attentionCount} MCP dikkat istiyor";
            detail = BuildAggregateDetail();
        }

        _statusItem.Text = summary;

        var tooltip = detail.Length == 0
            ? summary
            : $"{summary} - {detail}";
        _notifyIcon.Text = tooltip.Length <= 63
            ? tooltip
            : tooltip[..63];

        var color = aggregateState switch
        {
            TalvoraConnectionState.Ready => Color.FromArgb(49, 196, 112),
            TalvoraConnectionState.LocalOnly => Color.FromArgb(240, 178, 52),
            _ => Color.FromArgb(220, 72, 72),
        };

        var previous = _statusIcon;
        _statusIcon = TrayIconFactory.CreateStatusIcon(
            aggregateState,
            color);
        _notifyIcon.Icon = _statusIcon;
        previous?.Dispose();
    }

    private string BuildAggregateDetail() =>
        $"Talvora: {_talvoraStatus.Summary}; Gitea: {_giteaStatus.Summary}";

    private void SetTalvoraActionsEnabled(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        _reconnectItem.Enabled = enabled;
        _refreshTalvoraItem.Enabled = enabled;
    }

    private void SetGiteaActionsEnabled(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        _openGiteaItem.Enabled = enabled;
        _restartGiteaItem.Enabled = enabled;
        _refreshGiteaItem.Enabled = enabled;
    }

    private async Task<ManagedMcpRegistration?> ResolveManagedRegistrationAsync(
        string mcpId)
    {
        var registry = await ManagedMcpRegistryCoordinator.LoadOrRecoverAsync(
            _lifetimeCts.Token);

        var registration = registry.Mcps.FirstOrDefault(entry =>
            string.Equals(
                entry.Id,
                mcpId,
                StringComparison.OrdinalIgnoreCase));

        if (string.Equals(mcpId, "talvora", StringComparison.OrdinalIgnoreCase))
        {
            _talvoraRegistration = registration;
        }
        else if (string.Equals(mcpId, "gitea", StringComparison.OrdinalIgnoreCase))
        {
            _giteaRegistration = registration;
        }

        return registration;
    }

    private void HandleRecoveryFailure(
        ManagedMcpRecoveryState recoveryState,
        string displayName,
        ManagedMcpRecoveryFailure failure,
        string technicalDetail)
    {
        if (!failure.BecameSerious ||
            !recoveryState.MarkSeriousIncidentNotified())
        {
            return;
        }

        TrayLog.Write(
            $"Serious recovery incident detected. MCP={recoveryState.McpId}; Failures={failure.ConsecutiveFailures}; Detail={technicalDetail}");
        ControlCenterEventStore.Record(
            ControlCenterEventSeverity.Error,
            "recovery",
            $"{displayName}: müdahale gerekiyor",
            $"Otomatik kurtarma {failure.ConsecutiveFailures} kez başarısız oldu.",
            recoveryState.McpId,
            $"recovery:{recoveryState.McpId}:serious");

        ShowBalloon(
            $"{displayName}: müdahale gerekiyor",
            "Otomatik kurtarma birkaç kez başarısız oldu. Yönetim Merkezi ayrıntıları gösterecek.",
            ToolTipIcon.Warning);

        _controlCenterApplication.ShowControlCenter();
    }

    private void NotifyRecoveryResolved(
        string displayName,
        string detail)
    {
        TrayLog.Write($"Serious recovery incident resolved. MCP={displayName}");
        ControlCenterEventStore.Record(
            ControlCenterEventSeverity.Info,
            "recovery",
            $"{displayName} yeniden hazır",
            detail,
            displayName.StartsWith("Gitea", StringComparison.OrdinalIgnoreCase)
                ? "gitea"
                : "talvora",
            $"recovery:{displayName}:serious-resolved");

        ShowBalloon(
            $"{displayName} yeniden hazır",
            detail,
            ToolTipIcon.Info);
    }

    private void OpenGitea()
    {
        try
        {
            Process.Start(
                new ProcessStartInfo(GiteaUrl)
                {
                    UseShellExecute = true,
                });
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            TrayLog.Write("Opening Gitea in browser failed", ex);
            ShowBalloon("Gitea açılamadı", ex.Message, ToolTipIcon.Error);
        }
    }

    private void ShowBalloon(
        string title,
        string text,
        ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text.Length <= 240
            ? text
            : text[..240];
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void ExitTray()
    {
        if (_disposed)
        {
            return;
        }

        _talvoraTimer.Stop();
        _giteaTimer.Stop();
        _shutdownTimer.Stop();
        _lifetimeCts.Cancel();
        _notifyIcon.Visible = false;
        _controlCenterApplication.ExitFromTray();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _lifetimeCts.Cancel();
            _talvoraTimer.Stop();
            _giteaTimer.Stop();
            _shutdownTimer.Stop();
            _talvoraTimer.Dispose();
            _giteaTimer.Dispose();
            _shutdownTimer.Dispose();
            _shutdownEvent.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip = null;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _statusIcon?.Dispose();

            // Async operations may still be unwinding after cancellation.
            // Keep gates and CTS alive so their finally blocks remain valid.
        }

        base.Dispose(disposing);
    }
}