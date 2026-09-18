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
[IO.Compression.ZipFile]::CreateFromDirectory(
    $PayloadRoot,
    $PayloadZip,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

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