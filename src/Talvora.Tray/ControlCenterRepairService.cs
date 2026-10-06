using Talvora.Shared;

namespace Talvora.Tray;

internal sealed record ManagedMcpRepairResult(
    string Strategy,
    string Summary,
    string Detail,
    bool Escalated,
    ManagedMcpDashboardState InitialState,
    ManagedMcpDashboardState FinalState);

internal static class ControlCenterRepairService
{
    public static async Task<ManagedMcpRepairResult> RepairAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (ManagedMcpSessionState.IsManuallyStopped(registration.Id))
        {
            throw new InvalidOperationException(
                $"{registration.DisplayName} bu Windows oturumunda elle durduruldu. " +
                "Onarım kullanıcı tarafından verilen durdurma kararını geçersiz kılamaz; önce Başlat seçeneğini kullanın.");
        }

        var state = await ControlCenterDashboardService.GetStateAsync(
            registration,
            cancellationToken);

        return await RepairAsync(
            registration,
            state,
            cancellationToken);
    }

    public static async Task<ManagedMcpRepairResult> RepairAsync(
        ManagedMcpRegistration registration,
        ManagedMcpDashboardState observedState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(observedState);

        if (observedState.Health == ControlCenterHealthState.Ready)
        {
            return new ManagedMcpRepairResult(
                "Doğrulama",
                $"{registration.DisplayName} zaten hazır",
                "Sağlık ve readiness kontrolleri onarım gerektiren bir arıza göstermedi.",
                Escalated: false,
                observedState,
                observedState);
        }

        if (ManagedMcpSessionState.IsManuallyStopped(registration.Id))
        {
            throw new InvalidOperationException(
                $"{registration.DisplayName} bu Windows oturumunda elle durduruldu. " +
                "Otomatik onarım kullanıcı kararına müdahale etmeyecek.");
        }

        if (string.Equals(
                registration.Id,
                "gitea",
                StringComparison.OrdinalIgnoreCase))
        {
            return await RepairGiteaAsync(
                registration,
                observedState,
                cancellationToken);
        }

        if (string.Equals(
                registration.Id,
                "talvora",
                StringComparison.OrdinalIgnoreCase))
        {
            return await RepairTalvoraCoreAsync(
                registration,
                observedState,
                cancellationToken);
        }

        return await RepairGenericAsync(
            registration,
            observedState,
            cancellationToken);
    }

    private static async Task<ManagedMcpRepairResult> RepairTalvoraCoreAsync(
        ManagedMcpRegistration registration,
        ManagedMcpDashboardState observedState,
        CancellationToken cancellationToken)
    {
        var lifecycle = observedState.Health == ControlCenterHealthState.Offline
            ? await ControlCenterLifecycleService.StartAsync(
                registration,
                cancellationToken)
            : await ControlCenterLifecycleService.RestartAsync(
                registration,
                cancellationToken);

        var finalState = await VerifyReadyAsync(
            registration,
            cancellationToken);

        return new ManagedMcpRepairResult(
            observedState.Health == ControlCenterHealthState.Offline
                ? "Core servisi başlat"
                : "Core servisi yeniden başlat",
            $"{registration.DisplayName} onarıldı",
            $"{lifecycle.Detail} Sağlık doğrulaması: {finalState.StatusText}.",
            Escalated: false,
            observedState,
            finalState);
    }

    private static async Task<ManagedMcpRepairResult> RepairGiteaAsync(
        ManagedMcpRegistration registration,
        ManagedMcpDashboardState observedState,
        CancellationToken cancellationToken)
    {
        var repair = await GiteaTrayClient.RepairAsync(cancellationToken);
        var finalState = await VerifyReadyAsync(
            registration,
            cancellationToken);

        return new ManagedMcpRepairResult(
            repair.Strategy,
            $"{registration.DisplayName} onarıldı",
            $"{repair.Detail} Sağlık doğrulaması: {finalState.StatusText}.",
            repair.Escalated,
            observedState,
            finalState);
    }

    private static async Task<ManagedMcpRepairResult> RepairGenericAsync(
        ManagedMcpRegistration registration,
        ManagedMcpDashboardState observedState,
        CancellationToken cancellationToken)
    {
        (
            string Strategy,
            string Summary,
            string Detail)? targeted = null;
        Exception? targetedFailure = null;

        try
        {
            targeted = await TryRepairWithoutRestartAsync(
                registration,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ManagedMcpOperationInProgressException)
        {
            throw;
        }
        catch (Exception ex)
        {
            targetedFailure = ex;
            TrayLog.Write(
                $"Targeted managed MCP repair failed. MCP={registration.Id}; escalating to controlled restart.",
                ex);
        }

        if (targeted is not null)
        {
            var targetedState = await GetFreshStateAsync(
                registration,
                cancellationToken);
            if (targetedState.Health == ControlCenterHealthState.Ready)
            {
                return new ManagedMcpRepairResult(
                    targeted.Value.Strategy,
                    targeted.Value.Summary,
                    targeted.Value.Detail,
                    Escalated: false,
                    observedState,
                    targetedState);
            }

            var escalated = await ControlCenterLifecycleService.RestartAsync(
                registration,
                cancellationToken);
            var finalState = await VerifyReadyAsync(
                registration,
                cancellationToken);

            return new ManagedMcpRepairResult(
                targeted.Value.Strategy + " → kontrollü yeniden başlatma",
                $"{registration.DisplayName} onarıldı",
                $"Hedefli onarım yeterli olmadı; çalışma zinciri kontrollü olarak yeniden başlatıldı. " +
                $"{escalated.Detail} Sağlık doğrulaması: {finalState.StatusText}.",
                Escalated: true,
                observedState,
                finalState);
        }

        if (targetedFailure is not null)
        {
            var escalated = await ControlCenterLifecycleService.RestartAsync(
                registration,
                cancellationToken);
            var finalState = await VerifyReadyAsync(
                registration,
                cancellationToken);

            return new ManagedMcpRepairResult(
                "Hedefli onarım başarısız → kontrollü yeniden başlatma",
                $"{registration.DisplayName} onarıldı",
                $"Hedefli onarım uygulanamadı; çalışma zinciri kontrollü olarak yeniden başlatıldı. " +
                $"{escalated.Detail} Sağlık doğrulaması: {finalState.StatusText}.",
                Escalated: true,
                observedState,
                finalState);
        }

        var lifecycle = observedState.Health == ControlCenterHealthState.Offline
            ? await ControlCenterLifecycleService.StartAsync(
                registration,
                cancellationToken)
            : await ControlCenterLifecycleService.RestartAsync(
                registration,
                cancellationToken);
        var verified = await VerifyReadyAsync(
            registration,
            cancellationToken);

        return new ManagedMcpRepairResult(
            observedState.Health == ControlCenterHealthState.Offline
                ? "Çalışma zincirini başlat"
                : "Çalışma zincirini yeniden başlat",
            $"{registration.DisplayName} onarıldı",
            $"{lifecycle.Detail} Sağlık doğrulaması: {verified.StatusText}.",
            Escalated: false,
            observedState,
            verified);
    }

    private static async Task<(
        string Strategy,
        string Summary,
        string Detail)?> TryRepairWithoutRestartAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        using var lease =
            ManagedMcpOperationCoordinator.TryAcquire(registration.Id);
        if (lease is null)
        {
            throw new ManagedMcpOperationInProgressException(
                registration.Id);
        }

        if (registration.ProtocolProbe is { } probeRegistration)
        {
            var protocol =
                await ManagedMcpProtocolProbeService.ProbeAsync(
                    registration,
                    runBrowserSmoke: false,
                    cancellationToken);

            if (protocol.Ready &&
                probeRegistration.BrowserSmokeRequired &&
                !protocol.BrowserSmokePassed)
            {
                var smoke =
                    await ManagedMcpProtocolProbeService.WaitUntilReadyAsync(
                        registration,
                        runBrowserSmoke: true,
                        timeout: TimeSpan.FromSeconds(45),
                        cancellationToken);

                if (smoke.Ready && smoke.BrowserSmokePassed)
                {
                    return (
                        "Browser doğrulamasını yenile",
                        $"{registration.DisplayName} browser doğrulaması yenilendi",
                        "Çalışan MCP/browser zinciri kesilmeden gerçek browser smoke yeniden doğrulandı.");
                }

                throw new InvalidOperationException(
                    $"{registration.DisplayName} browser smoke yenilemesi tamamlanamadı; " +
                    $"çalışan MCP/browser zinciri korunuyor: {smoke.Detail}");
            }

            if (protocol.Ready &&
                (!probeRegistration.BrowserSmokeRequired ||
                 protocol.BrowserSmokePassed) &&
                registration.Tunnel is { Required: true })
            {
                var tunnelStatus =
                    await ManagedMcpTunnelProvisioningService
                        .GetRuntimeStatusAsync(
                            registration,
                            cancellationToken);

                if (!tunnelStatus.Ready)
                {
                    await ManagedMcpTunnelProvisioningService
                        .ConnectExistingAsync(
                            registration,
                            cancellationToken);

                    var refreshedTunnel =
                        await ManagedMcpTunnelProvisioningService
                            .GetRuntimeStatusAsync(
                                registration,
                                cancellationToken);

                    if (refreshedTunnel.Ready)
                    {
                        return (
                            "Yalnız güvenli tüneli yeniden bağla",
                            $"{registration.DisplayName} tüneli yeniden bağlandı",
                            "Yerel MCP/browser zinciri kesilmeden yalnız Secure MCP Tunnel yeniden hazırlandı.");
                    }

                    throw new InvalidOperationException(
                        $"{registration.DisplayName} tüneli yeniden bağlanamadı; " +
                        $"yerel MCP/browser zinciri korunuyor: {refreshedTunnel.Detail}");
                }

                var tunnelHealth =
                    await ManagedMcpTunnelHealthService.GetSnapshotAsync(
                        registration,
                        cancellationToken);
                // A control-plane degradation commonly represents an upstream
                // network/DNS interruption. tunnel-client already owns retry and
                // exponential backoff for that condition. Restarting an otherwise
                // ready runtime here creates a restart storm and makes the remote
                // MCP less available. Only response-delivery degradation warrants
                // a tunnel-runtime renewal while the runtime itself still reports
                // Ready=true; a genuinely unready runtime is handled above.
                if (tunnelHealth is { RequiresRuntimeRenewal: true })
                {
                    await ManagedMcpTunnelProvisioningService
                        .DisconnectExistingAsync(
                            registration,
                            cancellationToken);
                    await ManagedMcpTunnelProvisioningService
                        .ConnectExistingAsync(
                            registration,
                            cancellationToken);

                    var refreshedTunnel =
                        await ManagedMcpTunnelProvisioningService
                            .GetRuntimeStatusAsync(
                                registration,
                                cancellationToken);
                    var refreshedHealth =
                        await ManagedMcpTunnelHealthService.GetSnapshotAsync(
                            registration,
                            cancellationToken);

                    if (refreshedTunnel.Ready &&
                        refreshedHealth is not { RequiresRuntimeRenewal: true })
                    {
                        return (
                            "Yalnız tünel çalışma katmanını yenile",
                            $"{registration.DisplayName} tünel tanısı düzeldi",
                            "Control-plane veya yanıt teslimi katmanındaki bozulma nedeniyle yalnız Secure MCP Tunnel runtime yenilendi; yerel MCP/browser zinciri korunuyor.");
                    }

                    throw new InvalidOperationException(
                        $"{registration.DisplayName} ayrıntılı tünel sağlık sorunu hedefli runtime yenilemesiyle giderilemedi.");
                }
            }
        }

        return null;
    }

    private static async Task<ManagedMcpDashboardState> VerifyReadyAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        var state = await GetFreshStateAsync(
            registration,
            cancellationToken);

        if (state.Health != ControlCenterHealthState.Ready)
        {
            throw new InvalidOperationException(
                $"{registration.DisplayName} onarım işlemi tamamlandı ancak readiness doğrulaması başarısız: {state.Detail}");
        }

        return state;
    }

    private static async Task<ManagedMcpDashboardState> GetFreshStateAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        ManagedMcpProtocolProbeService.InvalidateCache(registration.Id);
        ManagedMcpTunnelProvisioningService.InvalidateRuntimeStatusCache(
            registration.Id);

        return await ControlCenterDashboardService.GetStateAsync(
            registration,
            cancellationToken);
    }
}
