using System.IO;
using System.Net.Http;
using Talvora.Shared;

namespace Talvora.Tray;

internal sealed record ManagedMcpComponentState(
    ManagedMcpComponentRegistration Component,
    ControlCenterHealthState Health,
    string StatusText,
    string Detail);

internal static class ControlCenterComponentHealthService
{
    private static readonly HttpClient Http = TalvoraHttp.CreateClient(
        timeout: TimeSpan.FromSeconds(3));

    public static async Task<IReadOnlyList<ManagedMcpComponentState>> GetStatesAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        var tasks = registration.Components.Select(component =>
            GetStateAsync(registration, component, cancellationToken));

        return await Task.WhenAll(tasks);
    }

    private static Task<ManagedMcpComponentState> GetStateAsync(
        ManagedMcpRegistration registration,
        ManagedMcpComponentRegistration component,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                component.Id,
                "tunnel",
                StringComparison.OrdinalIgnoreCase) &&
            registration.Tunnel is not null)
        {
            return GetTunnelStateAsync(
                registration.Tunnel,
                component,
                cancellationToken);
        }

        var healthEndpoint = component.HealthEndpoint;
        if (string.IsNullOrWhiteSpace(healthEndpoint) &&
            string.Equals(
                registration.Id,
                "talvora",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                component.Id,
                "service",
                StringComparison.OrdinalIgnoreCase))
        {
            healthEndpoint = registration.HealthEndpoint;
        }

        if (!string.IsNullOrWhiteSpace(healthEndpoint))
        {
            return ProbeHttpAsync(
                component,
                healthEndpoint,
                cancellationToken);
        }

        return Task.FromResult(
            new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Attention,
                "Durum bilgisi sınırlı",
                "Bu bileşen için doğrudan sağlık uç noktası (endpoint) bulunmuyor."));
    }

    private static async Task<ManagedMcpComponentState> GetTunnelStateAsync(
        ManagedMcpTunnelRegistration tunnel,
        ManagedMcpComponentRegistration component,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tunnel.StateRoot))
        {
            return new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Attention,
                "Tünel hazırlanıyor",
                "Tünel çalışma dizini henüz bilinmiyor.");
        }

        var healthUrlPath = Path.Combine(
            tunnel.StateRoot,
            "health",
            tunnel.Alias + ".url");

        if (!File.Exists(healthUrlPath))
        {
            return new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Offline,
                "Tünel çalışmıyor",
                "Tünel sağlık adresi bulunamadı.");
        }

        string baseUrl;
        try
        {
            baseUrl = (await File.ReadAllTextAsync(
                healthUrlPath,
                cancellationToken)).Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException)
        {
            return new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Offline,
                "Tünel durumu okunamadı",
                ex.Message);
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Attention,
                "Tünel durumu geçersiz",
                "Yerel tünel sağlık adresi geçerli değil.");
        }

        try
        {
            var healthUri = new Uri(baseUri, "/healthz");
            var readyUri = new Uri(baseUri, "/readyz");

            using var health = await Http.GetAsync(
                healthUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            using var ready = await Http.GetAsync(
                readyUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (health.IsSuccessStatusCode &&
                ready.IsSuccessStatusCode)
            {
                return new ManagedMcpComponentState(
                    component,
                    ControlCenterHealthState.Ready,
                    "Hazır",
                    $"Tünel {tunnel.Alias} bağlı ve istek almaya hazır.");
            }

            return new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Attention,
                "Tünel hazır değil",
                $"Sağlık HTTP {(int)health.StatusCode}, hazır olma HTTP {(int)ready.StatusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            OperationCanceledException)
        {
            return new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Offline,
                "Tünel erişilemiyor",
                ex is OperationCanceledException
                    ? "Tünel sağlık kontrolü zaman aşımına uğradı."
                    : "Tünel sağlık uç noktasına ulaşılamadı.");
        }
    }

    private static async Task<ManagedMcpComponentState> ProbeHttpAsync(
        ManagedMcpComponentRegistration component,
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            return response.IsSuccessStatusCode
                ? new ManagedMcpComponentState(
                    component,
                    ControlCenterHealthState.Ready,
                    "Hazır",
                    "Sağlık kontrolü başarılı.")
                : new ManagedMcpComponentState(
                    component,
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
            return new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Offline,
                "Erişilemiyor",
                ex is OperationCanceledException
                    ? "Sağlık kontrolü zaman aşımına uğradı."
                    : "Bileşen sağlık uç noktasına ulaşılamadı.");
        }
    }
}
