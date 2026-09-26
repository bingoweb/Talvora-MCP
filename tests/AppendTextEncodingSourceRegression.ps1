$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$path = Join-Path $repoRoot 'src\Talvora\Tools\ConfigAssetTools.Text.cs'
$text = Get-Content -LiteralPath $path -Raw

if ($text -notmatch 'SourceTextCodec\s*\.ReadEncodingDescriptor\(fullPath\)\s*\.Encoding') {
    throw 'talvora_append_text does not reuse the detected encoding for existing text files.'
}

if ($text -notmatch 'new UTF8Encoding\(\s*encoderShouldEmitUTF8Identifier:\s*false,\s*throwOnInvalidBytes:\s*true\)') {
    throw 'talvora_append_text no longer defaults new/empty files to strict BOM-less UTF-8.'
}

if ($text -notmatch 'new StreamWriter\(\s*stream,\s*appendEncoding,') {
    throw 'talvora_append_text is not writing with the selected append encoding.'
}

Write-Output 'APPEND_TEXT_ENCODING_SOURCE_GREEN'
