$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-RepoText {
    param([Parameter(Mandatory = $true)][string] $RelativePath)
    [IO.File]::ReadAllText(
        (Join-Path $repoRoot $RelativePath),
        [Text.Encoding]::UTF8)
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Pattern,
        [Parameter(Mandatory = $true)][string] $Contract
    )

    if ($Text -notmatch $Pattern) {
        throw "Penpot on-demand contract failed: $Contract"
    }
}

$discovery = Read-RepoText 'src\Talvora.Tray\ManagedMcpRecoveryDiscovery.cs'
$lifecycle = Read-RepoText 'src\Talvora.Tray\ControlCenterLifecycleService.cs'
$tray = Read-RepoText 'src\Talvora.Tray\TrayApplicationContext.cs'
$dashboard = Read-RepoText 'src\Talvora.Tray\ControlCenterDashboardService.cs'

Assert-Contains $discovery 'Id = Id,[\s\S]*?AutoStart = false' 'canonical Penpot registration remains opt-in'
Assert-Contains $lifecycle 'private const string PenpotId = "penpot"' 'Penpot lifecycle has an explicit canonical identity'
Assert-Contains $lifecycle 'EnsureOnDemandStoppedAsync' 'on-demand lifecycle exposes startup normalization'
Assert-Contains $lifecycle 'Disable-ScheduledTask -TaskName \$taskName' 'stopped Penpot disables its scheduled task and logon trigger path'
Assert-Contains $lifecycle 'Enable-ScheduledTask -TaskName \$taskName' 'Penpot task is re-enabled before an explicit start'
Assert-Contains $lifecycle 'Stop-OwnedProcesses' 'stop semantics terminate owned local MCP/plugin processes'
Assert-Contains $discovery 'Kind = "process-name",[\s\S]*?Value = "node\.exe"' 'Penpot process ownership requires the Node executable name'
Assert-Contains $discovery 'Value = serverScript' 'Penpot process ownership uses the exact MCP server script marker'
Assert-Contains $discovery 'Value = viteScript' 'Penpot process ownership uses the exact Vite script marker'
Assert-Contains $lifecycle 'var processNames = registration\.DiscoveryHints' 'generic lifecycle derives an explicit process-name allowlist'
Assert-Contains $lifecycle 'processNames\.Length == 0' 'marker-based cleanup fails closed without an executable allowlist'
Assert-Contains $lifecycle '\$processName = \[string\]\$_\.Name' 'lifecycle checks the candidate executable name before marker matching'
Assert-Contains $tray 'EnsureOnDemandManagedMcpsStoppedAsync' 'tray startup normalizes opt-in Penpot to stopped'
Assert-Contains $tray '"penpot"' 'startup normalization is scoped to canonical Penpot'
Assert-Contains $dashboard 'onDemandStopped' 'expected opt-in shutdown is tracked separately from faults'
Assert-Contains $dashboard '"İsteğe bağlı • kapalı"' 'dashboard explains the expected opt-in stopped state'
Assert-Contains $dashboard '!state\.Registration\.AutoStart' 'opt-in stopped MCPs are excluded from global offline fault count'

Write-Output 'PENPOT_ON_DEMAND_SOURCE_GREEN'
