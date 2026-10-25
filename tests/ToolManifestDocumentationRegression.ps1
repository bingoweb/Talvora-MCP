[CmdletBinding()]
param(
    [string] $ReadmePath,
    [string] $ArchitecturePath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ReadmePath) { $ReadmePath = Join-Path $repoRoot 'README.md' }
if (-not $ArchitecturePath) { $ArchitecturePath = Join-Path $repoRoot 'docs/ARCHITECTURE.md' }

# Compile the canonical sources even in a docs-only checkout without build outputs or a restored SDK project.
$globalUsings = Join-Path ([IO.Path]::GetTempPath()) ('Talvora.ToolCounts.' + [guid]::NewGuid().ToString('N') + '.cs')
try {
    [IO.File]::WriteAllText($globalUsings, 'global using System; global using System.Collections.Generic; global using System.Linq;')
    Add-Type -Path @(
        (Join-Path $repoRoot 'src/Talvora.Shared/TalvoraToolManifest.cs'),
        (Join-Path $repoRoot 'src/Talvora.Shared/TalvoraMcpToolSurfacePolicy.cs'),
        $globalUsings
    ) -CompilerOptions '/nullable:enable'
}
finally { [IO.File]::Delete($globalUsings) }
$full = [Talvora.Shared.TalvoraToolManifest]::Names.Count
$dev = [Talvora.Shared.TalvoraMcpToolSurfacePolicy]::GetToolNames(
    [Talvora.Shared.TalvoraMcpToolSurface]::Development).Count
$admin = [Talvora.Shared.TalvoraMcpToolSurfacePolicy]::GetToolNames(
    [Talvora.Shared.TalvoraMcpToolSurface]::Administration).Count
$shared = [Talvora.Shared.TalvoraMcpToolSurfacePolicy]::GetSharedToolNames().Count
$readme = [IO.File]::ReadAllText($ReadmePath)
$architecture = [IO.File]::ReadAllText($ArchitecturePath)

function Assert-DocumentCounts {
    param(
        [string] $Name,
        [string] $Text,
        [string] $Pattern,
        [int[]] $Expected
    )
    $matches = [regex]::Matches($Text, $Pattern)
    if ($matches.Count -ne 1) {
        throw "$Name must contain exactly one documented count block; found $($matches.Count)."
    }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        $actual = [int]$matches[0].Groups[$index + 1].Value
        if ($actual -ne $Expected[$index]) {
            throw "$Name count mismatch: actual=$actual; manifest=$($Expected[$index])."
        }
    }
}

# Check every count presentation, including badge, prose, diagrams and endpoint table.
Assert-DocumentCounts 'README badge text' $readme 'alt="MCP (\d+) tools"' @($full)
Assert-DocumentCounts 'README badge URL' $readme '/badge/MCP-(\d+)%20tools-' @($full)
Assert-DocumentCounts 'README summary' $readme '\*\*(\d+) total\*\*, \*\*(\d+) Dev\*\*, \*\*(\d+) Admin\*\*, with \*\*(\d+) explicitly shared\*\*' @($full, $dev, $admin, $shared)
Assert-DocumentCounts 'README focused diagram' $readme '(?m)^\s*(\d+) tools[ \t]+(\d+) tools[ \t]*\r?$' @($dev, $admin)
Assert-DocumentCounts 'README full diagram' $readme '(?m)^\s*(\d+) tools[ \t]+(\d+) tools[ \t]+(\d+) tools[ \t]*\r?$' @($full, $dev, $admin)
foreach ($entry in @(
    @{ Path = '/mcp'; Count = $full },
    @{ Path = '/mcp/dev'; Count = $dev },
    @{ Path = '/mcp/admin'; Count = $admin }
)) {
    $escaped = [regex]::Escape($entry.Path)
    Assert-DocumentCounts ("README " + $entry.Path) $readme (
        '(?m)^\| <code>' + $escaped + '</code> \|[^\r\n]*\*\*(\d+)\*\* \|\r?$') @($entry.Count)
    Assert-DocumentCounts ("ARCHITECTURE " + $entry.Path) $architecture (
        [regex]::Escape([string][char]96 + $entry.Path + [char]96) + ' \(\*\*(\d+) tools\*\*\)') @($entry.Count)
}
Assert-DocumentCounts 'README shared' $readme 'share \*\*(\d+) reviewed tools\*\*' @($shared)
Assert-DocumentCounts 'ARCHITECTURE complete surface' $architecture 'complete (\d+)-tool capability set' @($full)
Assert-DocumentCounts 'ARCHITECTURE shared' $architecture 'overlap on exactly (\d+) reviewed tools' @($shared)

Write-Host ("Manifest counts: Full={0}, Dev={1}, Admin={2}, Shared={3}" -f $full, $dev, $admin, $shared)
Write-Host 'TOOL_MANIFEST_DOCUMENTATION_GREEN'
