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

    $ownershipRoot = Join-Path $fixtureRoot 'Ownership'
    $ownedCandidate = Join-Path $ownershipRoot 'Owned-candidate'
    New-Item -ItemType Directory -Path $ownedCandidate -Force | Out-Null
    $systemFile = Join-Path $ownedCandidate 'system.txt'
    $foreignFile = Join-Path $ownedCandidate 'foreign.txt'
    [IO.File]::WriteAllText($systemFile, 'system')
    [IO.File]::WriteAllText($foreignFile, 'foreign')
    foreach ($path in @($systemFile, $foreignFile)) { [IO.File]::SetLastWriteTimeUtc($path, $old) }
    [IO.Directory]::SetLastWriteTimeUtc($ownedCandidate, $old)
    $allowCandidate = [Func[string,bool]] { param($path) $true }
    $allowTreeEntry = [Func[string,bool]] {
        param($path)
        return -not [IO.Path]::GetFileName($path).Equals('foreign.txt', [StringComparison]::OrdinalIgnoreCase)
    }
    $ownershipResult = [Talvora.Shared.TalvoraOwnedTempCleanup]::CleanupTopLevel(
        $ownershipRoot,
        $cutoff,
        [string[]]@('Owned-'),
        [string[]]@(),
        [Threading.CancellationToken]::None,
        $allowCandidate,
        $allowTreeEntry)
    if ($ownershipResult.DeletedEntries -ne 0 -or
        -not (Test-Path -LiteralPath $ownedCandidate) -or
        -not (Test-Path -LiteralPath $foreignFile)) {
        throw 'Descendant ownership fail-closed contract failed.'
    }

    $raceRoot = Join-Path $fixtureRoot 'OwnershipRace'
    $raceCandidate = Join-Path $raceRoot 'Owned-race'
    New-Item -ItemType Directory -Path $raceCandidate -Force | Out-Null
    $raceForeign = Join-Path $raceCandidate 'race-foreign.txt'
    [IO.File]::WriteAllText($raceForeign, 'foreign')
    [IO.File]::SetLastWriteTimeUtc($raceForeign, $old)
    [IO.Directory]::SetLastWriteTimeUtc($raceCandidate, $old)
    $raceState = [pscustomobject]@{ ForeignCalls = 0 }
    $flipTreeEntry = [Func[string,bool]] {
        param($path)
        if ([IO.Path]::GetFileName($path).Equals('race-foreign.txt', [StringComparison]::OrdinalIgnoreCase)) {
            $raceState.ForeignCalls++
            return $raceState.ForeignCalls -eq 1
        }
        return $true
    }
    $raceResult = [Talvora.Shared.TalvoraOwnedTempCleanup]::CleanupTopLevel(
        $raceRoot,
        $cutoff,
        [string[]]@('Owned-'),
        [string[]]@(),
        [Threading.CancellationToken]::None,
        $allowCandidate,
        $flipTreeEntry)
    if ($raceResult.DeletedEntries -ne 0 -or
        $raceState.ForeignCalls -lt 2 -or
        -not (Test-Path -LiteralPath $raceForeign)) {
        throw 'Deletion-time descendant safety revalidation contract failed.'
    }

    $cancelRoot = Join-Path $fixtureRoot 'Cancellation'
    $cancelCandidate = Join-Path $cancelRoot 'Owned-cancel'
    New-Item -ItemType Directory -Path $cancelCandidate -Force | Out-Null
    $cancelFile = Join-Path $cancelCandidate 'payload.txt'
    [IO.File]::WriteAllText($cancelFile, 'payload')
    [IO.File]::SetLastWriteTimeUtc($cancelFile, $old)
    [IO.Directory]::SetLastWriteTimeUtc($cancelCandidate, $old)
    $cts = [Threading.CancellationTokenSource]::new()
    try {
        $cancelState = [pscustomobject]@{ RootCalls = 0 }
        $cancelDuringDelete = [Func[string,bool]] {
            param($path)
            if ([IO.Path]::GetFullPath($path).Equals([IO.Path]::GetFullPath($cancelCandidate), [StringComparison]::OrdinalIgnoreCase)) {
                $cancelState.RootCalls++
                if ($cancelState.RootCalls -eq 2) { $cts.Cancel() }
            }
            return $true
        }
        $cancelThrown = $false
        try {
            $null = [Talvora.Shared.TalvoraOwnedTempCleanup]::TryDeleteStaleEntry(
                $cancelRoot,
                $cancelCandidate,
                $cutoff,
                [ref]([long]$cancelBytes = 0),
                $cts.Token,
                $cancelDuringDelete)
        } catch [OperationCanceledException] {
            $cancelThrown = $true
        }
        if (-not $cancelThrown -or -not (Test-Path -LiteralPath $cancelFile)) {
            throw 'Deletion cancellation must stop recursive cleanup before removing the next entry.'
        }
    } finally {
        $cts.Dispose()
    }

    Write-Output 'STORAGE_MAINTENANCE_BEHAVIOR_GREEN'
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
