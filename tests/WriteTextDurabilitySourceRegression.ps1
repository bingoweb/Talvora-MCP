$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$path = Join-Path $repoRoot 'src\Talvora\Tools\FileTools.cs'
$text = Get-Content -LiteralPath $path -Raw

if ($text -notmatch 'AtomicFile\.WriteAllTextAsync\(') {
    throw 'talvora_write_text does not publish through AtomicFile.'
}

if ($text -match 'await\s+File\.WriteAllTextAsync\(\s*fullPath,\s*content') {
    throw 'talvora_write_text still publishes in-place through File.WriteAllTextAsync.'
}

if ($text -notmatch 'new UTF8Encoding\(\s*encoderShouldEmitUTF8Identifier:\s*false\)') {
    throw 'talvora_write_text no longer preserves its BOM-less UTF-8 compatibility contract.'
}

Write-Output 'WRITE_TEXT_DURABILITY_SOURCE_GREEN'
