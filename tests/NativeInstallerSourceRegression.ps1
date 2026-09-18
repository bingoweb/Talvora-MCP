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
$buildRunnerTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\BuildRunnerTools.cs'))
$sessionTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\SessionTools.cs'))
$runtimeTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\RuntimeTools.cs'))
$networkDiagnosticTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\NetworkDiagnosticTools.cs'))
$windowsToolchainTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\WindowsToolchainTools.cs'))
$configFormatTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\ConfigFormatTools.cs'))
$trayProgram = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\Program.cs'))
$trayProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\Talvora.Tray.csproj'))
$installerProgram = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\Program.cs'))
$installerProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\Talvora.Installer.csproj'))
$buildInstallerScript = [IO.File]::ReadAllText((Join-Path $RepoRoot 'scripts\Build-Windows-Installer.ps1'))
$manifest = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\app.manifest'))
$iconPath = Join-Path $RepoRoot 'assets\Talvora.ico'
$iconBytes = [IO.File]::ReadAllBytes($iconPath)
$iconFrameCount = if ($iconBytes.Length -ge 6) { [BitConverter]::ToUInt16($iconBytes, 4) } else { 0 }

$result = [pscustomobject]@{
    BuildScriptVerifiesPayloadArchive = (
        $buildInstallerScript -match 'function New-VerifiedPayloadArchive' -and
        $buildInstallerScript -match 'function Assert-PayloadReadable' -and
        $buildInstallerScript -match 'catch \[IO\.IOException\]' -and
        $buildInstallerScript -match 'Remove-Item -LiteralPath \$Destination' -and
        $buildInstallerScript -match '\$archive\.Entries\.Count -ne \$expectedFileCount' -and
        $buildInstallerScript -match 'New-VerifiedPayloadArchive -Source \$PayloadRoot -Destination \$PayloadZip -Attempts 5'
    )
    WindowsToolchainContract = (
        $windowsToolchainTools -match 'talvora_visual_studio_instances' -and
        $windowsToolchainTools -match 'talvora_vs_dev_environment' -and
        $windowsToolchainTools -match 'talvora_msbuild_info' -and
        $windowsToolchainTools -match 'talvora_msbuild_run' -and
        $windowsToolchainTools -match 'talvora_windows_sdk_info' -and
        $windowsToolchainTools -match 'talvora_cmake_info' -and
        $windowsToolchainTools -match 'talvora_cmake_run' -and
        $windowsToolchainTools -match 'talvora_ninja_info' -and
        $windowsToolchainTools -match 'talvora_ninja_run' -and
        $windowsToolchainTools -match 'talvora_pe_info' -and
        $windowsToolchainTools -match 'talvora_file_version_info'
    )
    WindowsToolchainToolContract = (
        $windowsToolchainTools -match 'talvora_windows_toolchain_info' -and
        $windowsToolchainTools -match 'talvora_vs_instances' -and
        $windowsToolchainTools -match 'talvora_windows_sdk_list' -and
        $windowsToolchainTools -match 'talvora_vsdev_environment' -and
        $windowsToolchainTools -match 'talvora_visual_studio_instances' -and
        $windowsToolchainTools -match 'talvora_vs_dev_environment' -and
        $windowsToolchainTools -match 'talvora_msbuild_info' -and
        $windowsToolchainTools -match 'talvora_msbuild_run' -and
        $windowsToolchainTools -match 'talvora_windows_sdk_info' -and
        $windowsToolchainTools -match 'talvora_cmake_info' -and
        $windowsToolchainTools -match 'talvora_cmake_run' -and
        $windowsToolchainTools -match 'talvora_ninja_info' -and
        $windowsToolchainTools -match 'talvora_ninja_run' -and
        $windowsToolchainTools -match 'talvora_pe_info' -and
        $windowsToolchainTools -match 'talvora_file_version_info' -and
        $windowsToolchainTools -match 'VsDevCmd\.bat' -and
        $windowsToolchainTools -match 'dotnet msbuild' -and
        $windowsToolchainTools -match 'No target/property/project/option allowlist or denylist' -and
        $windowsToolchainTools -match 'No generator/preset/source/build-directory/option allowlist or denylist'
    )
    ConfigFormatToolContract = (
        $configFormatTools -match 'talvora_dotenv_list' -and
        $configFormatTools -match 'talvora_dotenv_get' -and
        $configFormatTools -match 'talvora_dotenv_set' -and
        $configFormatTools -match 'talvora_dotenv_delete' -and
        $configFormatTools -match 'talvora_ini_list' -and
        $configFormatTools -match 'talvora_ini_get' -and
        $configFormatTools -match 'talvora_ini_set' -and
        $configFormatTools -match 'talvora_ini_delete' -and
        $configFormatTools -match 'talvora_xml_query' -and
        $configFormatTools -match 'talvora_xml_set' -and
        $configFormatTools -match 'talvora_xml_delete' -and
        $configFormatTools -match 'talvora_test_report_summary' -and
        $configFormatTools -match 'TRX, JUnit/xUnit-style XML, or NUnit3'
    )
    NetworkDiagnosticToolContract = (
        $networkDiagnosticTools -match 'talvora_network_interfaces' -and
        $networkDiagnosticTools -match 'talvora_dns_lookup' -and
        $networkDiagnosticTools -match 'talvora_ping' -and
        $networkDiagnosticTools -match 'talvora_tcp_exchange' -and
        $networkDiagnosticTools -match 'talvora_tls_inspect' -and
        $networkDiagnosticTools -match 'talvora_websocket_exchange' -and
        $networkDiagnosticTools -match 'ClientWebSocket' -and
        $networkDiagnosticTools -match 'SslStream'
    )
    PythonDockerToolContract = (
        $runtimeTools -match 'talvora_python_info' -and
        $runtimeTools -match 'talvora_python_run' -and
        $runtimeTools -match 'talvora_python_venv_create' -and
        $runtimeTools -match 'talvora_pip_install' -and
        $runtimeTools -match 'talvora_pip_run' -and
        $runtimeTools -match 'talvora_docker_info' -and
        $runtimeTools -match 'talvora_docker_ps' -and
        $runtimeTools -match 'talvora_docker_images' -and
        $runtimeTools -match 'talvora_docker_logs' -and
        $runtimeTools -match 'talvora_docker_exec' -and
        $runtimeTools -match 'talvora_docker_run' -and
        $runtimeTools -match 'talvora_docker_compose_run' -and
        $runtimeTools -match 'complete Docker CLI surface' -and
        $runtimeTools -match 'No pip subcommand, package, index, target, or option allowlist/denylist'
    )
    SessionToolContract = (
        $sessionTools -match 'talvora_session_list' -and
        $sessionTools -match 'talvora_session_get' -and
        $sessionTools -match 'talvora_user_process_start' -and
        $sessionTools -match 'WTSQueryUserToken' -and
        $sessionTools -match 'CreateEnvironmentBlock' -and
        $sessionTools -match 'CreateProcessAsUserW' -and
        $sessionTools -match 'winsta0\\default'
    )
    BuildRunnerToolContract = (
        $buildRunnerTools -match 'talvora_dotnet_info' -and
        $buildRunnerTools -match 'talvora_dotnet_restore' -and
        $buildRunnerTools -match 'talvora_dotnet_build' -and
        $buildRunnerTools -match 'talvora_dotnet_test' -and
        $buildRunnerTools -match 'talvora_dotnet_publish' -and
        $buildRunnerTools -match 'talvora_dotnet_run' -and
        $buildRunnerTools -match 'talvora_node_info' -and
        $buildRunnerTools -match 'talvora_npm_install' -and
        $buildRunnerTools -match 'talvora_npm_ci' -and
        $buildRunnerTools -match 'talvora_npm_run_script' -and
        $buildRunnerTools -match 'talvora_npm_run' -and
        $buildRunnerTools -match 'complete dotnet CLI surface' -and
        $buildRunnerTools -match 'complete npm CLI surface'
    )
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
    InstallerDeclares140Tools = (
        ([regex]::Matches($installerProgram, '"(?:talvora_[a-z0-9_]+|search|fetch)"')).Count -ge 140
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