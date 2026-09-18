param(
    [string] $SourcePath = (Join-Path $PSScriptRoot '..\src\Talvora\RuntimeMetadata.cs')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw "RuntimeMetadata source not found: $SourcePath"
}

$text = [IO.File]::ReadAllText((Resolve-Path $SourcePath))

$hasLazyCache = $text -match 'static\s+readonly\s+Lazy<TalvoraRuntimeMetadata>'
$loadReturnsCachedValue = $text -match 'public\s+static\s+TalvoraRuntimeMetadata\s+Load\(\)\s*=>\s*[^;]*\.Value\s*;'
$hasDedicatedDiskLoader = $text -match 'private\s+static\s+TalvoraRuntimeMetadata\s+LoadFromDisk\s*\('

[pscustomobject]@{
    HasLazyCache = $hasLazyCache
    LoadReturnsCachedValue = $loadReturnsCachedValue
    HasDedicatedDiskLoader = $hasDedicatedDiskLoader
} | Format-List

if (-not $hasLazyCache -or -not $loadReturnsCachedValue -or -not $hasDedicatedDiskLoader) {
    throw 'Runtime metadata is not process-lifetime cached.'
}

Write-Output 'RUNTIME_METADATA_CACHE_GREEN'
