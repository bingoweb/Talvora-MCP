$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$sharedAssembly = Join-Path $repoRoot 'src\Talvora.Shared\bin\Release\net10.0-windows\Talvora.Shared.dll'
if (-not (Test-Path -LiteralPath $sharedAssembly -PathType Leaf)) {
    throw "Talvora.Shared Release assembly is missing: $sharedAssembly"
}
Add-Type -Path $sharedAssembly

$fixtureRoot = Join-Path $env:TEMP ('TalvoraStorageBehavior-' + [guid]::NewGuid().ToString('N'))
$cutoff = [DateTimeOffset]::UtcNow.AddDays(-7)
$old = [DateTime]::UtcNow.AddDays(-10)

try {
    $structuralRoot = Join-Path $fixtureRoot 'Structural'
    $oldStructural = Join-Path $structuralRoot ([guid]::NewGuid().ToString('N'))
    $newStructural = Join-Path $structuralRoot ([guid]::NewGuid().ToString('N'))
    $foreignStructural = Join-Path $structuralRoot 'not-a-guid'
    foreach ($path in @($oldStructural, $newStructural, $foreignStructural)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $path 'payload.txt'), $path)
    }
    foreach ($path in @($oldStructural, $foreignStructural)) {
        [IO.File]::SetLastWriteTimeUtc((Join-Path $path 'payload.txt'), $old)
        [IO.Directory]::SetLastWriteTimeUtc($path, $old)
    }

    $structuralResult = [Talvora.Shared.TalvoraOwnedTempCleanup]::CleanupGuidDirectories(
        $structuralRoot,
        $cutoff,
        [Threading.CancellationToken]::None,
        $null)
    if ($structuralResult.DeletedEntries -ne 1 -or
        (Test-Path -LiteralPath $oldStructural) -or
        -not (Test-Path -LiteralPath $newStructural) -or
        -not (Test-Path -LiteralPath $foreignStructural)) {
        throw 'Structural child-level retention contract failed.'
    }

    $semanticRoot = Join-Path $fixtureRoot 'semantic-worker'
    New-Item -ItemType Directory -Path $semanticRoot -Force | Out-Null
    $oldSemantic = Join-Path $semanticRoot (([guid]::NewGuid().ToString('N')) + '.json')
    $newSemantic = Join-Path $semanticRoot (([guid]::NewGuid().ToString('N')) + '.json')
    $foreignSemantic = Join-Path $semanticRoot 'notes.json'
    foreach ($path in @($oldSemantic, $newSemantic, $foreignSemantic)) { [IO.File]::WriteAllText($path, '{}') }
    foreach ($path in @($oldSemantic, $foreignSemantic)) { [IO.File]::SetLastWriteTimeUtc($path, $old) }

    $semanticResult = [Talvora.Shared.TalvoraOwnedTempCleanup]::CleanupGuidJsonFiles(
        $semanticRoot,
        $cutoff,
        [Threading.CancellationToken]::None,
        $null)
    if ($semanticResult.DeletedEntries -ne 1 -or
        (Test-Path -LiteralPath $oldSemantic) -or
        -not (Test-Path -LiteralPath $newSemantic) -or
        -not (Test-Path -LiteralPath $foreignSemantic)) {
        throw 'Semantic-worker child-level retention contract failed.'
    }

    Write-Output 'STORAGE_MAINTENANCE_BEHAVIOR_GREEN'
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
