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
        throw "Storage maintenance contract failed: $Contract"
    }
}

$maintenance = Read-RepoText 'src\Talvora.Tray\TalvoraStorageMaintenanceService.cs'
$sharedCleanup = Read-RepoText 'src\Talvora.Shared\TalvoraOwnedTempCleanup.cs'
$systemMaintenance = Read-RepoText 'src\Talvora\TalvoraSystemStorageMaintenanceService.cs'
$penpotSupervisor = Read-RepoText 'src\Talvora\TalvoraPenpotSupervisorMaintenance.cs'
$serviceProgram = Read-RepoText 'src\Talvora\Program.cs'
$health = Read-RepoText 'src\Talvora.Tray\ManagedMcpTunnelHealthService.cs'
$tray = Read-RepoText 'src\Talvora.Tray\TrayApplicationContext.cs'
$trayMaintenance = Read-RepoText 'src\Talvora.Tray\TrayApplicationContext.StorageMaintenance.cs'
$program = Read-RepoText 'src\Talvora.Tray\Program.cs'

Assert-Contains $maintenance '32L * 1024 * 1024' 'tunnel logs have a bounded 32 MiB rotation threshold'
Assert-Contains $maintenance 'TimeSpan.FromMinutes(5)' 'tunnel log rotation requires an idle quiet window'
Assert-Contains $maintenance 'TimeSpan.FromDays(7)' 'general Talvora temporary artifacts have age retention'

Assert-Contains $sharedCleanup 'MaximumScannedEntriesPerCandidate = 50_000' 'shared cleanup traversal is bounded'
Assert-Contains $sharedCleanup 'MaximumCleanupCandidatesPerRun = 512' 'shared cleanup work per run is bounded'
Assert-Contains $sharedCleanup '"Talvora-Deploy-"' 'canonical deploy leftovers are explicitly allowlisted'
Assert-Contains $sharedCleanup '"TalvoraReparse"' 'known source-edit regression leftovers are explicitly allowlisted'
Assert-Contains $sharedCleanup 'IsOwnedTempName' 'Tray and SYSTEM maintenance share one allowlist policy'
Assert-Contains $sharedCleanup 'root.Attributes & FileAttributes.ReparsePoint' 'a candidate root reparse point is never traversed'
Assert-Contains $sharedCleanup 'DeleteTreeWithoutFollowingReparsePoints' 'cleanup never recursively follows reparse points'
Assert-Contains $maintenance 'TalvoraOwnedTempCleanup.CleanupTopLevel' 'interactive-user cleanup delegates to the shared engine'

Assert-Contains $systemMaintenance 'TimeSpan.FromDays(2)' 'system-owned test artifacts use a shorter retention window'
Assert-Contains $systemMaintenance '"PenpotSmoke"' 'SYSTEM maintenance recognizes Penpot smoke data'
Assert-Contains $systemMaintenance '"TestBrowserVisible"' 'SYSTEM maintenance recognizes Penpot browser smoke profiles'
Assert-Contains $systemMaintenance 'TalvoraOwnedTempCleanup.TryDeleteStaleEntry' 'SYSTEM cleanup uses the shared reparse-safe deletion engine'
Assert-Contains $systemMaintenance 'TimeSpan.FromHours(6)' 'SYSTEM maintenance runs periodically'
Assert-Contains $systemMaintenance 'TalvoraPenpotSupervisorMaintenance.Maintain' 'SYSTEM maintenance repairs Penpot supervisor drift and old logs'
Assert-Contains $serviceProgram 'AddHostedService<TalvoraSystemStorageMaintenanceService>()' 'the SYSTEM maintenance background service is registered'

Assert-Contains $penpotSupervisor "'local-mcp.out.log'" 'Penpot MCP uses a stable stdout log instead of timestamp-file churn'
Assert-Contains $penpotSupervisor "'plugin.out.log'" 'Penpot plugin uses a stable stdout log instead of timestamp-file churn'
Assert-Contains $penpotSupervisor 'MaxBytes = 8388608' 'Penpot child logs have an 8 MiB restart rotation threshold'
Assert-Contains $penpotSupervisor 'MaxBytes 1048576' 'Penpot supervisor error log has a 1 MiB rotation threshold'
Assert-Contains $penpotSupervisor '[Math]::Min(60' 'Penpot crash loops use bounded exponential backoff'
Assert-Contains $penpotSupervisor 'RetainedLegacyLogs = 16' 'only a small diagnostic tail of legacy timestamp logs is retained'
Assert-Contains $penpotSupervisor 'TimeSpan.FromDays(1)' 'legacy Penpot logs expire after one day'
Assert-Contains $penpotSupervisor 'MaximumLegacyLogCandidates = 20_000' 'legacy Penpot log pruning is bounded'
Assert-Contains $penpotSupervisor 'AtomicFile.WriteAllTextAsync' 'the SYSTEM-owned supervisor script is published atomically'
Assert-Contains $penpotSupervisor 'IsLegacyTimestampLogName' 'only the known legacy timestamp-log format is pruned'

Assert-Contains $maintenance 'PruneObsoleteTunnelClientVersions' 'old tunnel-client versions have a retention policy'
Assert-Contains $maintenance 'JsonSerializer.Deserialize<BusinessConfig>' 'active tunnel configs protect their referenced client version'
Assert-Contains $maintenance 'versionDirectories.Take(2)' 'at least two newest tunnel-client versions are retained for rollback'

Assert-Contains $maintenance 'ManagedMcpOperationCoordinator.TryAcquire' 'log maintenance coordinates with MCP lifecycle operations'
Assert-Contains $maintenance 'health.IsQuietForMaintenance' 'log rotation requires live idle telemetry'
Assert-Contains $maintenance 'DisconnectExistingAsync' 'the writer is stopped before rotating its log'
Assert-Contains $maintenance 'reconnectRequired = true' 'a rotation attempt always enters reconnect protection'
Assert-Contains $maintenance 'ConnectExistingAsync' 'the tunnel is restored after rotation'
Assert-Contains $maintenance 'CancellationToken.None' 'reconnect is not abandoned by shutdown/caller cancellation after maintenance started'
Assert-Contains $maintenance 'logPath + ".1"' 'only one bounded tunnel-log archive is retained'

Assert-Contains $health 'QueueDepth == 0' 'maintenance idle state requires an empty queue'
Assert-Contains $health 'DispatcherActive == 0' 'maintenance idle state requires no active dispatcher work'
Assert-Contains $health 'ResponseInProgress == 0' 'maintenance idle state requires no in-flight response delivery'
Assert-Contains $health '"last_enqueue"' 'queue activity timestamp participates in the quiet window'
Assert-Contains $health '"last_completion"' 'dispatcher completion timestamp participates in the quiet window'
Assert-Contains $health '"last_completed"' 'response completion timestamp participates in the quiet window'

Assert-Contains $tray 'InitializeStorageMaintenanceTimer();' 'tray creates the maintenance scheduler'
Assert-Contains $tray 'await RunStorageMaintenanceAsync(' 'startup performs one immediate maintenance pass'
Assert-Contains $tray 'StartStorageMaintenanceTimer();' 'periodic maintenance starts after registry recovery'
Assert-Contains $tray 'StopStorageMaintenanceTimer();' 'tray shutdown stops periodic maintenance'
Assert-Contains $tray 'DisposeStorageMaintenanceTimer();' 'tray disposal releases the maintenance timer'
Assert-Contains $trayMaintenance '6 * 60 * 60 * 1000' 'periodic maintenance cadence is six hours'
Assert-Contains $trayMaintenance 'ControlCenterEventStore.Record' 'meaningful cleanup is visible in Control Center history'

Assert-Contains $program 'TalvoraStorageMaintenanceService.AssertPolicyContract();' 'runtime self-test protects the maintenance policy'
Assert-Contains $program '"--storage-maintenance"' 'operators have a deterministic maintenance command for acceptance'

Write-Output 'STORAGE_MAINTENANCE_SOURCE_GREEN'
