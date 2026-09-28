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
        throw "Control Center Memory Inspector contract failed: $Contract"
    }
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Expected,
        [Parameter(Mandatory = $true)][string] $Contract
    )
    if ($Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "Control Center Memory Inspector contract failed: $Contract"
    }
}

$memoryService = Read-RepoText 'src\Talvora.Tray\ControlCenterMemoryService.cs'
$memoryWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Memory.cs'
$memoryActions = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Memory.Actions.cs'
$mainWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.cs'
$eventsWindow = Read-RepoText 'src\Talvora.Tray\ControlCenterWindow.Events.cs'
$application = Read-RepoText 'src\Talvora.Tray\ControlCenterApplication.cs'
$smoke = Read-RepoText 'src\Talvora.Tray\ControlCenterSmoke.cs'
$memoryTools = Read-RepoText 'src\Talvora\Tools\MemoryTools.cs'

Assert-Contains $memoryTools 'Name = "talvora_memory_list"' 'bounded browse tool exists'
Assert-Contains $memoryService 'ModelContextProtocol.Client' 'Control Center uses the official MCP client'
Assert-Contains $memoryService 'TalvoraConstants.McpDevUrl' 'Memory Inspector uses the local focused development endpoint'
Assert-Contains $memoryService 'CancelAfter(timeout)' 'memory calls have a bounded timeout'
Assert-Contains $memoryService 'StructuredContent is not JsonElement structured' 'structured results are validated'
Assert-NotContains $memoryService 'Microsoft.Data.Sqlite' 'Control Center is not coupled to the SQLite schema'
Assert-NotContains $memoryWindow 'Microsoft.Data.Sqlite' 'Memory UI never opens SQLite directly'

Assert-Contains $memoryWindow 'BuildMemoryView()' 'Memory Inspector has a dedicated in-window view'
Assert-Contains $memoryWindow 'ScheduleMemoryRefresh' 'text input is debounced'
Assert-Contains $memoryWindow '_memoryRefreshGate.WaitAsync(0)' 'memory refresh suppresses overlapping work'
Assert-Contains $memoryWindow 'requestVersion != Volatile.Read(ref _memoryRefreshVersion)' 'stale async search results are discarded'
Assert-Contains $memoryWindow 'ControlCenterMemoryService.DiagnosticsAsync' 'database health is surfaced'
Assert-Contains $memoryWindow 'ControlCenterMemoryService.EmbeddingStatusAsync' 'semantic health is surfaced'
Assert-Contains $memoryWindow 'ControlCenterMemoryService.ReembedAsync' 'bounded re-embed maintenance is surfaced'
Assert-Contains $memoryWindow '_refreshTimer.Stop();' 'dashboard polling stops while memory view is active'

Assert-Contains $memoryActions '_memoryMutationGate.WaitAsync(0)' 'memory mutations are serialized'
Assert-Contains $memoryActions 'EnsureMemoryCurrentAsync' 'mutations use a stale-result guard'
Assert-Contains $memoryActions 'UpdatedAtUtc != displayed.UpdatedAtUtc' 'stale guard compares canonical update timestamps'
Assert-Contains $memoryActions 'ControlAppearance.Danger' 'permanent forget has destructive visual treatment'
Assert-Contains $memoryActions 'Kalıcı olarak unut' 'permanent forget requires explicit confirmation'

Assert-Contains $application 'e.Handled = true;' 'recoverable dispatcher exceptions do not terminate the tray application'
Assert-Contains $application 'IsFatalUiException' 'fatal exceptions are not blindly swallowed'
Assert-Contains $mainWindow 'PrepareForApplicationExitAsync' 'application exit has an async window shutdown boundary'
Assert-Contains $mainWindow 'WaitAsync(TimeSpan.FromSeconds(2))' 'window placement persistence is bounded during exit'
Assert-NotContains $mainWindow 'SaveWindowPlacementAsync().GetAwaiter().GetResult()' 'exit no longer blocks the dispatcher synchronously'
Assert-Contains $mainWindow '_refreshTimer.Tick -= OnRefreshTimerTick' 'dashboard timer handler is detached during final shutdown'
Assert-Contains $mainWindow '_dashboardFilterDebounceTimer.Tick -= OnDashboardFilterDebounceTick' 'filter timer handler is detached during final shutdown'
Assert-Contains $mainWindow '_memorySearchDebounceTimer.Tick -= OnMemorySearchDebounceTick' 'memory debounce handler is detached during final shutdown'
Assert-Contains $eventsWindow '_dashboardScroller.Visibility == Visibility.Visible' 'raw-log polling runs only while the dashboard is visible'
Assert-Contains $mainWindow 'ScheduleDashboardFilter' 'dashboard card filtering is debounced'
Assert-Contains $smoke 'RunMemoryInspectorVisualSmokeScenarioAsync' 'visual smoke explicitly renders the Memory Inspector'

Write-Output 'CONTROL_CENTER_MEMORY_INSPECTOR_SOURCE_GREEN'
