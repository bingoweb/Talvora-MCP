param(
    [string] $RepoRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'

$service = [IO.File]::ReadAllText(
    (Join-Path $RepoRoot 'src\Talvora.Tray\ManagedMcpTunnelProvisioningService.cs'))
$setup = [IO.File]::ReadAllText(
    (Join-Path $RepoRoot 'src\Talvora.Tray\ControlCenterSetupService.cs'))
$window = [IO.File]::ReadAllText(
    (Join-Path $RepoRoot 'src\Talvora.Tray\ControlCenterWindow.Setup.cs'))

$checks = [ordered]@{
    BindEntryPointExists =
        $service.Contains('BindExistingTunnelAsync')
    SharedProfilePipelineExists =
        $service.Contains('ConfigureAndConnectTunnelAsync')
    ExistingBindingRequestsRollback =
        $service.Contains('rollbackOnFailure: true')
    SetupTracksMissingRegistrations =
        $setup.Contains('MissingTunnelRegistrationIds')
    SetupAcceptsExistingIds =
        $setup.Contains('existingTunnelIds')
    SetupInvokesBinding =
        $setup.Contains('BindExistingTunnelAsync(')
    DevFieldExists =
        $window.Contains('_setupDevTunnelIdBox')
    AdminFieldExists =
        $window.Contains('_setupAdminTunnelIdBox')
    UiPassesExistingIds =
        $window.Contains('existingTunnelIds')
    TunnelIdValidatorIsUsed =
        $window.Contains('IsValidTunnelId(') -and
        $setup.Contains('IsValidTunnelId(')
}

$failed = @(
    $checks.GetEnumerator() |
        Where-Object { -not $_.Value } |
        ForEach-Object { $_.Key }
)

if ($failed.Count -gt 0) {
    throw (
        'Focused tunnel binding source regression failed: ' +
        ($failed -join ', ')
    )
}

$checks.GetEnumerator() |
    ForEach-Object {
        "PASS $($_.Key)"
    }

'TALVORA FOCUSED TUNNEL BINDING SOURCE REGRESSION GREEN'
