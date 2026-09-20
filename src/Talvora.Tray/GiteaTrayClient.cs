using System.IO;
using System.Net.Http;
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
    private const string McpHealthUrl = "http://127.0.0.1:8081/healthz";
    private const string TalvoraMcpUrl = "http://127.0.0.1:7676/mcp";
    private const string TunnelAlias = "gitea-business";

    private static readonly HttpClient HealthClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2),
    };

    private static string TunnelStateRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gitea",
            "McpTunnel",
            "state");

    public static async Task<GiteaStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var backend = await ProbeAsync(
            BackendHealthUrl,
            "Gitea",
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
            "Caddy",
            cancellationToken);

        if (!proxy.Success)
        {
            return new GiteaStatus(
                GiteaConnectionState.Degraded,
                "Gitea çalışıyor, yerel erişim sorunlu",
                proxy.Detail);
        }

        var mcp = await ProbeAsync(
            McpHealthUrl,
            "Gitea MCP sunucusu",
            cancellationToken);

        if (!mcp.Success)
        {
            return new GiteaStatus(
                GiteaConnectionState.Degraded,
                "Gitea çalışıyor, MCP sunucusu hazır değil",
                mcp.Detail);
        }

        var protocol =
            await ProbeMcpProtocolAsync(
                cancellationToken);
        if (!protocol.Success)
        {
            return new GiteaStatus(
                GiteaConnectionState.Degraded,
                "Gitea MCP health açık, protokol hazır değil",
                protocol.Detail);
        }

        var tunnel = await ProbeTunnelAsync(cancellationToken);
        if (!tunnel.Success)
        {
            return new GiteaStatus(
                GiteaConnectionState.Degraded,
                "Gitea MCP çalışıyor, tünel hazır değil",
                tunnel.Detail);
        }

        return new GiteaStatus(
            GiteaConnectionState.Running,
            "Gitea MCP hazır",
            "Gitea, Caddy, MCP sunucusu ve güvenli tünel sağlıklı.");
    }

    public static async Task<GiteaStatus> StartAsync(
        CancellationToken cancellationToken)
    {
        using var operationLease =
            ManagedMcpOperationCoordinator.TryAcquire("gitea");
        if (operationLease is null)
        {
            throw new ManagedMcpOperationInProgressException("gitea");
        }

        await RunPrivilegedScriptAsync(
            BuildLifecycleScript("start"),
            cancellationToken);

        return await WaitForReadyAsync(cancellationToken);
    }

    public static async Task<GiteaStatus> StopAsync(
        CancellationToken cancellationToken)
    {
        using var operationLease =
            ManagedMcpOperationCoordinator.TryAcquire("gitea");
        if (operationLease is null)
        {
            throw new ManagedMcpOperationInProgressException("gitea");
        }

        await RunPrivilegedScriptAsync(
            BuildLifecycleScript("stop"),
            cancellationToken);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopped =
                await ProbeStoppedChainAsync(
                    cancellationToken);
            if (stopped.Success)
            {
                return new GiteaStatus(
                    GiteaConnectionState.Offline,
                    "Gitea MCP durduruldu",
                    "Gitea MCP zinciri bu Windows oturumu için elle durduruldu.");
            }

            await Task.Delay(400, cancellationToken);
        }

        var final =
            await ProbeStoppedChainAsync(
                cancellationToken);
        return final.Success
            ? new GiteaStatus(
                GiteaConnectionState.Offline,
                "Gitea MCP durduruldu",
                "Gitea MCP zinciri bu Windows oturumu için elle durduruldu.")
            : new GiteaStatus(
                GiteaConnectionState.Degraded,
                "Gitea MCP tam olarak durdurulamadı",
                final.Detail);
    }

    public static async Task<GiteaStatus> RestartAsync(
        CancellationToken cancellationToken)
    {
        using var operationLease =
            ManagedMcpOperationCoordinator.TryAcquire("gitea");
        if (operationLease is null)
        {
            throw new ManagedMcpOperationInProgressException("gitea");
        }

        await RunPrivilegedScriptAsync(
            BuildLifecycleScript("restart"),
            cancellationToken);

        return await WaitForReadyAsync(cancellationToken);
    }

    private static async Task<GiteaStatus> WaitForReadyAsync(
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
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
        catch (HttpRequestException)
        {
            return (false, $"{component} erişilemiyor.");
        }
    }

    private static async Task<(bool Success, string Detail)> ProbeMcpProtocolAsync(
        CancellationToken cancellationToken)
    {
        var registration =
            new GiteaManagedMcpRecoveryDiscovery()
                .Discover();
        if (registration is null)
        {
            return (
                false,
                "Gitea MCP protokol kaydı bulunamadı.");
        }

        var result =
            await ManagedMcpProtocolProbeService
                .ProbeCachedAsync(
                    registration,
                    cancellationToken);
        return result.Ready
            ? (
                true,
                $"Gitea MCP initialize + tools/list hazır. Araç sayısı: {result.ToolCount}.")
            : (
                false,
                result.Detail);
    }

    private static async Task<(bool Success, string Detail)> ProbeTunnelAsync(
        CancellationToken cancellationToken)
    {
        var healthUrlPath = Path.Combine(
            TunnelStateRoot,
            "health",
            TunnelAlias + ".url");

        if (!File.Exists(healthUrlPath))
        {
            return (false, "Secure MCP Tunnel çalışma bilgisi bulunamadı.");
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
            ex is IOException or UnauthorizedAccessException)
        {
            return (false, "Secure MCP Tunnel durumu okunamadı.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return (false, "Secure MCP Tunnel health adresi geçersiz.");
        }

        try
        {
            using var health = await HealthClient.GetAsync(
                new Uri(baseUri, "/healthz"),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            using var ready = await HealthClient.GetAsync(
                new Uri(baseUri, "/readyz"),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            return health.IsSuccessStatusCode && ready.IsSuccessStatusCode
                ? (true, "Secure MCP Tunnel hazır.")
                : (false, "Secure MCP Tunnel henüz hazır değil.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "Secure MCP Tunnel health kontrolü zaman aşımına uğradı.");
        }
        catch (HttpRequestException)
        {
            return (false, "Secure MCP Tunnel erişilemiyor.");
        }
    }

    private static async Task<(bool Success, string Detail)> ProbeStoppedChainAsync(
        CancellationToken cancellationToken)
    {
        var backend = await ProbeAsync(
            BackendHealthUrl,
            "Gitea",
            cancellationToken);
        var proxy = await ProbeAsync(
            ProxyHealthUrl,
            "Caddy",
            cancellationToken);
        var mcp = await ProbeAsync(
            McpHealthUrl,
            "Gitea MCP sunucusu",
            cancellationToken);
        var tunnel = await ProbeTunnelAsync(
            cancellationToken);

        var active = new List<string>(4);
        if (backend.Success)
        {
            active.Add("Gitea");
        }
        if (proxy.Success)
        {
            active.Add("Caddy");
        }
        if (mcp.Success)
        {
            active.Add("Gitea MCP sunucusu");
        }
        if (tunnel.Success)
        {
            active.Add("Secure MCP Tunnel");
        }

        return active.Count == 0
            ? (
                true,
                "Gitea MCP zincirinin tüm health/readiness yüzeyleri kapalı.")
            : (
                false,
                "Durdurma sonrasında hâlâ çalışan bileşenler: " +
                string.Join(", ", active));
    }

    private static async Task RunPrivilegedScriptAsync(
        string script,
        CancellationToken cancellationToken)
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

        var result = await client.CallToolAsync(
            "talvora_run_powershell",
            new Dictionary<string, object?>
            {
                ["script"] = script,
                ["workingDirectory"] = @"C:\Windows\System32",
                ["timeoutSeconds"] = 120,
            },
            cancellationToken: cancellationToken);

        if (result.IsError is true)
        {
            throw new InvalidOperationException(
                "Talvora MCP, Gitea yaşam döngüsü işlemini tamamlayamadı.");
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
                    ? $"Gitea yaşam döngüsü komutu exit code {exitCode.GetInt32()} döndürdü."
                    : error);
        }
    }

    private static string BuildLifecycleScript(string operation)
    {
        var operationLiteral = operation.Replace("'", "''", StringComparison.Ordinal);

        return $$"""
$ErrorActionPreference = 'Stop'
$operation = '{{operationLiteral}}'

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

function Stop-TaskIfRunning {
    param([Parameter(Mandatory = $true)][string] $Name)

    $task = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        return
    }

    $owned = [Collections.Generic.HashSet[int]]::new()
    $actions = @($task.Actions | Where-Object {
        -not [string]::IsNullOrWhiteSpace([string]$_.Execute)
    })

    function Add-OwnedTaskProcesses {
        $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
        if ($all.Count -eq 0) {
            return
        }

        $roots = @($all | Where-Object {
            $process = $_
            foreach ($action in $actions) {
                $execute = [Environment]::ExpandEnvironmentVariables(
                    [string]$action.Execute)
                if (-not [string]::Equals(
                        [string]$process.ExecutablePath,
                        $execute,
                        [StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }

                $arguments = [string]$action.Arguments
                if ([string]::IsNullOrWhiteSpace($arguments) -or
                    (-not [string]::IsNullOrWhiteSpace(
                            [string]$process.CommandLine) -and
                     ([string]$process.CommandLine).IndexOf(
                         $arguments,
                         [StringComparison]::OrdinalIgnoreCase) -ge 0)) {
                    return $true
                }
            }

            return $false
        })

        $frontier = @()
        foreach ($root in $roots) {
            if ($owned.Add([int]$root.ProcessId)) {
                $frontier += [int]$root.ProcessId
            }
        }

        while ($frontier.Count -gt 0) {
            $parents = @($frontier)
            $frontier = @()
            foreach ($child in @($all | Where-Object {
                $parents -contains [int]$_.ParentProcessId
            })) {
                if ($owned.Add([int]$child.ProcessId)) {
                    $frontier += [int]$child.ProcessId
                }
            }
        }
    }

    Add-OwnedTaskProcesses

    if ($null -ne $task -and $task.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $Name -ErrorAction Stop
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $task = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
        Add-OwnedTaskProcesses

        foreach ($processId in @($owned)) {
            if ($processId -eq $PID) {
                continue
            }

            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }

        $ownedAlive = @(
            $owned | Where-Object {
                $null -ne (
                    Get-Process -Id $_ -ErrorAction SilentlyContinue)
            })

        if (($null -eq $task -or $task.State -ne 'Running') -and
            $ownedAlive.Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    $remaining = @(
        $owned | Where-Object {
            $null -ne (
                Get-Process -Id $_ -ErrorAction SilentlyContinue)
        })
    throw (
        "Scheduled task '$Name' did not reach terminal stopped state " +
        "within 20 seconds. TaskState=$($task.State); " +
        "OwnedProcesses=$($remaining -join ',')")
}

function Start-TaskIfNeeded {
    param([Parameter(Mandatory = $true)][string] $Name)

    $task = Get-ScheduledTask -TaskName $Name -ErrorAction Stop
    if ($task.State -ne 'Running') {
        Start-ScheduledTask -TaskName $Name -ErrorAction Stop
    }
}

function Stop-ServiceIfRunning {
    param([Parameter(Mandatory = $true)][string] $Name)

    $service = Get-CimInstance Win32_Service -Filter "Name='$Name'" -ErrorAction Stop
    if ($service.State -eq 'Stopped') {
        return
    }

    # Stop-Service can block indefinitely while a stubborn service stays
    # StopPending. sc.exe submits the control request and returns immediately,
    # so our bounded fallback remains reachable.
    $null = & sc.exe stop $Name 2>&1

    $graceDeadline = [DateTime]::UtcNow.AddSeconds(4)
    do {
        $service = Get-CimInstance Win32_Service -Filter "Name='$Name'" -ErrorAction Stop
        if ($service.State -eq 'Stopped') {
            return
        }

        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $graceDeadline)

    $service = Get-CimInstance Win32_Service -Filter "Name='$Name'" -ErrorAction Stop
    if ($service.State -ne 'Stopped' -and [int] $service.ProcessId -gt 0) {
        Stop-Process -Id ([int] $service.ProcessId) -Force -ErrorAction Stop
    }

    $null = Wait-ServiceState -Name $Name -State 'Stopped' -TimeoutSeconds 20
}

function Start-ServiceIfNeeded {
    param([Parameter(Mandatory = $true)][string] $Name)

    $service = Get-Service -Name $Name -ErrorAction Stop
    if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Running) {
        Start-Service -Name $Name -ErrorAction Stop
    }

    (Get-Service -Name $Name).WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(30))
}

function Stop-Chain {
    Stop-TaskIfRunning -Name 'Gitea MCP Tunnel'
    Stop-TaskIfRunning -Name 'Gitea MCP Server'
    Stop-ServiceIfRunning -Name 'caddy'
    Stop-ServiceIfRunning -Name 'gitea'
}

function Start-Chain {
    Start-ServiceIfNeeded -Name 'gitea'
    Start-ServiceIfNeeded -Name 'caddy'
    Start-TaskIfNeeded -Name 'Gitea MCP Server'
    Start-Sleep -Milliseconds 700
    Start-TaskIfNeeded -Name 'Gitea MCP Tunnel'
}

switch ($operation) {
    'start' {
        Start-Chain
    }
    'stop' {
        Stop-Chain
    }
    'restart' {
        Stop-Chain
        Start-Sleep -Milliseconds 500
        Start-Chain
    }
    default {
        throw "Unsupported Gitea lifecycle operation: $operation"
    }
}
""";
    }
}