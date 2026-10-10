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
$policy = Read-RepoText 'src\Talvora.Tray\ManagedMcpAggregateHealthPolicy.cs'
$trayProgram = Read-RepoText 'src\Talvora.Tray\Program.cs'

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
Assert-Contains $dashboard 'ManagedMcpAggregateHealthPolicy\.IsIgnored' 'dashboard delegates inactive MCP classification to shared health policy'
Assert-Contains $dashboard '\.Where\(item => !item\.Ignored\)' 'only actionable MCP states contribute to global health counts'
Assert-Contains $dashboard 'var offline = active\.Count' 'global offline faults count only active monitored MCPs'
Assert-Contains $policy '!registration\.AutoStart' 'on-demand registrations remain part of the inactive classification'
Assert-Contains $policy 'health is null or ControlCenterHealthState\.Offline' 'on-demand offline or not-yet-checked states do not trigger system errors'
Assert-Contains $policy 'IsIgnored\(optional, ControlCenterHealthState\.Offline, false\)' 'optional disabled services are explicitly ignored in runtime contract'
Assert-Contains $policy 'IsIgnored\(required, ControlCenterHealthState\.Offline, false\)' 'real required service outages must remain visible'
Assert-Contains $policy 'IsIgnored\(core, ControlCenterHealthState\.Offline, true\)' 'Talvora Core outages may never be hidden as optional'
Assert-Contains $policy 'IsIgnored\(optional, ControlCenterHealthState\.Attention, false\)' 'running optional services with warnings still require attention'
Assert-Contains $trayProgram 'ManagedMcpAggregateHealthPolicy\.AssertContract\(\);' 'the semantic policy contract runs during the WPF tray self-test'

Write-Output 'PENPOT_ON_DEMAND_SOURCE_GREEN'
