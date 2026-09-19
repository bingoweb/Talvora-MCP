[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $RuntimeIdentifier = 'win-x64',
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Refresh-ProcessPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($machine, $user) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.TrimEnd(';') }
    $env:Path = $parts -join ';'
}

function Ensure-Chocolatey {
    if (Get-Command choco.exe -ErrorAction SilentlyContinue) {
        return
    }

    Set-ExecutionPolicy Bypass -Scope Process -Force
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor 3072

    Invoke-Expression (
        (New-Object Net.WebClient).DownloadString(
            'https://community.chocolatey.org/install.ps1'))

    Refresh-ProcessPath
}

function Ensure-BuildDependencies {
    Ensure-Chocolatey

    if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
        & choco.exe install git -y --no-progress
        if ($LASTEXITCODE -ne 0) {
            throw "Chocolatey failed to install Git: $LASTEXITCODE"
        }
    }

    if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
        & choco.exe install dotnet-10.0-sdk -y --no-progress
        if ($LASTEXITCODE -ne 0) {
            throw "Chocolatey failed to install .NET 10 SDK: $LASTEXITCODE"
        }
    }

    Refresh-ProcessPath
}

$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
$buildScript = Join-Path $RepoRoot 'scripts\Build-Windows-Installer.ps1'
$installer = Join-Path $RepoRoot 'artifacts\installer\Talvora-Setup.exe'

if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
    throw "Native installer build script not found: $buildScript"
}

if (-not (Test-Administrator)) {
    $powerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        ('"{0}"' -f $PSCommandPath),
        '-RepoRoot',
        ('"{0}"' -f $RepoRoot),
        '-RuntimeIdentifier',
        ('"{0}"' -f $RuntimeIdentifier)
    )
    if ($SkipBuild) {
        $arguments += '-SkipBuild'
    }

    $child = Start-Process -FilePath $powerShell -Verb RunAs -Wait -PassThru -ArgumentList $arguments
    exit $child.ExitCode
}

if (-not $SkipBuild) {
    Ensure-BuildDependencies

    Write-Host 'Building canonical Talvora native installer...' -ForegroundColor Cyan
    & $buildScript -RuntimeIdentifier $RuntimeIdentifier
    if ($LASTEXITCODE -ne 0) {
        throw "Build-Windows-Installer.ps1 failed: $LASTEXITCODE"
    }
}

if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Native Talvora installer was not produced: $installer"
}

Write-Host 'Running canonical Talvora native installer...' -ForegroundColor Cyan
& $installer --silent
$installerExitCode = $LASTEXITCODE

if ($installerExitCode -ne 0) {
    throw "Talvora native installer failed: $installerExitCode"
}

Write-Host ''
Write-Host 'TALVORA READY' -ForegroundColor Green
Write-Host "Installer: $installer"
