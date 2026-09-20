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
$trayProgram = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora.Tray')
$trayProgramFile = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\Program.cs'))
$trayApplicationContext = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\TrayApplicationContext.cs'))
$businessTunnelClient = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\BusinessTunnelClient.cs'))
$componentHealth = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ControlCenterComponentHealthService.cs'))
$controlCenterLifecycleService = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ControlCenterLifecycleService.cs'))
$managedMcpSessionState = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ManagedMcpSessionState.cs'))

$registryCoordinator = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ManagedMcpRegistryCoordinator.cs'))
$ownershipManifestStore = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\ManagedMcpOwnershipManifestStore.cs'))
$installerFlow = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\InstallerEngine.Flow.cs'))
$installerService = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\InstallerEngine.Service.cs'))
$trayProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Tray\Talvora.Tray.csproj'))
$installerProgram = Read-ProjectSources (Join-Path $RepoRoot 'src\Talvora.Installer')
$installerProject = [IO.File]::ReadAllText((Join-Path $RepoRoot 'src\Talvora.Installer\Talvora.Installer.csproj'))
$buildInstallerScript = [IO.File]::ReadAllText((Join-Path $RepoRoot 'scripts\Build-Windows-Installer.ps1'))
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

$result = [pscustomobject]@{
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
        $buildInstallerScript -match '@latest' -and
        $buildInstallerScript -match '--ignore-scripts' -and
        $buildInstallerScript -match 'dist\.integrity' -and
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
        $buildInstallerScript -match '--ignore-scripts' -and
        $buildInstallerScript -match 'dist\.integrity' -and
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
    WindowsPowerShellFallbackContract = (
        $componentHealth -match 'CommandLine\.IndexOf\(\$marker,\[StringComparison\]::OrdinalIgnoreCase\) -ge 0' -and
        $componentHealth -notmatch 'CommandLine\.Contains\(\$marker,\[StringComparison\]::OrdinalIgnoreCase\)'


    )
    RegistryOwnershipManifestContract = (
        $registryCoordinator -match 'ManagedMcpOwnershipManifestStore\.ReadAllAsync' -and
        $registryCoordinator -match 'ManagedMcpOwnershipManifestStore\.PersistAsync' -and
        $registryCoordinator -match 'ManagedMcpOwnershipManifestStore\.NeedsSeed' -and
        $ownershipManifestStore -match 'managed-mcps\.d' -and
        $ownershipManifestStore -match 'AtomicFile\.WriteAllTextAsync' -and
        $ownershipManifestStore -match 'ManagedMcpRegistryStore\.ValidateRegistration' -and
        $ownershipManifestStore -match 'Directory\.EnumerateFiles' -and
        $ownershipManifestStore -match 'File\.Delete\(stale\)'
    )
    BuildScriptFingerprintsDirtyProvenance = (
        $buildInstallerScript -match 'git -C \$RepoRoot status --porcelain=v1 --untracked-files=all' -and
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
        $windowsToolchainTools -match 'talvora_cmake_info' -and
        $windowsToolchainTools -match 'talvora_cmake_run' -and
        $windowsToolchainTools -match 'talvora_ninja_info' -and
        $windowsToolchainTools -match 'talvora_ninja_run' -and
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
        $installerService -match 'Kill\(entireProcessTree:\s*true\)'
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
    TrayUsesEmbeddedBrandIcon = (
        $trayProgram -match 'Icon\.ExtractAssociatedIcon\(Application\.ExecutablePath\)' -and
        $trayProgram -match 'DrawStatusBadge' -and
        $trayProgram -match 'TalvoraConnectionState\.Ready'
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
    TalvoraLifecycleUsesOperationCoordinator = (
        $controlCenterLifecycleService -match 'ManagedMcpOperationCoordinator\.TryAcquire\(TalvoraId\)' -and
        $controlCenterLifecycleService -match 'ManagedMcpOperationInProgressException\(TalvoraId\)'
    )
    TrayAutoReconnectsAfterStartup = (
        $trayApplicationContext -match 'await MaintainTalvoraConnectionAsync\(\)' -and
        $trayApplicationContext -match '_talvoraTimer\.Tick\s*\+=' -and
        $trayApplicationContext -match '_talvoraRecoveryState\.RegisterFailure' -and
        $trayApplicationContext -match '_talvoraRecoveryState\.CanAttempt' -and
        $trayApplicationContext -match 'RetryIn='
    )
    GenericAttentionRemediationAvoidsFullRestart = (
        $trayApplicationContext -match 'TryRepairGenericWithoutRestartAsync' -and
        $trayApplicationContext -match 'browser smoke yenilemesi .*MCP/browser zinciri korunuyor' -and
        $trayApplicationContext -match 't.neli yeniden ba.lanamad.; yerel MCP/browser zinciri korunuyor'
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
}

$result | Format-List

if ($result.PSObject.Properties.Value -contains $false) {
    throw 'Native installer/tray source contract is incomplete.'
}

Write-Output 'NATIVE_INSTALLER_SOURCE_GREEN'