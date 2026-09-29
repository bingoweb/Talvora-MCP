$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$noticePath = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
$readmePath = Join-Path $repoRoot 'README.md'

if (-not (Test-Path -LiteralPath $noticePath -PathType Leaf)) {
    throw 'THIRD-PARTY-NOTICES.md is missing.'
}

$notice = [IO.File]::ReadAllText($noticePath, [Text.Encoding]::UTF8)
$readme = [IO.File]::ReadAllText($readmePath, [Text.Encoding]::UTF8)

$lockPaths = @(
    (Join-Path $repoRoot 'src\Talvora\packages.lock.json'),
    (Join-Path $repoRoot 'src\Talvora.Tray\packages.lock.json')
)

$lockedPackages = @{}
foreach ($lockPath in $lockPaths) {
    if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
        throw "Runtime lock file is missing: $lockPath"
    }

    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    foreach ($tfm in $lock.dependencies.PSObject.Properties) {
        foreach ($package in $tfm.Value.PSObject.Properties) {
            $resolvedProperty =
                $package.Value.PSObject.Properties['resolved']
            if ($null -eq $resolvedProperty) {
                continue
            }

            $resolved = [string] $resolvedProperty.Value
            if (-not [string]::IsNullOrWhiteSpace($resolved)) {
                $lockedPackages[$package.Name.ToLowerInvariant()] = [pscustomobject]@{
                    Id = $package.Name
                    Version = $resolved
                }
            }
        }
    }
}

$missing = @()
foreach ($package in ($lockedPackages.Values | Sort-Object Id)) {
    $linePattern =
        [regex]::Escape($package.Id) +
        '[^\r\n]{0,120}' +
        [regex]::Escape($package.Version)
    if (-not [regex]::IsMatch(
            $notice,
            $linePattern,
            [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        $missing += "$($package.Id) $($package.Version)"
    }
}

if ($missing.Count -gt 0) {
    throw "Third-party notice is missing locked runtime packages: $($missing -join ', ')"
}

$requiredNoticeContracts = @(
    'Microsoft.NETCore.App.Runtime.win-x64',
    'Microsoft.WindowsDesktop.App.Runtime.win-x64',
    'Microsoft.AspNetCore.App.Runtime.win-x64',
    'sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2',
    'Gitea MCP Server',
    'Caddy',
    'Penpot',
    'OpenAI Secure MCP Tunnel client',
    'Modal SDK / CLI',
    'Trademarks and project names'
)

foreach ($contract in $requiredNoticeContracts) {
    if ($notice.IndexOf($contract, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Third-party notice contract is missing: $contract"
    }
}

$requiredReadmeContracts = @(
    'Third-party software & license attribution',
    'THIRD-PARTY-NOTICES.md',
    '48 locked NuGet runtime packages'
)

foreach ($contract in $requiredReadmeContracts) {
    if ($readme.IndexOf($contract, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "README third-party attribution contract is missing: $contract"
    }
}

Write-Output "THIRD_PARTY_NOTICES_SOURCE_GREEN packages=$($lockedPackages.Count)"

