param(
    [string] $SourcePath = (Join-Path $PSScriptRoot '..\src\Talvora\Tools\KnowledgeTools.cs')
)

$ErrorActionPreference = 'Stop'
$text = [IO.File]::ReadAllText((Resolve-Path $SourcePath))

$avoidsRedundantFullPath = $text -notmatch 'fullPath\s*=\s*Path\.GetFullPath\(file\)'
$avoidsExistsProbe = $text -notmatch '!info\.Exists\s*\|\|\s*info\.Length'
$fetchChecksFileBudget =
    $text -match 'info\.Length\s*>\s*MaxSearchFileBytes'
$fetchChecksDecodedBudget =
    $text -match 'text\.Length\s*>\s*\r?\n\s*ConfigAssetTools\.AbsoluteTextResponseCharacters'
$fetchDirectsToBoundedReader =
    ($text | Select-String -Pattern 'Use talvora_read_text_range for larger documents' -AllMatches).Matches.Count -ge 2

[pscustomobject]@{
    AvoidsRedundantFullPath = $avoidsRedundantFullPath
    AvoidsExistsProbe = $avoidsExistsProbe
    FetchChecksFileBudget = $fetchChecksFileBudget
    FetchChecksDecodedBudget = $fetchChecksDecodedBudget
    FetchDirectsToBoundedReader = $fetchDirectsToBoundedReader
} | Format-List

if (-not $avoidsRedundantFullPath -or
    -not $avoidsExistsProbe -or
    -not $fetchChecksFileBudget -or
    -not $fetchChecksDecodedBudget -or
    -not $fetchDirectsToBoundedReader) {
    throw 'Knowledge search/fetch scan or response-bound contract regressed.'
}

Write-Output 'KNOWLEDGE_SEARCH_SCAN_GREEN'
