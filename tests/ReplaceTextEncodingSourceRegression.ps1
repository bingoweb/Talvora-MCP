$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$path = Join-Path $repoRoot 'src\Talvora\Tools\DeveloperTools.FileMutation.cs'
$text = Get-Content -LiteralPath $path -Raw

if ($text -notmatch 'SourceTextCodec\.ReadSnapshotAsync\(' -or
    $text -notmatch 'var document = snapshot\.Document') {
    throw 'talvora_replace_text does not read through the canonical encoding-aware source text codec.'
}

if ($text -notmatch 'CreateReplacementWriterEncoding\(') {
    throw 'talvora_replace_text does not select a writer encoding from the detected source encoding.'
}

foreach ($encodingName in @(
    'utf-8',
    'utf-8-bom',
    'utf-16le-bom',
    'utf-16be-bom',
    'utf-32le-bom',
    'utf-32be-bom'
)) {
    if ($text -notmatch [regex]::Escape('"' + $encodingName + '"')) {
        throw "talvora_replace_text is missing preservation mapping for $encodingName."
    }
}

if ($text -match 'File\.WriteAllTextAsync\(fullPath, updated, new UTF8Encoding') {
    throw 'talvora_replace_text still hard-codes BOM-less UTF-8 output.'
}

Write-Output 'REPLACE_TEXT_ENCODING_SOURCE_GREEN'
