using System.Net.Http;
using Talvora.Shared;

namespace Talvora.Tray;

internal enum ControlCenterHealthState
{
    Ready,
    Attention,
    Offline,
    Checking,
}

internal sealed record ManagedMcpDashboardState(
    ManagedMcpRegistration Registration,
    ControlCenterHealthState Health,
    string StatusText,
    string Detail);

internal sealed record ControlCenterDashboardSnapshot(
    IReadOnlyList<ManagedMcpDashboardState> Mcps,
    string HealthSummary,
    string TechnicalSummary,
    int ReadyCount,
    int AttentionCount,
    int OfflineCount);

internal static class ControlCenterDashboardService
{
    private static readonly HttpClient Http = TalvoraHttp.CreateClient(
        timeout: TimeSpan.FromSeconds(3));

    public static async Task<ControlCenterDashboardSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var registry = await ManagedMcpRegistryCoordinator.LoadOrRecoverAsync(
            cancellationToken);

        var states = await Task.WhenAll(
            registry.Mcps.Select(registration =>
                GetStateAsync(registration, cancellationToken)));

        var ordered = states
            .OrderBy(state => GetSortWeight(state.Health))
            .ThenBy(
                state => state.Registration.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var ready = ordered.Count(state =>
            state.Health == ControlCenterHealthState.Ready);
        var attention = ordered.Count(state =>
            state.Health is ControlCenterHealthState.Attention
                or ControlCenterHealthState.Checking);
        var offline = ordered.Count(state =>
            state.Health == ControlCenterHealthState.Offline);
        var tunnelCount = ordered.Count(state =>
            state.Registration.Tunnel is not null);

        var summary = ordered.Count switch
        {
            0 => "Henüz yönetilen MCP yok",
            _ when offline > 0 => "Müdahale gerekiyor",
            _ when attention > 0 => $"{attention} MCP dikkat istiyor",
            _ => "Her şey hazır",
        };

        var technical =
            $"{ordered.Count} MCP • {ready} hazır • {attention + offline} sorunlu • {tunnelCount} tünel";

        return new ControlCenterDashboardSnapshot(
            ordered,
            summary,
            technical,
            ready,
            attention,
            offline);
    }

    internal static async Task<ManagedMcpDashboardState> GetStateAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        if (ManagedMcpSessionState.IsManuallyStopped(registration.Id))
        {
            return new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Offline,
                "Durduruldu",
                "Bu MCP bu Windows oturumu için elle durduruldu.");
        }

        if (string.Equals(
                registration.Id,
                "talvora",
                StringComparison.OrdinalIgnoreCase))
        {
            return await GetTalvoraStateAsync(
                registration,
                cancellationToken);
        }

        if (string.Equals(
                registration.Id,
                "gitea",
                StringComparison.OrdinalIgnoreCase))
        {
            return await GetGiteaStateAsync(
                registration,
                cancellationToken);
        }

        return await GetGenericStateAsync(
            registration,
            cancellationToken);
    }

    private static async Task<ManagedMcpDashboardState> GetTalvoraStateAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        var status = await BusinessTunnelClient.GetStatusAsync(
            cancellationToken);

        return status.State switch
        {
            TalvoraConnectionState.Ready => new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Ready,
                "Hazır",
                status.Detail),
            TalvoraConnectionState.LocalOnly => new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Attention,
                "Bağlantı hazırlanıyor",
                status.Detail),
            _ => new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Offline,
                "Çalışmıyor",
                status.Detail),
        };
    }

    private static async Task<ManagedMcpDashboardState> GetGiteaStateAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        var status = await GiteaTrayClient.GetStatusAsync(
            cancellationToken);

        return status.State switch
        {
            GiteaConnectionState.Running => new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Ready,
                "Hazır",
                status.Detail),
            GiteaConnectionState.Degraded => new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Attention,
                "Dikkat gerekiyor",
                status.Detail),
            GiteaConnectionState.Restarting => new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Checking,
                "Hazırlanıyor",
                status.Detail),
            _ => new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Offline,
                "Çalışmıyor",
                status.Detail),
        };
    }

    private static async Task<ManagedMcpDashboardState> GetGenericStateAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        if (registration.ProtocolProbe is not null)
        {
            var protocol = await ManagedMcpProtocolProbeService.ProbeCachedAsync(
                registration,
                cancellationToken);

            if (!protocol.Ready)
            {
                return new ManagedMcpDashboardState(
                    registration,
                    ControlCenterHealthState.Offline,
                    "Çalışmıyor",
                    protocol.Detail);
            }

            if (registration.ProtocolProbe.BrowserSmokeRequired &&
                !protocol.BrowserSmokePassed)
            {
                return new ManagedMcpDashboardState(
                    registration,
                    ControlCenterHealthState.Attention,
                    "Browser doğrulaması bekleniyor",
                    "MCP hazır; mevcut çalışma nesli için gerçek browser navigate/snapshot doğrulaması henüz tamamlanmadı.");
            }

            var tunnelAssessment =
                ManagedMcpTunnelProvisioningService.Assess(registration);
            if (tunnelAssessment.Required &&
                (!tunnelAssessment.HasTunnelId ||
                 !tunnelAssessment.HasConfig ||
                 !tunnelAssessment.HasRuntimeCredential))
            {
                return new ManagedMcpDashboardState(
                    registration,
                    ControlCenterHealthState.Attention,
                    "Bağlantı hazırlanıyor",
                    tunnelAssessment.Summary);
            }

            var components =
                await ControlCenterComponentHealthService.GetStatesAsync(
                    registration,
                    protocol,
                    cancellationToken);
            var requiredNotReady = components
                .Where(state =>
                    state.Component.Required &&
                    state.Health != ControlCenterHealthState.Ready)
                .ToArray();

            if (requiredNotReady.Length > 0)
            {
                var hasOffline = requiredNotReady.Any(state =>
                    state.Health == ControlCenterHealthState.Offline);
                var detail = string.Join(
                    "; ",
                    requiredNotReady.Select(state =>
                        $"{state.Component.DisplayName}: {state.StatusText}"));

                return new ManagedMcpDashboardState(
                    registration,
                    hasOffline
                        ? ControlCenterHealthState.Offline
                        : ControlCenterHealthState.Attention,
                    hasOffline ? "Çalışmıyor" : "Dikkat gerekiyor",
                    detail);
            }

            return new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Ready,
                "Hazır",
                protocol.Detail);
        }

        if (string.IsNullOrWhiteSpace(registration.HealthEndpoint))
        {
            return new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Attention,
                "Sağlık kontrolü bekleniyor",
                "Bu MCP için henüz bir sağlık kontrolü tanımlı değil.");
        }

        try
        {
            using var response = await Http.GetAsync(
                registration.HealthEndpoint,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new ManagedMcpDashboardState(
                    registration,
                    ControlCenterHealthState.Ready,
                    "Hazır",
                    "Yerel sağlık kontrolü başarılı.");
            }

            return new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Attention,
                "Dikkat gerekiyor",
                $"Sağlık kontrolü HTTP {(int)response.StatusCode} döndürdü.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            OperationCanceledException)
        {
            return new ManagedMcpDashboardState(
                registration,
                ControlCenterHealthState.Offline,
                "Erişilemiyor",
                ex is OperationCanceledException
                    ? "Sağlık kontrolü zaman aşımına uğradı."
                    : "Yerel MCP sağlık kontrolüne ulaşılamadı.");
        }
    }

    private static int GetSortWeight(ControlCenterHealthState health) =>
        health switch
        {
            ControlCenterHealthState.Offline => 0,
            ControlCenterHealthState.Attention => 1,
            ControlCenterHealthState.Checking => 2,
            _ => 3,
        };
}