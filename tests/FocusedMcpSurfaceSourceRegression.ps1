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

$localStart = $recovery.IndexOf(
    'internal sealed class TalvoraManagedMcpRecoveryDiscovery',
    [StringComparison]::Ordinal)
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
$localBlock = if (
    $localStart -ge 0 -and
    $focusedStart -gt $localStart
) {
    $recovery.Substring(
        $localStart,
        $focusedStart - $localStart)
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
        $surfacePolicy.Contains('ExpectedFullToolCount = 236') -and
        $surfacePolicy.Contains('ExpectedDevelopmentToolCount = 199') -and
        $surfacePolicy.Contains('ExpectedAdministrationToolCount = 88') -and
        $surfacePolicy.Contains('ExpectedSharedToolCount = 51')
    )
    SharedSurfaceContractIsExplicit = (
        $surfacePolicy.Contains('private static readonly HashSet<string> SharedTools') -and
        $surfacePolicy.Contains('public static bool IsShared(string toolName)') -and
        $surfacePolicy.Contains('public static IReadOnlyList<string> GetSharedToolNames()') -and
        $surfacePolicy.Contains('.Intersect(administration, StringComparer.Ordinal)') -and
        $surfacePolicy.Contains('actualOverlap.SequenceEqual(')
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
    LocalTalvoraRegistrationIsServiceOnly = (
        $localBlock.Contains('Kind = "windows-service"') -and
        -not $localBlock.Contains('Kind = "tunnel"') -and
        -not $localBlock.Contains('business.json') -and
        -not $localBlock.Contains('Tunnel =')
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
    TalvoraReconnectCoordinatesFocusedTunnels = (
        $lifecycle.Contains('ReconnectTalvoraConnectionsAsync') -and
        $lifecycle.Contains('TalvoraDevId = "talvora-dev"') -and
        $lifecycle.Contains('TalvoraAdminId = "talvora-admin"') -and
        $lifecycle.Contains('ReconnectTalvoraFocusedAsync') -and
        $lifecycle.Contains('DisconnectTalvoraFocusedAsync')
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
