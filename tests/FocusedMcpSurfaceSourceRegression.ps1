param(
    [string] $RepoRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'

$constants = [IO.File]::ReadAllText(
    (Join-Path $RepoRoot 'src\Talvora.Shared\TalvoraConstants.cs'))
$program = [IO.File]::ReadAllText(
    (Join-Path $RepoRoot 'src\Talvora\Program.cs'))
$surfacePolicy = [IO.File]::ReadAllText(
    (Join-Path $RepoRoot 'src\Talvora.Shared\TalvoraMcpToolSurfacePolicy.cs'))
$recovery = [IO.File]::ReadAllText(
    (Join-Path $RepoRoot 'src\Talvora.Tray\ManagedMcpRecoveryDiscovery.cs'))
$coordinator = [IO.File]::ReadAllText(
    (Join-Path $RepoRoot 'src\Talvora.Tray\ManagedMcpRegistryCoordinator.cs'))
$lifecycle = [IO.File]::ReadAllText(
    (Join-Path $RepoRoot 'src\Talvora.Tray\ControlCenterLifecycleService.cs'))

$focusedStart = $recovery.IndexOf(
    'internal sealed class TalvoraFocusedManagedMcpRecoveryDiscovery',
    [StringComparison]::Ordinal)
$focusedEnd = $recovery.IndexOf(
    'internal sealed class GiteaManagedMcpRecoveryDiscovery',
    [StringComparison]::Ordinal)
$focusedBlock = if (
    $focusedStart -ge 0 -and
    $focusedEnd -gt $focusedStart
) {
    $recovery.Substring(
        $focusedStart,
        $focusedEnd - $focusedStart)
}
else {
    ''
}

$checks = [ordered]@{
    ConstantsExposeFocusedEndpoints = (
        $constants.Contains('McpDevPath = "/mcp/dev"') -and
        $constants.Contains('McpAdminPath = "/mcp/admin"') -and
        $constants.Contains('McpDevUrl = "http://127.0.0.1:7676/mcp/dev"') -and
        $constants.Contains('McpAdminUrl = "http://127.0.0.1:7676/mcp/admin"')
    )
    ServiceMapsAllThreeMcpEndpoints = (
        $program.Contains('app.MapMcp("/mcp");') -and
        $program.Contains('app.MapMcp("/mcp/dev");') -and
        $program.Contains('app.MapMcp("/mcp/admin");') -and
        $program.Contains('ConfigureSessionOptions')
    )
    SurfaceCountsArePinned = (
        $surfacePolicy.Contains('ExpectedFullToolCount = 204') -and
        $surfacePolicy.Contains('ExpectedDevelopmentToolCount = 174') -and
        $surfacePolicy.Contains('ExpectedAdministrationToolCount = 91')
    )
    FocusedRecoveryRegistrationsExist = (
        $coordinator.Contains('"talvora-dev"') -and
        $coordinator.Contains('"talvora-admin"') -and
        $coordinator.Contains('"talvora-dev-business"') -and
        $coordinator.Contains('"talvora-admin-business"') -and
        $coordinator.Contains('TalvoraConstants.McpDevUrl') -and
        $coordinator.Contains('TalvoraConstants.McpAdminUrl')
    )
    FocusedRegistrationsAreTunnelOnly = (
        $focusedBlock.Contains('Kind = "tunnel"') -and
        -not $focusedBlock.Contains('Kind = "windows-service"') -and
        -not $focusedBlock.Contains('Kind = "scheduled-task"')
    )
    FocusedRegistrationsHaveProtocolProbes = (
        $coordinator.Contains('"talvora_apply_patch", "talvora_dotnet_build"') -and
        $coordinator.Contains('"talvora_service_get", "talvora_registry_get"')
    )
    GenericLifecycleSupportsTunnelOnlyRegistration = (
        $lifecycle.Contains('var hasLocalLifecycleComponents =') -and
        $lifecycle.Contains('if (hasLocalLifecycleComponents &&') -and
        $lifecycle.Contains('else if (hasLocalLifecycleComponents)')
    )
}

$failed = @(
    $checks.GetEnumerator() |
        Where-Object { -not $_.Value } |
        ForEach-Object { $_.Key }
)

if ($failed.Count -gt 0) {
    throw (
        'Focused MCP surface source regression failed: ' +
        ($failed -join ', ')
    )
}

$checks.GetEnumerator() |
    ForEach-Object {
        "PASS $($_.Key)"
    }

'TALVORA FOCUSED MCP SURFACE SOURCE REGRESSION GREEN'
