[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$batchPath = Join-Path $root 'TALVORA-SERVIS-KUR.bat'
$scriptPath = Join-Path $root 'scripts\Install-HostAutostart.ps1'

foreach ($path in @($batchPath, $scriptPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Talvora host autostart contract missing file: $path"
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
    throw "Talvora host autostart PowerShell parse failed: $($details -join ' | ')"
}

$scriptContent = [System.IO.File]::ReadAllText($scriptPath)
$requiredScriptFragments = @(
    'Resolve-DotNet.ps1',
    'dotnetPath publish',
    "LOCALAPPDATA",
    "Talvora\\Host",
    'New-ScheduledTaskAction',
    'New-ScheduledTaskTrigger',
    '-AtLogOn',
    'New-ScheduledTaskPrincipal',
    '-LogonType Interactive',
    '-RunLevel Limited',
    'Register-ScheduledTask',
    'RestartCount',
    'RestartInterval',
    'Start-ScheduledTask',
    '127.0.0.1:7676/health'
)

foreach ($fragment in $requiredScriptFragments) {
    if (-not $scriptContent.Contains($fragment, [System.StringComparison]::Ordinal)) {
        throw "Talvora host autostart contract missing fragment: $fragment"
    }
}

foreach ($forbiddenFragment in @('LocalSystem', '-RunLevel Highest')) {
    if ($scriptContent.Contains($forbiddenFragment, [System.StringComparison]::Ordinal)) {
        throw "Talvora host autostart must stay in the normal user context: $forbiddenFragment"
    }
}

$batchContent = [System.IO.File]::ReadAllText($batchPath)
foreach ($fragment in @('Resolve-PowerShell.cmd', 'Install-HostAutostart.ps1')) {
    if (-not $batchContent.Contains($fragment, [System.StringComparison]::Ordinal)) {
        throw "Talvora host autostart BAT contract missing fragment: $fragment"
    }
}

Write-Host 'Talvora host autostart launcher contract: GREEN' -ForegroundColor Green
