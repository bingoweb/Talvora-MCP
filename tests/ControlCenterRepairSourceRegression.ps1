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

    if (-not $Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "Control Center repair contract failed: $Contract"
    }
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Expected,
        [Parameter(Mandatory = $true)][string] $Contract
    )

    if ($Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "Control Center repair contract failed: $Contract"
    }
}

$repair = Read-RepoText 'src\Talvora.Tray\ControlCenterRepairService.cs'
$actions = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Actions.cs'
$tray = Read-RepoText 'src\Talvora.Tray\TrayApplicationContext.cs'
$gitea = Read-RepoText 'src\Talvora.Tray\GiteaTrayClient.cs'
$program = Read-RepoText 'src\Talvora.Tray\Program.cs'

Assert-Contains $repair 'ManagedMcpRepairResult' 'repair returns an inspectable strategy/result contract'
Assert-Contains $repair 'TryRepairWithoutRestartAsync' 'repair attempts a targeted non-restart path first'
Assert-Contains $repair 'ConnectExistingAsync' 'a broken secure tunnel can be repaired without restarting a healthy local MCP'
Assert-Contains $repair 'VerifyReadyAsync' 'repair success is gated by post-repair readiness verification'
Assert-Contains $repair 'kontrollü yeniden başlatma' 'targeted generic repair has an explicit escalation path'
Assert-Contains $repair 'targetedFailure' 'a thrown targeted repair failure is retained for controlled fallback'
Assert-Contains $repair 'escalating to controlled restart' 'a thrown targeted repair failure escalates instead of ending the repair pipeline'
Assert-NotContains $repair 'if (state.Health == ControlCenterHealthState.Offline)' 'an offline overall state does not prevent tunnel-only diagnosis when the local protocol is still healthy'
Assert-Contains $repair 'ManagedMcpSessionState.IsManuallyStopped' 'repair respects an explicit user stop decision'

Assert-Contains $gitea 'DiagnoseRepairAsync' 'Gitea repair diagnoses the failed layer before acting'
Assert-Contains $gitea '"repair-backend"' 'Gitea backend has a targeted repair strategy'
Assert-Contains $gitea '"repair-proxy"' 'Caddy has a targeted repair strategy'
Assert-Contains $gitea '"repair-mcp"' 'Gitea MCP runtime has a targeted repair strategy'
Assert-Contains $gitea '"repair-tunnel"' 'Gitea tunnel has a targeted repair strategy'
Assert-Contains $gitea 'escalating to full chain restart' 'Gitea targeted repair escalates only after readiness remains broken'

$giteaAttemptStart = $tray.IndexOf(
    'if (!_giteaRecoveryState.CanAttempt(nowUtc))',
    [StringComparison]::Ordinal)
$giteaFailure = $tray.IndexOf(
    'var failure = _giteaRecoveryState.RegisterFailure(nowUtc);',
    $giteaAttemptStart,
    [StringComparison]::Ordinal)
if ($giteaAttemptStart -lt 0 -or $giteaFailure -le $giteaAttemptStart) {
    throw 'Control Center repair contract failed: Gitea recovery attempt range was not found'
}
$giteaAttemptBody = $tray.Substring(
    $giteaAttemptStart,
    $giteaFailure - $giteaAttemptStart)
Assert-NotContains $giteaAttemptBody '_giteaRecoveryState.ResetForManualAction()' 'Gitea failure counter is not reset before an automatic repair attempt'
Assert-NotContains $giteaAttemptBody 'ManagedMcpSessionState.ClearManualStop("gitea")' 'automatic repair does not silently override a manual-stop decision'

Assert-Contains $tray 'ControlCenterRepairService.RepairAsync' 'automatic managed-MCP recovery uses the same canonical repair engine as Control Center'
Assert-NotContains $tray 'TryRepairGenericWithoutRestartAsync' 'the legacy duplicate repair implementation was removed'
Assert-Contains $actions 'ControlCenterRepairService.RepairAsync' 'the Sorunu düzelt UI action invokes the canonical repair engine'
Assert-Contains $actions 'Sorun tanılanıyor; en az kesintiyle onarım uygulanıyor...' 'detail repair communicates diagnostic-first behavior'
Assert-Contains $program '"--managed-mcp-repair"' 'a deterministic repair command exists for acceptance and operations'

Write-Output 'CONTROL_CENTER_REPAIR_SOURCE_GREEN'
