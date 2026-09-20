using System.Diagnostics;
using System.Windows;
using Talvora.Shared;
using Wpf.Ui.Controls;
using UiButton = Wpf.Ui.Controls.Button;

namespace Talvora.Tray;

internal sealed partial class ControlCenterWindow
{
    private readonly record struct CardContextAction(
        string Text,
        SymbolRegular Icon,
        bool Primary);

    private static CardContextAction GetCardContextAction(
        ManagedMcpDashboardState state)
    {
        if (state.Health == ControlCenterHealthState.Offline)
        {
            return new CardContextAction(
                "Başlat",
                SymbolRegular.Play20,
                Primary: true);
        }

        if (state.Health == ControlCenterHealthState.Attention)
        {
            return string.Equals(
                    state.Registration.Id,
                    "talvora",
                    StringComparison.OrdinalIgnoreCase)
                ? new CardContextAction(
                    "Yeniden bağlan",
                    SymbolRegular.PlugConnected20,
                    Primary: true)
                : new CardContextAction(
                    "Sorunu düzelt",
                    SymbolRegular.Wrench20,
                    Primary: true);
        }

        if (state.Health == ControlCenterHealthState.Ready &&
            string.Equals(
                state.Registration.Id,
                "gitea",
                StringComparison.OrdinalIgnoreCase))
        {
            return new CardContextAction(
                "Aç",
                SymbolRegular.Open20,
                Primary: false);
        }

        return new CardContextAction(
            "Ayrıntılar",
            SymbolRegular.ChevronRight20,
            Primary: false);
    }

    private async Task ExecuteCardContextActionAsync(
        ManagedMcpDashboardState state,
        UiButton sourceButton)
    {
        if (_detailOperationInProgress)
        {
            return;
        }

        sourceButton.IsEnabled = false;

        try
        {
            if (state.Health == ControlCenterHealthState.Ready)
            {
                if (string.Equals(
                        state.Registration.Id,
                        "gitea",
                        StringComparison.OrdinalIgnoreCase))
                {
                    OpenGiteaHome();
                    return;
                }

                await ShowDetailAsync(state);
                return;
            }

            if (state.Health == ControlCenterHealthState.Checking)
            {
                await ShowDetailAsync(state);
                return;
            }

            if (state.Health == ControlCenterHealthState.Offline)
            {
                var result = await ControlCenterLifecycleService.StartAsync(
                    state.Registration,
                    _lifetimeCts.Token);
                RecordLifecycleSuccess(state.Registration, result);
            }
            else if (string.Equals(
                         state.Registration.Id,
                         "talvora",
                         StringComparison.OrdinalIgnoreCase))
            {
                ManagedMcpSessionState.ClearManualStop(state.Registration.Id);
                await BusinessTunnelClient.ReconnectAsync(_lifetimeCts.Token);
                ControlCenterEventStore.Record(
                    ControlCenterEventSeverity.Info,
                    "connection",
                    "Talvora bağlantısı yenilendi",
                    "ChatGPT Business güvenli MCP tüneli yeniden bağlandı.",
                    state.Registration.Id,
                    "connection:talvora:manual-reconnect-success");
            }
            else
            {
                var result = await ControlCenterLifecycleService.RestartAsync(
                    state.Registration,
                    _lifetimeCts.Token);
                RecordLifecycleSuccess(state.Registration, result);
            }

            await RefreshDashboardAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write(
                $"Control Center contextual action failed. MCP={state.Registration.Id}",
                ex);

            var friendly = GetFriendlyOperationError(ex);
            RecordOperationFailure(
                state.Registration,
                "Bağlamsal işlem başarısız",
                friendly);

            await ShowOperationErrorAsync(
                $"{state.Registration.DisplayName} işlemi tamamlanamadı",
                friendly);
        }
        finally
        {
            sourceButton.IsEnabled = true;
        }
    }

    private async Task ExecuteDetailPrimaryActionAsync()
    {
        var state = _selectedMcp;
        if (state is null || _detailOperationInProgress)
        {
            return;
        }

        var manuallyStopped =
            ControlCenterLifecycleService.IsManuallyStopped(state.Registration);

        if (manuallyStopped ||
            state.Health == ControlCenterHealthState.Offline)
        {
            await ExecuteDetailLifecycleOperationAsync(
                ManagedMcpLifecycleOperation.Start);
            return;
        }

        if (string.Equals(
                state.Registration.Id,
                "gitea",
                StringComparison.OrdinalIgnoreCase) &&
            state.Health == ControlCenterHealthState.Ready)
        {
            try
            {
                OpenGiteaHome();
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or
                System.ComponentModel.Win32Exception)
            {
                TrayLog.Write("Gitea detay sayfasından açılamadı", ex);
                var friendly = GetFriendlyOperationError(ex);
                RecordOperationFailure(
                    state.Registration,
                    "Gitea açılamadı",
                    friendly);
                await ShowOperationErrorAsync(
                    "Gitea açılamadı",
                    friendly);
            }
            return;
        }

        if (string.Equals(
                state.Registration.Id,
                "talvora",
                StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteTalvoraReconnectFromDetailAsync();
            return;
        }

        await ExecuteDetailLifecycleOperationAsync(
            ManagedMcpLifecycleOperation.Restart);
    }

    private async Task ExecuteTalvoraReconnectFromDetailAsync()
    {
        if (_selectedMcp is null || _detailOperationInProgress)
        {
            return;
        }

        SetDetailOperationBusy(
            true,
            "Güvenli MCP tüneli (Secure MCP Tunnel) yeniden bağlanıyor...");

        try
        {
            ManagedMcpSessionState.ClearManualStop(_selectedMcp.Registration.Id);
            await BusinessTunnelClient.ReconnectAsync(_lifetimeCts.Token);
            SetDetailOperationBanner(
                "Bağlantı yenilendi. Talvora MCP hazır.",
                success: true);
            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Info,
                "connection",
                "Talvora bağlantısı yenilendi",
                "ChatGPT Business güvenli MCP tüneli yeniden bağlandı.",
                _selectedMcp.Registration.Id,
                "connection:talvora:manual-reconnect-success");
            await RefreshDashboardAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Control Center Talvora reconnect failed", ex);
            var friendly = GetFriendlyOperationError(ex);
            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Error,
                "connection",
                "Talvora yeniden bağlanamadı",
                friendly,
                _selectedMcp?.Registration.Id ?? "talvora",
                "connection:talvora:manual-reconnect-failed");
            SetDetailOperationBanner(
                friendly,
                success: false);
            await ShowOperationErrorAsync(
                "Talvora yeniden bağlanamadı",
                friendly);
        }
        finally
        {
            SetDetailOperationBusy(false);
        }
    }

    private async Task ExecuteDetailLifecycleOperationAsync(
        ManagedMcpLifecycleOperation operation)
    {
        var state = _selectedMcp;
        if (state is null || _detailOperationInProgress)
        {
            return;
        }

        if (operation == ManagedMcpLifecycleOperation.Stop &&
            !await ConfirmStopAsync(state.Registration))
        {
            return;
        }

        SetDetailOperationBusy(
            true,
            operation switch
            {
                ManagedMcpLifecycleOperation.Start => "MCP başlatılıyor...",
                ManagedMcpLifecycleOperation.Stop => "MCP durduruluyor...",
                _ => "MCP yeniden başlatılıyor...",
            });

        try
        {
            var result = operation switch
            {
                ManagedMcpLifecycleOperation.Start =>
                    await ControlCenterLifecycleService.StartAsync(
                        state.Registration,
                        _lifetimeCts.Token),
                ManagedMcpLifecycleOperation.Stop =>
                    await ControlCenterLifecycleService.StopAsync(
                        state.Registration,
                        _lifetimeCts.Token),
                _ =>
                    await ControlCenterLifecycleService.RestartAsync(
                        state.Registration,
                        _lifetimeCts.Token),
            };

            SetDetailOperationBanner(
                $"{result.Summary}  {result.Detail}",
                success: true);
            RecordLifecycleSuccess(state.Registration, result);

            await RefreshDashboardAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write(
                $"Control Center lifecycle action failed. MCP={state.Registration.Id}; Operation={operation}",
                ex);

            var message = GetFriendlyOperationError(ex);
            RecordOperationFailure(
                state.Registration,
                $"{GetOperationLabel(operation)} işlemi başarısız",
                message);
            SetDetailOperationBanner(message, success: false);
            await ShowOperationErrorAsync(
                $"{state.Registration.DisplayName} işlemi tamamlanamadı",
                message);
        }
        finally
        {
            SetDetailOperationBusy(false);
        }
    }

    private static void RecordLifecycleSuccess(
        ManagedMcpRegistration registration,
        ManagedMcpLifecycleResult result)
    {
        ControlCenterEventStore.Record(
            ControlCenterEventSeverity.Info,
            "operation",
            result.Summary,
            result.Detail,
            registration.Id,
            $"operation:{registration.Id}:{result.Operation}:success");
    }

    private static void RecordOperationFailure(
        ManagedMcpRegistration registration,
        string title,
        string detail)
    {
        ControlCenterEventStore.Record(
            ControlCenterEventSeverity.Error,
            "operation",
            $"{registration.DisplayName}: {title}",
            detail,
            registration.Id,
            $"operation:{registration.Id}:{title}:failed");
    }

    private static string GetOperationLabel(
        ManagedMcpLifecycleOperation operation) =>
        operation switch
        {
            ManagedMcpLifecycleOperation.Start => "Başlatma",
            ManagedMcpLifecycleOperation.Stop => "Durdurma",
            _ => "Yeniden başlatma",
        };

    private async Task<bool> ConfirmStopAsync(
        ManagedMcpRegistration registration)
    {
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = $"{registration.DisplayName} durdurulsun mu?",
            Content =
                "Yerel MCP ve ona ait tünel birlikte durdurulacak. " +
                "Bu Windows oturumunda siz yeniden Başlat seçeneğini kullanana kadar " +
                "otomatik kurtarma bu MCP'yi yeniden başlatmayacak.",
            PrimaryButtonText = "Durdur",
            PrimaryButtonAppearance = ControlAppearance.Danger,
            CloseButtonText = "Vazgeç",
            CloseButtonAppearance = ControlAppearance.Secondary,
        };

        var result = await dialog.ShowDialogAsync(
            showAsDialog: true,
            cancellationToken: _lifetimeCts.Token);

        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private async Task ShowOperationErrorAsync(
        string title,
        string message)
    {
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = title,
            Content = message,
            PrimaryButtonText = "Tamam",
            PrimaryButtonAppearance = ControlAppearance.Primary,
        };

        _ = await dialog.ShowDialogAsync(
            showAsDialog: true,
            cancellationToken: _lifetimeCts.Token);
    }

    private void UpdateDetailActionState(ManagedMcpDashboardState state)
    {
        if (_detailPrimaryActionButton is null)
        {
            return;
        }

        var manuallyStopped =
            ControlCenterLifecycleService.IsManuallyStopped(state.Registration);
        var stopped =
            manuallyStopped ||
            state.Health == ControlCenterHealthState.Offline;

        _detailStartButton.IsEnabled =
            !_detailOperationInProgress && stopped;
        _detailStopButton.IsEnabled =
            !_detailOperationInProgress && !stopped;
        _detailRestartButton.IsEnabled =
            !_detailOperationInProgress && !stopped;

        if (stopped)
        {
            ConfigureButton(
                _detailPrimaryActionButton,
                "Başlat",
                SymbolRegular.Play20);
        }
        else if (string.Equals(
                     state.Registration.Id,
                     "gitea",
                     StringComparison.OrdinalIgnoreCase) &&
                 state.Health == ControlCenterHealthState.Ready)
        {
            ConfigureButton(
                _detailPrimaryActionButton,
                "Gitea'yı aç",
                SymbolRegular.Open20);
        }
        else if (string.Equals(
                     state.Registration.Id,
                     "talvora",
                     StringComparison.OrdinalIgnoreCase))
        {
            ConfigureButton(
                _detailPrimaryActionButton,
                state.Health == ControlCenterHealthState.Ready
                    ? "Bağlantıyı yenile"
                    : "Yeniden bağlan",
                SymbolRegular.PlugConnected20);
        }
        else
        {
            ConfigureButton(
                _detailPrimaryActionButton,
                state.Health == ControlCenterHealthState.Attention
                    ? "Sorunu düzelt"
                    : "Yeniden başlat",
                state.Health == ControlCenterHealthState.Attention
                    ? SymbolRegular.Wrench20
                    : SymbolRegular.ArrowClockwise20);
        }

        _detailPrimaryActionButton.IsEnabled = !_detailOperationInProgress;
    }

    private static void ConfigureButton(
        UiButton button,
        string text,
        SymbolRegular symbol)
    {
        button.Content = text;
        button.Icon = new SymbolIcon { Symbol = symbol };
    }

    private void SetDetailOperationBusy(
        bool busy,
        string? message = null)
    {
        _detailOperationInProgress = busy;

        if (!string.IsNullOrWhiteSpace(message))
        {
            _detailOperationBanner.Visibility = Visibility.Visible;
            _detailOperationBanner.Background = RaisedSurfaceBrush;
            _detailOperationBanner.BorderBrush = StrongBorderBrush;
            _detailOperationText.Foreground = SecondaryTextBrush;
            _detailOperationText.Text = message;
        }

        if (_selectedMcp is not null)
        {
            UpdateDetailActionState(_selectedMcp);
        }

        _refreshButton.IsEnabled = !busy;
    }

    private void SetDetailOperationBanner(
        string message,
        bool success)
    {
        _detailOperationBanner.Visibility = Visibility.Visible;
        _detailOperationBanner.Background =
            success ? ReadySoftBrush : OfflineSoftBrush;
        _detailOperationBanner.BorderBrush =
            success ? ReadyBrush : OfflineBrush;
        _detailOperationText.Foreground =
            success ? ReadyBrush : OfflineBrush;
        _detailOperationText.Text = message;
    }

    private static string GetFriendlyOperationError(Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            return "Bu işlem için gerekli Windows servis izni bulunamadı. " +
                   "Talvora'nın güncel kurulumunu yeniden çalıştırmak gerekebilir.";
        }

        if (exception is TimeoutException)
        {
            return "İşlem başladı ancak bileşenler beklenen sürede hazır duruma gelmedi.";
        }

        var message = exception.Message.Trim();
        return string.IsNullOrWhiteSpace(message)
            ? "İşlem tamamlanamadı. Ayrıntılar Talvora günlüğüne kaydedildi."
            : message;
    }

    private static void OpenGiteaHome()
    {
        using var launched = Process.Start(
            new ProcessStartInfo("http://127.0.0.1:3000/")
            {
                UseShellExecute = true,
            });
    }
}