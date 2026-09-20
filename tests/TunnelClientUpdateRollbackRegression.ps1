$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$mainPath = Join-Path $repoRoot 'src\Talvora.Tray\ManagedMcpTunnelProvisioningService.cs'
$transactionPath = Join-Path $repoRoot 'src\Talvora.Tray\ManagedMcpTunnelProvisioningService.ClientUpdate.cs'

$main = Get-Content -LiteralPath $mainPath -Raw
$transaction = Get-Content -LiteralPath $transactionPath -Raw

function Assert-Contract {
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

Assert-Contract ($main.Contains('StageLatestTunnelClientAsync(')) 'Tunnel-client release discovery must be a stage-only operation.'
Assert-Contract (-not $main.Contains('EnsureLatestTunnelClientAsync(')) 'The old pre-readiness config-committing updater must not remain reachable.'
Assert-Contract ($main.Contains('RecoverInterruptedClientUpdateAsync(')) 'Connect/update entry points must recover an interrupted durable update transaction.'
Assert-Contract ($main.Contains('ExecuteClientUpdateTransactionAsync(')) 'Both update and reconnect paths must use the transactional cutover helper.'
Assert-Contract ($main.Contains('ConnectExistingWithConfigAsync(')) 'Rollback/recovery must be able to connect an exact config without re-entering auto-update.'

$stageStart = $main.IndexOf('private static async Task<BusinessConfig> StageLatestTunnelClientAsync(', [StringComparison]::Ordinal)
$stageEnd = $main.IndexOf('private static async Task<string> GetLatestTunnelClientTagAsync(', $stageStart, [StringComparison]::Ordinal)
Assert-Contract ($stageStart -ge 0 -and $stageEnd -gt $stageStart) 'StageLatestTunnelClientAsync source range was not found.'
$stageBody = $main.Substring($stageStart, $stageEnd - $stageStart)
Assert-Contract (-not $stageBody.Contains('WriteBusinessConfig(')) 'StageLatestTunnelClientAsync must not publish the candidate config before readiness.'

Assert-Contract ($transaction.Contains('JsonFileStore.WriteAsync')) 'Client update journal must use the shared durable JSON store.'
Assert-Contract ($transaction.Contains('.client-update.pending.json')) 'Client update transaction must have a durable per-config recovery journal.'
Assert-Contract ($transaction.Contains('ClientUpdatePhasePrepared')) 'Client update journal must record the prepared phase before stopping the old runtime.'
Assert-Contract ($transaction.Contains('ClientUpdatePhaseCandidateConfigured')) 'Client update journal must record candidate config publication.'
Assert-Contract ($transaction.Contains('ClientUpdatePhaseCommitted')) 'Client update journal must record terminal candidate readiness.'
Assert-Contract ($transaction.Contains('ClientUpdatePhaseRolledBack')) 'Rollback completion must be represented durably.'

$executeStart = $transaction.IndexOf('private static async Task ExecuteClientUpdateTransactionAsync(', [StringComparison]::Ordinal)
$recoveryStart = $transaction.IndexOf('private static async Task<ClientUpdateRecoveryResult>', $executeStart, [StringComparison]::Ordinal)
Assert-Contract ($executeStart -ge 0 -and $recoveryStart -gt $executeStart) 'Client update transaction source range was not found.'
$executeBody = $transaction.Substring($executeStart, $recoveryStart - $executeStart)

$journalIndex = $executeBody.IndexOf('await WriteClientUpdateJournalAsync(', [StringComparison]::Ordinal)
$disconnectIndex = $executeBody.IndexOf('await DisconnectExistingAsync(', $journalIndex, [StringComparison]::Ordinal)
$candidateConfigIndex = $executeBody.IndexOf('WriteBusinessConfig(', $disconnectIndex, [StringComparison]::Ordinal)
$candidatePhaseIndex = $executeBody.IndexOf('ClientUpdatePhaseCandidateConfigured', $candidateConfigIndex, [StringComparison]::Ordinal)
$candidateConnectIndex = $executeBody.IndexOf('await ConnectExistingWithConfigAsync(', $candidatePhaseIndex, [StringComparison]::Ordinal)
$committedPhaseIndex = $executeBody.IndexOf('ClientUpdatePhaseCommitted', $candidateConnectIndex, [StringComparison]::Ordinal)
$successEventIndex = $executeBody.IndexOf('client-updated', $committedPhaseIndex, [StringComparison]::Ordinal)
$rollbackIndex = $executeBody.IndexOf('TryRollbackClientUpdateAsync(', $candidateConnectIndex, [StringComparison]::Ordinal)

Assert-Contract (
    $journalIndex -ge 0 -and
    $disconnectIndex -gt $journalIndex -and
    $candidateConfigIndex -gt $disconnectIndex -and
    $candidatePhaseIndex -gt $candidateConfigIndex -and
    $candidateConnectIndex -gt $candidatePhaseIndex -and
    $committedPhaseIndex -gt $candidateConnectIndex -and
    $successEventIndex -gt $committedPhaseIndex
) 'Cutover ordering must be journal -> stop old -> publish candidate -> candidate journal -> exact candidate ready -> committed journal -> success event.'
Assert-Contract ($rollbackIndex -gt $candidateConnectIndex) 'Any candidate readiness failure must enter explicit rollback.'
Assert-Contract ($executeBody.Contains('CancellationToken.None')) 'Once the update journal exists, durable transaction/compensation work must not be interrupted by caller cancellation.'

$rollbackStart = $transaction.IndexOf('private static async Task<Exception?> TryRollbackClientUpdateAsync(', [StringComparison]::Ordinal)
$journalWriterStart = $transaction.IndexOf('private static Task WriteClientUpdateJournalAsync(', $rollbackStart, [StringComparison]::Ordinal)
Assert-Contract ($rollbackStart -ge 0 -and $journalWriterStart -gt $rollbackStart) 'Rollback helper source range was not found.'
$rollbackBody = $transaction.Substring($rollbackStart, $journalWriterStart - $rollbackStart)

$restoreConfigIndex = $rollbackBody.IndexOf('WriteBusinessConfig(', [StringComparison]::Ordinal)
$rollbackStopIndex = $rollbackBody.IndexOf('await DisconnectExistingAsync(', $restoreConfigIndex, [StringComparison]::Ordinal)
$restoreConnectIndex = $rollbackBody.IndexOf('await ConnectExistingWithConfigAsync(', $rollbackStopIndex, [StringComparison]::Ordinal)
$rolledBackPhaseIndex = $rollbackBody.IndexOf('ClientUpdatePhaseRolledBack', $restoreConnectIndex, [StringComparison]::Ordinal)

Assert-Contract (
    $restoreConfigIndex -ge 0 -and
    $rollbackStopIndex -gt $restoreConfigIndex -and
    $restoreConnectIndex -gt $rollbackStopIndex -and
    $rolledBackPhaseIndex -gt $restoreConnectIndex
) 'Rollback must restore the old config, stop any candidate alias state, reconnect the exact old client, then mark rollback durable.'

Assert-Contract ($transaction.Contains('currentConfig == journal.CandidateConfig')) 'Crash recovery must distinguish a published candidate from a pre-cutover journal.'
Assert-Contract ($transaction.Contains('journal.PreviousConfig')) 'Crash recovery must retain the exact previous config for compensation.'
Assert-Contract ($transaction.Contains('The durable recovery journal was retained')) 'Failed compensation must preserve the journal instead of hiding an unrecovered state.'

Write-Output 'TUNNEL_CLIENT_UPDATE_ROLLBACK_GREEN'
