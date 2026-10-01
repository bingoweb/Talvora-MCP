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
$provisioning = Read-RepoText 'src\Talvora.Tray\ManagedMcpTunnelProvisioningService.cs'
$clientUpdate = Read-RepoText 'src\Talvora.Tray\ManagedMcpTunnelProvisioningService.ClientUpdate.cs'
$businessTunnelClient = Read-RepoText 'src\Talvora.Tray\BusinessTunnelClient.cs'
$recoveryDiscovery = Read-RepoText 'src\Talvora.Tray\ManagedMcpRecoveryDiscovery.cs'
$jsonFileStore = Read-RepoText 'src\Talvora.Shared\JsonFileStore.cs'
$textFileStore = Read-RepoText 'src\Talvora.Shared\TextFileStore.cs'
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
Assert-Contains $maintenance 'RuntimeLifecycle: "running"' 'maintenance self-test fixture uses the current runtime lifecycle contract'
Assert-Contains $maintenance 'IsVerifiedInPlaceRotationRuntimeVersion' 'in-place log rotation normalizes the pinned runtime version before gating'
Assert-Contains $maintenance '0.0.15+acceptance' 'maintenance self-test covers tunnel-client build metadata emitted by live health payloads'
Assert-Contains $maintenance '"response-delivery",' 'maintenance self-test fixture includes the required idle component set'
$provisionStart = $provisioning.IndexOf('public static async Task<ManagedMcpRegistration> ProvisionAsync(', [StringComparison]::Ordinal)
$bindStart = $provisioning.IndexOf('public static async Task<ManagedMcpRegistration> BindExistingTunnelAsync(', [StringComparison]::Ordinal)
$configureStart = $provisioning.IndexOf('private static async Task<ManagedMcpRegistration> ConfigureAndConnectTunnelAsync(', [StringComparison]::Ordinal)
if ($provisionStart -lt 0 -or $bindStart -le $provisionStart -or $configureStart -le $bindStart) { throw 'Storage maintenance contract failed: provisioning method boundaries are missing' }
$provisionSection = $provisioning.Substring($provisionStart, $bindStart - $provisionStart)
$bindSection = $provisioning.Substring($bindStart, $configureStart - $bindStart)
Assert-Contains $provisionSection 'ManagedMcpOperationCoordinator.TryAcquire(' 'automatic tunnel provisioning serializes against maintenance/lifecycle operations'
Assert-Contains $provisionSection 'registration.Id' 'automatic tunnel provisioning leases the target registration'
Assert-Contains $bindSection 'ManagedMcpOperationCoordinator.TryAcquire(' 'manual existing-tunnel binding serializes against maintenance/lifecycle operations'
Assert-Contains $bindSection 'registration.Id' 'manual tunnel binding leases the target registration'

Assert-Contains $sharedCleanup 'EnumerateMatchingTopLevelCandidates' 'shared cleanup enumerates only allowlisted top-level names instead of materializing an unrelated 100k-entry root'
Assert-Contains $sharedCleanup 'prefix + "*"' 'shared cleanup uses prefix-scoped lazy filesystem enumeration'
Assert-Contains $sharedCleanup 'MaximumScannedEntriesPerCandidate = 100_000' 'shared cleanup can inspect a 100k-entry owned tree while remaining bounded'
Assert-Contains $sharedCleanup 'MaximumCleanupCandidatesPerRun = 512' 'shared cleanup work per run is bounded'
Assert-Contains $sharedCleanup 'MaximumDirectChildEntriesPerRun = 10_000' 'nested managed child enumeration is bounded'
Assert-Contains $sharedCleanup 'CleanupGuidDirectories' 'Structural crash residue is retained and cleaned per GUID child directory'
Assert-Contains $sharedCleanup 'CleanupGuidJsonFiles' 'semantic-worker crash residue is retained and cleaned per GUID response file'
$directChildStart = $sharedCleanup.IndexOf('private static TalvoraOwnedTempCleanupResult CleanupDirectChildren(', [StringComparison]::Ordinal)
$tryDeleteStart = $sharedCleanup.IndexOf('public static bool TryDeleteStaleEntry(', [StringComparison]::Ordinal)
if ($directChildStart -lt 0 -or $tryDeleteStart -le $directChildStart) {
    throw 'Storage maintenance contract failed: direct-child cleanup boundaries are missing'
}
$directChildSection = $sharedCleanup.Substring($directChildStart, $tryDeleteStart - $directChildStart)
if ($directChildSection.Contains('catch (Exception', [StringComparison]::Ordinal)) {
    throw 'Storage maintenance contract failed: nested enumeration failures must propagate to Tray/SYSTEM observability'
}
Assert-Contains $sharedCleanup '"Talvora-Deploy-"' 'canonical deploy leftovers are explicitly allowlisted'
Assert-Contains $sharedCleanup '"Talvora-Setup-"' 'crash-left native installer setup roots are explicitly allowlisted'
Assert-Contains $sharedCleanup '"TalvoraReparse"' 'known source-edit regression leftovers are explicitly allowlisted'
Assert-Contains $sharedCleanup 'IsOwnedTempName' 'Tray and SYSTEM maintenance share one allowlist policy'
Assert-Contains $sharedCleanup 'TestPrefixes' 'test-only temporary artifacts have a dedicated retention class'
Assert-Contains $sharedCleanup '"talvora-pipe-test"' 'pipe regression leftovers are classified as test-only artifacts'
Assert-Contains $sharedCleanup 'root.Attributes & FileAttributes.ReparsePoint' 'a candidate root reparse point is never traversed'
Assert-Contains $sharedCleanup 'DeleteTreeWithoutFollowingReparsePoints' 'cleanup never recursively follows reparse points'
Assert-Contains $sharedCleanup 'treeEntryFilter' 'shared cleanup can fail closed on unsafe descendant entries'
Assert-Contains $sharedCleanup 'EnsureDeletionEntryAllowed' 'deletion revalidates descendant safety after the stale-tree scan'
Assert-Contains $sharedCleanup '!treeEntryFilter(entry.FullName)' 'tree scan rejects a descendant that fails the safety filter'
Assert-Contains $maintenance 'TalvoraOwnedTempCleanup.CleanupTopLevel' 'interactive-user cleanup delegates to the shared engine'
Assert-Contains $maintenance 'TestArtifactRetention' 'interactive-user test artifacts have a two-day retention path'
Assert-Contains $maintenance 'TalvoraOwnedTempCleanup.TestPrefixes' 'interactive-user cleanup applies the shared test classification'
Assert-Contains $maintenance 'CleanupGuidDirectories' 'interactive Structural maintenance ages GUID children independently'
Assert-Contains $maintenance 'CleanupGuidJsonFiles' 'interactive semantic-worker maintenance ages response files independently'
if ($maintenance.Contains('["Structural", "semantic-worker"]', [StringComparison]::Ordinal)) {
    throw 'Storage maintenance contract failed: nested Talvora temp roots must not be deleted as one age bucket'
}

$matchingEnumeratorIndex = $sharedCleanup.IndexOf(
    'foreach (var entry in EnumerateMatchingTopLevelCandidates(',
    [StringComparison]::Ordinal)
$candidateBoundIndex = $sharedCleanup.IndexOf(
    'if (++inspectedCandidates > MaximumCleanupCandidatesPerRun)',
    [StringComparison]::Ordinal)
if ($matchingEnumeratorIndex -lt 0 -or $candidateBoundIndex -le $matchingEnumeratorIndex) {
    throw 'Storage maintenance contract failed: candidate bound must count matched Talvora entries, not unrelated root entries'
}
Assert-Contains $sharedCleanup 'IsDirectoryPathReparseSafe' 'shared cleanup exposes trusted-anchor reparse-chain validation'
Assert-Contains $sharedCleanup 'Cleanup entry became active after stale inspection.' 'deletion revalidates retention freshness after the stale scan'
Assert-Contains $sharedCleanup 'allowedRoot,' 'stale entry deletion validates its allowed root before traversal'
Assert-Contains $maintenance 'Tunnel-client version maintenance deferred because its storage path contains a reparse point.' 'tunnel-client version cleanup refuses reparse storage paths'
Assert-Contains $maintenance 'Nested Talvora temp maintenance deferred because its path contains a reparse point.' 'nested user temp cleanup refuses reparse parent paths'
Assert-Contains $maintenance 'IsDirectoryPathReparseSafe' 'tunnel log maintenance validates state/log path reparse chains'

Assert-Contains $systemMaintenance 'TimeSpan.FromDays(2)' 'system-owned test artifacts use a shorter retention window'
Assert-Contains $systemMaintenance 'TalvoraOwnedTempCleanup.TestPrefixes' 'SYSTEM temp cleanup applies the shared test classification'
Assert-Contains $systemMaintenance 'CleanupGuidDirectories' 'SYSTEM Structural maintenance ages GUID children independently'
Assert-Contains $systemMaintenance 'CleanupGuidJsonFiles' 'SYSTEM semantic-worker maintenance ages response files independently'
Assert-Contains $systemMaintenance 'IsSystemOwnedCleanupCandidate(structuralRoot)' 'SYSTEM nested cleanup requires an owned Structural parent root'
Assert-Contains $systemMaintenance 'IsSystemOwnedCleanupCandidate(semanticWorkerRoot)' 'SYSTEM nested cleanup requires an owned semantic-worker parent root'
Assert-Contains $systemMaintenance 'IsSystemOwnedCleanupCandidate' 'SYSTEM cleanup refuses to delete matching names owned by an interactive user'
Assert-Contains $systemMaintenance 'IsDirectoryPathReparseSafe' 'SYSTEM nested TEMP and ProgramData cleanup validate parent reparse chains'
$ownerProbeStart = $systemMaintenance.IndexOf('private static bool IsSystemOwnedCleanupCandidate', [StringComparison]::Ordinal)
$ownerProbeEnd = $systemMaintenance.IndexOf('private static IReadOnlyList<string> GetSystemTempRoots()', [StringComparison]::Ordinal)
if ($ownerProbeStart -lt 0 -or $ownerProbeEnd -le $ownerProbeStart) { throw 'Storage maintenance contract failed: SYSTEM owner-probe boundaries are missing' }
$ownerProbeSection = $systemMaintenance.Substring($ownerProbeStart, $ownerProbeEnd - $ownerProbeStart)
if ($ownerProbeSection.Contains('SystemException', [StringComparison]::Ordinal)) {
    throw 'Storage maintenance contract failed: SYSTEM owner probe must not swallow the full SystemException family'
}
Assert-Contains $systemMaintenance '"PenpotSmoke"' 'SYSTEM maintenance recognizes Penpot smoke data'
Assert-Contains $systemMaintenance '"TestBrowserVisible"' 'SYSTEM maintenance recognizes Penpot browser smoke profiles'
$programDataCleanupStart = $systemMaintenance.IndexOf('CleanupSystemOwnedTestArtifacts(', [StringComparison]::Ordinal)
$ownerFilterStart = $systemMaintenance.IndexOf('private static bool IsSystemOwnedCleanupCandidate', [StringComparison]::Ordinal)
if ($programDataCleanupStart -lt 0 -or $ownerFilterStart -le $programDataCleanupStart) {
    throw 'Storage maintenance contract failed: ProgramData test-artifact cleanup boundaries are missing'
}
$programDataCleanup = $systemMaintenance.Substring($programDataCleanupStart, $ownerFilterStart - $programDataCleanupStart)
Assert-Contains $programDataCleanup 'IsSystemOwnedCleanupCandidate(candidate)' 'SYSTEM ProgramData test cleanup requires LocalSystem ownership before deletion'
Assert-Contains $programDataCleanup 'IsSystemOwnedCleanupCandidate))' 'SYSTEM ProgramData deletion revalidates descendant LocalSystem ownership'
if (($systemMaintenance.Split('IsSystemOwnedCleanupCandidate,').Count - 1) -lt 4) {
    throw 'Storage maintenance contract failed: SYSTEM temp cleanup must pass ownership as both candidate and descendant filter'
}
Assert-Contains $systemMaintenance 'TalvoraOwnedTempCleanup.TryDeleteStaleEntry' 'SYSTEM cleanup uses the shared reparse-safe deletion engine'
Assert-Contains $systemMaintenance 'TimeSpan.FromHours(6)' 'SYSTEM maintenance runs periodically'
Assert-Contains $systemMaintenance 'TalvoraPenpotSupervisorMaintenance.Maintain' 'SYSTEM maintenance repairs Penpot supervisor drift and old logs'
Assert-Contains $systemMaintenance 'logger.LogWarning' 'SYSTEM maintenance routes Penpot cleanup warnings to the service logger'
Assert-Contains $serviceProgram 'AddHostedService<TalvoraSystemStorageMaintenanceService>()' 'the SYSTEM maintenance background service is registered'

Assert-Contains $penpotSupervisor "'local-mcp.out.log'" 'Penpot MCP uses a stable stdout log instead of timestamp-file churn'
Assert-Contains $penpotSupervisor "'plugin.out.log'" 'Penpot plugin uses a stable stdout log instead of timestamp-file churn'
Assert-Contains $penpotSupervisor 'MaxBytes = 8388608' 'Penpot child logs have an 8 MiB rolling threshold'
Assert-Contains $penpotSupervisor 'TalvoraRollingLogStream' 'Penpot child output is drained through supervisor-owned rolling streams'
Assert-Contains $penpotSupervisor 'DroppedBytes' 'Penpot rolling streams expose dropped-byte pressure when disk rotation is blocked'
Assert-Contains $penpotSupervisor 'if (!RotateIfNeeded(count))' 'Penpot child logging drops bytes instead of exceeding its hard disk bound when rotation cannot proceed'
Assert-Contains $penpotSupervisor 'Locked archive must preserve the hard child-log byte bound.' 'Penpot self-test covers locked-archive hard-bound behavior'
Assert-Contains $penpotSupervisor 'LastLogPressureReportUtc' 'Penpot rolling pressure reporting tracks its last emission time'
Assert-Contains $penpotSupervisor 'TotalSeconds -lt 60' 'Penpot rolling pressure diagnostics are throttled to at most once per minute'
Assert-Contains $penpotSupervisor 'RedirectStandardOutput = $true' 'Penpot supervisor owns stdout instead of handing the log file directly to the child'
Assert-Contains $penpotSupervisor 'CopyToAsync' 'Penpot stdout/stderr remain continuously drained without child restart'
if ($penpotSupervisor.Contains('$restartForLogRotation', [StringComparison]::Ordinal) -or
    $penpotSupervisor.Contains('Test-ChildLogLimitReached', [StringComparison]::Ordinal)) {
    throw 'Storage maintenance contract failed: Penpot log rotation must not restart an active child process'
}
Assert-Contains $penpotSupervisor 'MaxBytes 1048576' 'Penpot supervisor error log has a 1 MiB rotation threshold'
Assert-Contains $penpotSupervisor '[Math]::Min(60' 'Penpot crash loops use bounded exponential backoff'
Assert-Contains $penpotSupervisor '$delaySeconds = $backoffSeconds' 'Penpot crash-loop delay starts at the documented three seconds before growth'
Assert-Contains $penpotSupervisor 'function Stop-LoggedChild' 'Penpot rolling-log child cleanup is centralized'
Assert-Contains $penpotSupervisor '$cleanupErrors = New-Object System.Collections.Generic.List[string]' 'Penpot child cleanup isolates individual resource failures'
Assert-Contains $penpotSupervisor '[Console]::Error.WriteLine' 'Penpot supervisor error logging has a non-file fallback'
$rotateStart = $penpotSupervisor.IndexOf('function Rotate-Log', [StringComparison]::Ordinal)
$startChildStart = $penpotSupervisor.IndexOf('function Start-LoggedChild', $rotateStart, [StringComparison]::Ordinal)
if ($rotateStart -lt 0 -or $startChildStart -le $rotateStart) { throw 'Storage maintenance contract failed: Penpot Rotate-Log boundaries are missing' }
$rotateSection = $penpotSupervisor.Substring($rotateStart, $startChildStart - $rotateStart)
Assert-Contains $rotateSection 'catch {' 'Penpot startup log rotation contains diagnostic I/O failures'
Assert-Contains $rotateSection 'return $false' 'Penpot startup log rotation reports deferral instead of terminating the supervisor'
$errorWriterStart = $penpotSupervisor.IndexOf('function Write-SupervisorError', [StringComparison]::Ordinal)
$serverOutStart = $penpotSupervisor.IndexOf('$serverOut =', $errorWriterStart, [StringComparison]::Ordinal)
if ($errorWriterStart -lt 0 -or $serverOutStart -le $errorWriterStart) { throw 'Storage maintenance contract failed: Penpot supervisor error writer boundaries are missing' }
$errorWriterSection = $penpotSupervisor.Substring($errorWriterStart, $serverOutStart - $errorWriterStart)
Assert-Contains $errorWriterSection 'catch {' 'Penpot diagnostic logging failure is contained'
Assert-Contains $errorWriterSection '[Console]::Error.WriteLine' 'Penpot diagnostic logging failure has a fallback sink'
Assert-Contains $errorWriterSection 'if (-not (Rotate-Log -Path $path -MaxBytes 1048576))' 'Penpot supervisor error log stops growing when its archive cannot rotate'
Assert-Contains $penpotSupervisor '$Child.Process.Dispose()' 'Penpot child Process handles are deterministically released each cycle'
Assert-Contains $penpotSupervisor '$Child.OutStream.Dispose()' 'Penpot rolling stdout streams are deterministically released'
Assert-Contains $penpotSupervisor '$Child.ErrStream.Dispose()' 'Penpot rolling stderr streams are deterministically released'
Assert-Contains $penpotSupervisor 'Stop-OrphanedPenpotProcesses' 'Penpot crash recovery sweeps marker-owned orphan child processes'
Assert-Contains $penpotSupervisor '$expectedExecutable = [IO.Path]::GetFullPath($node)' 'Penpot orphan cleanup pins the canonical Node executable'
Assert-Contains $penpotSupervisor '$executablePath = [string]$_.ExecutablePath' 'Penpot orphan cleanup inspects each candidate executable path'
Assert-Contains $penpotSupervisor '$executablePath).Equals($expectedExecutable' 'Penpot orphan cleanup rejects non-Node processes even when their command line contains Penpot paths'
Assert-Contains $penpotSupervisor '/T /F' 'Penpot orphan cleanup terminates the complete owned child process tree'
if ($penpotSupervisor -notmatch 'function Stop-OrphanedPenpotProcesses[\s\S]*?taskkill\.exe[\s\S]*?/T /F[\s\S]*?function Rotate-Log') {
    throw 'Storage maintenance contract failed: Penpot orphan function must terminate the owned process tree'
}
Assert-Contains $penpotSupervisor '$backoffSeconds = 3' 'Penpot supervisor normalizes old owned processes before its first launch cycle'
$backoffIndex = $penpotSupervisor.IndexOf('$backoffSeconds = 3', [StringComparison]::Ordinal)
$initialSweepIndex = $penpotSupervisor.IndexOf('Stop-OrphanedPenpotProcesses', $backoffIndex + 1, [StringComparison]::Ordinal)
$mainLoopIndex = $penpotSupervisor.IndexOf('while ($true) {', $backoffIndex + 1, [StringComparison]::Ordinal)
if ($backoffIndex -lt 0 -or $initialSweepIndex -le $backoffIndex -or $mainLoopIndex -le $initialSweepIndex) {
    throw 'Storage maintenance contract failed: Penpot prelaunch orphan normalization ordering'
}
Assert-Contains $penpotSupervisor 'RetainedLegacyLogs = 16' 'only a small diagnostic tail of legacy timestamp logs is retained'
Assert-Contains $penpotSupervisor 'TimeSpan.FromDays(1)' 'legacy Penpot logs expire after one day'
Assert-Contains $penpotSupervisor 'MaximumLegacyLogEntriesPerPattern = 10_000' 'each legacy Penpot log family has a hard enumeration budget'
Assert-Contains $penpotSupervisor 'scanLimitReached' 'Penpot legacy-log scan-limit deferral is propagated to SYSTEM observability'
Assert-Contains $penpotSupervisor 'AtomicFile.WriteAllTextAsync' 'the SYSTEM-owned supervisor script is published atomically'
Assert-Contains $penpotSupervisor 'EnsureDirectoryIsNotReparsePoint' 'Penpot SYSTEM maintenance rejects directory junction/reparse roots'
Assert-Contains $penpotSupervisor '"Talvora ProgramData root"' 'Talvora ProgramData parent is checked before Penpot maintenance'
Assert-Contains $penpotSupervisor '"Penpot ProgramData root"' 'Penpot ProgramData root is checked before supervisor publication'
Assert-Contains $penpotSupervisor '"Penpot legacy log root"' 'legacy Penpot log cleanup rejects a reparse log root'
Assert-Contains $penpotSupervisor 'Utf8NoBom.GetByteCount(expected)' 'Penpot supervisor drift check computes the canonical byte length before reading existing content'
$penpotDriftStart = $penpotSupervisor.IndexOf('private static void EnsureCanonicalSupervisorScript(', [StringComparison]::Ordinal)
$penpotCleanupStart = $penpotSupervisor.IndexOf('private static TalvoraOwnedTempCleanupResult CleanupLegacyLogs(', [StringComparison]::Ordinal)
if ($penpotDriftStart -lt 0 -or $penpotCleanupStart -le $penpotDriftStart) { throw 'Storage maintenance contract failed: Penpot drift-check boundaries are missing' }
$penpotDriftSection = $penpotSupervisor.Substring($penpotDriftStart, $penpotCleanupStart - $penpotDriftStart)
Assert-Contains $penpotDriftSection 'new FileStream(' 'Penpot supervisor drift check uses one bounded file handle'
Assert-Contains $penpotDriftSection 'existingStream.Length == expectedByteLength' 'Penpot supervisor validates canonical size on the same opened handle'
Assert-Contains $penpotDriftSection 'new StreamReader(' 'Penpot supervisor reads content only from the size-validated handle'
Assert-Contains $penpotDriftSection 'FileAttributes.ReparsePoint' 'existing Penpot supervisor script reparse points are refused before SYSTEM reads or writes them'
if ($penpotDriftSection.Contains('File.ReadAllText(', [StringComparison]::Ordinal)) { throw 'Storage maintenance contract failed: Penpot drift check must not reopen an unbounded path after the size check' }
Assert-Contains $penpotSupervisor 'IsLegacyTimestampLogName' 'only the known legacy timestamp-log format is pruned'
Assert-Contains $penpotSupervisor 'warning?.Invoke' 'Penpot legacy log deletion failures are observable without aborting the remaining cleanup'
Assert-Contains $penpotSupervisor 'Penpot legacy log cleanup deferred.' 'Penpot legacy log warning identifies the deferred file path'

Assert-Contains $maintenance 'PruneObsoleteTunnelClientVersions' 'old tunnel-client versions have a retention policy'
Assert-Contains $maintenance 'JsonFileStore.ReadBounded<BusinessConfig>' 'active tunnel configs protect their referenced client version through bounded JSON'
Assert-Contains $maintenance 'rollbackCandidates' 'two rollback slots are selected after active/journal-protected versions are excluded'
Assert-Contains $maintenance '.Take(2)' 'at least two non-active tunnel-client versions are retained for rollback'
Assert-Contains $maintenance 'GetClientUpdateProtectedVersionDirectories' 'interrupted update journals protect both previous and candidate client versions'
Assert-Contains $maintenance 'TryAcquireVersionPruneLeases' 'version pruning coordinates with every managed tunnel lifecycle/update operation'
Assert-Contains $maintenance 'Tunnel-client version pruning deferred' 'unreadable active tunnel configuration fails version pruning closed'
Assert-Contains $maintenance 'MaximumVersionEntriesPerRun = 10_000' 'tunnel-client staging/version enumeration has a hard work bound'
Assert-Contains $provisioning 'MaximumTunnelMetadataJsonBytes' 'tunnel metadata declares one shared hard JSON byte ceiling'
Assert-Contains $provisioning 'ReadBoundedFileSnapshotAsync' 'tunnel bind rollback snapshots are bounded before allocation'
Assert-Contains $provisioning 'Tunnel config snapshot exceeds the' 'oversized rollback config snapshots fail closed'
if ($provisioning.Contains('File.ReadAllBytesAsync(' + [Environment]::NewLine + '                configPath', [StringComparison]::Ordinal)) { throw 'Storage maintenance contract failed: tunnel bind rollback snapshot is unbounded' }
Assert-Contains $provisioning '256 * 1024' 'tunnel metadata ceiling is 256 KiB'
Assert-Contains $maintenance 'JsonFileStore.ReadBounded<BusinessConfig>' 'active tunnel config is read through a same-handle bounded JSON snapshot'
Assert-Contains $provisioning 'JsonFileStore.ReadBounded<BusinessConfig>' 'runtime tunnel config reads are bounded'
Assert-Contains $provisioning 'JsonFileStore.ReadBounded<TunnelProvisioningScope>' 'tunnel provisioning scope cache reads are bounded'
Assert-Contains $clientUpdate 'JsonFileStore.ReadBounded<TunnelClientUpdateJournal>' 'update-journal retention inspection is bounded'
Assert-Contains $clientUpdate 'JsonFileStore.ReadBoundedAsync<TunnelClientUpdateJournal>' 'interrupted update recovery journal read is bounded asynchronously'
Assert-Contains $clientUpdate 'journal.PreviousConfig is null' 'malformed update journal with missing previous config fails closed before dereference'
Assert-Contains $clientUpdate 'journal.CandidateConfig is null' 'malformed update journal with missing candidate config fails closed before dereference'
if ($clientUpdate.Contains('JsonFileStore.ReadAsync<TunnelClientUpdateJournal>', [StringComparison]::Ordinal)) { throw 'Storage maintenance contract failed: recovery journal read is unbounded' }
Assert-Contains $businessTunnelClient 'JsonFileStore.ReadBounded<BusinessConfig>' 'legacy business tunnel config reads are bounded'
Assert-Contains $recoveryDiscovery 'JsonFileStore.ReadBounded<BusinessConfig>' 'managed recovery discovery config reads are bounded'
if ($maintenance.Contains('File.ReadAllText(fullConfigPath)', [StringComparison]::Ordinal)) { throw 'Storage maintenance contract failed: active tunnel config read is unbounded' }
if ($provisioning.Contains('File.ReadAllText(configPath)', [StringComparison]::Ordinal)) { throw 'Storage maintenance contract failed: runtime tunnel config read is unbounded' }
if ($provisioning.Contains('File.ReadAllText(ScopePath)', [StringComparison]::Ordinal)) { throw 'Storage maintenance contract failed: tunnel scope cache read is unbounded' }
if ($clientUpdate.Contains('File.ReadAllText(journalPath)', [StringComparison]::Ordinal)) { throw 'Storage maintenance contract failed: update-journal retention read is unbounded' }
if ($businessTunnelClient.Contains('File.ReadAllText(ConfigPath)', [StringComparison]::Ordinal)) { throw 'Storage maintenance contract failed: legacy business tunnel config read is unbounded' }
if ($recoveryDiscovery.Contains('File.ReadAllText(configPath)', [StringComparison]::Ordinal)) { throw 'Storage maintenance contract failed: recovery discovery config read is unbounded' }
Assert-Contains $jsonFileStore 'ReadBounded<T>' 'shared JSON store exposes a same-handle bounded reader'
Assert-Contains $jsonFileStore 'ReadBoundedAsync<T>' 'shared JSON store exposes a cancellation-aware bounded async reader'
Assert-Contains $textFileStore 'snapshotLength = stream.Length' 'shared text store validates the opened file handle length before allocation'
Assert-Contains $textFileStore 'ReadExactlyAsync' 'shared text store reads only the validated snapshot length'
Assert-Contains $textFileStore 'maximumBytes' 'shared text store exposes an explicit byte ceiling'
if ($textFileStore.Contains('File.ReadAllText', [StringComparison]::Ordinal)) { throw 'Storage maintenance contract failed: shared bounded text reader must not reopen the path with File.ReadAllText' }

Assert-Contains $maintenance 'ManagedMcpOperationCoordinator.TryAcquire' 'log maintenance coordinates with MCP lifecycle operations'
Assert-Contains $maintenance 'health.IsQuietForMaintenance' 'log rotation requires live idle telemetry'
Assert-Contains $maintenance 'TryRotateTunnelLogWithoutStoppingRuntimeAsync' 'log maintenance never hard-stops the Windows tunnel runtime merely to rotate diagnostics'
Assert-Contains $maintenance 'FileStream' 'live tunnel log rotation uses a bounded in-place file operation instead of a runtime hard kill'
Assert-Contains $maintenance 'logPath + ".1"' 'only one bounded tunnel-log archive is retained'
Assert-Contains $maintenance 'Guid.NewGuid().ToString("N")' 'tunnel log rotation uses an unpredictable temporary archive name'
Assert-Contains $maintenance 'FileMode.CreateNew' 'tunnel log rotation never follows or truncates a pre-created temporary archive path'
if ($maintenance.Contains('archivePath + ".tmp"', [StringComparison]::Ordinal)) { throw 'Storage maintenance contract failed: fixed tunnel archive temp path permits pre-created link redirection' }
$rotationLoopStart = $maintenance.IndexOf('var rotatedLogs = 0;', [StringComparison]::Ordinal)
$rotationLoopEnd = $maintenance.IndexOf('if (cleanup.DeletedEntries > 0 ||', $rotationLoopStart, [StringComparison]::Ordinal)
if ($rotationLoopStart -lt 0 -or $rotationLoopEnd -le $rotationLoopStart) { throw 'Storage maintenance contract failed: tunnel rotation loop boundaries are missing' }
$rotationLoop = $maintenance.Substring($rotationLoopStart, $rotationLoopEnd - $rotationLoopStart)
Assert-Contains $rotationLoop 'InvalidDataException' 'malformed or oversized tunnel health defers only the affected registration'

Assert-Contains $health 'QueueDepth == 0' 'maintenance idle state requires an empty queue'
Assert-Contains $health 'DispatcherActive == 0' 'maintenance idle state requires no active dispatcher work'
Assert-Contains $health 'ResponseInProgress == 0' 'maintenance idle state requires no in-flight response delivery'
Assert-Contains $health 'SchemaVersion != 1' 'maintenance refuses unknown future health schemas until their semantics are reviewed'
Assert-Contains $health '"running"' 'maintenance refuses starting/draining tunnel runtimes'
Assert-Contains $health 'component.Limited' 'maintenance refuses limited idle evidence'
Assert-Contains $health 'component.Status' 'maintenance requires healthy queue/dispatcher/response-delivery observations'
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
Assert-Contains $trayMaintenance 'catch (OperationCanceledException ex)' 'non-lifetime HTTP timeout cancellation is contained at the async timer boundary'

Assert-Contains $systemMaintenance 'RunMaintenancePassSafelyAsync' 'recoverable SYSTEM maintenance failures are isolated from the BackgroundService host lifetime'
Assert-Contains $systemMaintenance 'logger.LogWarning' 'recoverable SYSTEM maintenance failure is observable'

Assert-Contains $program 'TalvoraStorageMaintenanceService.AssertPolicyContract();' 'runtime self-test protects the maintenance policy'
Assert-Contains $program '"--storage-maintenance"' 'operators have a deterministic maintenance command for acceptance'

Write-Output 'STORAGE_MAINTENANCE_SOURCE_GREEN'
