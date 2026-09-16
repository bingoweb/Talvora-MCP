$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Ensure-Chocolatey {
    if (Get-Command choco.exe -ErrorAction SilentlyContinue) { return }

    Set-ExecutionPolicy Bypass -Scope Process -Force
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
    Invoke-Expression ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))

    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not (Get-Command choco.exe -ErrorAction SilentlyContinue)) {
        throw 'Chocolatey installation completed but choco.exe is not available in PATH.'
    }
}

function Ensure-ChocoPackage([string] $Package, [string] $Command) {
    if (Get-Command $Command -ErrorAction SilentlyContinue) { return }
    & choco.exe install $Package -y --no-progress
    if ($LASTEXITCODE -notin 0, 1641, 3010) { throw "Chocolatey failed installing $Package with exit code $LASTEXITCODE." }
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
}

Ensure-Chocolatey
Ensure-ChocoPackage 'dotnet-10.0-sdk' 'dotnet.exe'
Ensure-ChocoPackage 'git' 'git.exe'
Ensure-ChocoPackage 'powershell-core' 'pwsh.exe'

$root = Split-Path -Parent $PSScriptRoot
$gateway = Join-Path $root 'src\Talvora.Gateway\Talvora.Gateway.csproj'

Push-Location $root
try {
    & dotnet.exe restore $gateway
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    & dotnet.exe build $gateway -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    Write-Host 'Talvora dependencies and Gateway build are ready.' -ForegroundColor Green
    Write-Host 'Start with: .\scripts\start-gateway.ps1' -ForegroundColor Cyan
}
finally {
    Pop-Location
}
