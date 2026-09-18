param(
    [string] $SourcePath = (Join-Path $PSScriptRoot '..\src\Talvora\Tools\FileTools.cs')
)

$ErrorActionPreference = 'Stop'

$text = [IO.File]::ReadAllText((Resolve-Path $SourcePath))

$usesMetadataEnumeration = $text -match 'DirectoryInfo\s*\(' -and
    $text -match 'EnumerateFileSystemInfos\s*\('
$listAvoidsPerEntryDirectoryProbe = $text -notmatch 'var\s+isDirectory\s*=\s*Directory\.Exists\(item\)'
$listAvoidsPerEntryFileInfoCreation = $text -notmatch 'var\s+info\s*=\s*isDirectory\s*\?\s*null\s*:\s*new\s+FileInfo\(item\)'

[pscustomobject]@{
    UsesMetadataEnumeration = $usesMetadataEnumeration
    AvoidsDirectoryExistsProbe = $listAvoidsPerEntryDirectoryProbe
    AvoidsFileInfoCreation = $listAvoidsPerEntryFileInfoCreation
} | Format-List

if (-not $usesMetadataEnumeration -or
    -not $listAvoidsPerEntryDirectoryProbe -or
    -not $listAvoidsPerEntryFileInfoCreation) {
    throw 'talvora_list still performs redundant per-entry metadata probes.'
}

Write-Output 'FILE_LIST_METADATA_GREEN'
