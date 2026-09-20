$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$mainPath = Join-Path $repoRoot 'src\Talvora.Tray\ManagedMcpTunnelProvisioningService.cs'
$pendingPath = Join-Path $repoRoot 'src\Talvora.Tray\ManagedMcpTunnelProvisioningService.Pending.cs'

$main = Get-Content -LiteralPath $mainPath -Raw
$pending = Get-Content -LiteralPath $pendingPath -Raw

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

Assert-Contract ($pending.Contains('JsonFileStore.WriteAsync')) 'Pending tunnel journal must use the shared durable JSON file store.'
Assert-Contract ($pending.Contains('ManagedMcpIdentityKey.Create')) 'Pending journal path must use the collision-proof managed-MCP identity key.'
Assert-Contract ($pending.Contains('CreateAttemptedAtUtc')) 'Pending journal must distinguish an attempted create from a not-yet-attempted request.'
Assert-Contract ($pending.Contains('VerifyPendingRemoteTunnelAsync') -and $pending.Contains('FindPendingRemoteTunnelAsync')) 'Pending retries must reconcile a known tunnel id or an ambiguous create result.'
Assert-Contract ($pending.Contains('"get"') -and $pending.Contains('"list"')) 'Pending reconciliation must use tunnel get/list rather than a blind second create.'
Assert-Contract ($pending.Contains('Otomatik ikinci create engellendi')) 'An unresolved create result must suppress automatic duplicate tunnel creation.'
Assert-Contract (([regex]::Matches($pending, 'WritePendingProvisionAsync\([\s\S]*?CancellationToken\.None\)').Count -ge 2)) 'A remote tunnel id learned from create/list must be persisted without caller cancellation interrupting the journal commit.'
Assert-Contract ($pending.Contains('string? adminKey')) 'A known pending tunnel id must be recoverable without requiring the admin credential again.'
Assert-Contract ($main.Contains('HasPendingRemoteTunnelId(')) 'Provisioning assessment must recognize a durable pending tunnel id as recoverable state.'

$attemptIndex = $pending.IndexOf('CreateAttemptedAtUtc = DateTimeOffset.UtcNow', [StringComparison]::Ordinal)
$attemptWriteIndex = $pending.IndexOf('await WritePendingProvisionAsync(', $attemptIndex, [StringComparison]::Ordinal)
$createIndex = $pending.IndexOf('tunnelId = await CreateRemoteTunnelAsync(', $attemptWriteIndex, [StringComparison]::Ordinal)
$tunnelIdJournalIndex = $pending.IndexOf('TunnelId = tunnelId', $createIndex, [StringComparison]::Ordinal)
$tunnelIdWriteIndex = $pending.IndexOf('await WritePendingProvisionAsync(', $tunnelIdJournalIndex, [StringComparison]::Ordinal)

Assert-Contract ($attemptIndex -ge 0 -and $attemptWriteIndex -gt $attemptIndex -and $createIndex -gt $attemptWriteIndex -and $tunnelIdJournalIndex -gt $createIndex -and $tunnelIdWriteIndex -gt $tunnelIdJournalIndex) 'Create attempt must be persisted before remote create and returned tunnel_id immediately afterward.'

$resolveIndex = $main.IndexOf('ResolveOrCreatePendingRemoteTunnelAsync(', [StringComparison]::Ordinal)
$activationDelayIndex = $main.IndexOf('TimeSpan.FromSeconds(TunnelActivationDelaySeconds)', $resolveIndex, [StringComparison]::Ordinal)
$registryCommitIndex = $main.IndexOf('ManagedMcpRegistryCoordinator.UpsertAsync(', $resolveIndex, [StringComparison]::Ordinal)
$pendingDeleteIndex = $main.IndexOf('DeletePendingProvisionAfterCommitAsync(', $registryCommitIndex, [StringComparison]::Ordinal)

Assert-Contract ($resolveIndex -ge 0 -and $activationDelayIndex -gt $resolveIndex) 'Remote tunnel_id must be durably resolved before the activation delay.'
Assert-Contract ($registryCommitIndex -gt $activationDelayIndex -and $pendingDeleteIndex -gt $registryCommitIndex) 'Pending journal must survive until the managed-MCP registry commit is durable.'
Assert-Contract ($main.Contains('TryDeleteCommittedPendingProvision(')) 'Registry passes must clean a journal left behind after a successful local commit.'

Write-Output 'TUNNEL_PROVISIONING_PENDING_GREEN'
