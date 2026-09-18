param(
    [string] $RepoRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'

$serviceProgram = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Program.cs'))
$serviceProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Talvora.csproj'))
$developerTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\DeveloperTools.cs'))
$jobTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\JobTools.cs'))
$gitTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\GitTools.cs'))
$configAssetTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\ConfigAssetTools.cs'))
$watchTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\WatchTools.cs'))
$chocoTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\ChocolateyTools.cs'))
$trayProgram = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\Program.cs'))
$trayProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\Talvora.Tray.csproj'))
$installerProgram = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\Program.cs'))
$installerProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\Talvora.Installer.csproj'))
$manifest = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\app.manifest'))
$iconPath = Join-Path $RepoRoot 'assets\Talvora.ico'
$iconBytes = [IO.File]::ReadAllBytes($iconPath)
$iconFrameCount = if ($iconBytes.Length -ge 6) { [BitConverter]::ToUInt16($iconBytes, 4) } else { 0 }

$result = [pscustomobject]@{
    WatchToolContract = (
        $watchTools -match 'talvora_watch_start' -and
        $watchTools -match 'talvora_watch_get' -and
        $watchTools -match 'talvora_watch_list' -and
        $watchTools -match 'talvora_watch_read' -and
        $watchTools -match 'talvora_watch_wait' -and
        $watchTools -match 'talvora_watch_stop'
    )
    ChocolateyToolContract = (
        $chocoTools -match 'talvora_choco_info' -and
        $chocoTools -match 'talvora_choco_list' -and
        $chocoTools -match 'talvora_choco_search' -and
        $chocoTools -match 'talvora_choco_install' -and
        $chocoTools -match 'talvora_choco_upgrade' -and
        $chocoTools -match 'talvora_choco_uninstall' -and
        $chocoTools -match 'talvora_choco_run' -and
        $chocoTools -match 'complete Chocolatey CLI surface' -and
        $chocoTools -notmatch '(?i)winget'
    )
    ConfigAssetToolContract = (
        $configAssetTools -match 'talvora_read_text_range' -and
        $configAssetTools -match 'talvora_tail_text' -and
        $configAssetTools -match 'talvora_append_text' -and
        $configAssetTools -match 'talvora_json_get' -and
        $configAssetTools -match 'talvora_json_set' -and
        $configAssetTools -match 'talvora_json_delete' -and
        $configAssetTools -match 'talvora_archive_list' -and
        $configAssetTools -match 'talvora_archive_create' -and
        $configAssetTools -match 'talvora_archive_extract' -and
        $configAssetTools -match 'talvora_http_download'
    )
    JobExitAvoidsDisposeRace = (
        $jobTools -notmatch 'LiveJobs\.TryRemove\(jobId, out _\);\s*runtime\.Dispose\(\);'
    )
    JobObserverAvoidsDisposeRace = (
        $jobTools -match 'LiveJobs\.TryRemove\(jobId, out _\)' -and
        $jobTools -notmatch 'LiveJobs\.TryRemove\(jobId, out _\);\s*runtime\.Dispose\(\)'
    )
    JobAndGitToolContract = (
        $jobTools -match 'talvora_job_start' -and
        $jobTools -match 'talvora_job_get' -and
        $jobTools -match 'talvora_job_list' -and
        $jobTools -match 'talvora_job_read_output' -and
        $jobTools -match 'talvora_job_write_stdin' -and
        $jobTools -match 'talvora_job_stop' -and
        $jobTools -match 'talvora_job_delete' -and
        $gitTools -match 'talvora_git_info' -and
        $gitTools -match 'talvora_git_status' -and
        $gitTools -match 'talvora_git_diff' -and
        $gitTools -match 'talvora_git_log' -and
        $gitTools -match 'talvora_git_branches' -and
        $gitTools -match 'talvora_git_run' -and
        $gitTools -match 'No Git subcommand, ref, remote, path, or option denylist/allowlist'
    )
    InstallerDeclares80Tools = (
        ([regex]::Matches($installerProgram, '"(?:talvora_[a-z0-9_]+|search|fetch)"')).Count -ge 80
    )
    DeveloperCoreToolContract = (
        $developerTools -match 'talvora_path_info' -and
        $developerTools -match 'talvora_file_hash' -and
        $developerTools -match 'talvora_find_files' -and
        $developerTools -match 'talvora_search_text' -and
        $developerTools -match 'talvora_read_bytes' -and
        $developerTools -match 'talvora_write_bytes' -and
        $developerTools -match 'talvora_replace_text' -and
        $developerTools -match 'talvora_http_request' -and
        $developerTools -match 'talvora_tcp_connections' -and
        $developerTools -match 'talvora_tcp_listeners' -and
        $developerTools -match 'talvora_wait_tcp' -and
        $developerTools -match 'talvora_project_discover' -and
        $developerTools -match 'talvora_resolve_command' -and
        $installerProgram -match 'talvora_project_discover'
    )
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
    SharedApplicationIcon = (
        $serviceProject -match '<ApplicationIcon>..\\..\\assets\\Talvora\.ico</ApplicationIcon>' -and
        $trayProject -match '<ApplicationIcon>..\\..\\assets\\Talvora\.ico</ApplicationIcon>' -and
        $installerProject -match '<ApplicationIcon>..\\..\\assets\\Talvora\.ico</ApplicationIcon>'
    )
    IconHasMultiResolutionFrames = (
        (Test-Path -LiteralPath $iconPath -PathType Leaf) -and
        $iconBytes.Length -gt 10000 -and
        $iconFrameCount -ge 9
    )
    TrayUsesEmbeddedBrandIcon = (
        $trayProgram -match 'Icon\.ExtractAssociatedIcon\(Application\.ExecutablePath\)' -and
        $trayProgram -match 'DrawStatusBadge' -and
        $trayProgram -match 'TalvoraConnectionState\.Ready'
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