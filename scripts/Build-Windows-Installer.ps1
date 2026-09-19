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
$PayloadRoot = Join-Path $WorkRoot 'payload'
$ServicePayload = Join-Path $PayloadRoot 'Service'
$TrayPayload = Join-Path $PayloadRoot 'Tray'
$InstallerProject = Join-Path $RepoRoot 'src\Talvora.Installer\Talvora.Installer.csproj'
$PayloadZip = Join-Path $RepoRoot 'src\Talvora.Installer\Payload.zip'

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

$SourceCommit = (& git -C $RepoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($SourceCommit)) {
    throw 'Unable to resolve local Talvora source commit.'
}

$SourceStatus = @(& git -C $RepoRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to resolve local Talvora working-tree state.'
}
if ($SourceStatus.Count -gt 0) {
    $WorkingTreeFingerprint = Get-WorkingTreeFingerprint -Root $RepoRoot
    if (-not [string]::IsNullOrWhiteSpace($WorkingTreeFingerprint)) {
        $SourceCommit += '-dirty-' + $WorkingTreeFingerprint.Substring(0, 12)
    }
}

Write-Host 'Publishing Talvora service...' -ForegroundColor Cyan
$serviceArgs = @(
    'publish',
    (Join-Path $RepoRoot 'src\Talvora\Talvora.csproj'),
    '-c','Release',
    '-r',$RuntimeIdentifier,
    '--self-contained','true',
    '-o',$ServicePayload
)
& dotnet @serviceArgs
if ($LASTEXITCODE -ne 0) {
    throw "Talvora service publish failed: $LASTEXITCODE"
}


Write-Host 'Publishing Talvora tray...' -ForegroundColor Cyan
$trayArgs = @(
    'publish',
    (Join-Path $RepoRoot 'src\Talvora.Tray\Talvora.Tray.csproj'),
    '-c','Release',
    '-r',$RuntimeIdentifier,
    '--self-contained','true',
    '-o',$TrayPayload
)
& dotnet @trayArgs
if ($LASTEXITCODE -ne 0) {
    throw "Talvora tray publish failed: $LASTEXITCODE"
}


Remove-Item (Join-Path $ServicePayload '*.pdb') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $TrayPayload '*.pdb') -Force -ErrorAction SilentlyContinue

$ServicePayloadManifest = @(
    Get-PayloadFileManifest -Root $ServicePayload -ArchivePrefix 'Service'
)
Assert-RequiredPayloadFiles -Files $ServicePayloadManifest -RequiredArchivePaths @('Service/Talvora.exe', 'Service/Talvora.dll')

$TrayPayloadManifest = @(
    Get-PayloadFileManifest -Root $TrayPayload -ArchivePrefix 'Tray'
)
Assert-RequiredPayloadFiles -Files $TrayPayloadManifest -RequiredArchivePaths @('Tray/Talvora.Tray.exe')

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

$PayloadManifest = @(
    $ServicePayloadManifest
    $TrayPayloadManifest
    $SourceCommitManifest
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

[pscustomobject]@{
    Product = 'Talvora Setup'
    SourceCommit = $SourceCommit
    RuntimeIdentifier = $RuntimeIdentifier
    Installer = $InstallerExe
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
