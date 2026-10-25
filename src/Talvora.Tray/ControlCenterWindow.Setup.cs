using System.Windows;
using Talvora.Tray.Controls;
using Talvora.Tray.ViewModels;
using UiButton = Wpf.Ui.Controls.Button;

namespace Talvora.Tray;

internal sealed partial class ShellWindow
{
    private StepperCard BuildSetupCard()
    {
        _settingsModel ??= new SettingsViewModel();
        var card = new StepperCard(_settingsModel);
        _setupDevTunnelIdBox = card.DevIdBox;
        _setupAdminTunnelIdBox = card.AdminIdBox;
        _setupAdminKeyBox = card.AdminPasswordBox;
        _setupDetailText = card.Details;
        _setupActionButton = card.TunnelAction;
        _setupDevTunnelIdBox.TextChanged += (_, _) => RefreshSetupActionAvailability();
        _setupAdminTunnelIdBox.TextChanged += (_, _) => RefreshSetupActionAvailability();
        _setupAdminKeyBox.PasswordChanged += (_, _) => RefreshSetupActionAvailability();
        card.SetupRequested += async (_, _) => await CompleteSetupFromDashboardAsync();
        card.RetryRequested += async (_, _) => await RefreshDashboardAsync();
        return card;
    }

    private void RefreshSetupCard()
    {
        if (_setupCard is null || _snapshot is null || _settingsModel is null)
            return;

        var registry = new Talvora.Shared.ManagedMcpRegistryDocument
        {
            Mcps = _snapshot.Mcps.Select(state => state.Registration).ToList()
        };
        var setup = ControlCenterSetupService.Evaluate(registry);
        _setupState = setup;
        var coreReady = _snapshot.Mcps.Any(state =>
            string.Equals(state.Registration.Id, "talvora", StringComparison.OrdinalIgnoreCase) &&
            state.Health == ControlCenterHealthState.Ready);
        _settingsModel.UpdateSetup(coreReady, setup);
        _settingsModel.ShowAclNotice = ProgramDataAclCheck.IsStale(ProgramDataAclCheck.DefaultRoot);
        _setupCard.Details.Text = setup.Detail;
        _setupCard.Visibility = Visibility.Visible;

        var needsDev = setup.MissingTunnelRegistrationIds.Contains(
            "talvora-dev", StringComparer.OrdinalIgnoreCase);
        var needsAdmin = setup.MissingTunnelRegistrationIds.Contains(
            "talvora-admin", StringComparer.OrdinalIgnoreCase);
        _setupCard.ShowTunnelIds(needsDev, needsAdmin);

        if (!_settingsModel.StepThree.IsActive)
            _setupAdminKeyBox.Password = string.Empty;
        if (_settingsModel.AllComplete)
        {
            _setupDevTunnelIdBox.Text = string.Empty;
            _setupAdminTunnelIdBox.Text = string.Empty;
            _setupAdminKeyBox.Password = string.Empty;
        }

        _setupActionButton = _settingsModel.StepTwo.IsActive
            ? _setupCard.TunnelAction : _setupCard.AdminAction;
        _setupCard.TunnelAction.Content = setup.RuntimeFoundationMissing
            ? "Yeniden denetle" : "Tünelleri bağla / kurulumu tamamla";
        RefreshSetupActionAvailability();
    }

    private void RefreshSetupActionAvailability()
    {
        if (_setupState is null || _setupCard is null || _settingsModel is null)
            return;

        if (_settingsModel.AllComplete)
        {
            _setupCard.TunnelAction.IsEnabled = false;
            _setupCard.AdminAction.IsEnabled = false;
            return;
        }

        var allExistingTunnelIdsProvided = _setupState.MissingTunnelRegistrationIds.All(id =>
            id switch
            {
                "talvora-dev" =>
                    ManagedMcpTunnelProvisioningService.IsValidTunnelId(
                        _setupDevTunnelIdBox.Text?.Trim()),
                "talvora-admin" =>
                    ManagedMcpTunnelProvisioningService.IsValidTunnelId(
                        _setupAdminTunnelIdBox.Text?.Trim()),
                _ => false,
            });

        _setupCard.TunnelAction.IsEnabled = _settingsModel.StepTwo.IsActive &&
            (_setupState.RuntimeFoundationMissing ||
             _setupState.MissingTunnelRegistrationIds.Count == 0 ||
             allExistingTunnelIdsProvided);
        _setupCard.AdminAction.IsEnabled = _settingsModel.StepThree.IsActive &&
            !string.IsNullOrWhiteSpace(_setupAdminKeyBox.Password);
    }

    private async Task CompleteSetupFromDashboardAsync()
    {
        if (_setupActionButton is null ||
            _lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        _setupActionButton.IsEnabled = false;
        char[]? adminKeyBuffer = null;
        var existingTunnelIds =
            new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase);

        if (_setupDevTunnelIdBox.Visibility == Visibility.Visible &&
            !string.IsNullOrWhiteSpace(_setupDevTunnelIdBox.Text))
        {
            existingTunnelIds["talvora-dev"] =
                _setupDevTunnelIdBox.Text.Trim();
        }

        if (_setupAdminTunnelIdBox.Visibility == Visibility.Visible &&
            !string.IsNullOrWhiteSpace(_setupAdminTunnelIdBox.Text))
        {
            existingTunnelIds["talvora-admin"] =
                _setupAdminTunnelIdBox.Text.Trim();
        }

        try
        {
            if (_setupAdminKeyBox.Visibility == Visibility.Visible)
            {
                adminKeyBuffer = ControlCenterAdminCredentialStore.CopyTransientKey(
                    _setupAdminKeyBox.Password);
            }

            var state = await ControlCenterSetupService.CompleteAsync(
                adminKeyBuffer,
                existingTunnelIds,
                _lifetimeCts.Token);

            _setupDevTunnelIdBox.Text = string.Empty;
            _setupAdminTunnelIdBox.Text = string.Empty;
            _setupAdminKeyBox.Password = string.Empty;

            if (state.IsComplete)
            {
                _setupCard.Visibility = Visibility.Visible;
                await RefreshDashboardAsync();
                return;
            }

            _setupDetailText.Text = state.Detail;
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Control Center setup completion failed", ex);
            ControlCenterEventStore.Record(
                ControlCenterEventSeverity.Warning,
                "setup",
                "Kurulum tamamlanamadı",
                GetFriendlySetupError(ex),
                dedupKey: "setup:completion-failed");

            _setupDetailText.Text = GetFriendlySetupError(ex);
            await ShowOperationErrorAsync(
                "Kurulum tamamlanamadı",
                "Kurulum tamamlanamadı. Son olay ve ham günlük ayrıntılarını kontrol edin.");
        }
        finally
        {
            ControlCenterAdminCredentialStore.ClearTransientKey(adminKeyBuffer);
            adminKeyBuffer = null;
            existingTunnelIds.Clear();
            _setupDevTunnelIdBox.Text = string.Empty;
            _setupAdminTunnelIdBox.Text = string.Empty;
            _setupAdminKeyBox.Password = string.Empty;
            RefreshSetupCard();
        }
    }

    private static string GetFriendlySetupError(Exception exception)
    {
        if (exception.Message.Contains(
                "tunnel ID",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return "Mevcut tunnel ID geçersiz veya bu MCP kaydıyla bağlanamadı. OpenAI tunnel kaydını ve ID biçimini kontrol edin.";
        }

        if (exception.Message.Contains(
                "Admin API key",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return "Yeni tünel oluşturmak için geçerli OpenAI Admin API key gerekiyor.";
        }

        if (exception.Message.Contains(
                "Runtime API key",
                StringComparison.CurrentCultureIgnoreCase) ||
            exception.Message.Contains(
                "temel kurulum",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return "Güvenli MCP tünelinin Runtime temel kurulumu eksik. Mevcut Business tünel kurulumunu tamamlayıp yeniden deneyin.";
        }

        return "Kurulum tamamlanamadı. Son olay ve ham günlük ayrıntılarını kontrol edin.";
    }
}