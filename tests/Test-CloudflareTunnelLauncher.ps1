[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$batchPath = Join-Path $root 'TALVORA-CLOUDFLARE-KUR.bat'
$scriptPath = Join-Path $root 'scripts\Install-CloudflareTunnel.ps1'

foreach ($path in @($batchPath, $scriptPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Cloudflare launcher contract missing file: $path"
    }
}

$tokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath,
    [ref]$tokens,
    [ref]$parseErrors
) | Out-Null

if ($parseErrors.Count -ne 0) {
    $details = $parseErrors | ForEach-Object { $_.Message }
    throw "Cloudflare PowerShell parse failed: $($details -join ' | ')"
}

$scriptContent = [System.IO.File]::ReadAllText($scriptPath)
$requiredScriptFragments = @(
    'TALVORA_CLOUDFLARE_TUNNEL_TOKEN',
    'https://api.github.com/repos/cloudflare/cloudflared/releases/latest',
    'cloudflared-windows-',
    'Get-FileHash',
    'service install',
    'Start-Process',
    '-Verb RunAs',
    '127.0.0.1:7676',
    "Get-Service -Name 'cloudflared'",
    'PROCESSOR_ARCHITECTURE'
)

foreach ($fragment in $requiredScriptFragments) {
    if (-not $scriptContent.Contains($fragment, [System.StringComparison]::Ordinal)) {
        throw "Cloudflare launcher contract missing fragment: $fragment"
    }
}

if ($scriptContent.Contains('RuntimeInformation]::OSArchitecture', [System.StringComparison]::Ordinal)) {
    throw 'Cloudflare launcher must not depend on RuntimeInformation.OSArchitecture because Windows PowerShell 5.1 does not expose it reliably.'
}

$batchContent = [System.IO.File]::ReadAllText($batchPath)
foreach ($fragment in @('Resolve-PowerShell.cmd', 'Install-CloudflareTunnel.ps1')) {
    if (-not $batchContent.Contains($fragment, [System.StringComparison]::Ordinal)) {
        throw "Cloudflare BAT launcher contract missing fragment: $fragment"
    }
}

Write-Host 'Talvora Cloudflare launcher contract: GREEN' -ForegroundColor Green
