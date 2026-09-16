$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installer = Join-Path $PSScriptRoot '..\..\scripts\install-dev-from-github.ps1'
$content = Get-Content -Raw -LiteralPath $installer

$healthProbe = "Invoke-RestMethod -Uri 'http://127.0.0.1:7676/healthz'"
$startGateway = "Start-Process -FilePath 'dotnet.exe'"

$healthIndex = $content.IndexOf($healthProbe, [StringComparison]::Ordinal)
$startIndex = $content.IndexOf($startGateway, [StringComparison]::Ordinal)

if ($healthIndex -lt 0) {
    throw 'Installer must probe the Talvora health endpoint.'
}

if ($startIndex -lt 0) {
    throw 'Installer must contain the Gateway start operation.'
}

if ($healthIndex -gt $startIndex) {
    throw 'Installer must check whether Talvora is already healthy before starting another Gateway process.'
}

Write-Host 'Installer idempotency regression GREEN.' -ForegroundColor Green
