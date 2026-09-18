param(
    [string] $SourcePath = (Join-Path $PSScriptRoot '..\src\Talvora\Tools\KnowledgeTools.cs')
)

$ErrorActionPreference = 'Stop'
$text = [IO.File]::ReadAllText((Resolve-Path $SourcePath))

$avoidsRedundantFullPath = $text -notmatch 'fullPath\s*=\s*Path\.GetFullPath\(file\)'
$avoidsExistsProbe = $text -notmatch '!info\.Exists\s*\|\|\s*info\.Length'

[pscustomobject]@{
    AvoidsRedundantFullPath = $avoidsRedundantFullPath
    AvoidsExistsProbe = $avoidsExistsProbe
} | Format-List

if (-not $avoidsRedundantFullPath -or -not $avoidsExistsProbe) {
    throw 'Knowledge search still performs redundant per-file path or existence probes.'
}

Write-Output 'KNOWLEDGE_SEARCH_SCAN_GREEN'
