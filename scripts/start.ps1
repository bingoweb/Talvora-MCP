[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$dotnetPath = (& (Join-Path $PSScriptRoot 'Resolve-DotNet.ps1') | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
    throw '.NET executable bulunamadı. Önce TALVORA-KUR.bat çalıştır.'
}

& $dotnetPath run --project (Join-Path $root 'src\Talvora.Host\Talvora.Host.csproj')
exit $LASTEXITCODE
