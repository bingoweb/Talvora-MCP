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
$eventsWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Events.cs'
$detailWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Detail.cs'
$mainWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.cs'

Assert-Contains $eventStore 'ReadRecentAsync' 'event history exposes an async WPF-safe read boundary'
Assert-Contains $eventsWindow 'await ControlCenterEventStore\.ReadRecentAsync' 'dashboard event history does not wait on the cross-process mutex on the dispatcher'
Assert-Contains $mainWindow 'await RefreshEventsPanelAsync\(\)' 'dashboard refresh awaits the async event-history path'
Assert-Contains $detailWindow 'Task\.WhenAll\(componentTask, versionTask, eventTask\)' 'detail health, version, and event history are gathered concurrently'
Assert-NotContains $detailWindow 'ControlCenterEventStore\s*\.ReadRecent\(' 'detail view does not synchronously read event history on the dispatcher'

Assert-Contains $rawLogService 'ReadTailAsync' 'bounded raw-log IO exposes an async WPF-safe read boundary'
Assert-Contains $eventsWindow '_rawLogRefreshGate\.WaitAsync\(0\)' 'live raw-log timer suppresses overlapping reads'
Assert-Contains $eventsWindow 'await ControlCenterRawLogService\.ReadTailAsync' 'live raw-log IO runs outside the dispatcher'
Assert-NotContains $eventsWindow 'ControlCenterRawLogService\.ReadTail\(' 'Control Center does not directly perform raw-log file IO on the dispatcher'
Assert-Contains $eventsWindow 'registrationId' 'stale raw-log results are associated with the selected MCP identity'
Assert-Contains $mainWindow 'ScheduleDashboardFilter' 'dashboard search avoids rebuilding the full card tree on every key event'
Assert-Contains $mainWindow '_dashboardFilterDebounceTimer' 'dashboard filter debounce has a dedicated dispatcher timer'
Assert-Contains $eventsWindow '_dashboardScroller.Visibility == Visibility.Visible' 'raw-log polling is paused outside the dashboard view'

Write-Output 'CONTROL_CENTER_UI_RESPONSIVENESS_SOURCE_GREEN'
