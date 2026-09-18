param(
    [string] $RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ArtifactsRoot = Join-Path $RepoRoot 'artifacts\installer'
$WorkRoot = Join-Path $RepoRoot 'artifacts\installer-work'
$PayloadRoot = Join-Path $WorkRoot 'payload'
$ServicePayload = Join-Path $PayloadRoot 'Service'
$TrayPayload = Join-Path $PayloadRoot 'Tray'
$InstallerProject = Join-Path $RepoRoot 'src\Talvora.Installer\Talvora.Installer.csproj'
$PayloadZip = Join-Path $RepoRoot 'src\Talvora.Installer\Payload.zip'

function Assert-PayloadReadable {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root
    )

    $required = @(
        (Join-Path $Root 'Service\Talvora.exe'),
        (Join-Path $Root 'Tray\Talvora.Tray.exe'),
        (Join-Path $Root 'source-commit.txt')
    )

    foreach ($requiredPath in $required) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Required installer payload file is missing: $requiredPath"
        }
    }

    $files = @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)
    if ($files.Count -lt 3) {
        throw "Installer payload is unexpectedly small: $($files.Count) file(s)."
    }

    foreach ($file in $files) {
        $stream = $null
        try {
            $stream = [IO.File]::Open(
                $file.FullName,
                [IO.FileMode]::Open,
                [IO.FileAccess]::Read,
                [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)

            if ($stream.Length -ne $file.Length) {
                throw "Payload file length changed while validating: $($file.FullName)"
            }
        }
        finally {
            if ($null -ne $stream) {
                $stream.Dispose()
            }
        }
    }

    return $files.Count
}

function New-VerifiedPayloadArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Source,

        [Parameter(Mandatory = $true)]
        [string] $Destination,

        [int] $Attempts = 5
    )

    if ($Attempts -lt 1) {
        throw 'Archive retry count must be at least 1.'
    }

    $expectedFileCount = Assert-PayloadReadable -Root $Source

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue

        try {
            [IO.Compression.ZipFile]::CreateFromDirectory(
                $Source,
                $Destination,
                [IO.Compression.CompressionLevel]::Optimal,
                $false)

            if (-not (Test-Path -LiteralPath $Destination -PathType Leaf)) {
                throw "Payload archive was not created: $Destination"
            }

            $archive = [IO.Compression.ZipFile]::OpenRead($Destination)
            try {
                if ($archive.Entries.Count -ne $expectedFileCount) {
                    throw "Payload archive entry count mismatch. Expected=$expectedFileCount Actual=$($archive.Entries.Count)"
                }
            }
            finally {
                $archive.Dispose()
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
            $expectedFileCount = Assert-PayloadReadable -Root $Source
        }
    }

    throw "Unable to create verified payload archive after $Attempts attempts."
}


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

[IO.File]::WriteAllText(
    (Join-Path $PayloadRoot 'source-commit.txt'),
    $SourceCommit + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Write-Host 'Creating embedded installer payload...' -ForegroundColor Cyan
New-VerifiedPayloadArchive -Source $PayloadRoot -Destination $PayloadZip -Attempts 5

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

Remove-Item $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue