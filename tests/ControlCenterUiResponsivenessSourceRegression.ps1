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
$detailWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Detail.cs'
$mainWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.cs'
$chromeWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Chrome.cs'
$actionsWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Actions.cs'
$dashboardService = Read-RepoText 'src\Talvora.Tray\ControlCenterDashboardService.cs'
$componentHealth = Read-RepoText 'src\Talvora.Tray\ControlCenterComponentHealthService.cs'
$versionService = Read-RepoText 'src\Talvora.Tray\ControlCenterVersionService.cs'
$smoke = Read-RepoText 'src\Talvora.Tray\ControlCenterSmoke.cs'
$stitchWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Stitch.cs'

Assert-Contains $eventStore 'ReadRecentAsync' 'event history exposes an async WPF-safe read boundary'
Assert-Contains $eventStore 'MaximumEventDocumentBytes\s*=\s*8\s*\*\s*1024\s*\*\s*1024' 'structured event history has an explicit 8 MiB persisted-document ceiling'
Assert-Contains $eventStore 'JsonFileStore\.ReadBounded<ControlCenterEventDocument>' 'structured event history uses the shared same-handle bounded JSON reader'
Assert-NotContains $eventStore 'File\.ReadAllText\(PathName' 'structured event history never allocates an unbounded persisted JSON string'
Assert-Contains $eventsWindow 'await ControlCenterEventStore\.ReadRecentAsync' 'dashboard event history does not wait on the cross-process mutex on the dispatcher'
Assert-Contains $mainWindow 'UpdateUi5Snapshot\(snapshot\)' 'dashboard refresh forwards the snapshot to the new events view'
Assert-Contains $detailWindow 'Task\.WhenAll\(componentTask, versionTask, eventTask\)' 'detail health, version, and event history are gathered concurrently'
Assert-NotContains $detailWindow 'ControlCenterEventStore\s*\.ReadRecent\(' 'detail view does not synchronously read event history on the dispatcher'

Assert-Contains $rawLogService 'ReadTailAsync' 'bounded raw-log IO exposes an async WPF-safe read boundary'
Assert-Contains $eventsWindow '_rawLogReadGate\.WaitAsync\(0\)' 'live raw-log timer suppresses overlapping reads'
Assert-Contains $eventsWindow 'await ControlCenterRawLogService\.ReadTailAsync' 'live raw-log IO runs outside the dispatcher'
Assert-NotContains $eventsWindow 'ControlCenterRawLogService\.ReadTail\(' 'Control Center does not directly perform raw-log file IO on the dispatcher'
Assert-Contains $eventsWindow '_rawLogTimer\.IsEnabled && IsVisible && Model\.RawLogVisible' 'stale raw-log results are discarded after the panel is hidden'
Assert-Contains $mainWindow 'ScheduleDashboardFilter' 'dashboard search avoids rebuilding the full card tree on every key event'
Assert-Contains $mainWindow '_dashboardFilterDebounceTimer' 'dashboard filter debounce has a dedicated dispatcher timer'
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
Assert-Contains $mainWindow 'dashboardVisible' 'hidden dashboard views avoid rebuilding card and event content'
Assert-Contains $mainWindow '_dashboardRefreshPending' 'overlapping refresh requests are coalesced instead of discarded'
Assert-Contains $mainWindow 'Son bilinen durum gösteriliyor' 'transient refresh failures preserve the last known dashboard state'
Assert-Contains $mainWindow '_lastSuccessfulDashboardRefresh' 'stale dashboard state exposes its last successful refresh time'

Assert-Contains $chromeWindow 'UpdateDashboardControlsLayout' 'dashboard controls have a dedicated compact layout policy'
Assert-Contains $chromeWindow 'MaximumWindowPlacementBytes\s*=\s*64\s*\*\s*1024' 'window placement restore has an explicit 64 KiB persisted-document ceiling'
Assert-Contains $chromeWindow 'JsonFileStore\.ReadBounded<WindowPlacementDocument>' 'window placement restore uses the shared same-handle bounded JSON reader'
Assert-NotContains $chromeWindow 'File\.ReadAllText\(path\)' 'window placement restore never allocates an unbounded persisted JSON string on the UI thread'
Assert-Contains $chromeWindow 'Grid\.SetColumnSpan\(_dashboardSearchStack, 3\)' 'compact layout gives search the full first row'
Assert-Contains $chromeWindow 'Grid\.SetRow\(_filterBox, compact \? 1 : 0\)' 'compact layout moves toolbar actions to the second row'
Assert-Contains $smoke 'AssertCompactDashboardLayout' 'visual smoke verifies the dashboard at the minimum supported window width'
Assert-Contains $smoke 'AssertElementFitsHorizontally' 'compact smoke rejects horizontally clipped primary controls'

Assert-Contains $mainWindow 'Focusable = true' 'dashboard cards participate in keyboard focus'
Assert-Contains $mainWindow 'KeyboardNavigation\.SetIsTabStop\(card, true\)' 'dashboard cards participate in tab navigation'
Assert-Contains $mainWindow 'e\.Key is not \(Key\.Enter or Key\.Space\)' 'dashboard cards support Enter and Space activation'
Assert-Contains $mainWindow 'AutomationProperties\.SetName' 'dashboard controls expose UI Automation names'

Assert-Contains $actionsWindow 'Dev/Admin bağlantılarını yenile' 'ready Core actions explicitly identify the focused ChatGPT connections'
Assert-Contains $actionsWindow 'Servisi yeniden başlat' 'degraded Core actions repair the Core service instead of pretending to reconnect it'
Assert-NotContains $actionsWindow '"Yeniden bağlan"[\s\r\n]+\s*SymbolRegular\.PlugConnected20' 'Core attention card no longer exposes the legacy tunnel-only contextual action'

Assert-Contains $stitchWindow 'BuildNavigationSidebar' 'Google Stitch control center navigation must exist as native WPF.'
Assert-Contains $stitchWindow 'Genel Bakış' 'Stitch navigation must expose the real overview.'
Assert-Contains $stitchWindow 'MCP Hizmetleri' 'Stitch navigation must expose the real managed MCP services.'
Assert-Contains $stitchWindow '_shellModel\?\.SelectPage\(4\)' 'Legacy Events navigation must open the new events page.'
Assert-Contains $stitchWindow 'RefreshOverviewMetrics' 'Overview metric values must be driven by actual MCP snapshot data.'
Assert-Contains $stitchWindow 'ManagedMcpAggregateHealthPolicy\.IsIgnored' 'Optional stopped MCPs must be classified as intentionally inactive.'
Assert-Contains $mainWindow 'var cards = new StackPanel' 'Managed MCP cards must use a gap-free full-width service list.'
Assert-NotContains $mainWindow 'var cards = new WrapPanel' 'Old multi-column service tiles must not regress.'
Assert-Contains $mainWindow 'intentionallyInactive' 'MCP status badge must distinguish expected offline from real failure.'
# UI1 replaces the obsolete Light-only assertion with behavioural palette validation.
# The Control Center visual smoke also runs with the real Gece and Gun styles and a live switch.
& (Join-Path $PSScriptRoot 'ControlCenterThemeContrastRegression.ps1') | Out-Null
Assert-Contains $smoke 'AssertStitchNavigation' 'Visible sidebar navigation must be tested in WPF smoke.'
Assert-Contains $smoke 'SaveVisualSnapshotIfRequested' 'WPF design must allow opt-in real rendered screenshot validation.'

Write-Output 'CONTROL_CENTER_UI_RESPONSIVENESS_SOURCE_GREEN'
