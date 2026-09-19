using System.Diagnostics;
using Talvora.Shared;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _reconnectItem;
    private readonly ToolStripMenuItem _refreshItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _shutdownTimer;
    private readonly EventWaitHandle _shutdownEvent = new(
        initialState: false,
        EventResetMode.AutoReset,
        @"Local\Talvora.Tray.Shutdown");
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private Icon? _statusIcon;
    private int _automaticReconnectFailures;
    private DateTime _nextAutomaticReconnectUtc = DateTime.MinValue;
    private TalvoraStatus _lastStatus = new(
        TalvoraConnectionState.LocalOnly,
        "Talvora durumu kontrol ediliyor...",
        string.Empty);

    public TrayApplicationContext()
    {
        _statusItem = new ToolStripMenuItem("Talvora durumu kontrol ediliyor...")
        {
            Enabled = false,
        };

        _reconnectItem = new ToolStripMenuItem("ChatGPT Business'a yeniden bağlan");
        _reconnectItem.Click += async (_, _) => await ReconnectAsync();

        _refreshItem = new ToolStripMenuItem("Durumu yenile");
        _refreshItem.Click += async (_, _) => await RefreshStatusAsync(showBalloon: true);

        var exitItem = new ToolStripMenuItem("Tepsi uygulamasından çık");
        exitItem.Click += (_, _) => ExitTray();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_reconnectItem);
        menu.Items.Add(_refreshItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = "Talvora",
            Visible = true,
        };
        _notifyIcon.DoubleClick += async (_, _) => await ReconnectAsync();
        _notifyIcon.MouseClick += async (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _lastStatus.State != TalvoraConnectionState.Ready)
            {
                await ReconnectAsync();
            }
        };

        SetTrayState(
            TalvoraConnectionState.LocalOnly,
            "Talvora durumu kontrol ediliyor...",
            "Çift tıklayın: yeniden bağlan");

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 5_000,
            Enabled = true,
        };
        _timer.Tick += async (_, _) => await MaintainConnectionAsync();

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

        _ = MaintainConnectionAsync();
    }

    private async Task MaintainConnectionAsync()
    {
        if (!await _operationGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var status = await BusinessTunnelClient.GetStatusAsync(CancellationToken.None);
            if (status.State == TalvoraConnectionState.Ready)
            {
                ResetAutomaticReconnectBackoff();
                SetTrayState(status.State, status.Summary, status.Detail);
                return;
            }

            if (status.State == TalvoraConnectionState.Offline)
            {
                _timer.Interval = 5_000;
                _nextAutomaticReconnectUtc = DateTime.UtcNow.AddSeconds(5);
                SetTrayState(
                    TalvoraConnectionState.LocalOnly,
                    "Talvora başlatılıyor...",
                    "Yerel servis bekleniyor; ChatGPT Business otomatik bağlanacak.");
                return;
            }

            SetTrayState(status.State, status.Summary, status.Detail);

            if (DateTime.UtcNow < _nextAutomaticReconnectUtc)
            {
                return;
            }

            _reconnectItem.Enabled = false;
            _refreshItem.Enabled = false;
            SetTrayState(
                TalvoraConnectionState.LocalOnly,
                "ChatGPT Business otomatik bağlanıyor...",
                "Tunnel hazır olana kadar Talvora otomatik yeniden deneyecek.");

            try
            {
                await BusinessTunnelClient.ReconnectAsync(CancellationToken.None);
                status = await BusinessTunnelClient.GetStatusAsync(CancellationToken.None);
                SetTrayState(status.State, status.Summary, status.Detail);

                if (status.State == TalvoraConnectionState.Ready)
                {
                    ResetAutomaticReconnectBackoff();
                    var alias = BusinessTunnelClient.LoadConfig().Alias;
                    TrayLog.Write($"Automatic reconnect succeeded. Alias={alias}");
                    return;
                }

                throw new InvalidOperationException(
                    "Tunnel connect tamamlandı ancak bağlantı henüz hazır değil.");
            }
            catch (Exception ex)
            {
                _automaticReconnectFailures++;
                var delay = GetAutomaticReconnectDelay(_automaticReconnectFailures);
                _nextAutomaticReconnectUtc = DateTime.UtcNow.Add(delay);
                _timer.Interval = 5_000;

                TrayLog.Write(
                    $"Automatic reconnect failed. Attempt={_automaticReconnectFailures}; RetryIn={delay.TotalSeconds:F0}s",
                    ex);

                var localHealthy = await BusinessTunnelClient.IsLocalMcpHealthyAsync(
                    CancellationToken.None);

                SetTrayState(
                    localHealthy ? TalvoraConnectionState.LocalOnly : TalvoraConnectionState.Offline,
                    localHealthy
                        ? "Talvora çalışıyor, Business bağlantısı bekleniyor"
                        : "Talvora servisi bekleniyor",
                    $"Otomatik yeniden deneme {delay.TotalSeconds:F0} saniye sonra.");
            }
            finally
            {
                _reconnectItem.Enabled = true;
                _refreshItem.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            _automaticReconnectFailures++;
            var delay = GetAutomaticReconnectDelay(_automaticReconnectFailures);
            _nextAutomaticReconnectUtc = DateTime.UtcNow.Add(delay);
            _timer.Interval = 5_000;
            TrayLog.Write(
                $"Automatic connection maintenance failed. Attempt={_automaticReconnectFailures}; RetryIn={delay.TotalSeconds:F0}s",
                ex);
            SetTrayState(
                TalvoraConnectionState.LocalOnly,
                "Talvora bağlantısı hazırlanıyor",
                $"Otomatik yeniden deneme {delay.TotalSeconds:F0} saniye sonra.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void ResetAutomaticReconnectBackoff()
    {
        _automaticReconnectFailures = 0;
        _nextAutomaticReconnectUtc = DateTime.MinValue;
        _timer.Interval = 30_000;
    }

    private static TimeSpan GetAutomaticReconnectDelay(int failureCount)
    {
        var seconds = failureCount switch
        {
            <= 1 => 5,
            2 => 10,
            3 => 20,
            4 => 30,
            _ => 60,
        };

        return TimeSpan.FromSeconds(seconds);
    }

    private async Task RefreshStatusAsync(bool showBalloon)
    {
        if (!await _operationGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var status = await BusinessTunnelClient.GetStatusAsync(CancellationToken.None);
            SetTrayState(status.State, status.Summary, status.Detail);
            if (showBalloon)
            {
                ShowBalloon(status.Summary, status.Detail);
            }
        }
        catch (Exception ex)
        {
            TrayLog.Write("Status refresh failed", ex);
            SetTrayState(
                TalvoraConnectionState.Offline,
                "Talvora durumu alınamadı",
                ex.Message);
            if (showBalloon)
            {
                ShowBalloon("Talvora", ex.Message, ToolTipIcon.Error);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ReconnectAsync()
    {
        if (!await _operationGate.WaitAsync(0))
        {
            return;
        }

        _reconnectItem.Enabled = false;
        _refreshItem.Enabled = false;

        try
        {
            SetTrayState(
                TalvoraConnectionState.LocalOnly,
                "ChatGPT Business yeniden bağlanıyor...",
                "Tunnel hazırlanıyor.");

            await BusinessTunnelClient.ReconnectAsync(CancellationToken.None);

            var status = await BusinessTunnelClient.GetStatusAsync(CancellationToken.None);
            SetTrayState(status.State, status.Summary, status.Detail);
            ShowBalloon(
                "Talvora bağlantısı hazır",
                "ChatGPT Business tunnel yeniden bağlandı.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            TrayLog.Write("Reconnect failed", ex);
            var localHealthy = await BusinessTunnelClient.IsLocalMcpHealthyAsync(CancellationToken.None);
            SetTrayState(
                localHealthy ? TalvoraConnectionState.LocalOnly : TalvoraConnectionState.Offline,
                localHealthy ? "Talvora çalışıyor, tunnel bağlı değil" : "Talvora erişilemiyor",
                ex.Message);
            ShowBalloon("Talvora yeniden bağlanamadı", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _reconnectItem.Enabled = true;
            _refreshItem.Enabled = true;
            _operationGate.Release();
        }
    }

    private void SetTrayState(TalvoraConnectionState state, string summary, string detail)
    {
        _lastStatus = new TalvoraStatus(state, summary, detail);
        _statusItem.Text = summary;

        var tooltip = detail.Length == 0 ? summary : $"{summary} - {detail}";
        _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];

        var color = state switch
        {
            TalvoraConnectionState.Ready => Color.FromArgb(49, 196, 112),
            TalvoraConnectionState.LocalOnly => Color.FromArgb(240, 178, 52),
            _ => Color.FromArgb(220, 72, 72),
        };

        var previous = _statusIcon;
        _statusIcon = TrayIconFactory.CreateStatusIcon(state, color);
        _notifyIcon.Icon = _statusIcon;
        previous?.Dispose();
    }

    private void ShowBalloon(
        string title,
        string text,
        ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text.Length <= 240 ? text : text[..240];
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void ExitTray()
    {
        _timer.Stop();
        _shutdownTimer.Stop();
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _shutdownTimer.Dispose();
            _shutdownEvent.Dispose();
            _notifyIcon.Dispose();
            _statusIcon?.Dispose();
            _operationGate.Dispose();
        }

        base.Dispose(disposing);
    }
}
