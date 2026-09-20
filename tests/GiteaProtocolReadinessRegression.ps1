param(
    [switch] $Live
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$trayProject = Join-Path $repoRoot 'src\Talvora.Tray\Talvora.Tray.csproj'
$giteaPath = Join-Path $repoRoot 'src\Talvora.Tray\GiteaTrayClient.cs'
$probePath = Join-Path $repoRoot 'src\Talvora.Tray\ManagedMcpProtocolProbeService.cs'
$discoveryPath = Join-Path $repoRoot 'src\Talvora.Tray\ManagedMcpRecoveryDiscovery.cs'
$trayExe = Join-Path $repoRoot 'src\Talvora.Tray\bin\Release\net10.0-windows\win-x64\Talvora.Tray.exe'

function Assert-Contract {
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$gitea = Get-Content -LiteralPath $giteaPath -Raw
$probe = Get-Content -LiteralPath $probePath -Raw
$discovery = Get-Content -LiteralPath $discoveryPath -Raw

$statusStart = $gitea.IndexOf(
    'public static async Task<GiteaStatus> GetStatusAsync(',
    [StringComparison]::Ordinal)
$startStart = $gitea.IndexOf(
    'public static async Task<GiteaStatus> StartAsync(',
    $statusStart,
    [StringComparison]::Ordinal)
Assert-Contract (
    $statusStart -ge 0 -and
    $startStart -gt $statusStart
) 'Gitea GetStatusAsync source range was not found.'
$statusBody = $gitea.Substring(
    $statusStart,
    $startStart - $statusStart)

$mcpHealthIndex = $statusBody.IndexOf(
    'McpHealthUrl',
    [StringComparison]::Ordinal)
$protocolIndex = $statusBody.IndexOf(
    'ProbeMcpProtocolAsync(',
    $mcpHealthIndex,
    [StringComparison]::Ordinal)
$tunnelIndex = $statusBody.IndexOf(
    'ProbeTunnelAsync(',
    $protocolIndex,
    [StringComparison]::Ordinal)
$runningIndex = $statusBody.LastIndexOf(
    'GiteaConnectionState.Running',
    [StringComparison]::Ordinal)

Assert-Contract (
    $mcpHealthIndex -ge 0 -and
    $protocolIndex -gt $mcpHealthIndex -and
    $tunnelIndex -gt $protocolIndex -and
    $runningIndex -gt $tunnelIndex
) 'Gitea Running must require MCP HTTP health, protocol initialize/tools readiness, then tunnel readiness.'
Assert-Contract (
    $statusBody.Contains('Gitea MCP health açık, protokol hazır değil')
) 'Protocol failure must degrade Gitea status instead of returning Running.'

foreach ($required in @(
    'NonSmokeCacheDuration',
    'TimeSpan.FromSeconds(2)',
    'NonSmokeProbeTimeout',
    'TimeSpan.FromSeconds(10)',
    'CancellationTokenSource.CreateLinkedTokenSource',
    'timeoutCts.CancelAfter(NonSmokeProbeTimeout)',
    'McpClient.CreateAsync(',
    'client.ListToolsAsync('
)) {
    Assert-Contract (
        $probe.Contains($required)
    ) "Managed MCP protocol probe is missing '$required'."
}

Assert-Contract (
    $probe.Contains('!cancellationToken.IsCancellationRequested') -and
    $probe.Contains('timeoutCts.IsCancellationRequested')
) 'Internal absolute timeout must remain distinguishable from caller cancellation.'
Assert-Contract (
    $probe.Contains('NonSmokeCache[registration.Id]') -and
    $probe.Contains('cached.ExpiresAtUtc > now')
) 'Ordinary dashboard/status protocol probes must use the short TTL cache.'

foreach ($requiredTool in @(
    'get_gitea_mcp_server_version',
    'get_me'
)) {
    Assert-Contract (
        $discovery.Contains('"' + $requiredTool + '"')
    ) "Gitea protocol readiness registration is missing required capability '$requiredTool'."
}

& dotnet build $trayProject -c Release -p:UseSharedCompilation=false --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Talvora.Tray Release build failed with exit code $LASTEXITCODE."
}

if ($Live) {
    if (-not (Test-Path -LiteralPath $trayExe -PathType Leaf)) {
        throw "Talvora.Tray executable was not produced: $trayExe"
    }

    & $trayExe --gitea-status
    if ($LASTEXITCODE -ne 0) {
        throw "Live Gitea protocol readiness command failed with exit code $LASTEXITCODE."
    }
}

Write-Output (
    'GITEA_PROTOCOL_READINESS_GREEN' +
    $(if ($Live) { '_LIVE' } else { '' }))
