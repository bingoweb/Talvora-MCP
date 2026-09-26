$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$path = Join-Path $repoRoot 'src\Talvora\Tools\DeveloperTools.FileMutation.cs'
$text = Get-Content -LiteralPath $path -Raw

if ($text -notmatch 'backupPath\s*=\s*await\s+AtomicFile\.WriteAllTextAsync\(\s*fullPath,\s*updated,') {
    throw 'talvora_replace_text does not publish replacement writes through AtomicFile.'
}

if ($text -match 'await\s+File\.WriteAllTextAsync\(\s*fullPath,\s*updated,') {
    throw 'talvora_replace_text still contains an in-place File.WriteAllTextAsync publication path.'
}

if ($text -match 'File\.Copy\(fullPath, backupPath') {
    throw 'talvora_replace_text still manages backups separately from atomic publication.'
}

Write-Output 'REPLACE_TEXT_DURABILITY_SOURCE_GREEN'
