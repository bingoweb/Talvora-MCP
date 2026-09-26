$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$registryPath = Join-Path $repoRoot 'src\Talvora\Tools\RegistryTools.cs'
$text = Get-Content -LiteralPath $registryPath -Raw

if ($text -notmatch 'AbsoluteRegistryListResponseCharacters\s*=\s*\r?\n\s*ConfigAssetTools\.AbsoluteStructuredValueResponseCharacters') {
    throw 'Registry list response budget is not aligned with the transport-safe structured-value budget.'
}

if ($text -notmatch 'AbsoluteRegistryGetResponseCharacters\s*=\s*\r?\n\s*ConfigAssetTools\.AbsoluteStructuredValueResponseCharacters') {
    throw 'Registry get response budget is not aligned with the transport-safe structured-value budget.'
}

if ($text -notmatch 'EstimateValueCharacters\(value\)\s*>\s*\r?\n\s*AbsoluteRegistryGetResponseCharacters') {
    throw 'Registry get does not reject oversized values before structured response creation.'
}

if ($text -notmatch 'finite 4 MiB response-character budget' -or
    $text -notmatch '4 MiB list response-character budget') {
    throw 'Registry host-facing contracts do not expose the finite transport-safe response budget.'
}

Write-Output 'REGISTRY_RESPONSE_BOUNDS_SOURCE_GREEN'
