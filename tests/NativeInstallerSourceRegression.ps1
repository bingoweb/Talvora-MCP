param(
    [string] $RepoRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'

$serviceProgram = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Program.cs'))
$trayProgram = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\Program.cs'))
$trayProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\Talvora.Tray.csproj'))
$installerProgram = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\Program.cs'))
$installerProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\Talvora.Installer.csproj'))
$manifest = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\app.manifest'))

$result = [pscustomobject]@{
    ServiceRejectsStopControl = (
        $serviceProgram -match 'CanStop\s*=\s*false' -and
        $serviceProgram -match 'CanPauseAndContinue\s*=\s*false'
    )
    InstallerCanReplaceNonStoppableService = (
        $installerProgram -match 'queryex' -and
        $installerProgram -match 'deleting registration before terminating PID' -and
        $installerProgram -match 'Kill\(entireProcessTree:\s*true\)'
    )
    InstallerConfiguresResilientSystemService = (
        $installerProgram -match 'restart/1000/restart/3000/restart/10000/restart/30000/restart/60000' -and
        $installerProgram -match '"sidtype"' -and
        $installerProgram -match '"unrestricted"'
    )
    TrayIsWinExe = ($trayProject -match '<OutputType>WinExe</OutputType>')
    TrayUsesNotifyIcon = ($trayProgram -match '\bNotifyIcon\b')
    TrayReconnectIsNative = (
        $trayProgram -match '"runtimes",\s*"connect"' -and
        $trayProgram -match 'CryptUnprotectData' -and
        $trayProgram -notmatch 'powershell\.exe'
    )
    TrayCommandModesBeforeMutex = (
        $trayProgram.IndexOf('--reconnect', [StringComparison]::Ordinal) -ge 0 -and
        $trayProgram.IndexOf('new Mutex', [StringComparison]::Ordinal) -gt
            $trayProgram.IndexOf('--reconnect', [StringComparison]::Ordinal)
    )
    TrayTunnelClientUsesStateWorkingDirectory = (
        $trayProgram -match 'WorkingDirectory\s*=\s*config\.StateRoot'
    )
    InstallerEmbedsPayload = ($installerProject -match 'EmbeddedResource Include="Payload\.zip"')
    InstallerRequiresAdmin = ($manifest -match 'requestedExecutionLevel level="requireAdministrator"')
    InstallerUsesProgramFiles = ($installerProgram -match 'SpecialFolder\.ProgramFiles')
    InstallerRegistersTrayStartup = ($installerProgram -match 'TalvoraTray')
    InstallerUsesVersionedPayload = (
        $installerProgram -match 'Versions' -and
        $installerProgram -match 'versionId' -and
        $installerProgram -match 'CleanupObsoleteInstallations'
    )
    InstallerSupportsRollback = (
        $installerProgram -match 'rolling back' -and
        $installerProgram -match 'previousServiceExecutable'
    )
    TraySupportsGracefulShutdown = (
        $trayProgram -match 'Talvora\.Tray\.Shutdown' -and
        $installerProgram -match 'EventWaitHandle\.OpenExisting'
    )
    InstallerSchedulesLockedCleanup = (
        $installerProgram -match 'MoveFileEx' -and
        $installerProgram -match 'DelayUntilReboot'
    )
}

$result | Format-List

if ($result.PSObject.Properties.Value -contains $false) {
    throw 'Native installer/tray source contract is incomplete.'
}

Write-Output 'NATIVE_INSTALLER_SOURCE_GREEN'