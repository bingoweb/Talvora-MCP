param(
    [string] $RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ArtifactsRoot = Join-Path $RepoRoot 'artifacts\installer'
$WorkRoot = Join-Path $RepoRoot 'artifacts\installer-work'
$SourceSnapshotRoot = Join-Path $WorkRoot 'source-snapshot'
$PayloadRoot = Join-Path $WorkRoot 'payload'
$ServicePayload = Join-Path $PayloadRoot 'Service'
$TrayPayload = Join-Path $PayloadRoot 'Tray'
$DependencyArtifactsRoot = Join-Path $WorkRoot 'dependency-build'
$DependencySnapshotRoot = Join-Path $WorkRoot 'dependency-assets'
$ServiceProject = Join-Path $SourceSnapshotRoot 'src\Talvora\Talvora.csproj'
$TrayProject = Join-Path $SourceSnapshotRoot 'src\Talvora.Tray\Talvora.Tray.csproj'
$ServiceAssetsSnapshotPath = Join-Path $DependencySnapshotRoot 'Talvora.project.assets.json'
$TrayAssetsSnapshotPath = Join-Path $DependencySnapshotRoot 'Talvora.Tray.project.assets.json'
$InstallerProject = Join-Path $SourceSnapshotRoot 'src\Talvora.Installer\Talvora.Installer.csproj'
$PayloadZip = Join-Path $SourceSnapshotRoot 'src\Talvora.Installer\Payload.zip'

function Get-PayloadFileManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root,

        [Parameter(Mandatory = $true)]
        [string] $ArchivePrefix
    )

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "Payload directory is missing: $Root"
    }

    $prefix = ($ArchivePrefix -replace '\\', '/').TrimEnd('/')
    $rootFull = [IO.Path]::GetFullPath($Root)
    $separator = [IO.Path]::DirectorySeparatorChar.ToString()
    if (-not $rootFull.EndsWith($separator, [StringComparison]::Ordinal)) {
        $rootFull += $separator
    }

    return @(
        Get-ChildItem -LiteralPath $Root -File -Recurse -Force |
            Sort-Object FullName |
            ForEach-Object {
                $fullPath = [IO.Path]::GetFullPath($_.FullName)
                if (-not $fullPath.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Payload file escaped the expected root: $fullPath"
                }

                $relative = $fullPath.Substring($rootFull.Length) -replace '\\', '/'
                [pscustomobject]@{
                    FullPath = $fullPath
                    ArchivePath = if ([string]::IsNullOrWhiteSpace($prefix)) {
                        $relative
                    }
                    else {
                        $prefix + '/' + $relative
                    }
                    Length = [long]$_.Length
                }
            }
    )
}

function Assert-RequiredPayloadFiles {
    param(
        [Parameter(Mandatory = $true)]
        [object[]] $Files,

        [Parameter(Mandatory = $true)]
        [string[]] $RequiredArchivePaths
    )

    if ($Files.Count -eq 0) {
        throw 'Published payload manifest is empty.'
    }

    $paths = @($Files | ForEach-Object { [string]$_.ArchivePath })
    foreach ($requiredPath in $RequiredArchivePaths) {
        if ($paths -notcontains $requiredPath) {
            throw "Required published payload file is missing from manifest: $requiredPath"
        }
    }
}

function Wait-PayloadFilesReady {
    param(
        [Parameter(Mandatory = $true)]
        [object[]] $Files,

        [int] $AttemptsPerFile = 40
    )

    if ($AttemptsPerFile -lt 1) {
        throw 'Payload readiness retry count must be at least 1.'
    }

    foreach ($file in $Files) {
        $ready = $false

        for ($attempt = 1; $attempt -le $AttemptsPerFile; $attempt++) {
            $stream = $null
            try {
                if (-not [IO.File]::Exists([string]$file.FullPath)) {
                    throw [IO.FileNotFoundException]::new(
                        "Published payload file is temporarily unavailable: $($file.FullPath)",
                        [string]$file.FullPath)
                }

                $stream = [IO.File]::Open(
                    [string]$file.FullPath,
                    [IO.FileMode]::Open,
                    [IO.FileAccess]::Read,
                    [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)

                if ($stream.Length -ne [long]$file.Length) {
                    throw [IO.IOException]::new(
                        "Published payload file length changed. Expected=$($file.Length) Actual=$($stream.Length) Path=$($file.FullPath)")
                }

                $ready = $true
            }
            catch [IO.IOException] {
                if ($attempt -ge $AttemptsPerFile) {
                    throw
                }

                Start-Sleep -Milliseconds ([Math]::Min(1000, 50 * $attempt))
            }
            finally {
                if ($null -ne $stream) {
                    $stream.Dispose()
                }
            }

            if ($ready) {
                break
            }
        }

        if (-not $ready) {
            throw "Payload file never became readable: $($file.FullPath)"
        }
    }
}

function New-VerifiedPayloadArchive {
    param(
        [Parameter(Mandatory = $true)]
        [object[]] $Files,

        [Parameter(Mandatory = $true)]
        [string] $Destination,

        [int] $Attempts = 5
    )

    if ($Attempts -lt 1) {
        throw 'Archive retry count must be at least 1.'
    }

    $expected = @{}
    foreach ($file in $Files) {
        $archivePath = [string]$file.ArchivePath
        if ($expected.ContainsKey($archivePath)) {
            throw "Duplicate payload archive path: $archivePath"
        }
        $expected[$archivePath] = [long]$file.Length
    }

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue

        try {
            Wait-PayloadFilesReady -Files $Files -AttemptsPerFile 40

            $archive = [IO.Compression.ZipFile]::Open(
                $Destination,
                [IO.Compression.ZipArchiveMode]::Create)
            try {
                foreach ($file in $Files) {
                    $source = $null
                    $target = $null
                    try {
                        $source = [IO.File]::Open(
                            [string]$file.FullPath,
                            [IO.FileMode]::Open,
                            [IO.FileAccess]::Read,
                            [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)

                        if ($source.Length -ne [long]$file.Length) {
                            throw [IO.IOException]::new(
                                "Payload file changed while archiving. Expected=$($file.Length) Actual=$($source.Length) Path=$($file.FullPath)")
                        }

                        $entry = $archive.CreateEntry(
                            [string]$file.ArchivePath,
                            [IO.Compression.CompressionLevel]::Optimal)
                        $target = $entry.Open()
                        $source.CopyTo($target)
                    }
                    finally {
                        if ($null -ne $target) {
                            $target.Dispose()
                        }
                        if ($null -ne $source) {
                            $source.Dispose()
                        }
                    }
                }
            }
            finally {
                $archive.Dispose()
            }

            $verified = [IO.Compression.ZipFile]::OpenRead($Destination)
            try {
                if ($verified.Entries.Count -ne $Files.Count) {
                    throw [IO.IOException]::new(
                        "Payload archive entry count mismatch. Expected=$($Files.Count) Actual=$($verified.Entries.Count)")
                }

                foreach ($entry in $verified.Entries) {
                    if (-not $expected.ContainsKey($entry.FullName)) {
                        throw [IO.IOException]::new(
                            "Unexpected payload archive entry: $($entry.FullName)")
                    }

                    if ($entry.Length -ne [long]$expected[$entry.FullName]) {
                        throw [IO.IOException]::new(
                            "Payload archive entry length mismatch. Expected=$($expected[$entry.FullName]) Actual=$($entry.Length) Entry=$($entry.FullName)")
                    }
                }
            }
            finally {
                $verified.Dispose()
            }

            return
        }
        catch [IO.IOException] {
            Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue

            if ($attempt -ge $Attempts) {
                throw
            }

            Write-Warning "Payload archive attempt $attempt/$Attempts hit a transient IO error: $($_.Exception.Message)"
            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }

    throw "Unable to create verified payload archive after $Attempts attempts."
}

function Test-RuntimeBuildInput {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $normalized = $RelativePath.Replace('\\', '/')
    if ($normalized.StartsWith('src/', [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith('assets/', [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    if ([string]::Equals(
            $normalized,
            'scripts/Build-Windows-Installer.ps1',
            [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $fileName = [IO.Path]::GetFileName($normalized)
    return $fileName -in @(
        'Directory.Build.props',
        'Directory.Build.targets',
        'Directory.Packages.props',
        'global.json',
        'NuGet.config'
    )
}

function Get-WorkingTreeFingerprint {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root
    )

    $changed = @(& git -C $Root diff --name-only HEAD --)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate tracked Talvora working-tree changes.'
    }

    $untracked = @(& git -C $Root ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate untracked Talvora working-tree files.'
    }

    $entries = New-Object System.Collections.Generic.List[string]
    foreach ($relativePath in @($changed + $untracked | Sort-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($relativePath) -or
            -not (Test-RuntimeBuildInput -RelativePath $relativePath)) {
            continue
        }

        $fullPath = Join-Path $Root $relativePath
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            $blobHash = (& git -C $Root hash-object -- $relativePath).Trim()
            if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($blobHash)) {
                throw "Unable to hash dirty working-tree file: $relativePath"
            }
            $entries.Add([string]::Concat($relativePath, [char]9, $blobHash))
        }
        else {
            $entries.Add([string]::Concat($relativePath, [char]9, '<deleted>'))
        }
    }

    if ($entries.Count -eq 0) {
        return $null
    }

    $fingerprintText = [string]::Join([Environment]::NewLine, $entries)
    $payload = [Text.UTF8Encoding]::new($false).GetBytes($fingerprintText)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return -join ($sha256.ComputeHash($payload) | ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-RuntimeBuildInputPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root
    )

    $paths = @(
        & git -C $Root ls-files --cached --others --exclude-standard --
    )
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate canonical runtime build inputs.'
    }

    return @(
        $paths |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                (Test-RuntimeBuildInput -RelativePath $_)
            } |
            ForEach-Object { $_.Replace('\', '/') } |
            Sort-Object -Unique
    )
}

function Get-RuntimeBuildInputFingerprint {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root,

        [Parameter(Mandatory = $true)]
        [string[]] $RelativePaths
    )

    $entries = New-Object System.Collections.Generic.List[string]
    foreach ($relativePath in $RelativePaths) {
        $nativeRelativePath = $relativePath.Replace(
            '/',
            [IO.Path]::DirectorySeparatorChar)
        $fullPath = Join-Path $Root $nativeRelativePath
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            $fileHash = (
                Get-FileHash -LiteralPath $fullPath -Algorithm SHA256
            ).Hash.ToLowerInvariant()
            $entries.Add(
                [string]::Concat(
                    $relativePath,
                    [char]9,
                    $fileHash))
        }
        elseif (Test-Path -LiteralPath $fullPath) {
            throw "Runtime build input is not a regular file: $relativePath"
        }
        else {
            $entries.Add(
                [string]::Concat(
                    $relativePath,
                    [char]9,
                    '<deleted>'))
        }
    }

    $fingerprintText = [string]::Join(
        [Environment]::NewLine,
        $entries)
    $payload = [Text.UTF8Encoding]::new($false).GetBytes(
        $fingerprintText)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        return -join (
            $hasher.ComputeHash($payload) |
                ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $hasher.Dispose()
    }
}

function Get-GitHeadObjectId {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root
    )

    $value = (& git -C $Root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or
        [string]::IsNullOrWhiteSpace($value)) {
        throw 'Unable to resolve canonical source HEAD object id.'
    }

    return $value
}

function Get-GitIndexTreeId {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root
    )

    $value = (& git -C $Root write-tree).Trim()
    if ($LASTEXITCODE -ne 0 -or
        [string]::IsNullOrWhiteSpace($value)) {
        throw 'Unable to resolve canonical source index tree identity.'
    }

    return $value
}

function New-RuntimeBuildSourceSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root,

        [Parameter(Mandatory = $true)]
        [string] $Destination
    )

    $head = Get-GitHeadObjectId -Root $Root
    $indexTree = Get-GitIndexTreeId -Root $Root
    $paths = @(Get-RuntimeBuildInputPaths -Root $Root)
    $fingerprint = Get-RuntimeBuildInputFingerprint -Root $Root -RelativePaths $paths

    Remove-Item -LiteralPath $Destination -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    foreach ($relativePath in $paths) {
        $nativeRelativePath = $relativePath.Replace(
            '/',
            [IO.Path]::DirectorySeparatorChar)
        $sourcePath = Join-Path $Root $nativeRelativePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            continue
        }

        $destinationPath = Join-Path $Destination $nativeRelativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    }

    $snapshotFingerprint = Get-RuntimeBuildInputFingerprint -Root $Destination -RelativePaths $paths
    $currentHead = Get-GitHeadObjectId -Root $Root
    $currentIndexTree = Get-GitIndexTreeId -Root $Root
    $currentPaths = @(Get-RuntimeBuildInputPaths -Root $Root)
    $currentFingerprint = Get-RuntimeBuildInputFingerprint -Root $Root -RelativePaths $currentPaths

    if (-not [string]::Equals(
            $head,
            $currentHead,
            [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            $indexTree,
            $currentIndexTree,
            [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            $fingerprint,
            $currentFingerprint,
            [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            $fingerprint,
            $snapshotFingerprint,
            [StringComparison]::Ordinal)) {
        throw 'Canonical runtime source changed while the immutable build snapshot was being captured. Re-run the build from a stable source state.'
    }

    return [pscustomobject]@{
        Root = $Destination
        HeadCommit = $head
        IndexTree = $indexTree
        RuntimeInputsSha256 = $fingerprint
        FileCount = $paths.Count
    }
}

function Assert-RuntimeBuildInputsUnchanged {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root,

        [AllowNull()]
        [string] $ExpectedFingerprint,

        [Parameter(Mandatory = $true)]
        [string] $Stage
    )

    $currentFingerprint = Get-WorkingTreeFingerprint -Root $Root
    $expected = if ($null -eq $ExpectedFingerprint) { '' } else { $ExpectedFingerprint }
    $current = if ($null -eq $currentFingerprint) { '' } else { $currentFingerprint }
    if (-not [string]::Equals(
            $expected,
            $current,
            [StringComparison]::Ordinal)) {
        throw "Runtime build inputs changed during canonical installer build at stage '$Stage'. ExpectedFingerprint='$expected' CurrentFingerprint='$current'. Re-run the build from a stable source snapshot."
    }
}

function Get-ProjectAssetsPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectPath,

        [Parameter(Mandatory = $true)]
        [string] $ArtifactsPath
    )

    $projectName = [IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    return Join-Path $ArtifactsPath ('obj\' + $projectName + '\project.assets.json')
}

function Invoke-CanonicalDependencyRestore {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectPath,

        [Parameter(Mandatory = $true)]
        [string] $Rid,

        [Parameter(Mandatory = $true)]
        [string] $ArtifactsPath
    )

    $restoreArgs = @(
        'restore',
        $ProjectPath,
        '-r',$Rid,
        '-p:SelfContained=true',
        '--artifacts-path',$ArtifactsPath
    )
    & dotnet @restoreArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Dependency restore failed for '$ProjectPath': $LASTEXITCODE"
    }
}

function New-DependencyAssetsSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectPath,

        [Parameter(Mandatory = $true)]
        [string] $DestinationPath,

        [Parameter(Mandatory = $true)]
        [string] $ArtifactsPath
    )

    $sourcePath = Get-ProjectAssetsPath -ProjectPath $ProjectPath -ArtifactsPath $ArtifactsPath
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "NuGet assets manifest is missing after restore: $sourcePath"
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $DestinationPath) -Force | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $DestinationPath -Force

    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $snapshotHash = (Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not [string]::Equals($sourceHash, $snapshotHash, [StringComparison]::Ordinal)) {
        throw "NuGet assets snapshot hash mismatch for '$ProjectPath'."
    }

    return [pscustomobject]@{
        ProjectPath = $ProjectPath
        SourcePath = $sourcePath
        SnapshotPath = $DestinationPath
        Sha256 = $snapshotHash
    }
}

function Assert-DependencyAssetsUnchanged {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Snapshot,

        [Parameter(Mandatory = $true)]
        [string] $Stage
    )

    if (-not (Test-Path -LiteralPath $Snapshot.SourcePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $Snapshot.SnapshotPath -PathType Leaf)) {
        throw "Dependency assets disappeared during canonical build at stage '$Stage'."
    }

    $liveHash = (Get-FileHash -LiteralPath $Snapshot.SourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $snapshotHash = (Get-FileHash -LiteralPath $Snapshot.SnapshotPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not [string]::Equals($liveHash, [string]$Snapshot.Sha256, [StringComparison]::Ordinal) -or
        -not [string]::Equals($snapshotHash, [string]$Snapshot.Sha256, [StringComparison]::Ordinal)) {
        throw "Dependency assets changed during canonical build at stage '$Stage' for '$($Snapshot.ProjectPath)'. Re-run the build."
    }
}

function Install-AstGrepPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Rid,

        [Parameter(Mandatory = $true)]
        [string] $DestinationRoot,

        [Parameter(Mandatory = $true)]
        [string] $StagingRoot
    )

    $packageName = switch ($Rid) {
        'win-x64' { '@ast-grep/cli-win32-x64-msvc' }
        'win-arm64' { '@ast-grep/cli-win32-arm64-msvc' }
        'win-x86' { '@ast-grep/cli-win32-ia32-msvc' }
        default { throw "No official ast-grep Windows package mapping exists for RuntimeIdentifier '$Rid'." }
    }

    $npmCommand = Get-Command npm.cmd -ErrorAction SilentlyContinue
    if ($null -eq $npmCommand) {
        $npmCommand = Get-Command npm -ErrorAction SilentlyContinue
    }
    if ($null -eq $npmCommand) {
        throw 'npm is required to resolve the current official ast-grep platform package.'
    }
    $registry = 'https://registry.npmjs.org/'

    # Resolve all provenance fields from the same registry response.
    $metadataJson = (& $npmCommand.Source view ($packageName + '@latest') --json "--registry=$registry" | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($metadataJson)) {
        throw "Unable to resolve ast-grep npm metadata for $packageName."
    }
    $metadataItems = @($metadataJson | ConvertFrom-Json)
    if ($metadataItems.Count -ne 1) {
        throw "Unexpected ast-grep package metadata cardinality: $($metadataItems.Count)."
    }
    $metadata = $metadataItems[0]
    if ($null -eq $metadata -or
        -not [string]::Equals([string]$metadata.name, $packageName, [StringComparison]::Ordinal)) {
        throw 'Unexpected ast-grep package metadata identity or shape.'
    }
    $version = [string]$metadata.version
    $license = [string]$metadata.license
    $integrity = [string]$metadata.dist.integrity

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'ast-grep latest version metadata is empty.'
    }
    if (-not [string]::Equals($license, 'MIT', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected ast-grep license '$license'."
    }
    if (-not $integrity.StartsWith('sha512-', [StringComparison]::Ordinal)) {
        throw 'ast-grep package metadata does not provide SHA-512 SRI integrity.'
    }

    Remove-Item -LiteralPath $StagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null
    $stagingPackageJson = Join-Path $StagingRoot 'package.json'
    [IO.File]::WriteAllText(
        $stagingPackageJson,
        '{"name":"talvora-ast-grep-stage","private":true}',
        [Text.UTF8Encoding]::new($false))

    Push-Location $StagingRoot
    try {
        $installOutput = @(& $npmCommand.Source install --ignore-scripts --no-audit --no-fund --save-exact "--registry=$registry" ($packageName + '@' + $version) 2>&1)
        $installExitCode = $LASTEXITCODE
        foreach ($line in $installOutput) {
            Write-Host ([string]$line)
        }
        if ($installExitCode -ne 0) {
            throw "ast-grep platform package install failed: $installExitCode"
        }
    }
    finally {
        Pop-Location
    }

    $packageRelative = $packageName -replace '/', [IO.Path]::DirectorySeparatorChar
    $sourceExecutable = Join-Path $StagingRoot ('node_modules\' + $packageRelative + '\ast-grep.exe')
    if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
        throw "ast-grep platform package did not contain ast-grep.exe: $sourceExecutable"
    }

    $versionOutput = (& $sourceExecutable --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or
        -not [string]::Equals($versionOutput, ('ast-grep ' + $version), [StringComparison]::Ordinal)) {
        throw "ast-grep executable version verification failed. Expected='ast-grep $version' Actual='$versionOutput'"
    }

    Remove-Item -LiteralPath $DestinationRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
    $destinationExecutable = Join-Path $DestinationRoot 'ast-grep.exe'
    Copy-Item -LiteralPath $sourceExecutable -Destination $destinationExecutable -Force
    $executableSha256 = (Get-FileHash -LiteralPath $destinationExecutable -Algorithm SHA256).Hash

    $provenance = [ordered]@{
        packageName = $packageName
        version = $version
        license = $license
        integrity = $integrity
        executableSha256 = $executableSha256
        registry = $registry
        resolvedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    $provenancePath = Join-Path $DestinationRoot 'provenance.json'
    [IO.File]::WriteAllText(
        $provenancePath,
        ($provenance | ConvertTo-Json -Depth 4),
        [Text.UTF8Encoding]::new($false))

    return [pscustomobject]@{
        packageName = $packageName
        version = $version
        license = $license
        integrity = $integrity
        executableSha256 = $executableSha256
    }
}

$BuildMutexName = 'Global\Talvora.BuildWindowsInstaller.v2'
$BuildMutex = [Threading.Mutex]::new($false, $BuildMutexName)
$BuildMutexOwned = $false

try {
    try {
        $BuildMutexOwned = $BuildMutex.WaitOne(0)
    }
    catch [Threading.AbandonedMutexException] {
        $BuildMutexOwned = $true
    }

    if (-not $BuildMutexOwned) {
        throw "Another Talvora Windows installer build is already running."
    }
}
catch {
    $BuildMutex.Dispose()
    throw
}

try {
Remove-Item $ArtifactsRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $PayloadZip -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Path $ArtifactsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $ServicePayload -Force | Out-Null
New-Item -ItemType Directory -Path $TrayPayload -Force | Out-Null
$SourceHeadCommit = Get-GitHeadObjectId -Root $RepoRoot
$SourceCommit = $SourceHeadCommit

$SourceStatus = @(& git -C $RepoRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to resolve local Talvora working-tree state.'
}
$WorkingTreeFingerprint = Get-WorkingTreeFingerprint -Root $RepoRoot
if (-not [string]::IsNullOrWhiteSpace($WorkingTreeFingerprint)) {
    $SourceCommit += '-dirty-' + $WorkingTreeFingerprint.Substring(0, 12)
}

$SourceBuildSnapshot = New-RuntimeBuildSourceSnapshot -Root $RepoRoot -Destination $SourceSnapshotRoot
if (-not [string]::Equals(
        $SourceHeadCommit,
        $SourceBuildSnapshot.HeadCommit,
        [StringComparison]::Ordinal)) {
    throw 'Canonical source HEAD changed before immutable source snapshot capture completed.'
}

Write-Host 'Restoring canonical dependency graphs...' -ForegroundColor Cyan
Invoke-CanonicalDependencyRestore -ProjectPath $ServiceProject -Rid $RuntimeIdentifier -ArtifactsPath $DependencyArtifactsRoot
Invoke-CanonicalDependencyRestore -ProjectPath $TrayProject -Rid $RuntimeIdentifier -ArtifactsPath $DependencyArtifactsRoot
$ServiceDependencySnapshot = New-DependencyAssetsSnapshot -ProjectPath $ServiceProject -DestinationPath $ServiceAssetsSnapshotPath -ArtifactsPath $DependencyArtifactsRoot
$TrayDependencySnapshot = New-DependencyAssetsSnapshot -ProjectPath $TrayProject -DestinationPath $TrayAssetsSnapshotPath -ArtifactsPath $DependencyArtifactsRoot
Assert-RuntimeBuildInputsUnchanged -Root $RepoRoot -ExpectedFingerprint $WorkingTreeFingerprint -Stage 'dependency-restore'

Write-Host 'Publishing Talvora service...' -ForegroundColor Cyan
$serviceArgs = @(
    'publish',
    $ServiceProject,
    '-c','Release',
    '-r',$RuntimeIdentifier,
    '--self-contained','true',
    '--artifacts-path',$DependencyArtifactsRoot,
    '--no-restore',
    '-o',$ServicePayload
)
& dotnet @serviceArgs
if ($LASTEXITCODE -ne 0) {
    throw "Talvora service publish failed: $LASTEXITCODE"
}
Assert-DependencyAssetsUnchanged -Snapshot $ServiceDependencySnapshot -Stage 'service-publish'
Assert-DependencyAssetsUnchanged -Snapshot $TrayDependencySnapshot -Stage 'service-publish'
Assert-RuntimeBuildInputsUnchanged -Root $RepoRoot -ExpectedFingerprint $WorkingTreeFingerprint -Stage 'service-publish'


Write-Host 'Publishing Talvora tray...' -ForegroundColor Cyan
$trayArgs = @(
    'publish',
    $TrayProject,
    '-c','Release',
    '-r',$RuntimeIdentifier,
    '--self-contained','true',
    '--artifacts-path',$DependencyArtifactsRoot,
    '--no-restore',
    '-o',$TrayPayload
)
& dotnet @trayArgs
if ($LASTEXITCODE -ne 0) {
    throw "Talvora tray publish failed: $LASTEXITCODE"
}
Assert-DependencyAssetsUnchanged -Snapshot $ServiceDependencySnapshot -Stage 'tray-publish'
Assert-DependencyAssetsUnchanged -Snapshot $TrayDependencySnapshot -Stage 'tray-publish'
Assert-RuntimeBuildInputsUnchanged -Root $RepoRoot -ExpectedFingerprint $WorkingTreeFingerprint -Stage 'tray-publish'


Remove-Item (Join-Path $ServicePayload '*.pdb') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $TrayPayload '*.pdb') -Force -ErrorAction SilentlyContinue

Write-Host 'Resolving and vendoring ast-grep structural toolchain...' -ForegroundColor Cyan
$AstGrepPayloadRoot = Join-Path $ServicePayload 'tools\ast-grep'
$AstGrepStagingRoot = Join-Path $WorkRoot 'ast-grep-package'
$AstGrepProvenance = Install-AstGrepPayload -Rid $RuntimeIdentifier -DestinationRoot $AstGrepPayloadRoot -StagingRoot $AstGrepStagingRoot
Assert-RuntimeBuildInputsUnchanged -Root $RepoRoot -ExpectedFingerprint $WorkingTreeFingerprint -Stage 'external-toolchain-vendor'


$ServicePayloadManifest = @(
    Get-PayloadFileManifest -Root $ServicePayload -ArchivePrefix 'Service'
)
Assert-RequiredPayloadFiles -Files $ServicePayloadManifest -RequiredArchivePaths @(
    'Service/Talvora.exe',
    'Service/Talvora.dll',
    'Service/tools/ast-grep/ast-grep.exe',
    'Service/tools/ast-grep/provenance.json'
)

$TrayPayloadManifest = @(
    Get-PayloadFileManifest -Root $TrayPayload -ArchivePrefix 'Tray'
)
Assert-RequiredPayloadFiles -Files $TrayPayloadManifest -RequiredArchivePaths @('Tray/Talvora.Tray.exe')

$ResolvedDependenciesFile = Join-Path $PayloadRoot 'resolved-dependencies.json'

function Get-ResolvedPackageManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string] $AssetsPath
    )

    if (-not (Test-Path -LiteralPath $AssetsPath -PathType Leaf)) {
        throw "Immutable NuGet assets snapshot is missing: $AssetsPath"
    }

    $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
    $direct = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)

    foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
        foreach ($dependency in $framework.Value.dependencies.PSObject.Properties) {
            [void]$direct.Add([string]$dependency.Name)
        }
    }

    $resolved = foreach ($library in $assets.libraries.PSObject.Properties) {
        if ([string]$library.Value.type -ne 'package') {
            continue
        }

        $identity = [string]$library.Name
        $separator = $identity.LastIndexOf('/')
        if ($separator -le 0 -or $separator -ge $identity.Length - 1) {
            continue
        }

        $name = $identity.Substring(0, $separator)
        $version = $identity.Substring($separator + 1)

        [pscustomobject]@{
            name = $name
            version = $version
            direct = $direct.Contains($name)
        }
    }

    return @($resolved | Sort-Object name, version)
}

function Get-PublishedPackageManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DepsPath
    )

    if (-not (Test-Path -LiteralPath $DepsPath -PathType Leaf)) {
        throw "Published dependency manifest is missing: $DepsPath"
    }

    $deps = Get-Content -LiteralPath $DepsPath -Raw | ConvertFrom-Json
    if ($null -eq $deps.libraries) {
        throw "Published dependency manifest does not contain libraries: $DepsPath"
    }

    $resolved = foreach ($library in $deps.libraries.PSObject.Properties) {
        if ([string]$library.Value.type -ne 'package') {
            continue
        }

        $identity = [string]$library.Name
        $separator = $identity.LastIndexOf('/')
        if ($separator -le 0 -or $separator -ge $identity.Length - 1) {
            continue
        }

        [pscustomobject]@{
            name = $identity.Substring(0, $separator)
            version = $identity.Substring($separator + 1)
        }
    }

    return @($resolved | Sort-Object name, version)
}

function Get-SingleFilePublishDepsPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ArtifactsPath,

        [Parameter(Mandatory = $true)]
        [string] $ProjectName,

        [Parameter(Mandatory = $true)]
        [string] $FileName
    )

    $binRoot = Join-Path $ArtifactsPath ('bin\' + $ProjectName)
    if (-not (Test-Path -LiteralPath $binRoot -PathType Container)) {
        throw "Published single-file build output directory is missing: $binRoot"
    }

    $matches = @(
        Get-ChildItem -LiteralPath $binRoot -Filter $FileName -File -Recurse -Force
    )
    if ($matches.Count -ne 1) {
        throw "Expected exactly one published dependency manifest '$FileName' beneath '$binRoot', found $($matches.Count)."
    }

    return [string]$matches[0].FullName
}

function Assert-PublishedPackagesMatchResolvedAssets {
    param(
        [Parameter(Mandatory = $true)]
        [object[]] $ResolvedPackages,

        [Parameter(Mandatory = $true)]
        [object[]] $PublishedPackages,

        [Parameter(Mandatory = $true)]
        [string] $ProjectName
    )

    $resolvedIdentities = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($package in $ResolvedPackages) {
        [void]$resolvedIdentities.Add(([string]$package.name + '/' + [string]$package.version))
    }

    foreach ($package in $PublishedPackages) {
        $identity = [string]$package.name + '/' + [string]$package.version
        if (-not $resolvedIdentities.Contains($identity)) {
            throw "Published runtime dependency '$identity' for '$ProjectName' is absent from the immutable restore graph."
        }
    }
}

Assert-DependencyAssetsUnchanged -Snapshot $ServiceDependencySnapshot -Stage 'dependency-provenance'
Assert-DependencyAssetsUnchanged -Snapshot $TrayDependencySnapshot -Stage 'dependency-provenance'
$ServiceResolvedPackages = @(Get-ResolvedPackageManifest -AssetsPath $ServiceDependencySnapshot.SnapshotPath)
$TrayResolvedPackages = @(Get-ResolvedPackageManifest -AssetsPath $TrayDependencySnapshot.SnapshotPath)
$ServicePublishedDepsPath = Join-Path $ServicePayload 'Talvora.deps.json'
$TrayPublishedDepsPath = Get-SingleFilePublishDepsPath -ArtifactsPath $DependencyArtifactsRoot -ProjectName 'Talvora.Tray' -FileName 'Talvora.Tray.deps.json'
$ServicePublishedPackages = @(Get-PublishedPackageManifest -DepsPath $ServicePublishedDepsPath)
$TrayPublishedPackages = @(Get-PublishedPackageManifest -DepsPath $TrayPublishedDepsPath)
Assert-PublishedPackagesMatchResolvedAssets -ResolvedPackages $ServiceResolvedPackages -PublishedPackages $ServicePublishedPackages -ProjectName 'Talvora'
Assert-PublishedPackagesMatchResolvedAssets -ResolvedPackages $TrayResolvedPackages -PublishedPackages $TrayPublishedPackages -ProjectName 'Talvora.Tray'

$dotnetSdkVersion = (& dotnet --version | Select-Object -First 1).Trim()
if ([string]::IsNullOrWhiteSpace($dotnetSdkVersion)) {
    throw 'Unable to resolve dotnet SDK version for dependency provenance.'
}

$dependencyProvenance = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    dotnetSdkVersion = $dotnetSdkVersion
    externalToolchains = @(
        [ordered]@{
            name = 'ast-grep'
            packageName = $AstGrepProvenance.packageName
            version = $AstGrepProvenance.version
            license = $AstGrepProvenance.license
            integrity = $AstGrepProvenance.integrity
            executableSha256 = $AstGrepProvenance.executableSha256
        }
    )
    projects = @(
        [ordered]@{
            name = 'Talvora'
            project = 'src/Talvora/Talvora.csproj'
            assetsSha256 = [string]$ServiceDependencySnapshot.Sha256
            publishedDepsSha256 = (Get-FileHash -LiteralPath $ServicePublishedDepsPath -Algorithm SHA256).Hash.ToLowerInvariant()
            packages = $ServiceResolvedPackages
            publishedRuntimePackages = $ServicePublishedPackages
        },
        [ordered]@{
            name = 'Talvora.Tray'
            project = 'src/Talvora.Tray/Talvora.Tray.csproj'
            assetsSha256 = [string]$TrayDependencySnapshot.Sha256
            publishedDepsSha256 = (Get-FileHash -LiteralPath $TrayPublishedDepsPath -Algorithm SHA256).Hash.ToLowerInvariant()
            packages = $TrayResolvedPackages
            publishedRuntimePackages = $TrayPublishedPackages
        }
    )
}

[IO.File]::WriteAllText(
    $ResolvedDependenciesFile,
    ($dependencyProvenance | ConvertTo-Json -Depth 12),
    [Text.UTF8Encoding]::new($false))

$ResolvedDependenciesManifest = [pscustomobject]@{
    FullPath = $ResolvedDependenciesFile
    ArchivePath = 'resolved-dependencies.json'
    Length = [long](Get-Item -LiteralPath $ResolvedDependenciesFile).Length
}

$SourceCommitFile = Join-Path $PayloadRoot 'source-commit.txt'
[IO.File]::WriteAllText(
    $SourceCommitFile,
    $SourceCommit + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

$SourceCommitManifest = [pscustomobject]@{
    FullPath = $SourceCommitFile
    ArchivePath = 'source-commit.txt'
    Length = [long](Get-Item -LiteralPath $SourceCommitFile).Length
}

$SourceSnapshotFile = Join-Path $PayloadRoot 'source-snapshot.json'
$SourceSnapshotProvenance = [ordered]@{
    schemaVersion = 1
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    headCommit = $SourceBuildSnapshot.HeadCommit
    indexTree = $SourceBuildSnapshot.IndexTree
    runtimeInputsSha256 = $SourceBuildSnapshot.RuntimeInputsSha256
    runtimeInputFileCount = $SourceBuildSnapshot.FileCount
    workingTreeFingerprint = $WorkingTreeFingerprint
}
[IO.File]::WriteAllText(
    $SourceSnapshotFile,
    ($SourceSnapshotProvenance | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

$SourceSnapshotManifest = [pscustomobject]@{
    FullPath = $SourceSnapshotFile
    ArchivePath = 'source-snapshot.json'
    Length = [long](Get-Item -LiteralPath $SourceSnapshotFile).Length
}

$PayloadManifest = @(
    $ServicePayloadManifest
    $TrayPayloadManifest
    $ResolvedDependenciesManifest
    $SourceCommitManifest
    $SourceSnapshotManifest
)

Write-Host 'Creating embedded installer payload...' -ForegroundColor Cyan
New-VerifiedPayloadArchive -Files $PayloadManifest -Destination $PayloadZip -Attempts 5

try {
    Write-Host 'Publishing Talvora Setup EXE...' -ForegroundColor Cyan
    $installerArgs = @(
        'publish',
        $InstallerProject,
        '-c','Release',
        '-r',$RuntimeIdentifier,
        '--self-contained','true',
        '-o',$ArtifactsRoot
    )
    & dotnet @installerArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Talvora installer publish failed: $LASTEXITCODE"
    }
    Assert-RuntimeBuildInputsUnchanged -Root $RepoRoot -ExpectedFingerprint $WorkingTreeFingerprint -Stage 'installer-publish'
}
finally {
    Remove-Item $PayloadZip -Force -ErrorAction SilentlyContinue
}

$InstallerExe = Join-Path $ArtifactsRoot 'Talvora-Setup.exe'
if (-not (Test-Path -LiteralPath $InstallerExe -PathType Leaf)) {
    throw "Installer EXE was not produced: $InstallerExe"
}

$hash = (Get-FileHash -LiteralPath $InstallerExe -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $InstallerExe).Length

$InstallerManifest = Join-Path $ArtifactsRoot 'Talvora-Setup.manifest.json'
$InstallerManifestTemp = Join-Path $ArtifactsRoot (
    '.Talvora-Setup.manifest.' +
    [Guid]::NewGuid().ToString('N') +
    '.tmp')
$InstallerIdentity = [ordered]@{
    schemaVersion = 1
    product = 'Talvora Setup'
    installerFileName = [IO.Path]::GetFileName($InstallerExe)
    sizeBytes = [long]$size
    sha256 = $hash.ToLowerInvariant()
    sourceCommit = $SourceCommit
    sourceHeadCommit = $SourceBuildSnapshot.HeadCommit
    sourceIndexTree = $SourceBuildSnapshot.IndexTree
    runtimeInputsSha256 = $SourceBuildSnapshot.RuntimeInputsSha256
    runtimeIdentifier = $RuntimeIdentifier
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}

try {
    [IO.File]::WriteAllText(
        $InstallerManifestTemp,
        ($InstallerIdentity | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
    $manifestStream = [IO.File]::Open(
        $InstallerManifestTemp,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Write,
        [IO.FileShare]::Read)
    try {
        $manifestStream.Flush($true)
    }
    finally {
        $manifestStream.Dispose()
    }

    if (Test-Path -LiteralPath $InstallerManifest -PathType Leaf) {
        [IO.File]::Replace(
            $InstallerManifestTemp,
            $InstallerManifest,
            $null)
    }
    else {
        [IO.File]::Move(
            $InstallerManifestTemp,
            $InstallerManifest)
    }
}
finally {
    Remove-Item -LiteralPath $InstallerManifestTemp -Force -ErrorAction SilentlyContinue
}

[pscustomobject]@{
    Product = 'Talvora Setup'
    SourceCommit = $SourceCommit
    RuntimeIdentifier = $RuntimeIdentifier
    Installer = $InstallerExe
    Manifest = $InstallerManifest
    SizeBytes = $size
    Sha256 = $hash
} | ConvertTo-Json -Compress

Remove-Item $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue}
finally {
    try {
        if ($BuildMutexOwned) {
            $BuildMutex.ReleaseMutex()
        }
    }
    finally {
        $BuildMutex.Dispose()
    }
}
