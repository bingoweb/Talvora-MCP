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
$scopedFetch = $text.Contains('ResolveFinalPath(') -and
    $text.Contains('IsWithinSearchRoot(') -and
    $text.Contains('talvora_read_text_range')
$activeUserRoots = $text.Contains('GetActiveInteractiveUser()') -and
    -not $text.Contains('Directory.GetDirectories(usersRoot)') -and
    $text.Contains('"Knowledge"')
$skipsCloudFiles = $text.Contains('IsCloudOnly(') -and
    $text.Contains('IsCloudReparseTag(') -and
    $text.Contains('0x00040000') -and $text.Contains('0x00400000')

[pscustomobject]@{
    AvoidsRedundantFullPath = $avoidsRedundantFullPath
    AvoidsExistsProbe = $avoidsExistsProbe
    FetchChecksFileBudget = $fetchChecksFileBudget
    FetchChecksDecodedBudget = $fetchChecksDecodedBudget
    FetchDirectsToBoundedReader = $fetchDirectsToBoundedReader
    ScopedFetch = $scopedFetch
    ActiveUserRootsOnly = $activeUserRoots
    SkipsCloudPlaceholders = $skipsCloudFiles
} | Format-List

if (-not $avoidsRedundantFullPath -or
    -not $avoidsExistsProbe -or
    -not $fetchChecksFileBudget -or
    -not $fetchChecksDecodedBudget -or
    -not $fetchDirectsToBoundedReader -or
    -not $scopedFetch -or
    -not $activeUserRoots -or
    -not $skipsCloudFiles) {
    throw 'Knowledge search/fetch scan or response-bound contract regressed.'
}

Write-Output 'KNOWLEDGE_SEARCH_SCAN_GREEN'
