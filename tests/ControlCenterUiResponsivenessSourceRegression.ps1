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
        [Parameter(Mandatory = $true)][string] $Pattern,
        [Parameter(Mandatory = $true)][string] $Contract
    )

    if ($Text -notmatch $Pattern) {
        throw "Control Center responsiveness contract failed: $Contract"
    }
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Pattern,
        [Parameter(Mandatory = $true)][string] $Contract
    )

    if ($Text -match $Pattern) {
        throw "Control Center responsiveness contract failed: $Contract"
    }
}

$eventStore = Read-RepoText 'src\Talvora.Tray\ControlCenterEventStore.cs'
$rawLogService = Read-RepoText 'src\Talvora.Tray\ControlCenterRawLogService.cs'
$eventsWindow = Read-RepoText 'src\Talvora.Tray\Views\EventsView.xaml.cs'
$mainWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.cs'
$chromeWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Chrome.cs'
$shellWindow = Read-RepoText 'src\Talvora.Tray\Views\ShellWindow.xaml.cs'
$shellXaml = Read-RepoText 'src\Talvora.Tray\Views\ShellWindow.xaml'
$dashboardService = Read-RepoText 'src\Talvora.Tray\ControlCenterDashboardService.cs'
$componentHealth = Read-RepoText 'src\Talvora.Tray\ControlCenterComponentHealthService.cs'
$versionService = Read-RepoText 'src\Talvora.Tray\ControlCenterVersionService.cs'
$smoke = Read-RepoText 'src\Talvora.Tray\ControlCenterSmoke.cs'

Assert-Contains $eventStore 'ReadRecentAsync' 'event history exposes an async WPF-safe read boundary'
Assert-Contains $eventStore 'MaximumEventDocumentBytes\s*=\s*8\s*\*\s*1024\s*\*\s*1024' 'structured event history has an explicit 8 MiB persisted-document ceiling'
Assert-Contains $eventStore 'JsonFileStore\.ReadBounded<ControlCenterEventDocument>' 'structured event history uses the shared same-handle bounded JSON reader'
Assert-NotContains $eventStore 'File\.ReadAllText\(PathName' 'structured event history never allocates an unbounded persisted JSON string'
Assert-Contains $eventsWindow 'await ControlCenterEventStore\.ReadRecentAsync' 'event history does not wait on the cross-process mutex on the dispatcher'
Assert-Contains $mainWindow 'UpdateUi5Snapshot\(snapshot\)' 'dashboard refresh forwards the snapshot to the events view'

Assert-Contains $rawLogService 'ReadTailAsync' 'bounded raw-log IO exposes an async WPF-safe read boundary'
Assert-Contains $eventsWindow '_rawLogReadGate\.WaitAsync\(0\)' 'live raw-log timer suppresses overlapping reads'
Assert-Contains $eventsWindow 'await ControlCenterRawLogService\.ReadTailAsync' 'live raw-log IO runs outside the dispatcher'
Assert-NotContains $eventsWindow 'ControlCenterRawLogService\.ReadTail\(' 'Control Center does not directly perform raw-log file IO on the dispatcher'
Assert-Contains $eventsWindow '_rawLogTimer\.IsEnabled && IsVisible && Model\.RawLogVisible' 'stale raw-log results are discarded after the panel is hidden'
Assert-Contains $eventsWindow 'IsVisible && Model\.RawLogVisible' 'raw-log polling is paused when the events panel is hidden'

Assert-Contains $dashboardService 'ControlCenterComponentHealthService\.GetStatesAsync' 'Talvora Core health is derived from the local service health chain'
Assert-Contains $componentHealth 'MaximumTunnelHealthUrlBytes\s*=\s*4\s*\*\s*1024' 'Control Center tunnel health URL has an explicit 4 KiB ceiling'
Assert-Contains $componentHealth 'TextFileStore\.ReadBoundedAsync\(' 'Control Center tunnel health URL uses the shared same-handle bounded text reader'
Assert-NotContains $componentHealth 'File\.ReadAllTextAsync\(\s*healthUrlPath' 'Control Center never loads an unbounded tunnel health URL file'
Assert-Contains $versionService 'MaximumTalvoraVersionMetadataBytes\s*=\s*512\s*\*\s*1024' 'Talvora current version metadata has an explicit 512 KiB ceiling'
Assert-Contains $versionService 'MaximumPackageVersionMetadataBytes\s*=\s*4\s*\*\s*1024\s*\*\s*1024' 'managed package version metadata has an explicit 4 MiB ceiling'
Assert-Contains $versionService 'JsonFileStore\.ReadBoundedAsync<JsonDocument>' 'version metadata uses the shared same-handle bounded async JSON reader'
Assert-NotContains $versionService 'File\.ReadAllTextAsync\(' 'version metadata never loads an unbounded local JSON file before parsing'
Assert-NotContains $dashboardService 'var status = await BusinessTunnelClient\.GetStatusAsync' 'Talvora Core dashboard health is not conflated with Business tunnel state'
Assert-Contains $mainWindow '_dashboardRefreshPending' 'overlapping refresh requests are coalesced instead of discarded'
Assert-Contains $mainWindow '_overviewView\?\.Model\.MarkUnavailable\(\)' 'a failed first refresh shows the calm unavailable state; later failures keep the last snapshot'

Assert-Contains $chromeWindow 'MaximumWindowPlacementBytes\s*=\s*64\s*\*\s*1024' 'window placement restore has an explicit 64 KiB persisted-document ceiling'
Assert-Contains $chromeWindow 'JsonFileStore\.ReadBounded<WindowPlacementDocument>' 'window placement restore uses the shared same-handle bounded JSON reader'
Assert-NotContains $chromeWindow 'File\.ReadAllText\(path\)' 'window placement restore never allocates an unbounded persisted JSON string on the UI thread'

# UI9: the imperative dashboard/detail/sidebar builders are gone; every page is a XAML view in the shell.
foreach ($removed in @('ControlCenterWindow.Detail.cs', 'ControlCenterWindow.Stitch.cs', 'ControlCenterWindow.EmbeddedIntegrations.cs')) {
    if (Test-Path -LiteralPath (Join-Path $repoRoot ('src\Talvora.Tray\' + $removed))) {
        throw "Control Center responsiveness contract failed: UI9 removed $removed"
    }
}
Assert-NotContains $mainWindow 'BuildContent|BuildDashboard|BuildDetailView|_cardsPanel' 'UI9 the window builds no legacy visual tree'
Assert-NotContains ($shellWindow + $shellXaml) 'LegacyContentHost' 'UI9 the shell has no legacy content host'

# UI1 replaces the obsolete Light-only assertion with behavioural palette validation.
# The Control Center visual smoke also runs with the real Gece and Gun styles and a live switch.
& (Join-Path $PSScriptRoot 'ControlCenterThemeContrastRegression.ps1') | Out-Null
Assert-Contains $smoke 'AssertStitchNavigation' 'Visible shell navigation must be tested in WPF smoke.'
Assert-Contains $smoke 'SaveVisualSnapshotIfRequested' 'WPF design must allow opt-in real rendered screenshot validation.'

Write-Output 'CONTROL_CENTER_UI_RESPONSIVENESS_SOURCE_GREEN'
