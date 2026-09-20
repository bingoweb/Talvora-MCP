param(
    [string] $RepoRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'

$flow = [IO.File]::ReadAllText(
    (Join-Path $RepoRoot 'src\Talvora.Installer\InstallerEngine.Flow.cs'))
$rollback = [IO.File]::ReadAllText(
    (Join-Path $RepoRoot 'src\Talvora.Installer\InstallerEngine.RollbackState.cs'))

$captureIndex = $flow.IndexOf(
    'previousUserState = await CaptureInstallerUserStateAsync')
$registerIndex = $flow.IndexOf(
    'RegisterTrayStartup(trayExecutable')
$restoreIndex = $flow.IndexOf(
    'await RestoreInstallerUserStateAsync')
$rollbackStartIndex = $flow.IndexOf(
    'Install failed after service switch; rolling back')

$result = [pscustomobject]@{
    SnapshotPrecedesUserStateMutation = (
        $captureIndex -ge 0 -and
        $registerIndex -gt $captureIndex
    )
    RestoreRunsInsideRollbackPath = (
        $rollbackStartIndex -ge 0 -and
        $restoreIndex -gt $rollbackStartIndex
    )
    RollbackRestoresCurrentStateBytes = (
        $rollback -match 'current\.json' -and
        $rollback -match 'File\.ReadAllBytesAsync' -and
        $rollback -match 'File\.Move\(' -and
        $rollback -match 'overwrite:\s*true' -and
        $rollback -match 'Flush\(flushToDisk:\s*true\)'
    )
    RollbackRestoresCodexConfigurationBytes = (
        $rollback -match 'config\.toml' -and
        $rollback -match 'CodexConfiguration'
    )
    RollbackRestoresTrayStartupIdentity = (
        $rollback -match 'RegistryValueOptions\.DoNotExpandEnvironmentNames' -and
        $rollback -match 'GetValueKind\(RunValueName\)' -and
        $rollback -match 'DeleteValue\(' -and
        $rollback -match 'SetValue\('
    )
    PreviouslyAbsentFilesAreRemoved = (
        $rollback -match 'DeleteRollbackCreatedFile' -and
        $rollback -match 'if \(!snapshot\.Existed\)'
    )
    LegacyExecutableOnlyRegistryRestoreRemoved = (
        $flow -notmatch 'RegisterTrayStartup\(previousTrayExecutable'
    )
}

$failed = @(
    $result.PSObject.Properties |
        Where-Object { -not [bool]$_.Value } |
        Select-Object -ExpandProperty Name
)

$result

if ($failed.Count -gt 0) {
    throw "Installer rollback state regression failed: $($failed -join ', ')"
}

Write-Host 'INSTALLER_ROLLBACK_STATE_GREEN'
