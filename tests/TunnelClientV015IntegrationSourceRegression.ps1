$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-RepoText {
    param([Parameter(Mandatory = $true)][string] $RelativePath)
    return [IO.File]::ReadAllText(
        (Join-Path $repoRoot $RelativePath),
        [Text.Encoding]::UTF8)
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Expected,
        [Parameter(Mandatory = $true)][string] $Contract
    )

    if ($Text.IndexOf($Expected, [StringComparison]::Ordinal) -lt 0) {
        throw "Tunnel-client v0.0.15 integration contract failed: $Contract"
    }
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Expected,
        [Parameter(Mandatory = $true)][string] $Contract
    )

    if ($Text.IndexOf($Expected, [StringComparison]::Ordinal) -ge 0) {
        throw "Tunnel-client v0.0.15 integration contract failed: $Contract"
    }
}

$provisioning = Read-RepoText 'src\Talvora.Tray\ManagedMcpTunnelProvisioningService.cs'
$health = Read-RepoText 'src\Talvora.Tray\ManagedMcpTunnelHealthService.cs'
$componentHealth = Read-RepoText 'src\Talvora.Tray\ControlCenterComponentHealthService.cs'
$repair = Read-RepoText 'src\Talvora.Tray\ControlCenterRepairService.cs'
$program = Read-RepoText 'src\Talvora.Tray\Program.cs'
$detail = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Detail.cs'

Assert-Contains $provisioning 'HealthDetailsUrl' 'structured runtime status must retain the v0.0.15 aggregate health URL'
Assert-Contains $provisioning 'McpHealthUrl' 'structured runtime status must retain the v0.0.15 MCP health URL'
Assert-Contains $provisioning '"health_details_url"' 'runtime status parser must read the aggregate health URL'
Assert-Contains $provisioning '"mcp_health_url"' 'runtime status parser must read the MCP component health URL'
Assert-Contains $provisioning '"v0.0.15"' 'source policy fixture must track the current tunnel-client generation'

Assert-Contains $health 'MaximumHealthPayloadBytes' 'detailed health payloads must be size-bounded'
Assert-Contains $health 'ReadBoundedPayloadAsync' 'detailed health reads must enforce the payload ceiling while streaming'
Assert-Contains $health 'MaximumHealthPayloadBytes + 1 - total' 'stream reads must stop immediately after the configured ceiling'
Assert-Contains $health 'ReadAsStreamAsync' 'detailed health must avoid unbounded body buffering when content length is absent'
Assert-Contains $health 'candidate.IsLoopback' 'detailed health reads must be restricted to loopback'
Assert-Contains $health '"schema_version"' 'health schema version must be parsed'
Assert-Contains $health '"control-plane"' 'control-plane health must be parsed'
Assert-Contains $health '"response-delivery"' 'response-delivery health must be parsed'
Assert-Contains $health '"queue"' 'queue health must be parsed'
Assert-Contains $health '"dispatcher"' 'dispatcher health must be parsed'
Assert-Contains $health '"mcp"' 'MCP observation health must be parsed'
Assert-Contains $health 'HasCriticalDegradation' 'critical tunnel component degradation must be explicit'
Assert-Contains $health 'RequiresRuntimeRenewal' 'runtime renewal must distinguish actionable delivery degradation from upstream control-plane faults'

Assert-Contains $componentHealth 'GetTunnelDiagnosticStatesAsync' 'Control Center must consume detailed tunnel diagnostics'
Assert-Contains $componentHealth 'Detailed tunnel health timed out.' 'supplemental diagnostics timeout must not fail the full Control Center refresh'
Assert-Contains $componentHealth '"Tunnel runtime"' 'Control Center must expose the tunnel runtime generation'
Assert-Contains $componentHealth '"Control plane"' 'Control Center must expose control-plane health'
Assert-Contains $componentHealth '"Yanıt teslimi"' 'Control Center must expose response-delivery health'
Assert-Contains $componentHealth '"İstek kuyruğu"' 'Control Center must expose queue health'
Assert-Contains $componentHealth '"İş dağıtıcı"' 'Control Center must expose dispatcher health'
Assert-Contains $componentHealth '"Tünel MCP gözlemi"' 'Control Center must expose tunnel-side MCP observations'
Assert-Contains $detail '"tunnel-health" => "Tünel tanısı"' 'detail UI must label v0.0.15 health rows in user language'

Assert-Contains $repair 'tunnelHealth is { RequiresRuntimeRenewal: true }' 'repair must renew the runtime only for actionable response-delivery degradation'
Assert-Contains $repair 'tunnel-client already owns retry and' 'repair must document why control-plane network faults are left to tunnel-client backoff'
Assert-Contains $repair 'DisconnectExistingAsync' 'critical detailed degradation must use tunnel-only remediation'
Assert-Contains $repair '"Yalnız tünel çalışma katmanını yenile"' 'targeted repair strategy must remain explicit'
Assert-Contains $program 'TunnelHealthSchema=' 'managed MCP probe must report detailed health schema'
Assert-Contains $program 'TunnelCriticalDegradation=' 'managed MCP probe must fail on critical detailed degradation'
Assert-Contains $program '"--managed-mcp-tunnel-update"' 'a deterministic tunnel update/package-completion acceptance command must exist'

foreach ($requiredAsset in @(
    '"cloudflared.exe"',
    '"cloudflared-manifest.json"',
    '"LICENSE"',
    '"NOTICE"',
    'packageBaseName + "-licenses.txt"',
    'packageBaseName + ".spdx.json"'
)) {
    Assert-Contains $provisioning $requiredAsset "official v0.0.15 release asset must be retained: $requiredAsset"
}

Assert-Contains $provisioning 'HasCompleteTunnelClientPackage(' 'same-version installs with missing sidecars must be repaired'
Assert-Contains $provisioning 'FilesHaveSameSha256(' 'same-hash in-use binaries must be preserved while missing sidecars are completed'
Assert-Contains $provisioning 'SHA256.HashData(archiveBytes)' 'release archive must remain checksum-verified'
Assert-NotContains $provisioning 'EnsureLatestTunnelClientAsync(' 'legacy pre-readiness updater must not return'

Write-Output 'TUNNEL_CLIENT_V015_INTEGRATION_GREEN'

