using System.Windows;
using Talvora.Shared;
using Talvora.Tray.Controls;
using Talvora.Tray.Views;

namespace Talvora.Tray;

/// <summary>UI4 adapters retain the existing lifecycle, repair, confirmation and redaction paths.</summary>
internal sealed partial class ShellWindow
{
    private ServicesView? _servicesView;
    private Task? _ui4RunningOperation;
    private bool _ui4SmokeStopApproved;
    private bool _ui4OperationInProgress;
    private CancellationTokenSource? _ui4DetailsCancellation;

    private void EnsureUi4Page()
    {
        if (_servicesView is null)
        {
            _servicesView = new ServicesView();
            ServicesContentHost.Content = _servicesView;
            _servicesView.RefreshRequested += () => _ = RefreshDashboardAsync();
            _servicesView.ActionRequested += (state, action) =>
                _ui4RunningOperation = RunUi4OperationAsync(state, action);
            _servicesView.DetailOpened += state => _ = LoadUi4DetailsAsync(state);
            _servicesView.Detail.EvidenceRequested += () => _ = LoadUi4EvidenceAsync();
        }

        if (_snapshot is { } snapshot)
            _servicesView.ApplySnapshot(snapshot, DateTimeOffset.Now);
    }

    internal void OpenUi4Detail(ManagedMcpDashboardState state)
    {
        if (_shellModel?.SelectedIndex != 1)
            _shellModel?.SelectPage(1);
        EnsureUi4Page();
        _servicesView?.OpenDetail(state);
    }

    private void UpdateUi4Snapshot(ControlCenterDashboardSnapshot snapshot)
    {
        _servicesView?.ApplySnapshot(snapshot, DateTimeOffset.Now);
    }

    private async Task LoadUi4DetailsAsync(ManagedMcpDashboardState state)
    {
        _ui4DetailsCancellation?.Cancel();
        _ui4DetailsCancellation?.Dispose();
        _ui4DetailsCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var token = _ui4DetailsCancellation.Token;
        try
        {
            var componentTask = ControlCenterComponentHealthService.GetStatesAsync(state.Registration, token);
            var versionTask = ControlCenterVersionService.GetVersionAsync(state.Registration, token);
            var eventTask = ControlCenterEventStore.ReadRecentAsync(
                TimeSpan.FromDays(7), maxRecords: 40, cancellationToken: token);
            await Task.WhenAll(componentTask, versionTask, eventTask);
            if (token.IsCancellationRequested ||
                _servicesView?.Detail.Model.Source?.Registration.Id != state.Registration.Id)
                return;
            _servicesView.Detail.Model.SetDetails(
                await componentTask, await eventTask, await versionTask);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Control Center UI4 service detail read failed", ex);
            if (_servicesView?.Detail.Model.Source?.Registration.Id == state.Registration.Id)
                _servicesView.Detail.Model.IsLoading = false;
        }
    }

    private async Task LoadUi4EvidenceAsync()
    {
        var state = _servicesView?.Detail.Model.Source;
        if (state is null) return;
        try
        {
            var raw = await ControlCenterRawLogService.ReadTailAsync(
                state.Registration, _lifetimeCts.Token);
            if (_servicesView?.Detail.Model.Source?.Registration.Id != state.Registration.Id)
                return;

            // Isolate the selected service's sources from the shared tray log.
            var ownSources = state.Registration.DiscoveryHints
                .Where(h => string.Equals(h.Kind, "log-file", StringComparison.OrdinalIgnoreCase))
                .Select(h => System.IO.Path.GetFileName(h.Value))
                .ToArray();
            var lines = ServiceEvidenceFilter.Filter(raw, state.Registration.Id, ownSources);
            var technical = string.Join(Environment.NewLine,
            [
                "Sürüm: " + _servicesView.Detail.Model.Version,
                "Uç nokta: " + state.Registration.Endpoint,
                "İletim: " + state.Registration.Transport,
                "Güvenli tünel: " + (state.Registration.Tunnel?.Alias ?? "Tanımlı değil"),
                "Bileşenler: " + string.Join("; ",
                    _servicesView.Detail.Model.Components.Select(x => x.Name + " — " + x.Status)),
                "",
                lines,
            ]);
            _servicesView.Detail.Model.SetEvidence(technical);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            TrayLog.Write("Control Center UI4 service evidence read failed", ex);
        }
    }

    /// <summary>Identical service APIs and confirmation semantics as the legacy detail.</summary>
    private async Task RunUi4OperationAsync(ManagedMcpDashboardState state, Ui4ServiceAction action)
    {
        if (_ui4OperationInProgress || _servicesView is null ||
            action == Ui4ServiceAction.Details) return;

        var testFixture = _smokeMode && state.Registration.Id.StartsWith(
            "ui4-smoke-", StringComparison.Ordinal);
        if (action == Ui4ServiceAction.Stop)
        {
            var confirmed = testFixture
                ? _ui4SmokeStopApproved
                : await ConfirmStopAsync(state.Registration);
            if (!confirmed) return;
        }

        if (action == Ui4ServiceAction.Repair &&
            ControlCenterLifecycleService.IsManuallyStopped(state.Registration))
            return;

        _ui4OperationInProgress = true;
        var label = action switch
        {
            Ui4ServiceAction.Start => "Bileşen başlatılıyor...",
            Ui4ServiceAction.Stop => "Bileşen durduruluyor...",
            Ui4ServiceAction.Restart => "Bileşen yeniden başlatılıyor...",
            _ => "Sorun tanılanıyor; en az kesintiyle onarım uygulanıyor...",
        };
        _servicesView.Detail.StartBusy(label);

        try
        {
            string message;
            ManagedMcpDashboardState updated = state;
            if (action == Ui4ServiceAction.Repair)
            {
                var repair = await ControlCenterRepairService.RepairAsync(
                    state.Registration, state, _lifetimeCts.Token);
                if (!testFixture) RecordRepairSuccess(state.Registration, repair);
                message = repair.Summary + "  " + repair.Detail;
                updated = repair.FinalState;
            }
            else
            {
                var result = action switch
                {
                    Ui4ServiceAction.Start => await ControlCenterLifecycleService.StartAsync(
                        state.Registration, _lifetimeCts.Token),
                    Ui4ServiceAction.Stop => await ControlCenterLifecycleService.StopAsync(
                        state.Registration, _lifetimeCts.Token),
                    _ => await ControlCenterLifecycleService.RestartAsync(
                        state.Registration, _lifetimeCts.Token),
                };
                if (!testFixture) RecordLifecycleSuccess(state.Registration, result);
                message = result.Summary + "  " + result.Detail;
            }

            if (_servicesView.Detail.Model.Source?.Registration.Id == state.Registration.Id)
            {
                _servicesView.Detail.Model.ApplyState(updated, DateTimeOffset.Now,
                    ControlCenterLifecycleService.IsManuallyStopped(state.Registration));
                _servicesView.Detail.CompleteBusy(message, success: true);
            }
            if (!testFixture) await RefreshDashboardAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            _servicesView.Detail.CompleteBusy("İşlem iptal edildi.", success: false);
        }
        catch (Exception ex)
        {
            TrayLog.Write($"Control Center UI4 action failed. MCP={state.Registration.Id}; Action={action}", ex);
            var friendly = GetFriendlyOperationError(ex);
            if (!testFixture)
            {
                RecordOperationFailure(state.Registration, "Hizmet işlemi başarısız", friendly);
                await ShowOperationErrorAsync(
                    state.Registration.DisplayName + " işlemi tamamlanamadı", friendly);
            }
            _servicesView.Detail.CompleteBusy(friendly, success: false);
        }
        finally
        {
            _ui4OperationInProgress = false;
        }
    }

    internal void SetUi4SmokeStopApproval(bool allowed)
    {
        if (!_smokeMode)
            throw new InvalidOperationException("A production stop must require confirmation.");
        _ui4SmokeStopApproved = allowed;
    }

    internal Task AwaitUi4OperationForSmokeAsync()
    {
        if (!_smokeMode) throw new InvalidOperationException("Not a UI4 smoke window.");
        return _ui4RunningOperation ?? Task.CompletedTask;
    }
}

internal static class ServiceEvidenceFilter
{
    internal static string Filter(
        string raw,
        string serviceId,
        IReadOnlyCollection<string> ownedSourceNames)
    {
        var ownSource = false;
        var lines = new List<string>();
        foreach (var line in raw.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (line.StartsWith("--- ", StringComparison.Ordinal) &&
                line.EndsWith(" ---", StringComparison.Ordinal))
            {
                var sourceName = line[4..^4];
                ownSource = ownedSourceNames.Contains(sourceName,
                    StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (ownSource ||
                line.Contains("MCP=" + serviceId, StringComparison.OrdinalIgnoreCase) ||
                line.Contains(serviceId, StringComparison.OrdinalIgnoreCase))
                lines.Add(line);
        }
        return FileLog.RedactSensitiveData(
            string.Join(Environment.NewLine, lines.TakeLast(300)));
    }
}
