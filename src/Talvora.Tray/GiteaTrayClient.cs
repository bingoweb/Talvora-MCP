using ModelContextProtocol.Client;

namespace Talvora.Tray;

internal enum GiteaConnectionState
{
    Running,
    Degraded,
    Restarting,
    Offline,
}

internal sealed record GiteaStatus(
    GiteaConnectionState State,
    string Summary,
    string Detail);

internal static class GiteaTrayClient
{
    private const string BackendHealthUrl = "http://127.0.0.1:3001/api/healthz";
    private const string ProxyHealthUrl = "http://127.0.0.1:3000/api/healthz";
    private const string TalvoraMcpUrl = "http://127.0.0.1:7676/mcp";
    private const string ServiceName = "gitea";

    private static readonly HttpClient HealthClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2),
    };

    public static async Task<GiteaStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var backend = await ProbeAsync(
            BackendHealthUrl,
            "Gitea backend",
            cancellationToken);

        if (!backend.Success)
        {
            return new GiteaStatus(
                GiteaConnectionState.Offline,
                "Gitea çalışmıyor",
                backend.Detail);
        }

        var proxy = await ProbeAsync(
            ProxyHealthUrl,
            "Gitea yerel proxy",
            cancellationToken);

        if (!proxy.Success)
        {
            return new GiteaStatus(
                GiteaConnectionState.Degraded,
                "Gitea çalışıyor, yerel erişim sorunlu",
                proxy.Detail);
        }

        return new GiteaStatus(
            GiteaConnectionState.Running,
            "Gitea çalışıyor",
            "Gitea backend ve yerel erişim sağlıklı.");
    }

    private static async Task<(bool Success, string Detail)> ProbeAsync(
        string url,
        string component,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HealthClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            return response.IsSuccessStatusCode
                ? (true, $"{component} sağlıklı.")
                : (false, $"{component} HTTP {(int)response.StatusCode} döndürdü.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, $"{component} health kontrolü zaman aşımına uğradı.");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"{component} erişilemiyor: {ex.Message}");
        }
    }

    public static async Task<GiteaStatus> RestartAsync(CancellationToken cancellationToken)
    {
        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(TalvoraMcpUrl),
                TransportMode = HttpTransportMode.StreamableHttp,
                ConnectionTimeout = TimeSpan.FromSeconds(10),
            });

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        const string restartScript = """
$ErrorActionPreference = 'Stop'

function Wait-ServiceState {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,
        [Parameter(Mandatory = $true)]
        [string] $State,
        [int] $TimeoutSeconds = 30
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $service = Get-CimInstance Win32_Service -Filter "Name='$Name'" -ErrorAction Stop
        if ([string]::Equals(
                [string] $service.State,
                $State,
                [StringComparison]::OrdinalIgnoreCase)) {
            return $service
        }

        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Service '$Name' did not reach state '$State' within $TimeoutSeconds seconds."
}

$caddy = Get-CimInstance Win32_Service -Filter "Name='caddy'" -ErrorAction Stop
if (-not [string]::Equals(
        [string] $caddy.State,
        'Stopped',
        [StringComparison]::OrdinalIgnoreCase)) {
    if ([int] $caddy.ProcessId -gt 0) {
        Stop-Process -Id ([int] $caddy.ProcessId) -Force -ErrorAction Stop
    }

    $null = Wait-ServiceState -Name 'caddy' -State 'Stopped' -TimeoutSeconds 20
}

$gitea = Get-Service -Name 'gitea' -ErrorAction Stop
if ($gitea.Status -eq [ServiceProcess.ServiceControllerStatus]::Running) {
    Restart-Service -Name 'gitea' -Force -ErrorAction Stop
}
else {
    Start-Service -Name 'gitea' -ErrorAction Stop
}

(Get-Service -Name 'gitea').WaitForStatus(
    [ServiceProcess.ServiceControllerStatus]::Running,
    [TimeSpan]::FromSeconds(30))

Start-Service -Name 'caddy' -ErrorAction Stop
(Get-Service -Name 'caddy').WaitForStatus(
    [ServiceProcess.ServiceControllerStatus]::Running,
    [TimeSpan]::FromSeconds(30))

$giteaFinal = Get-CimInstance Win32_Service -Filter "Name='gitea'" -ErrorAction Stop
$caddyFinal = Get-CimInstance Win32_Service -Filter "Name='caddy'" -ErrorAction Stop

if ($giteaFinal.State -ne 'Running' -or $caddyFinal.State -ne 'Running') {
    throw 'Gitea/Caddy service chain did not return to Running.'
}
""";

        var result = await client.CallToolAsync(
            "talvora_run_powershell",
            new Dictionary<string, object?>
            {
                ["script"] = restartScript,
                ["workingDirectory"] = @"C:\Windows\System32",
                ["timeoutSeconds"] = 90,
            },
            cancellationToken: cancellationToken);

        if (result.IsError is true)
        {
            throw new InvalidOperationException(
                "Talvora MCP Gitea/Caddy servis zincirini yeniden başlatamadı.");
        }

        if (result.StructuredContent is { } structured &&
            structured.TryGetProperty("exitCode", out var exitCode) &&
            exitCode.GetInt32() != 0)
        {
            var error = structured.TryGetProperty("standardError", out var stderr)
                ? stderr.GetString()
                : null;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"Gitea/Caddy restart komutu exit code {exitCode.GetInt32()} döndürdü."
                    : error);
        }

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = await GetStatusAsync(cancellationToken);
            if (status.State == GiteaConnectionState.Running)
            {
                return status;
            }

            await Task.Delay(500, cancellationToken);
        }

        return await GetStatusAsync(cancellationToken);
    }
}
