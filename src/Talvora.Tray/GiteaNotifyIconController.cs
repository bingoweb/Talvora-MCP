using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Talvora.Tray;

internal sealed class GiteaNotifyIconController : IDisposable
{
    private const string GiteaUrl = "http://127.0.0.1:3000/";

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _restartItem;
    private readonly ToolStripMenuItem _refreshItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();

    private Icon? _statusIcon;
    private bool _disposed;

    public GiteaNotifyIconController()
    {
        _statusItem = new ToolStripMenuItem("Gitea durumu kontrol ediliyor...")
        {
            Enabled = false,
        };

        _openItem = new ToolStripMenuItem("Gitea'yı Aç");
        _openItem.Click += (_, _) => OpenGitea();

        _restartItem = new ToolStripMenuItem("Gitea'yı Yeniden Başlat");
        _restartItem.Click += async (_, _) => await RestartAsync();

        _refreshItem = new ToolStripMenuItem("Durumu Yenile");
        _refreshItem.Click += async (_, _) => await RefreshStatusAsync(showBalloon: true);

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_openItem);
        _menu.Items.Add(_restartItem);
        _menu.Items.Add(_refreshItem);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Text = "Gitea durumu kontrol ediliyor...",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenGitea();

        SetState(
            GiteaConnectionState.Restarting,
            "Gitea durumu kontrol ediliyor...",
            "Yerel Gitea health endpoint'i kontrol ediliyor.");

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 10_000,
            Enabled = true,
        };
        _timer.Tick += async (_, _) => await RefreshStatusAsync(showBalloon: false);

        TrayLog.Write("Gitea tray icon initialized.");
        _ = RefreshStatusAsync(showBalloon: false);
    }

    private async Task RefreshStatusAsync(bool showBalloon)
    {
        if (_disposed)
        {
            return;
        }

        var lockTaken = false;
        try
        {
            lockTaken = await _operationGate.WaitAsync(
                0,
                _lifetimeCts.Token);
            if (!lockTaken)
            {
                return;
            }

            var status = await GiteaTrayClient.GetStatusAsync(
                _lifetimeCts.Token);
            SetState(status.State, status.Summary, status.Detail);

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
            SetState(
                GiteaConnectionState.Offline,
                "Gitea durumu alınamadı",
                ex.Message);

            if (showBalloon)
            {
                ShowBalloon("Gitea", ex.Message, ToolTipIcon.Error);
            }
        }
        finally
        {
            if (lockTaken)
            {
                _operationGate.Release();
            }
        }
    }

    private async Task RestartAsync()
    {
        if (_disposed)
        {
            return;
        }

        var lockTaken = false;
        try
        {
            lockTaken = await _operationGate.WaitAsync(
                0,
                _lifetimeCts.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        if (!lockTaken)
        {
            return;
        }

        _restartItem.Enabled = false;
        _refreshItem.Enabled = false;
        _openItem.Enabled = false;

        try
        {
            SetState(
                GiteaConnectionState.Restarting,
                "Gitea yeniden başlatılıyor...",
                "Talvora MCP üzerinden Windows servisi yeniden başlatılıyor.");

            var status = await GiteaTrayClient.RestartAsync(_lifetimeCts.Token);
            SetState(status.State, status.Summary, status.Detail);

            if (status.State == GiteaConnectionState.Running)
            {
                ShowBalloon(
                    "Gitea yeniden başlatıldı",
                    "Yerel Gitea servisi sağlıklı ve kullanıma hazır.",
                    ToolTipIcon.Info);
                TrayLog.Write("Gitea service restart succeeded.");
            }
            else
            {
                ShowBalloon(
                    "Gitea yeniden başladı ancak hazır değil",
                    status.Detail,
                    ToolTipIcon.Warning);
                TrayLog.Write(
                    $"Gitea service restart completed but health is not ready. Detail={status.Detail}");
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Gitea service restart failed", ex);
            SetState(
                GiteaConnectionState.Offline,
                "Gitea yeniden başlatılamadı",
                ex.Message);
            ShowBalloon("Gitea", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            if (!_disposed)
            {
                _restartItem.Enabled = true;
                _refreshItem.Enabled = true;
                _openItem.Enabled = true;
            }

            _operationGate.Release();
        }
    }

    private void SetState(
        GiteaConnectionState state,
        string summary,
        string detail)
    {
        if (_disposed)
        {
            return;
        }

        _statusItem.Text = summary;

        var tooltip = detail.Length == 0
            ? $"Gitea - {summary}"
            : $"Gitea - {summary} - {detail}";
        _notifyIcon.Text = tooltip.Length <= 63
            ? tooltip
            : tooltip[..63];

        var previous = _statusIcon;
        _statusIcon = TrayIconFactory.CreateGiteaStatusIcon(state);
        _notifyIcon.Icon = _statusIcon;
        previous?.Dispose();
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
        ToolTipIcon icon)
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCts.Cancel();
        _timer.Stop();
        _timer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _statusIcon?.Dispose();

        // Do not dispose the async gate/CTS here. An in-flight status or restart
        // operation may still be unwinding after cancellation and must be able
        // to release the gate safely during tray shutdown.
    }
}
