$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$path = Join-Path $repoRoot 'src\Talvora\Tools\ConfigAssetTools.Text.cs'
$text = Get-Content -LiteralPath $path -Raw

if ($text -notmatch 'DetectAppendNewlineAsync\(') {
    throw 'talvora_append_text does not inspect the existing newline convention.'
}

if ($text -notmatch 'return character == ''\\n''\s*\? "\\r\\n"\s*:\s*"\\r"') {
    throw 'talvora_append_text does not distinguish CRLF from CR while probing.'
}

if ($text -notmatch 'else if \(character == ''\\n''\)\s*\{\s*return "\\n";') {
    throw 'talvora_append_text does not preserve LF-only files.'
}

if ($text -match 'writer\.WriteAsync\(Environment\.NewLine\.AsMemory\(\)') {
    throw 'talvora_append_text still hard-codes the platform newline for every append.'
}

Write-Output 'APPEND_TEXT_NEWLINE_SOURCE_GREEN'
