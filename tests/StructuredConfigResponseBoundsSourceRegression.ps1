$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$configAssetRoot = Join-Path $repoRoot 'src\Talvora\Tools\ConfigAssetTools.cs'
$jsonPath = Join-Path $repoRoot 'src\Talvora\Tools\ConfigAssetTools.Json.cs'
$sharedPath = Join-Path $repoRoot 'src\Talvora\Tools\StructuredConfigTools.Shared.cs'
$formatRoot = Join-Path $repoRoot 'src\Talvora\Tools\ConfigFormatTools.cs'
$xmlPath = Join-Path $repoRoot 'src\Talvora\Tools\ConfigFormatTools.Xml.cs'

$configAssetText = Get-Content -LiteralPath $configAssetRoot -Raw
$jsonText = Get-Content -LiteralPath $jsonPath -Raw
$sharedText = Get-Content -LiteralPath $sharedPath -Raw
$formatText = Get-Content -LiteralPath $formatRoot -Raw
$xmlText = Get-Content -LiteralPath $xmlPath -Raw

if ($configAssetText -notmatch 'AbsoluteStructuredValueResponseCharacters\s*=\s*\r?\n\s*AbsoluteTextResponseCharacters') {
    throw 'Structured config response budget is not bound to the finite text response budget.'
}

if ($jsonText -notmatch 'ToBoundedJson\(' -or
    $jsonText -notmatch 'AbsoluteStructuredValueResponseCharacters') {
    throw 'JSON get does not enforce the structured-value response budget.'
}

if ($sharedText -notmatch 'ConfigAssetTools\.ToBoundedJson\(' -or
    $sharedText -notmatch 'talvora_\{format\}_get') {
    throw 'YAML/TOML get response path does not enforce the structured-value response budget.'
}

if ($formatText -notmatch 'AbsoluteXmlQueryResponseCharacters\s*=\s*\r?\n\s*ConfigAssetTools\.AbsoluteStructuredValueResponseCharacters') {
    throw 'XML query response budget is not aligned with the structured-value transport budget.'
}

if ($xmlText -notmatch 'finite 4 MiB response-character budget' -or
    $xmlText -match '8 MiB response-character budget') {
    throw 'XML query host contract does not expose the 4 MiB transport-safe budget.'
}

Write-Output 'STRUCTURED_CONFIG_RESPONSE_BOUNDS_SOURCE_GREEN'
