param(
    [string] $RepoRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'

function Read-ProjectSources {
    param(
        [Parameter(Mandatory)][string] $Path,
        [string] $Filter = '*.cs'
    )

    return (Get-ChildItem -LiteralPath $Path -Filter $Filter -File |
        Sort-Object Name |
        ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join [Environment]::NewLine
}

$serviceProgram = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Program.cs'))
$serviceProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Talvora.csproj'))
$developerTools = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora\Tools') 'DeveloperTools*.cs'
$jobTools = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora\Tools') 'JobTools*.cs'
$gitTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\GitTools.cs'))
$configAssetTools = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora\Tools') 'ConfigAssetTools*.cs'
$watchTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\WatchTools.cs'))
$chocoTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\ChocolateyTools.cs'))
$buildRunnerTools = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora\Tools') 'BuildRunnerTools*.cs'
$sessionTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\SessionTools.cs'))
$sharedSession = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora.Shared') 'WindowsSessionLauncher*.cs'
$sharedConstants = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Shared\TalvoraConstants.cs'))
$runtimeTools = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora\Tools') 'RuntimeTools*.cs'
$networkDiagnosticTools = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora\Tools') 'NetworkDiagnosticTools*.cs'
$windowsToolchainTools = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora\Tools') 'WindowsToolchainTools*.cs'
$toolchainTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\ToolchainTools.cs'))
$httpMockTools = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora\Tools') 'HttpMockTools*.cs'
$configFormatTools = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora\Tools') 'ConfigFormatTools*.cs'
$sqliteTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\SqliteTools.cs'))
$devServerTools = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora\Tools') 'DevServerTools*.cs'
$structuredConfigTools = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora\Tools') 'StructuredConfigTools*.cs'
$structuralEditTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\StructuralEditTools.cs'))
$astGrepToolchainManager = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\SourceEditing\AstGrepToolchainManager.cs'))
$astGrepStructuralEngine = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\SourceEditing\AstGrepStructuralEditEngine.cs'))
$semanticEditTools = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\Tools\SemanticEditTools.cs'))
$roslynSemanticEngine = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\SourceEditing\RoslynSemanticEditEngine.cs'))
$roslynMsBuildBootstrap = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\SourceEditing\RoslynMsBuildBootstrap.cs'))
$semanticWorkerProtocol = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora\SourceEditing\SemanticWorkerProtocol.cs'))
$trayProgram = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora.Tray')
$trayProgramFile = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\Program.cs'))
$trayApplicationContext = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\TrayApplicationContext.cs'))
$businessTunnelClient = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\BusinessTunnelClient.cs'))
$componentHealth = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ControlCenterComponentHealthService.cs'))
$controlCenterLifecycleService = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ControlCenterLifecycleService.cs'))
$managedMcpSessionState = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ManagedMcpSessionState.cs'))
$controlCenterWindow = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ControlCenterWindow.cs'))
$controlCenterWindowChrome = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ControlCenterWindow.Chrome.cs'))
$controlCenterActions = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ControlCenterWindow.Actions.cs'))
$controlCenterUi4 = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\Views\ShellWindow.UI4.cs'))

$registryCoordinator = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ManagedMcpRegistryCoordinator.cs'))
$ownershipManifestStore = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ManagedMcpOwnershipManifestStore.cs'))
$managedMcpRegistryStore = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Shared\ManagedMcpRegistryStore.cs'))
$installerFlow = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\InstallerEngine.Flow.cs'))
$installerService = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\InstallerEngine.Service.cs'))
$installerCleanupState = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\InstallerEngine.CleanupState.cs'))
$installerRollbackState = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\InstallerEngine.RollbackState.cs'))
$rebootCleanupStart = $installerCleanupState.IndexOf('private static void ScheduleDirectoryDeletionOnReboot', [StringComparison]::Ordinal)
$copyTreeStart = $installerCleanupState.IndexOf('private static void CopyTree', $rebootCleanupStart, [StringComparison]::Ordinal)
if ($rebootCleanupStart -lt 0 -or $copyTreeStart -le $rebootCleanupStart) { throw 'Native installer regression could not isolate reboot cleanup.' }
$rebootCleanupSection = $installerCleanupState.Substring($rebootCleanupStart, $copyTreeStart - $rebootCleanupStart)
$immediateCleanupStart = $installerCleanupState.IndexOf('private static async Task TryDeleteDirectoryAsync', [StringComparison]::Ordinal)
$writeStateStart = $installerCleanupState.IndexOf('private static async Task WriteCurrentStateAsync', $immediateCleanupStart, [StringComparison]::Ordinal)
if ($immediateCleanupStart -lt 0 -or $writeStateStart -le $immediateCleanupStart) { throw 'Native installer regression could not isolate immediate cleanup.' }
$immediateCleanupSection = $installerCleanupState.Substring($immediateCleanupStart, $writeStateStart - $immediateCleanupStart)
$trayProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\Talvora.Tray.csproj'))
$installerProgram = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora.Installer')
$installerForm = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\InstallerForm.cs'))
$uninstallPath = Join-Path $RepoRoot 'src\Talvora.Installer\InstallerEngine.Uninstall.cs'
if (-not (Test-Path -LiteralPath $uninstallPath -PathType Leaf)) {
    throw 'RL3 uninstall implementation must exist before Talvora is registered with Windows Apps.'
}
$uninstallSource = [IO.File]::ReadAllText($uninstallPath)
$installerProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\Talvora.Installer.csproj'))
$powerShellPinPath = Join-Path $RepoRoot 'src\Talvora.Installer\powershell-pin.json'
$powerShellSourcePath = Join-Path $RepoRoot 'src\Talvora.Installer\InstallerEngine.PowerShell.cs'
if (!(Test-Path -LiteralPath $powerShellPinPath -PathType Leaf) -or
    !(Test-Path -LiteralPath $powerShellSourcePath -PathType Leaf)) {
    throw 'RL2 requires a pinned PowerShell MSI and a preflight implementation.'
}
$powerShellPin = Get-Content -LiteralPath $powerShellPinPath -Raw | ConvertFrom-Json
$powerShellProvisioner = [IO.File]::ReadAllText($powerShellSourcePath)
$buildInstallerScript = [IO.File]::ReadAllText((Join-Path $RepoRoot 'scripts\Build-Windows-Installer.ps1'))
$installScript = [IO.File]::ReadAllText((Join-Path $RepoRoot 'scripts\Install.ps1'))
$windowsCiWorkflow = [IO.File]::ReadAllText((Join-Path $RepoRoot '.github\workflows\windows-ci.yml'))
$smokeProgram = [IO.File]::ReadAllText((Join-Path $RepoRoot 'tests\Talvora.Smoke\Program.cs'))
$deployInstallerScript = [IO.File]::ReadAllText((Join-Path $RepoRoot 'scripts\Deploy-Windows-Installer.ps1'))
$manifest = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\app.manifest'))
$toolManifest = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Shared\TalvoraToolManifest.cs'))
$canonicalToolNames = @([regex]::Matches($toolManifest, '"(?:talvora_[a-z0-9_]+|search|fetch)"') | ForEach-Object { $_.Value.Trim('"') })
$canonicalToolCount = $canonicalToolNames.Count
$canonicalUniqueToolCount = @($canonicalToolNames | Sort-Object -Unique).Count
$iconPath = Join-Path $RepoRoot 'assets\Talvora.ico'
$iconBytes = [IO.File]::ReadAllBytes($iconPath)
$iconFrameCount = if ($iconBytes.Length -ge 6) { [BitConverter]::ToUInt16($iconBytes, 4) } else { 0 }
$upgradeStopStart = $installerService.IndexOf(
    'private static async Task StopServiceForUpgradeAsync',
    [StringComparison]::Ordinal)
$restoreRecoveryStart = $installerService.IndexOf(
    'private static async Task RestoreCanonicalServiceRecoveryPolicyAsync',
    $upgradeStopStart,
    [StringComparison]::Ordinal)
if ($upgradeStopStart -lt 0 -or $restoreRecoveryStart -le $upgradeStopStart) {
    throw 'Native installer regression could not isolate canonical service upgrade stop path.'
}
$upgradeStopSection = $installerService.Substring(
    $upgradeStopStart,
    $restoreRecoveryStart - $upgradeStopStart)

$result = [pscustomobject]@{
    UninstallRegistrationAndWindowWired = (
        $installerFlow -match 'RegisterTalvoraUninstall' -and
        $uninstallSource -match 'WriteUninstallRegistration' -and
        $installerProgram -match '--uninstall' -and
        $installerProgram -match '--what-if' -and
        $installerProgram -match '--remove-data' -and
        $installerProgram -match 'SetupMode.Uninstall' -and
        $uninstallSource -match 'DisplayName' -and
        $uninstallSource -match 'DisplayVersion' -and
        $uninstallSource -match 'QuietUninstallString' -and
        $uninstallSource -match 'RegistryValueKind.DWord'
    )
    UninstallPlanAndProtectedData = (
        $uninstallSource -match 'BuildUninstallPlan' -and
        $uninstallSource -match 'Talvora Kontrol Merkezi.lnk' -and
        $uninstallSource -match 'mcp_servers.talvora_local' -and
        $uninstallSource -match 'Secrets' -and
        $uninstallSource -match 'StopTrayProcessesAsync' -and
        $uninstallSource -match 'StopAndDeleteServiceAsync' -and
        $uninstallSource -match 'MoveFileEx' -and
        $uninstallSource -match 'RunValueName'
    )
    UninstallDataRemovalDoesNotRecreateLog = (
        $uninstallSource.IndexOf('InstallerLog.Write("Uninstall succeeded.")', [StringComparison]::Ordinal) -gt 0 -and
        $uninstallSource.IndexOf('InstallerLog.Write("Uninstall succeeded.")', [StringComparison]::Ordinal) -lt
            $uninstallSource.IndexOf('if (removeData && Directory.Exists(dataRoot))', [StringComparison]::Ordinal) -and
        $installerProgram -match 'isUninstall\s*&&\s*removeData'
    )
    PowerShell7PinIsVersionedAndHasSha256 = (
        [string]$powerShellPin.version -match '^7\.\d+\.\d+$' -and
        [string]$powerShellPin.url -ceq ('https://github.com/PowerShell/PowerShell/releases/download/v' +
            $powerShellPin.version + '/PowerShell-' + $powerShellPin.version + '-win-x64.msi') -and
        [string]$powerShellPin.sha256 -cmatch '^[0-9a-f]{64}$' -and
        $installerProject -match 'EmbeddedResource Include="powershell-pin\.json"'
    )
    PowerShell7PreflightPrecedesTalvoraChange = (
        $installerFlow -match 'await EnsurePowerShell7Async\(progress, cancellationToken\)' -and
        $installerFlow.IndexOf('await EnsurePowerShell7Async', [StringComparison]::Ordinal) -lt
            $installerFlow.IndexOf('Yeni Talvora sürümü hazırlanıyor...', [StringComparison]::Ordinal) -and
        $installerFlow.IndexOf('await EnsurePowerShell7Async', [StringComparison]::Ordinal) -lt
            $installerFlow.IndexOf('Directory.CreateDirectory(versionsRoot)', [StringComparison]::Ordinal)
    )
    PowerShell7InstallerHasVerificationAndQuietMsi = (
        $installerProgram -match '--verify-powershell-download' -and
        $powerShellProvisioner -match 'Func<bool>' -and
        $powerShellProvisioner -match 'TimeSpan\.FromMinutes\(10\)' -and
        $powerShellProvisioner -match 'SHA256\.HashData' -and
        $powerShellProvisioner -match 'msiexec\.exe' -and
        $powerShellProvisioner -match 'ADD_PATH=1' -and
        $powerShellProvisioner -match 'REGISTER_MANIFEST=1' -and
        $powerShellProvisioner -match 'USE_MU=1' -and
        $powerShellProvisioner -match 'ENABLE_MU=1'
    )
    PowerShell7MsiUsesExclusiveNewFile = (
        $powerShellProvisioner -match 'Guid\.NewGuid\(\):N' -and
        $powerShellProvisioner -match '(?s)FileMode\.CreateNew,\s*FileAccess\.Write,\s*FileShare\.None'
    )
    PowerShell7HashAndInstallKeepReadLock = (
        $powerShellProvisioner -match '(?s)FileMode\.Open,\s*FileAccess\.Read,\s*FileShare\.Read' -and
        $powerShellProvisioner -match 'ValidatePowerShell7MsiHash\(verifiedStream, pin\.Sha256\)' -and
        $powerShellProvisioner -match 'ValidatePowerShell7MsiHash\(FileStream stream,' -and
        $installerProgram -match 'Console\.OutputEncoding = new UTF8Encoding\(false\);'
    )
    PowerShell7DownloadReportsWholePercent = (
        $powerShellProvisioner -match 'ContentLength' -and
        $powerShellProvisioner -match 'new byte\[81920\]' -and
        $powerShellProvisioner -match 'CultureInfo\.InvariantCulture' -and
        $powerShellProvisioner -match 'PowerShell 7 indiriliyor… %\{percent\.ToString\(CultureInfo\.InvariantCulture\)\}'
    )
    CanonicalDeployUsesIndependentSystemTask = (
        $deployInstallerScript -match 'Schedule\.Service' -and
        $deployInstallerScript -match 'RegisterTaskDefinition' -and
        $deployInstallerScript -match 'taskLogonServiceAccount\s*=\s*5' -and
        $deployInstallerScript -match "'SYSTEM'" -and
        $deployInstallerScript -match '\$action\.Arguments\s*=\s*''--silent''' -and
        $deployInstallerScript -match '\.Run\(\$null\)' -and
        $deployInstallerScript -notmatch 'Start-Process\s+-FilePath\s+\$installerFullPath'
    )
    CanonicalDeployObservesFastTaskCompletion = (
        $deployInstallerScript -match '\$previousLastRunTime\s*=\s*\[DateTime\]\$registered\.LastRunTime' -and
        $deployInstallerScript -match '\$taskInstanceGuid\s*=\s*\[string\]\$running\.InstanceGuid' -and
        $deployInstallerScript -match '\[string\]\$_\.InstanceGuid\s+-eq\s+\$taskInstanceGuid' -and
        $deployInstallerScript -match '\$lastRunTransitioned\s*=\s*\$currentLastRunTime\s+-ne\s+\$previousLastRunTime' -and
        $deployInstallerScript -match '\$completionObserved\s*=\s*\$true' -and
        $deployInstallerScript -notmatch 'no running installer instance was observed'
    )
    CanonicalDeployDetachesServiceAncestorWait = (
        $deployInstallerScript -match 'function Test-TalvoraServiceAncestor' -and
        $deployInstallerScript -match '\$WaitForCompletion\s+-and\s+\(Test-TalvoraServiceAncestor\)' -and
        $deployInstallerScript -match '\$WaitForCompletion\s*=\s*\$false'
    )
    CanonicalDeployPinsBuiltArtifactIdentity = (
        $buildInstallerScript.Contains('Talvora-Setup.manifest.json') -and
        $buildInstallerScript.Contains('sourceHeadCommit') -and
        $buildInstallerScript.Contains('sourceIndexTree') -and
        $buildInstallerScript.Contains('runtimeInputsSha256') -and
        $deployInstallerScript.Contains('ManifestPath') -and
        $deployInstallerScript.Contains('Assert-InstallerArtifactIdentity') -and
        ([regex]::Matches($deployInstallerScript, 'Assert-InstallerArtifactIdentity').Count -ge 3) -and
        $deployInstallerScript.Contains('Global\Talvora.BuildWindowsInstaller.v2') -and
        $deployInstallerScript.IndexOf('Assert-InstallerArtifactIdentity', [StringComparison]::Ordinal) -lt
            $deployInstallerScript.IndexOf('$registered.Run($null)', [StringComparison]::Ordinal)
    )
    LegacyCloudflaredRetirementIsOwnershipAwareAndPostCommit = (
        $installerService -match 'cloudflaredServiceOwned' -and
        $installerService -match 'cloudflaredServiceExecutable' -and
        $installerProgram -match 'GetServiceExecutablePath\("Cloudflared"\)' -and
        $installerProgram -match 'Path\.GetFullPath\(executable\)' -and
        $installerService -match 'legacyCloudflaredExecutable' -and
        $installerService -match 'Generic ProgramData cloudflared data preserved' -and
        $installerFlow.IndexOf('WriteCurrentStateAsync') -ge 0 -and
        $installerFlow.IndexOf('RemoveLegacyInstallationAsync') -gt
            $installerFlow.IndexOf('WriteCurrentStateAsync')
    )
    RetiredPlaywrightMcpHasOwnershipAwareUpgradeCleanup = (
        $installerService -match 'RetireLegacyPlaywrightMcpAsync' -and
        $installerProgram -match 'LegacyPlaywrightTaskName' -and
        $installerProgram -match 'Talvora Playwright MCP' -and
        $installerProgram -match 'Start-PlaywrightMcp\.ps1' -and
        $installerProgram -match 'IsOwnedLegacyPlaywrightTaskXml' -and
        $installerProgram -match 'WorkingDirectory' -and
        $installerProgram -match 'Win32_Process' -and
        $installerProgram -match 'playwright-mcp-retired-v1\.json' -and
        $installerProgram -match 'LocalTunnelStateRemoved' -and
        $installerProgram -notmatch 'LocalPort -in @\(8931, 8932\)'
    )
    BuildScriptPreventsConcurrentBuilds = (
        $buildInstallerScript -match "Global\\Talvora\.BuildWindowsInstaller" -and
        $buildInstallerScript -match '\$BuildMutex\.WaitOne\(0\)' -and
        $buildInstallerScript -match 'Another Talvora Windows installer build is already running' -and
        $buildInstallerScript -match '\$BuildMutex\.ReleaseMutex\(\)' -and
        $buildInstallerScript -match 'finally\s*\{'
    )
    BuildScriptVerifiesPayloadArchive = (
        $buildInstallerScript -match 'Add-Type -AssemblyName System\.IO\.Compression' -and
        $buildInstallerScript -match 'function Get-PayloadFileManifest' -and
        $buildInstallerScript -match 'function Assert-RequiredPayloadFiles' -and
        $buildInstallerScript -match 'function Wait-PayloadFilesReady' -and
        $buildInstallerScript -match 'function New-VerifiedPayloadArchive' -and
        $buildInstallerScript -match '\$ServicePayloadManifest' -and
        $buildInstallerScript -match '\$TrayPayloadManifest' -and
        $buildInstallerScript -match '\$PayloadManifest' -and
        $buildInstallerScript -match '\$archive\.CreateEntry' -and
        $buildInstallerScript -match '\$verified\.Entries\.Count -ne \$Files\.Count' -and
        $buildInstallerScript -match 'New-VerifiedPayloadArchive -Files \$PayloadManifest -Destination \$PayloadZip -Attempts 5'
    )
    BuildScriptIncludesDependencyProvenance = (
        $buildInstallerScript -match 'resolved-dependencies\.json' -and
        $buildInstallerScript -match 'Get-ResolvedPackageManifest' -and
        $buildInstallerScript -match 'Invoke-CanonicalDependencyRestore' -and
        $buildInstallerScript -match 'New-DependencyAssetsSnapshot' -and
        $buildInstallerScript -match 'Assert-DependencyAssetsUnchanged' -and
        $buildInstallerScript -match '\$DependencyArtifactsRoot' -and
        $buildInstallerScript -match '--artifacts-path' -and
        $buildInstallerScript -match '--no-restore' -and
        $buildInstallerScript -match 'Get-PublishedPackageManifest' -and
        $buildInstallerScript -match 'Get-SingleFilePublishDepsPath -ArtifactsPath \$DependencyArtifactsRoot -ProjectName ''Talvora\.Tray''' -and
        $buildInstallerScript -match 'Assert-PublishedPackagesMatchResolvedAssets' -and
        $buildInstallerScript -match 'assetsSha256' -and
        $buildInstallerScript -match 'publishedDepsSha256' -and
        $installerFlow -match 'DependencyProvenance'
    )
    BuildScriptVendorsAstGrepStructuralToolchain = (
        $buildInstallerScript -match 'function Install-AstGrepPayload' -and
        $buildInstallerScript -match '@ast-grep/cli-win32-x64-msvc' -and
        $buildInstallerScript -match '@ast-grep/cli-win32-arm64-msvc' -and
        $buildInstallerScript -match '@ast-grep/cli-win32-ia32-msvc' -and
        $buildInstallerScript -notmatch '@latest' -and
        $buildInstallerScript -match 'Get-PinnedAstGrepMetadata' -and
        $buildInstallerScript -match 'Assert-PinnedAstGrepTarball' -and
        $buildInstallerScript -match 'Assert-PinnedAstGrepExecutable' -and
        $buildInstallerScript -match 'ast-grep-pin\.json' -and
        $buildInstallerScript -match 'sha512-' -and
        $buildInstallerScript -match 'Get-FileHash.*SHA256' -and
        $buildInstallerScript -match 'Service/tools/ast-grep/ast-grep\.exe' -and
        $buildInstallerScript -match 'Service/tools/ast-grep/provenance\.json' -and
        $buildInstallerScript -match 'externalToolchains'
    )
    StructuralEditToolContract = (
        $structuralEditTools -match 'Name = "talvora_structural_edit"' -and
        $structuralEditTools -match 'Structural Source Editor' -and
        $structuralEditTools -match 'PRIMARY/default' -and
        $structuralEditTools -match 'ruleFile' -and
        $astGrepStructuralEngine -match '--json=stream' -and
        $astGrepStructuralEngine -notmatch '--update-all' -and
        $astGrepStructuralEngine -notmatch '--interactive' -and
        $astGrepStructuralEngine -match 'replacementOffsets' -and
        $astGrepStructuralEngine -match 'GetUtf8ByteOffset' -and
        $astGrepStructuralEngine -match 'ScalarColumnToUtf16Index' -and
        $astGrepStructuralEngine -match 'ApplyGeneratedEditsAsync' -and
        $astGrepStructuralEngine -match 'ExpectedRevision' -and
        $astGrepStructuralEngine -match 'ExpectedText' -and
        $astGrepToolchainManager -match 'TryResolveVendoredAsync' -and
        $astGrepToolchainManager -match 'provenance\.json' -and
        $astGrepToolchainManager -match 'executableSha256' -and
        $buildInstallerScript -match 'https://registry\.npmjs\.org/' -and
        $buildInstallerScript -notmatch '@latest' -and
        $buildInstallerScript -match 'Assert-PinnedAstGrepTarball' -and
        $buildInstallerScript -match 'Assert-PinnedAstGrepExecutable' -and
        $astGrepToolchainManager -match 'LoadPinnedMetadata' -and
        $buildInstallerScript -match 'Service/tools/ast-grep/ast-grep\.exe' -and
        $buildInstallerScript -match 'Service/tools/ast-grep/provenance\.json' -and
        $toolManifest -match 'talvora_structural_edit'
    )
    SemanticEditToolContract = (
        $semanticEditTools -match 'Name = "talvora_semantic_edit"' -and
        $semanticEditTools -match 'C# Semantic Rename' -and
        $semanticEditTools -match 'SourceEditRoutingContract.SemanticEditDescription' -and
        $serviceProject -match 'Microsoft.Build.Locator' -and
        $serviceProject -match 'Microsoft.CodeAnalysis.CSharp.Workspaces' -and
        $serviceProject -match 'Microsoft.CodeAnalysis.Workspaces.MSBuild' -and
        $serviceProject -match 'Microsoft.Build.Framework.*ExcludeAssets="runtime".*PrivateAssets="all"' -and
        $roslynMsBuildBootstrap -match 'QueryVisualStudioInstances' -and
        $roslynMsBuildBootstrap -match 'RegisterInstance' -and
        $roslynSemanticEngine -match 'RegisterWorkspaceFailedHandler' -and
        $roslynSemanticEngine -match 'FindSymbolAtPositionAsync' -and
        $roslynSemanticEngine -match 'Renamer.RenameSymbolAsync' -and
        $roslynSemanticEngine -match 'RenameFile: false' -and
        $roslynSemanticEngine -match 'GetTextChanges' -and
        $roslynSemanticEngine -match 'ApplyGeneratedEditsAsync' -and
        $roslynSemanticEngine -match 'ExpectedRevision' -and
        $roslynSemanticEngine -notmatch 'TryApplyChanges' -and
        $toolManifest -match 'talvora_semantic_edit'
    )
    SourceEditWorkerCleanupWaitsForExit = (
        $astGrepStructuralEngine -match 'private static async Task TerminateProcessAsync' -and
        $astGrepStructuralEngine -match 'ProcessExitGraceSeconds' -and
        $astGrepStructuralEngine -match 'await process\.WaitForExitAsync' -and
        ([regex]::Matches($astGrepStructuralEngine, 'await TerminateProcessAsync').Count -ge 2) -and
        $semanticWorkerProtocol -match 'private static async Task TerminateProcessAsync' -and
        $semanticWorkerProtocol -match 'WorkerTerminationGraceSeconds' -and
        $semanticWorkerProtocol -match 'await process\.WaitForExitAsync' -and
        $semanticWorkerProtocol -match '(?s)finally\s*\{.*await TerminateProcessAsync.*TryDeleteResponse'
    )
    WindowsPowerShellFallbackContract = (
        $componentHealth -match 'CommandLine\.IndexOf\(\$marker,\[StringComparison\]::OrdinalIgnoreCase\) -ge 0' -and
        $componentHealth -notmatch 'CommandLine\.Contains\(\$marker,\[StringComparison\]::OrdinalIgnoreCase\)'


    )
    RegistryOwnershipManifestContract = (
        $registryCoordinator -match 'ManagedMcpOwnershipManifestStore\.ReadAllAsync' -and
        $registryCoordinator -match 'ManagedMcpOwnershipManifestStore\.PersistAsync' -and
        $registryCoordinator -match 'ManagedMcpOwnershipManifestStore\.NeedsSeed' -and
        $registryCoordinator -match 'per-entry recovery manifests could not be read' -and
        $registryCoordinator -match 'ownership manifests could not be read' -and
        $managedMcpRegistryStore -match 'MaximumRecoveryManifestEntries\s*=\s*4_096' -and
        $managedMcpRegistryStore -match 'RecoveryManifestPattern\s*=\s*"v2-\*\.json"' -and
        $managedMcpRegistryStore -match 'Take\(MaximumRecoveryManifestEntries \+ 1\)' -and
        $ownershipManifestStore -match 'managed-mcps\.d' -and
        $ownershipManifestStore -match 'AtomicFile\.WriteAllTextAsync' -and
        $ownershipManifestStore -match 'ManagedMcpRegistryStore\.ValidateRegistration' -and
        $ownershipManifestStore -match 'ReadBounded<ManagedMcpRegistration>' -and
        $ownershipManifestStore -match 'ReadBoundedAsync<ManagedMcpRegistration>' -and
        $ownershipManifestStore -match 'MaximumRecoveryManifestEntries' -and
        $ownershipManifestStore -match '"v2-\*\.json"' -and
        $ownershipManifestStore -match 'File\.Delete\(stale\)'
    )
    BuildScriptFingerprintsDirtyProvenance = (
        $buildInstallerScript -match '\$SourceStatus\s*=\s*@\(\s*Invoke-RepositoryGit -Root \$RepoRoot -Arguments @\(\s*''status''' -and
        $buildInstallerScript -match 'function Invoke-RepositoryGit' -and
        $buildInstallerScript -match 'safe\.directory=\{0\}' -and
        $buildInstallerScript -notmatch 'safe\.directory=\*' -and
        $buildInstallerScript -match 'Test-RuntimeBuildInput' -and
        $buildInstallerScript -match 'Get-WorkingTreeFingerprint' -and
        $buildInstallerScript -match 'Assert-RuntimeBuildInputsUnchanged' -and
        ([regex]::Matches($buildInstallerScript, 'Assert-RuntimeBuildInputsUnchanged').Count -ge 5) -and
        $buildInstallerScript -match 'if \(-not \[string\]::IsNullOrWhiteSpace\(\$WorkingTreeFingerprint\)\)' -and
        $buildInstallerScript -match '\$SourceCommit \+= ''-dirty-'' \+ \$WorkingTreeFingerprint\.Substring\(0, 12\)'
    )
    BuildScriptUsesImmutableSourceSnapshot = (
        $buildInstallerScript.Contains('New-RuntimeBuildSourceSnapshot') -and
        $buildInstallerScript.Contains('$SourceSnapshotRoot') -and
        $buildInstallerScript.Contains('Get-GitHeadObjectId') -and
        $buildInstallerScript.Contains('Get-GitIndexTreeId') -and
        $buildInstallerScript.Contains('Get-RuntimeBuildInputFingerprint') -and
        $buildInstallerScript.Contains('runtimeInputsSha256') -and
        $buildInstallerScript.Contains('source-snapshot.json')
    )
    HttpMockToolContract = (
        $httpMockTools -match 'talvora_http_mock_start' -and
        $httpMockTools -match 'talvora_http_mock_get' -and
        $httpMockTools -match 'talvora_http_mock_list' -and
        $httpMockTools -match 'talvora_http_mock_read' -and
        $httpMockTools -match 'talvora_http_mock_reply' -and
        $httpMockTools -match 'talvora_http_mock_stop' -and
        $httpMockTools -match 'HttpListener' -and
        $httpMockTools -match 'autoReply=false' -and
        $httpMockTools -match 'AbsoluteMaxQueuedRequests' -and
        $httpMockTools -match 'AbsoluteGlobalQueuedRetainedBytes' -and
        $httpMockTools -match 'GlobalHandlerSlots' -and
        $httpMockTools -match 'maxQueuedRequests == 0' -and
        $httpMockTools -match 'maxConcurrentRequests == 0'
    )
    WindowsToolchainContract = (
        $windowsToolchainTools -match 'talvora_visual_studio_instances' -and
        $windowsToolchainTools -match 'talvora_vs_dev_environment' -and
        $windowsToolchainTools -match 'talvora_msbuild_info' -and
        $windowsToolchainTools -match 'talvora_msbuild_run' -and
        $windowsToolchainTools -match 'talvora_windows_sdk_info' -and
        $toolchainTools -match 'talvora_toolchain_info' -and
        $toolchainTools -match 'talvora_toolchain_run' -and
        $windowsToolchainTools -match 'CMakeInfo' -and
        $windowsToolchainTools -match 'CMakeRun' -and
        $windowsToolchainTools -match 'NinjaInfo' -and
        $windowsToolchainTools -match 'NinjaRun' -and
        $windowsToolchainTools -match 'talvora_pe_info' -and
        $windowsToolchainTools -match 'talvora_file_version_info'
    )
    WindowsToolchainToolContract = (
        $windowsToolchainTools -match 'talvora_windows_toolchain_info' -and
        $windowsToolchainTools -match 'talvora_visual_studio_instances' -and
        $windowsToolchainTools -match 'talvora_vs_dev_environment' -and
        $windowsToolchainTools -match 'talvora_msbuild_info' -and
        $windowsToolchainTools -match 'talvora_msbuild_run' -and
        $windowsToolchainTools -match 'talvora_windows_sdk_info' -and
        $toolchainTools -match 'talvora_toolchain_info' -and
        $toolchainTools -match 'talvora_toolchain_run' -and
        $windowsToolchainTools -match 'CMakeInfo' -and
        $windowsToolchainTools -match 'CMakeRun' -and
        $windowsToolchainTools -match 'NinjaInfo' -and
        $windowsToolchainTools -match 'NinjaRun' -and
        $windowsToolchainTools -match 'talvora_pe_info' -and
        $windowsToolchainTools -match 'talvora_file_version_info' -and
        $windowsToolchainTools -match 'VsDevCmd\.bat' -and
        $windowsToolchainTools -match 'dotnet msbuild' -and
        $windowsToolchainTools -match 'No target/property/project/option allowlist or denylist' -and
        $toolchainTools -match 'arbitrary argv'
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
    ResponseBudgetContract = (
        $developerTools -match 'AbsoluteReadBytesResponseBytes' -and
        $developerTools -match 'AbsoluteHttpResponseBytes' -and
        $developerTools -match 'AbsoluteFileSearchResults' -and
        $developerTools -match 'AbsoluteTextSearchMatches' -and
        $developerTools -match 'AbsoluteSearchResponseCharacters' -and
        $developerTools -match 'ResponseLimited' -and
        $developerTools -match 'NextOffset' -and
        $networkDiagnosticTools -match 'AbsoluteTcpResponseBytes' -and
        $networkDiagnosticTools -match 'AbsoluteWebSocketMessageBytes' -and
        $networkDiagnosticTools -match 'AbsoluteWebSocketResponseBytes' -and
        $networkDiagnosticTools -match 'ResponseTruncated' -and
        $configAssetTools -match 'AbsoluteTextResponseCharacters' -and
        $configAssetTools -match 'AbsoluteTextResponseLines' -and
        $configAssetTools -match 'NextStartCharacter' -and
        $sqliteTools -match 'AbsoluteQueryRows' -and
        $sqliteTools -match 'AbsoluteQueryResponseCharacters'
    )
    PythonDockerToolContract = (
        $runtimeTools -match 'talvora_python_info' -and
        $runtimeTools -match 'talvora_python_run' -and
        $runtimeTools -match 'talvora_uv_run' -and
        $runtimeTools -match 'talvora_docker_info' -and
        $runtimeTools -match 'talvora_docker_ps' -and
        $runtimeTools -match 'talvora_docker_images' -and
        $runtimeTools -match 'talvora_docker_logs' -and
        $runtimeTools -match 'talvora_docker_exec' -and
        $runtimeTools -match 'talvora_docker_run' -and
        $runtimeTools -match 'talvora_docker_compose_run' -and
        $runtimeTools -match 'complete Docker CLI surface' -and
        $runtimeTools -match 'Use instead of pip or venv'
    )
    SessionToolContract = (
        $sessionTools -match 'talvora_session_list' -and
        $sessionTools -match 'talvora_session_get' -and
        $sessionTools -match 'talvora_user_process_start' -and
        $sessionTools -match 'WindowsSessionLauncher' -and
        $sharedSession -match 'WTSQueryUserToken' -and
        $sharedSession -match 'CreateEnvironmentBlock' -and
        $sharedSession -match 'CreateProcessAsUserW' -and
        $sharedSession -match 'CreateProcessWithTokenW' -and
        $sharedSession -match 'createAsUserError is 5 or 1314' -and
        $sharedSession -match 'winsta0'
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
        $watchTools -match 'talvora_watch_stop' -and
        $watchTools -match 'AbsoluteMaxQueuedEvents' -and
        $watchTools -match 'OverflowCount' -and
        $watchTools -match 'ResyncRequired'
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
        $gitTools -match 'Known working-tree mutation commands in recognized development workspaces route to talvora_apply_patch by default' -and
        $gitTools -match 'explicitAdmin=true deliberately preserves the unrestricted Git administration path'
    )
    SqliteToolContract = (
        $sqliteTools -match 'talvora_sqlite_info' -and
        $sqliteTools -match 'talvora_sqlite_query' -and
        $sqliteTools -match 'talvora_sqlite_execute' -and
        $sqliteTools -match 'talvora_sqlite_schema' -and
        $sqliteTools -match 'talvora_sqlite_backup' -and
        $sqliteTools -match 'Microsoft\.Data\.Sqlite' -and
        $sqliteTools -match 'SqliteOpenMode\.ReadOnly' -and
        $sqliteTools -match 'BackupDatabase' -and
        $serviceProject -match 'PackageReference Include="Microsoft\.Data\.Sqlite"' -and
        $toolManifest -match 'talvora_sqlite_info' -and
        $toolManifest -match 'talvora_sqlite_query' -and
        $toolManifest -match 'talvora_sqlite_execute' -and
        $toolManifest -match 'talvora_sqlite_schema' -and
        $toolManifest -match 'talvora_sqlite_backup'
    )
    DevServerToolContract = (
        $devServerTools -match 'talvora_dev_server_start' -and
        $devServerTools -match 'talvora_dev_server_get' -and
        $devServerTools -match 'talvora_dev_server_list' -and
        $devServerTools -match 'talvora_dev_server_wait' -and
        $devServerTools -match 'talvora_dev_server_stop' -and
        $devServerTools -match 'JobTools\.Start' -and
        $devServerTools -match 'JobTools\.Stop' -and
        $devServerTools -match 'TcpClient' -and
        $devServerTools -match 'HttpClient' -and
        $devServerTools -match 'DevServers' -and
        $toolManifest -match 'talvora_dev_server_start' -and
        $toolManifest -match 'talvora_dev_server_stop'
    )
    StructuredConfigToolContract = (
        $structuredConfigTools -match 'talvora_yaml_get' -and
        $structuredConfigTools -match 'talvora_yaml_set' -and
        $structuredConfigTools -match 'talvora_yaml_delete' -and
        $structuredConfigTools -match 'talvora_toml_get' -and
        $structuredConfigTools -match 'talvora_toml_set' -and
        $structuredConfigTools -match 'talvora_toml_delete' -and
        $structuredConfigTools -match 'ConfigAssetTools\.ParsePointer' -and
        $structuredConfigTools -match 'TomlSerializer' -and
        $structuredConfigTools -match 'YamlDeserializer' -and
        $serviceProject -match 'PackageReference Include="YamlDotNet"' -and
        $serviceProject -match 'PackageReference Include="Tomlyn"' -and
        $toolManifest -match 'talvora_yaml_get' -and
        $toolManifest -match 'talvora_toml_delete'
    )
    CanonicalManifestHasUniqueToolNames = ($canonicalToolCount -gt 0 -and $canonicalToolCount -eq $canonicalUniqueToolCount)
    LegacyToolAliasesRemoved = (
        $toolManifest -notmatch 'talvora_vs_instances' -and
        $toolManifest -notmatch 'talvora_windows_sdk_list' -and
        $toolManifest -notmatch 'talvora_vsdev_environment' -and
        $windowsToolchainTools -notmatch 'Name = "talvora_vs_instances"' -and
        $windowsToolchainTools -notmatch 'Name = "talvora_windows_sdk_list"' -and
        $windowsToolchainTools -notmatch 'Name = "talvora_vsdev_environment"'
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
        $toolManifest -match 'talvora_project_discover'
    )
    ServiceSupportsManualStopControl = (
        $serviceProgram -match 'CanStop\s*=\s*true' -and
        $serviceProgram -match 'CanPauseAndContinue\s*=\s*false' -and
        $serviceProgram -match 'CanShutdown\s*=\s*true'
    )
    InstallerCanReplaceNonStoppableService = (
        $installerFlow -match 'serviceSwitchStarted\s*=\s*true' -and
        $installerFlow -match 'StopServiceForUpgradeAsync\(ServiceName' -and
        $installerFlow -notmatch 'StopAndDeleteServiceAsync\(ServiceName' -and
        $installerService -match '"config"' -and
        $installerService -match '"binPath="' -and
        $installerService -match 'Kill\(entireProcessTree:\s*false\)' -and
        $installerService -notmatch 'process\.Kill\(entireProcessTree:\s*true\)'
    )
    InstallerStopsTrayBeforeService = (
        $installerFlow.IndexOf('serviceSwitchStarted = true;', [StringComparison]::Ordinal) -ge 0 -and
        $installerFlow.IndexOf('await StopTrayProcessesAsync(cancellationToken);', [StringComparison]::Ordinal) -gt
            $installerFlow.IndexOf('serviceSwitchStarted = true;', [StringComparison]::Ordinal) -and
        $installerFlow.IndexOf('await StopServiceForUpgradeAsync(ServiceName, cancellationToken);', [StringComparison]::Ordinal) -gt
            $installerFlow.IndexOf('await StopTrayProcessesAsync(cancellationToken);', [StringComparison]::Ordinal)
    )
    InstallerNamesRestartedService = (
        $installerService.Contains('was started again by another process during the upgrade')
    )
    InstallerUpgradeNeverDeletesCanonicalService = (
        $upgradeStopSection -match 'Kill\(entireProcessTree:\s*false\)' -and
        $upgradeStopSection -notmatch '"delete"' -and
        $upgradeStopSection -notmatch 'WaitForServiceDeletionAsync'
    )
    PlaywrightMcpRemovedFromTalvora = (
        $installerFlow -notmatch 'InstallPlaywrightManagedMcpAsync' -and
        $installerProgram -notmatch 'InstallPlaywrightManagedMcpAsync' -and
        $installerProgram -notmatch 'WaitForPlaywrightMcpReadinessAsync' -and
        $installerProgram -notmatch 'EnsureLatestNodeCurrentAsync' -and
        $installerProgram -notmatch 'PlaywrightEndpoint' -and
        $installerProject -notmatch 'ModelContextProtocol' -and
        $buildInstallerScript -notmatch 'PlaywrightPayload' -and
        $buildInstallerScript -notmatch 'Start-PlaywrightMcp' -and
        $buildInstallerScript -notmatch 'supervisor\.mjs' -and
        $registryCoordinator -notmatch 'PlaywrightManagedMcpRecoveryDiscovery' -and
        $registryCoordinator -match 'RetiredCanonicalMcpIds' -and
        $registryCoordinator -match '"playwright"' -and
        $registryCoordinator -match '!RetiredCanonicalMcpIds\.Contains\(entry\.Id\)' -and
        $trayProgram -notmatch 'PlaywrightManagedMcpRecoveryDiscovery' -and
        $smokeProgram -notmatch '--playwright-mcp' -and
        -not (Test-Path -LiteralPath (Join-Path $RepoRoot 'assets\playwright')) -and
        -not (Test-Path -LiteralPath (Join-Path $RepoRoot 'src\Talvora.Installer\InstallerEngine.Playwright.cs'))
    )
    InstallerConfiguresResilientSystemService = (
        $installerProgram -match 'restart/1000/restart/3000/restart/10000/restart/30000/restart/60000' -and
        $installerProgram -match '"sidtype"' -and
        $installerProgram -match '"unrestricted"'
    )
    InstallerGrantsInteractiveServiceLifecycle = (
        $installerProgram -match 'ConfigureInteractiveUserServiceAccessAsync' -and
        $installerProgram -match '"sdset"' -and
        $installerProgram -match 'CCLCSWRPWPDTLOCRRC' -and
        $installerProgram -match 'installUser\.Sid'
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
    # TI1: the tray draws the brand mark as vector art at the real tray size instead of shrinking the exe icon.
    TrayUsesBrandMarkArt = (
        $trayProgram -match 'TrayIconArt\.Render\(' -and
        $trayProgram -match 'TrayIconAppearance\.Current\(\)' -and
        $trayProgram -match 'TalvoraConnectionState\.Ready' -and
        $trayProgram -notmatch 'Icon\.ExtractAssociatedIcon\(Application\.ExecutablePath\)'
    )
    TrayIsWinExe = ($trayProject -match '<OutputType>WinExe</OutputType>')
    TrayUsesNotifyIcon = ($trayProgram -match '\bNotifyIcon\b')
    TrayReconnectIsNative = (
        $businessTunnelClient -match '"runtimes",\s*"connect"' -and
        $businessTunnelClient -match 'ReadRuntimeCredential' -and
        $businessTunnelClient -notmatch 'powershell\.exe'
    )
    TrayCommandModesBeforeMutex = (
        $trayProgramFile.IndexOf('--reconnect', [StringComparison]::Ordinal) -ge 0 -and
        $trayProgramFile.IndexOf('new Mutex', [StringComparison]::Ordinal) -gt
            $trayProgramFile.IndexOf('--reconnect', [StringComparison]::Ordinal)
    )
    TrayTunnelClientUsesStateWorkingDirectory = (
        $trayProgram -match 'ProcessRunner\.RunAsync' -and
        $trayProgram -match 'config\.StateRoot'
    )
    ManualStopStateIsWindowsSessionScoped = (
        $managedMcpSessionState -match 'session-state\.\{sessionId\}\.json' -and
        $managedMcpSessionState -match 'TryLoadLegacyCurrentScope' -and
        $managedMcpSessionState -match 'GetStatePath\(Process\.GetCurrentProcess\(\)\.SessionId\)'
    )
    ManualStopStateReadIsBounded = (
        $managedMcpSessionState -match 'MaximumSessionStateDocumentBytes\s*=\s*256\s*\*\s*1024' -and
        $managedMcpSessionState -match 'JsonFileStore\.ReadBounded<SessionStateDocument>' -and
        $managedMcpSessionState -notmatch 'File\.ReadAllText\(statePath\)' -and
        $managedMcpSessionState -notmatch 'File\.ReadAllText\(LegacyStatePath\)'
    )
    TalvoraLifecycleUsesOperationCoordinator = (
        $controlCenterLifecycleService -match 'ManagedMcpOperationCoordinator\.TryAcquire\(TalvoraId\)' -and
        $controlCenterLifecycleService -match 'ManagedMcpOperationInProgressException\(TalvoraId\)'
    )
    LifecycleRuntimeGenerationStateReadIsBounded = (
        $controlCenterLifecycleService -match 'MaximumRuntimeGenerationStateBytes\s*=\s*256\s*\*\s*1024' -and
        $controlCenterLifecycleService -match 'JsonFileStore\.ReadBoundedAsync<JsonDocument>\(' -and
        $controlCenterLifecycleService -notmatch 'File\.ReadAllTextAsync\(\s*statePath'
    )
    ControlCenterExitPersistsWindowPlacementBeforeShutdown = (
        $controlCenterWindow -match 'public async Task PrepareForApplicationExitAsync\(\)' -and
        $controlCenterWindow -match '_applicationExitRequested\s*=\s*true;' -and
        $controlCenterWindow -match 'await SaveWindowPlacementAsync\(\)\.WaitAsync\(TimeSpan\.FromSeconds\(2\)\);' -and
        $controlCenterWindow -match 'DetachControlCenterTimers\(\);' -and
        $controlCenterWindow -match '_lifetimeCts\.Cancel\(\);' -and
        $controlCenterWindow -notmatch 'SaveWindowPlacementAsync\(\)\.GetAwaiter\(\)\.GetResult\(\)'
    )
    ControlCenterWindowPlacementSaveLatestWins = (
        $controlCenterWindow -match 'SemaphoreSlim _windowPlacementSaveGate = new\(1, 1\)' -and
        $controlCenterWindow -match 'long _windowPlacementSaveVersion' -and
        $controlCenterWindowChrome -match 'Interlocked\.Increment\(\s*ref _windowPlacementSaveVersion\)' -and
        $controlCenterWindowChrome -match '_windowPlacementSaveGate\.WaitAsync\(\)\.ConfigureAwait\(false\)' -and
        $controlCenterWindowChrome -match 'saveVersion != Volatile\.Read\(\s*ref _windowPlacementSaveVersion\)' -and
        $controlCenterWindowChrome -match '_windowPlacementSaveGate\.Release\(\)'
    )
    # UI9 removed the legacy Control Center detail panel and its Gitea launch; the tray launch stays checked.
    ControlCenterDetailGiteaOpenHandlesShellFailure = (
        $controlCenterUi4 -match 'Gitea detay sayfasından açılamadı' -and
        $controlCenterUi4 -match 'ex is InvalidOperationException or\s+System\.ComponentModel\.Win32Exception' -and
        $controlCenterUi4 -match 'await ShowOperationErrorAsync\("Gitea açılamadı"' -and
        $controlCenterUi4 -match 'using var launched = System\.Diagnostics\.Process\.Start\('
    )
    GiteaBrowserLaunchDisposesProcessHandles = (
        $trayApplicationContext -match 'using var launched = Process\.Start\('
    )
    TrayAutoReconnectsAfterStartup = (
        $trayApplicationContext -match 'await MaintainTalvoraConnectionAsync\(\)' -and
        $trayApplicationContext -match '_talvoraTimer\.Tick\s*\+=' -and
        $trayApplicationContext -match '_talvoraRecoveryState\.RegisterFailure' -and
        $trayApplicationContext -match '_talvoraRecoveryState\.CanAttempt' -and
        $trayApplicationContext -match 'RetryIn='
    )
    GenericAttentionRemediationAvoidsFullRestart = (
        $trayProgram -match 'TryRepairWithoutRestartAsync' -and
        $trayProgram -match 'ConnectExistingAsync' -and
        $trayProgram -match 'Escalated: true'
    )
    TrayBoundsTunnelLogging = (
        $trayProgram -match '\["LOG_LEVEL"\]\s*=\s*"warn"' -and
        $trayProgram -match '\["ADMIN_UI_LOG_BUFFER_EVENTS"\]\s*=\s*"500"'
    )
    InstallerEmbedsPayload = ($installerProject -match 'EmbeddedResource Include="Payload\.zip"')
    InstallerRequiresAdmin = ($manifest -match 'requestedExecutionLevel level="requireAdministrator"')
    InstallerUsesProgramFiles = ($installerProgram -match 'SpecialFolder\.ProgramFiles')
    InstallerRegistersTrayStartup = (
        $installerProgram -match 'RunValueName' -and
        $sharedConstants -match 'TalvoraTray'
    )
    InstallerRetriesTrayLaunchWithoutRollingBackHealthyService = (
        $installerProgram -match 'TryStartTrayAsync' -and
        $installerProgram -match 'Tray could not be started immediately' -and
        $installerProgram -match 'startup registration is intact'
    )
    InstallScriptWaitsOnlyForInstallerProcess = (
        $installScript -match '\$installerProcess\.WaitForExit\(\)' -and
        $installScript -match '\$installerExitCode\s*=\s*\$installerProcess\.ExitCode' -and
        $installScript -match '(?s)\$installerProcess\s*=\s*Start-Process.*?-ArgumentList\s+@\(''--silent''\).*?-PassThru\s*\r?\n\$installerProcess\.WaitForExit\(\)' -and
        $installScript -notmatch '(?s)\$installerProcess\s*=\s*Start-Process.*?-ArgumentList\s+@\(''--silent''\).*?-Wait.*?\$installerProcess\.ExitCode'
    )
    ServiceHostedInstallUsesDetachedGracefulBootstrap = (
        $installScript -match 'Test-TalvoraServiceAncestor' -and
        $installScript -match 'TALVORA_UPDATE_DETACHED' -and
        $installScript -match "'--defer-ms', '2500'" -and
        $installerProgram -match 'ReadBoundedIntegerArgument' -and
        $installerProgram -match '"--defer-ms"' -and
        $installerProgram -match 'Thread\.Sleep\(deferMilliseconds\)'
    )
    HostedWindowsCiAvoidsInteractiveUserSmoke = (
        $windowsCiWorkflow -match '(?s)- name: Install and verify canonical runtime.*?--surface-policy-live.*?Installed MCP surface smoke failed' -and
        $windowsCiWorkflow -notmatch 'Talvora\.Smoke/Talvora\.Smoke\.csproj -c Release --no-build -- ''http://127\.0\.0\.1:7676/mcp'' \$PWD\.Path'
    )
    InstallerUsesVersionedPayload = (
        $installerProgram -match 'Versions' -and
        $installerProgram -match 'versionId' -and
        $installerProgram -match 'CleanupObsoleteInstallations'
    )
    InstallerSupportsRollback = (
        $installerProgram -match 'rolling back' -and
        $installerProgram -match 'previousServiceExecutable'
    )
    InstallerUserStateRollbackReadsAreBounded = (
        $installerRollbackState -match 'MaximumInstallerUserStateFileBytes\s*=\s*8\s*\*\s*1024\s*\*\s*1024' -and
        $installerRollbackState -match 'snapshotLength\s*=\s*stream\.Length' -and
        $installerRollbackState -match 'ReadExactlyAsync' -and
        $installerRollbackState -notmatch 'File\.ReadAllBytesAsync' -and
        $installerCleanupState -match 'TextFileStore\.ReadBoundedAsync\(' -and
        $installerCleanupState -match 'MaximumInstallerUserStateFileBytes' -and
        $installerCleanupState -notmatch 'File\.ReadAllTextAsync\(path,\s*cancellationToken\)'
    )
    TraySupportsGracefulShutdown = (
        $trayProgram -match 'Global\\Talvora\.Tray\.Shutdown' -and
        $installerProgram -match 'Global\\Talvora\.Tray\.Shutdown' -and
        $installerProgram -match 'EventWaitHandle\.OpenExisting'
    )
    InstallerVerifiesStableTrayPid = (
        $installerProgram -match 'launchedProcessId' -and
        $installerProgram -match 'stableSinceUtc' -and
        $installerProgram -match 'TimeSpan\.FromSeconds\(2\)' -and
        $installerProgram -match 'Tray started and remained stable'
    )
    InstallerSchedulesLockedCleanup = (
        $installerProgram -match 'MoveFileEx' -and
        $installerProgram -match 'DelayUntilReboot'
    )
    InstallerRebootCleanupIsReparseSafeAndBounded = (
        $rebootCleanupSection -match 'MaximumScheduledCleanupEntries' -and
        $rebootCleanupSection -match 'FileAttributes\.ReparsePoint' -and
        $rebootCleanupSection -match 'SearchOption\.TopDirectoryOnly' -and
        $rebootCleanupSection -notmatch 'SearchOption\.AllDirectories'
    )
    InstallerImmediateCleanupIsCancellationAwareAndBounded = (
        $immediateCleanupSection -match 'MaximumImmediateCleanupEntries' -and
        $immediateCleanupSection -match 'DeleteDirectoryTreeCancellationAware' -and
        $immediateCleanupSection -match 'cancellationToken\.ThrowIfCancellationRequested\(\)' -and
        $immediateCleanupSection -match 'FileAttributes\.ReparsePoint' -and
        $immediateCleanupSection -notmatch 'Directory\.Delete\(path, recursive: true\)'
    )
    InstallerVersionRootEnumerationIsBounded = (
        $installerCleanupState -match 'MaximumInstalledVersionDirectories =\s*10_000' -and
        $installerCleanupState -match '\.Take\(MaximumInstalledVersionDirectories \+ 1\)' -and
        $installerCleanupState -match 'Installed-version cleanup reached its bounded scan limit'
    )
    # KUR1: the Santral setup window, its developer plate, no message boxes, no closing half way.
    SetupWindowKeepsSantralLookAndDeveloperPlate = (
        $installerProgram.Contains('DeveloperCredit = "Geliştiren: Taylan Soylu"') -and
        $installerProgram.Contains('PlateLabel = "GELİŞTİREN"') -and
        $installerProgram.Contains('PlateName = "TAYLAN SOYLU"') -and
        $installerProgram.Contains('WindowTitle: "Talvora Kurulumu"') -and
        $installerProgram.Contains('theme-preference.json') -and
        $installerProgram.Contains('SystemInformation.HighContrast') -and
        $installerProgram.Contains('Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)') -and
        $installerForm.Contains('e.CloseReason == CloseReason.UserClosing') -and
        $installerForm.Contains('e.Cancel = true;') -and
        $installerForm.Contains('await Task.Run(() => _operation(progress, option, CancellationToken.None))') -and
        -not $installerForm.Contains('MessageBox.Show')
    )
}

$result | Format-List

if ($result.PSObject.Properties.Value -contains $false) {
    throw 'Native installer/tray source contract is incomplete.'
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('TalvoraPwshCheck-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
try {
    $testSourcePath = Join-Path $testRoot 'PowerShellInstallerRegression.cs'
    $msiFixturePath = Join-Path $testRoot 'untrusted.msi'
    [IO.File]::WriteAllText($msiFixturePath, 'not a valid PowerShell MSI')
    [IO.File]::WriteAllText($testSourcePath, @'
#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace Talvora.Installer;

internal sealed record InstallProgress(int Percent, string Message);
internal static class InstallerLog
{
    public static void Write(string message, Exception? exception = null) {}
}

public static class PowerShellInstallerRegression
{
    public static bool RandomMsiPathHasExpectedName()
    {
        var method = typeof(InstallerEngine).GetMethod(
            "GetPowerShellMsiPath", BindingFlags.Static | BindingFlags.NonPublic)!;
        var first = (string)method.Invoke(null, new object[] { "7.6.9" })!;
        var second = (string)method.Invoke(null, new object[] { "7.6.9" })!;
        return first != second && Regex.IsMatch(Path.GetFileName(first),
            @"^Talvora-PowerShell-7\.\d+\.\d+-[0-9a-f]{32}\.msi$");
    }

    public static bool ExistingDownloadFileCannotBeOverwritten(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return File.ReadAllText(path) == "not a valid PowerShell MSI";
        }
    }

    public static bool VerifiedStreamBlocksWrites(string path)
    {
        using var verifiedStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return false;
        }
        catch (IOException)
        {
            try
            {
                InstallerEngine.ValidatePowerShell7MsiHash(verifiedStream, new string('0', 64));
                return false;
            }
            catch (InvalidOperationException error)
            {
                return error.Message == "İndirilen PowerShell 7 paketi doğrulanamadı; güvenlik için kurulmadı.";
            }
        }
    }

    public static bool ExistingPowerShellSkipsDownload()
    {
        var checks = 0;
        InstallerEngine.EnsurePowerShell7Async(
            new Progress<InstallProgress>(), CancellationToken.None,
            () => { checks++; return true; }).GetAwaiter().GetResult();
        return checks == 1;
    }

    public static bool WrongHashIsRejected(string path)
    {
        using var verifiedStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            InstallerEngine.ValidatePowerShell7MsiHash(verifiedStream, new string('0', 64));
            return false;
        }
        catch (InvalidOperationException error)
        {
            return error.Message == "İndirilen PowerShell 7 paketi doğrulanamadı; güvenlik için kurulmadı.";
        }
    }
}
'@)
    Add-Type -Path @($powerShellSourcePath, $testSourcePath) -ErrorAction Stop
    if (-not [Talvora.Installer.PowerShellInstallerRegression]::RandomMsiPathHasExpectedName()) {
        throw 'PowerShell MSI download must use an unpredictable 32-digit lowercase hex name.'
    }
    if (-not [Talvora.Installer.PowerShellInstallerRegression]::ExistingDownloadFileCannotBeOverwritten($msiFixturePath)) {
        throw 'An existing PowerShell MSI download file must not be overwritten.'
    }
    if (-not [Talvora.Installer.PowerShellInstallerRegression]::VerifiedStreamBlocksWrites($msiFixturePath)) {
        throw 'A verified PowerShell MSI must remain locked against writers while hashing.'
    }
    if (-not [Talvora.Installer.PowerShellInstallerRegression]::ExistingPowerShellSkipsDownload()) {
        throw 'An installed PowerShell 7 must bypass the download through the injectable probe.'
    }
    if (-not [Talvora.Installer.PowerShellInstallerRegression]::WrongHashIsRejected($msiFixturePath)) {
        throw 'A mismatched PowerShell MSI digest must be rejected before installation.'
    }
    $windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
    try {
        $readableMsiPath = Join-Path $testRoot 'locked-read.msi'
        $newDatabase = $windowsInstaller.OpenDatabase($readableMsiPath, 3)
        $newDatabase.Commit()
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($newDatabase)
        $verifiedMsi = [IO.FileStream]::new(
            $readableMsiPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            $readonlyDatabase = $windowsInstaller.OpenDatabase($readableMsiPath, 0)
            if ($null -eq $readonlyDatabase) {
                throw 'Windows Installer must open the MSI for reading while the verified stream stays locked.'
            }
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($readonlyDatabase)
        }
        finally {
            $verifiedMsi.Dispose()
        }
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($windowsInstaller)
    }
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# RL3: compile and exercise the actual uninstall plan and registry-writing code
# with a temporary user hive and temporary data tree; never touch HKLM or real data.
$rl3FixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('TalvoraUninstallCheck-' + [Guid]::NewGuid().ToString('N'))
$rl3RegistryName = 'Software\TalvoraUninstallCheck-' + [Guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $rl3FixtureRoot -Force | Out-Null
try {
    $rl3DataRoot = Join-Path $rl3FixtureRoot 'Data'
    New-Item -ItemType Directory -Path (Join-Path $rl3DataRoot 'Secrets') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $rl3DataRoot 'Events') -Force | Out-Null
    $rl3FixturePath = Join-Path $rl3FixtureRoot 'Rl3Contracts.cs'
    [IO.File]::WriteAllText($rl3FixturePath, @'
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Talvora.Shared.Rl3Fixture
{
    internal static class TextFileStore
    {
        public static Task<string> ReadBoundedAsync(string path, int limit, CancellationToken ct) =>
            Task.FromResult(string.Empty);
    }
    internal static class AtomicFile
    {
        public static Task<bool> WriteAllTextAsync(string path, string text, System.Text.Encoding encoding,
            bool createBackup, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
namespace Talvora.Installer.Rl3Fixture
{
    internal sealed record InstallProgress(int Percent, string Message);
    internal sealed record InstallUserContext(string UserProfile, string Sid);
    internal static class InstallerLog
    {
        public static void Write(string message, Exception? exception = null) {}
    }
    internal static partial class InstallerEngine
    {
        private const string ServiceName = "Talvora";
        private const string RunValueName = "TalvoraTray";
        private const string McpUrl = "http://127.0.0.1:7676/mcp";
        private const int MaximumInstallerUserStateFileBytes = 8 * 1024 * 1024;
        private enum MoveFileFlags : uint { DelayUntilReboot = 4 }
        private static bool MoveFileEx(string path, string? unused, MoveFileFlags flags) => true;
        private static InstallUserContext ResolveInstallUserContext() =>
            throw new NotSupportedException();
        private static Task StopTrayProcessesAsync(CancellationToken ct) => Task.CompletedTask;
        private static Task StopAndDeleteServiceAsync(string name, CancellationToken ct) => Task.CompletedTask;
        private static RegistryKey OpenInstallUserRoot(InstallUserContext user, bool writable) =>
            Registry.CurrentUser;
        private static Task TryDeleteDirectoryAsync(string path, CancellationToken ct) => Task.CompletedTask;
    }

    public static class Rl3Contracts
    {
        public static string StripCodex(string content) =>
            Talvora.Shared.CodexMcpConfiguration.RemoveTalvoraLocal(
                content, "http://127.0.0.1:7676/mcp/dev");

        public static string RetireSetup(string installRoot) =>
            InstallerEngine.RetireRunningSetup(installRoot);

        public static string[] Plan(string dataRoot, bool removeData) =>
            InstallerEngine.BuildUninstallPlan(@"C:\Program Files\Talvora", dataRoot,
                @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs",
                @"C:\Users\fixture", "S-1-5-21-fixture", removeData).ToArray();

        public static void AssertRegistryValues(string fixtureRoot)
        {
            using var root = Registry.CurrentUser.CreateSubKey(fixtureRoot, writable: true)!;
            const string install = @"C:\Program Files\Talvora";
            const string tray = @"C:\Program Files\Talvora\Versions\example\Tray\Talvora.Tray.exe";
            InstallerEngine.WriteUninstallRegistration(root, install, tray, "0.0.1.0+abcdef", 4096);
            using var key = root.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Talvora")!;
            if (key.GetValue("DisplayName") as string != "Talvora" ||
                key.GetValue("DisplayVersion") as string != "0.0.1.0" ||
                key.GetValue("Publisher") as string != "Taylan Soylu" ||
                key.GetValue("DisplayIcon") as string != tray + ",0" ||
                key.GetValue("InstallLocation") as string != install ||
                key.GetValue("UninstallString") as string != '"' + install + @"\Talvora-Kurulum.exe" + '"' + " --uninstall" ||
                key.GetValue("QuietUninstallString") as string != '"' + install + @"\Talvora-Kurulum.exe" + '"' + " --uninstall --silent" ||
                key.GetValue("URLInfoAbout") as string != "https://github.com/bingoweb/Talvora-MCP" ||
                (int)key.GetValue("NoModify")! != 1 ||
                (int)key.GetValue("NoRepair")! != 1 ||
                (int)key.GetValue("EstimatedSize")! != 4096 ||
                key.GetValueKind("NoModify") != RegistryValueKind.DWord ||
                key.GetValueKind("NoRepair") != RegistryValueKind.DWord ||
                key.GetValueKind("EstimatedSize") != RegistryValueKind.DWord)
                throw new InvalidOperationException("RL3 uninstall registry values do not match the contract.");
        }
    }
}
'@)
    $rl3IsolatedSource = Join-Path $rl3FixtureRoot 'InstallerEngine.Uninstall.cs'
    [IO.File]::WriteAllText($rl3IsolatedSource,
        $uninstallSource.Replace('namespace Talvora.Installer;', 'namespace Talvora.Installer.Rl3Fixture;').
            Replace('using Talvora.Shared;', "using Talvora.Shared;`nusing Talvora.Shared.Rl3Fixture;"))
    $rl3CodexSource = Join-Path $rl3FixtureRoot 'CodexMcpConfiguration.cs'
    [IO.File]::WriteAllText($rl3CodexSource,
        "#nullable enable`nusing System;`nusing System.Collections.Generic;`n" +
        [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Shared\CodexMcpConfiguration.cs')))
    Add-Type -Path @($rl3IsolatedSource, $rl3FixturePath, $rl3CodexSource) -ErrorAction Stop
    $ordinary = @([Talvora.Installer.Rl3Fixture.Rl3Contracts]::Plan($rl3DataRoot, $false))
    $withData = @([Talvora.Installer.Rl3Fixture.Rl3Contracts]::Plan($rl3DataRoot, $true))
    if ($ordinary.Count -ne 6 -or
        $ordinary[0] -notmatch 'Windows hizmeti: Talvora' -or
        $ordinary[1] -notmatch 'Run' -or
        $ordinary[2] -notmatch '\.lnk' -or
        $ordinary[3] -notmatch 'mcp_servers\.talvora_local' -or
        $ordinary[4] -notmatch 'C:\\Program Files\\Talvora' -or
        $ordinary[5] -notmatch 'Uninstall\\Talvora') {
        throw 'RL3 uninstall plan must list six precise operations in order.'
    }
    if ($withData.Count -ne 7 -or
        $withData[-1] -notmatch 'Events' -or
        (@($withData | Where-Object { $_ -match '(?i)[\\/]+Secrets([\\/]|$)' }).Count -ne 0)) {
        throw 'RL3 remove-data plan must include only non-secret children.'
    }
    [Talvora.Installer.Rl3Fixture.Rl3Contracts]::AssertRegistryValues($rl3RegistryName)
    $codexSample = "[mcp_servers.elsewhere]`r`nurl = `"other`"`r`n" +
        "[mcp_servers.talvora_local]`r`nurl = `"http://127.0.0.1:7676/mcp/dev`"`r`nenabled_tools = []`r`n" +
        "  [user.preferences] # keep me`r`nvalue = 42`r`n"
    $codexTrimmed = [Talvora.Installer.Rl3Fixture.Rl3Contracts]::StripCodex($codexSample)
    if (-not $codexTrimmed.Contains('[mcp_servers.elsewhere]') -or
        -not $codexTrimmed.Contains('  [user.preferences] # keep me') -or
        $codexTrimmed.Contains('[mcp_servers.talvora_local]')) {
        throw 'RL3 may remove only the Talvora MCP table while preserving all other TOML tables.'
    }
    $crlfCodex = (@(
        'model = "gpt-5"'
        '[mcp_servers.talvora_local]'
        'url = "http://127.0.0.1:7676/mcp/dev"'
        '[mcp_servers.talvora_local.env]'
        'TALVORA_X = "1"'
        '[mcp_servers.github]'
        'command = "gh"'
        '[notes]'
        'text = """'
        '[mcp_servers.talvora_local]'
        'This is part of the note.'
        '"""'
    ) -join "`r`n") + "`r`n"
    $crlfRetained = (@(
        'model = "gpt-5"'
        '[mcp_servers.github]'
        'command = "gh"'
        '[notes]'
        'text = """'
        '[mcp_servers.talvora_local]'
        'This is part of the note.'
        '"""'
    ) -join "`r`n") + "`r`n"
    if ([Talvora.Installer.Rl3Fixture.Rl3Contracts]::StripCodex($crlfCodex) -cne $crlfRetained) {
        throw 'D65 must remove Talvora sub-tables but retain all other CRLF TOML bytes and multiline strings.'
    }
    $customTalvora = "[mcp_servers.talvora_local]`nurl = `"https://example.invalid/custom`"`n"
    if ([Talvora.Installer.Rl3Fixture.Rl3Contracts]::StripCodex($customTalvora) -cne $customTalvora) {
        throw 'RL3 must preserve a user-edited Talvora table that does not match the installer URL.'
    }
    $retireFolder = Join-Path $rl3FixtureRoot 'RetireSetup'
    New-Item -ItemType Directory -Path $retireFolder -Force | Out-Null
    $setupPath = Join-Path $retireFolder 'Talvora-Kurulum.exe'
    [IO.File]::WriteAllText($setupPath, 'fixture setup')
    $retiredPath = [Talvora.Installer.Rl3Fixture.Rl3Contracts]::RetireSetup($retireFolder)
    $retiredFiles = @(Get-ChildItem -LiteralPath $retireFolder -File)
    if ((Test-Path -LiteralPath $setupPath) -or
        $retiredFiles.Count -ne 1 -or
        $retiredFiles[0].Name -cnotmatch '^Talvora-Kurulum\.[0-9a-f]{32}\.kaldirildi$' -or
        $retiredPath -cne $retiredFiles[0].FullName) {
        throw 'D66 must retire only the running setup under its unique name.'
    }
    if ($uninstallSource -match 'MoveFileEx\s*\(\s*retainedSetup') {
        throw 'D66 must not schedule the stable setup filename for deletion on reboot.'
    }
}
finally {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($rl3RegistryName, $false)
    Remove-Item -LiteralPath $rl3FixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'NATIVE_INSTALLER_SOURCE_GREEN'