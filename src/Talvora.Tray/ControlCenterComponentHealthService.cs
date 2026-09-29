using System.IO;
using System.Net.Http;
using System.Text.Json;
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
        ManagedMcpProtocolProbeResult? protocolProbe = null;
        if (registration.ProtocolProbe is not null)
        {
            protocolProbe = await ManagedMcpProtocolProbeService.ProbeCachedAsync(
                registration,
                cancellationToken);
        }

        return await GetStatesAsync(
            registration,
            protocolProbe,
            cancellationToken);
    }

    internal static async Task<IReadOnlyList<ManagedMcpComponentState>> GetStatesAsync(
        ManagedMcpRegistration registration,
        ManagedMcpProtocolProbeResult? protocolProbe,
        CancellationToken cancellationToken)
    {
        var tasks = registration.Components.Select(component =>
            GetStateAsync(registration, component, protocolProbe, cancellationToken));

        var configuredStates = await Task.WhenAll(tasks);
        if (registration.Tunnel is not { Required: true })
        {
            return configuredStates;
        }

        var tunnelDiagnostics = await GetTunnelDiagnosticStatesAsync(
            registration,
            cancellationToken);
        if (tunnelDiagnostics.Count == 0)
        {
            return configuredStates;
        }

        return configuredStates
            .Concat(tunnelDiagnostics)
            .ToArray();
    }

    private static async Task<IReadOnlyList<ManagedMcpComponentState>>
        GetTunnelDiagnosticStatesAsync(
            ManagedMcpRegistration registration,
            CancellationToken cancellationToken)
    {
        try
        {
            var snapshot =
                await ManagedMcpTunnelHealthService.GetSnapshotCachedAsync(
                    registration,
                    cancellationToken);
            if (snapshot is null)
            {
                return [];
            }

            var states = new List<ManagedMcpComponentState>
            {
                new(
                    new ManagedMcpComponentRegistration
                    {
                        Id = "tunnel-runtime-health",
                        DisplayName = "Tunnel runtime",
                        Kind = "tunnel-health",
                        Required = false,
                    },
                    snapshot.Live && snapshot.Ready
                        ? ControlCenterHealthState.Ready
                        : ControlCenterHealthState.Attention,
                    snapshot.Live && snapshot.Ready
                        ? "Sağlıklı"
                        : "Dikkat gerekiyor",
                    BuildTunnelRuntimeDetail(snapshot)),
            };

            foreach (var component in snapshot.Components)
            {
                if (!ShouldExposeTunnelHealthComponent(component))
                {
                    continue;
                }

                states.Add(ToManagedTunnelHealthState(component));
            }

            return states;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            TrayLog.Write(
                $"Detailed tunnel health timed out. MCP={registration.Id}",
                ex);
            return [];
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            JsonException or
            InvalidDataException or
            InvalidOperationException or
            UnauthorizedAccessException)
        {
            TrayLog.Write(
                $"Detailed tunnel health unavailable. MCP={registration.Id}",
                ex);
            return [];
        }
    }

    private static ManagedMcpComponentState ToManagedTunnelHealthState(
        ManagedMcpTunnelHealthComponentSnapshot component)
    {
        var health = component.Status.ToLowerInvariant() switch
        {
            "ok" => ControlCenterHealthState.Ready,
            "degraded" => ControlCenterHealthState.Attention,
            "unknown" => ControlCenterHealthState.Checking,
            "disabled" => ControlCenterHealthState.Ready,
            _ => ControlCenterHealthState.Checking,
        };
        var statusText = component.Status.ToLowerInvariant() switch
        {
            "ok" => "Sağlıklı",
            "degraded" => "Dikkat gerekiyor",
            "unknown" => "Gözlemlenmedi",
            "disabled" => "Devre dışı",
            _ => "Durum bilinmiyor",
        };

        return new ManagedMcpComponentState(
            new ManagedMcpComponentRegistration
            {
                Id = "tunnel-health-" + component.Id,
                DisplayName = GetTunnelHealthDisplayName(component.Id),
                Kind = "tunnel-health",
                Name = component.Id,
                Required =
                    component.Critical &&
                    string.Equals(
                        component.Status,
                        "degraded",
                        StringComparison.OrdinalIgnoreCase),
            },
            health,
            statusText,
            component.Detail);
    }

    private static bool ShouldExposeTunnelHealthComponent(
        ManagedMcpTunnelHealthComponentSnapshot component)
    {
        if (string.Equals(
                component.Status,
                "degraded",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return component.Id.ToLowerInvariant() is
            "control-plane" or
            "response-delivery" or
            "queue" or
            "dispatcher" or
            "mcp";
    }

    private static string BuildTunnelRuntimeDetail(
        ManagedMcpTunnelHealthSnapshot snapshot)
    {
        var version = string.IsNullOrWhiteSpace(snapshot.RuntimeVersion)
            ? "sürüm bilinmiyor"
            : "v" + snapshot.RuntimeVersion.TrimStart('v');
        var lifecycle = string.IsNullOrWhiteSpace(snapshot.RuntimeLifecycle)
            ? "lifecycle bilinmiyor"
            : snapshot.RuntimeLifecycle;

        return
            $"OpenAI tunnel-client {version} • {lifecycle} • " +
            $"health schema v{snapshot.SchemaVersion} • " +
            $"Live={snapshot.Live} • Ready={snapshot.Ready}.";
    }

    private static string GetTunnelHealthDisplayName(string id) =>
        id.ToLowerInvariant() switch
        {
            "control-plane" => "Control plane",
            "response-delivery" => "Yanıt teslimi",
            "queue" => "İstek kuyruğu",
            "dispatcher" => "İş dağıtıcı",
            "mcp" => "Tünel MCP gözlemi",
            "oauth" => "OAuth discovery",
            "proxy" => "Proxy",
            "cloudflared" => "Cloudflared",
            "harpoon" => "Harpoon",
            _ => id,
        };

    private static Task<ManagedMcpComponentState> GetStateAsync(
        ManagedMcpRegistration registration,
        ManagedMcpComponentRegistration component,
        ManagedMcpProtocolProbeResult? protocolProbe,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                component.Id,
                "tunnel",
                StringComparison.OrdinalIgnoreCase) &&
            registration.Tunnel is not null)
        {
            return GetTunnelStateAsync(
                registration,
                component,
                cancellationToken);
        }

        if (string.Equals(
                component.Kind,
                "mcp-protocol",
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(
                protocolProbe is { Ready: true }
                    ? new ManagedMcpComponentState(
                        component,
                        ControlCenterHealthState.Ready,
                        "Hazır",
                        protocolProbe.Detail)
                    : new ManagedMcpComponentState(
                        component,
                        ControlCenterHealthState.Offline,
                        "MCP erişilemiyor",
                        protocolProbe?.Detail ?? "MCP protokol sağlık kontrolü tanımlı değil."));
        }

        if (string.Equals(
                component.Kind,
                "browser-runtime",
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(
                protocolProbe is { BrowserRuntimeReady: true }
                    ? new ManagedMcpComponentState(
                        component,
                        ControlCenterHealthState.Ready,
                        "Hazır",
                        "Gerçek tarayıcı çalışma bağlantısı doğrulandı.")
                    : new ManagedMcpComponentState(
                        component,
                        ControlCenterHealthState.Offline,
                        "Tarayıcı hazır değil",
                        protocolProbe?.Detail ?? "Tarayıcı çalışma durumu alınamadı."));
        }

        if (string.Equals(
                component.Kind,
                "browser-smoke",
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(
                protocolProbe is { BrowserSmokePassed: true }
                    ? new ManagedMcpComponentState(
                        component,
                        ControlCenterHealthState.Ready,
                        "Doğrulandı",
                        "Gerçek tarayıcı açma ve sayfa görüntüsü doğrulaması başarılı.")
                    : new ManagedMcpComponentState(
                        component,
                        ControlCenterHealthState.Attention,
                        "Doğrulama bekleniyor",
                        "Gerçek tarayıcı doğrulaması henüz tamamlanmadı."));
        }

        if (string.Equals(
                component.Kind,
                "process",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(component.Name))
        {
            return ProbeProcessAsync(
                component,
                cancellationToken);
        }

        if (string.Equals(
                component.Kind,
                "scheduled-task",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(component.Name))
        {
            return ProbeScheduledTaskAsync(
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
        ManagedMcpRegistration registration,
        ManagedMcpComponentRegistration component,
        CancellationToken cancellationToken)
    {
        var tunnel = registration.Tunnel
            ?? throw new InvalidOperationException("Tunnel registration is missing.");

        ManagedMcpTunnelRuntimeStatus runtimeStatus;
        try
        {
            runtimeStatus = await ManagedMcpTunnelProvisioningService.GetRuntimeStatusCachedAsync(
                registration,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            InvalidOperationException or
            UnauthorizedAccessException)
        {
            return new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Attention,
                "Tünel kimliği doğrulanamadı",
                "Structured tunnel runtime durumu okunamadı.");
        }

        if (!runtimeStatus.Ready)
        {
            return new ManagedMcpComponentState(
                component,
                runtimeStatus.ProcessRunning
                    ? ControlCenterHealthState.Attention
                    : ControlCenterHealthState.Offline,
                runtimeStatus.ProcessRunning
                    ? "Tünel hazır değil"
                    : "Tünel çalışmıyor",
                runtimeStatus.Detail);
        }
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
                ControlCenterUserMessage.ForOperation(
                    ex,
                    "Tünel durumu"));
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

    private static async Task<ManagedMcpComponentState> ProbeProcessAsync(
        ManagedMcpComponentRegistration component,
        CancellationToken cancellationToken)
    {
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var pwsh = Path.Combine(
            programFiles,
            "PowerShell",
            "7",
            "pwsh.exe");

        if (!File.Exists(pwsh))
        {
            pwsh = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
        }

        var marker = component.Name!
            .Replace("'", "''", StringComparison.Ordinal);
        var script =
            "$marker='" + marker + "';" +
            "$p=Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | " +
            "Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($marker,[StringComparison]::OrdinalIgnoreCase) -ge 0 } | " +
            "Select-Object -First 1 ProcessId,Name,SessionId;" +
            "if($null -eq $p){exit 3};" +
            "[Console]::Write(($p | ConvertTo-Json -Compress))";

        var result = await ProcessRunner.RunAsync(
            pwsh,
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                script,
            },
            timeoutSeconds: 10,
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0)
        {
            return new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Offline,
                "Çalışmıyor",
                "İşlem bulunamadı.");
        }

        return new ManagedMcpComponentState(
            component,
            ControlCenterHealthState.Ready,
            "Çalışıyor",
            "İşlem aktif.");
    }

    private static async Task<ManagedMcpComponentState> ProbeScheduledTaskAsync(
        ManagedMcpComponentRegistration component,
        CancellationToken cancellationToken)
    {
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var pwsh = Path.Combine(
            programFiles,
            "PowerShell",
            "7",
            "pwsh.exe");

        if (!File.Exists(pwsh))
        {
            pwsh = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
        }

        var taskName = component.Name!
            .Replace("'", "''", StringComparison.Ordinal);
        var script =
            "$t=Get-ScheduledTask -TaskName '" + taskName +
            "' -ErrorAction SilentlyContinue;" +
            "if($null -eq $t){exit 3};" +
            "[Console]::Write($t.State.ToString())";

        var result = await ProcessRunner.RunAsync(
            pwsh,
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                script,
            },
            timeoutSeconds: 10,
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0)
        {
            return new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Offline,
                "Görev bulunamadı",
                "Yönetilen zamanlanmış görev okunamadı.");
        }

        var state = result.StandardOutput.Trim();
        return state switch
        {
            "Running" => new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Ready,
                "Çalışıyor",
                "Zamanlanmış görev aktif."),
            "Disabled" => new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Offline,
                "Devre dışı",
                "Zamanlanmış görev devre dışı."),
            "Ready" => new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Attention,
                "Beklemede",
                "Zamanlanmış görev kayıtlı ancak şu anda çalışmıyor."),
            _ => new ManagedMcpComponentState(
                component,
                ControlCenterHealthState.Attention,
                string.IsNullOrWhiteSpace(state) ? "Durum bilinmiyor" : state,
                "Zamanlanmış görev hazır durumda değil."),
        };
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
