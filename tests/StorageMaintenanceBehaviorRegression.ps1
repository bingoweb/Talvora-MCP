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

    $prefixRoot = Join-Path $fixtureRoot 'PrefixSafety'
    New-Item -ItemType Directory -Path $prefixRoot -Force | Out-Null
    $userNamedFile = Join-Path $prefixRoot 'TalvoraBugReport.txt'
    $managedDelimited = Join-Path $prefixRoot ('TalvoraBug-' + [guid]::NewGuid().ToString('N'))
    $managedGuidSuffix = Join-Path $prefixRoot ('TalvoraReparse' + [guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($userNamedFile, 'user-data')
    New-Item -ItemType Directory -Path $managedDelimited,$managedGuidSuffix -Force | Out-Null
    [IO.File]::SetLastWriteTimeUtc($userNamedFile, $old)
    [IO.Directory]::SetLastWriteTimeUtc($managedDelimited, $old)
    [IO.Directory]::SetLastWriteTimeUtc($managedGuidSuffix, $old)
    $prefixResult = [Talvora.Shared.TalvoraOwnedTempCleanup]::CleanupTopLevel(
        $prefixRoot,
        $cutoff,
        [Talvora.Shared.TalvoraOwnedTempCleanup]::DefaultPrefixes,
        [Talvora.Shared.TalvoraOwnedTempCleanup]::DefaultExactNames,
        [Threading.CancellationToken]::None,
        $null,
        $null)
    if ($prefixResult.DeletedEntries -ne 2 -or
        -not (Test-Path -LiteralPath $userNamedFile) -or
        (Test-Path -LiteralPath $managedDelimited) -or
        (Test-Path -LiteralPath $managedGuidSuffix)) {
        throw 'Legacy prefix matching must preserve unrelated names while retaining delimiter/GUID-shaped Talvora cleanup.'
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

    $freshRaceRoot = Join-Path $fixtureRoot 'FreshnessRace'
    $freshRaceCandidate = Join-Path $freshRaceRoot 'Owned-fresh-race'
    New-Item -ItemType Directory -Path $freshRaceCandidate -Force | Out-Null
    $freshRaceOld = Join-Path $freshRaceCandidate 'old.txt'
    [IO.File]::WriteAllText($freshRaceOld, 'old')
    [IO.File]::SetLastWriteTimeUtc($freshRaceOld, $old)
    [IO.Directory]::SetLastWriteTimeUtc($freshRaceCandidate, $old)
    $freshRaceState = [pscustomobject]@{ RootCalls = 0; Injected = $false }
    $injectFreshAtDelete = [Func[string,bool]] {
        param($path)
        if ([IO.Path]::GetFullPath($path).Equals([IO.Path]::GetFullPath($freshRaceCandidate), [StringComparison]::OrdinalIgnoreCase)) {
            $freshRaceState.RootCalls++
            if ($freshRaceState.RootCalls -eq 2) {
                [IO.File]::WriteAllText((Join-Path $freshRaceCandidate 'fresh.txt'), 'fresh')
                $freshRaceState.Injected = $true
            }
        }
        return $true
    }
    $freshRaceResult = [Talvora.Shared.TalvoraOwnedTempCleanup]::CleanupTopLevel(
        $freshRaceRoot,
        $cutoff,
        [string[]]@('Owned-'),
        [string[]]@(),
        [Threading.CancellationToken]::None,
        $allowCandidate,
        $injectFreshAtDelete)
    if (-not $freshRaceState.Injected -or
        $freshRaceResult.DeletedEntries -ne 0 -or
        -not (Test-Path -LiteralPath (Join-Path $freshRaceCandidate 'fresh.txt'))) {
        throw 'Deletion-time freshness revalidation must preserve a candidate that becomes active after the stale scan.'
    }

    $junctionHolder = Join-Path $fixtureRoot 'JunctionHolder'
    $junctionOutside = Join-Path $fixtureRoot 'JunctionOutside'
    $junctionRoot = Join-Path $junctionHolder 'versions'
    $junctionVictim = Join-Path $junctionOutside 'old-version'
    New-Item -ItemType Directory -Path $junctionHolder,$junctionVictim -Force | Out-Null
    $junctionFile = Join-Path $junctionVictim 'payload.txt'
    [IO.File]::WriteAllText($junctionFile, 'outside')
    [IO.File]::SetLastWriteTimeUtc($junctionFile, $old)
    [IO.Directory]::SetLastWriteTimeUtc($junctionVictim, $old)
    $mklink = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\cmd.exe') -ArgumentList @('/d','/c',('mklink /J "{0}" "{1}"' -f $junctionRoot,$junctionOutside)) -PassThru -Wait -WindowStyle Hidden
    if ($mklink.ExitCode -ne 0) { throw 'Junction fixture creation failed.' }
    try {
        $junctionBytes = [long]0
        $junctionDeleted = [Talvora.Shared.TalvoraOwnedTempCleanup]::TryDeleteStaleEntry(
            $junctionRoot,
            (Join-Path $junctionRoot 'old-version'),
            $cutoff,
            [ref]$junctionBytes)
        if ($junctionDeleted -or
            -not (Test-Path -LiteralPath $junctionVictim) -or
            [Talvora.Shared.TalvoraOwnedTempCleanup]::IsDirectoryPathReparseSafe($junctionRoot, $junctionRoot)) {
            throw 'Cleanup must refuse a reparse allowed root and preserve the outside target.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $junctionRoot) {
            & (Join-Path $env:SystemRoot 'System32\cmd.exe') /d /c ('rmdir "{0}"' -f $junctionRoot) | Out-Null
        }
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

    $partialRoot = Join-Path $fixtureRoot 'PartialCancellation'
    $partialCandidate = Join-Path $partialRoot 'Owned-partial'
    New-Item -ItemType Directory -Path $partialCandidate -Force | Out-Null
    1..32 | ForEach-Object {
        $path = Join-Path $partialCandidate (('{0:D2}.tmp' -f $_))
        [IO.File]::WriteAllText($path, 'payload')
        [IO.File]::SetLastWriteTimeUtc($path, $old)
    }
    [IO.Directory]::SetLastWriteTimeUtc($partialCandidate, $old)
    $partialCts = [Threading.CancellationTokenSource]::new()
    try {
        $partialState = [pscustomobject]@{ RootCalls = 0; DeleteEntryCalls = 0; InDelete = $false }
        $cancelAfterDeletes = [Func[string,bool]] {
            param($path)
            if ([IO.Path]::GetFullPath($path).Equals([IO.Path]::GetFullPath($partialCandidate), [StringComparison]::OrdinalIgnoreCase)) {
                $partialState.RootCalls++
                if ($partialState.RootCalls -eq 2) { $partialState.InDelete = $true }
                return $true
            }
            if ($partialState.InDelete) {
                $partialState.DeleteEntryCalls++
                if ($partialState.DeleteEntryCalls -eq 10) { $partialCts.Cancel() }
            }
            return $true
        }
        $partialThrown = $false
        try {
            $null = [Talvora.Shared.TalvoraOwnedTempCleanup]::TryDeleteStaleEntry(
                $partialRoot,
                $partialCandidate,
                $cutoff,
                [ref]([long]$partialBytes = 0),
                $partialCts.Token,
                $cancelAfterDeletes)
        } catch [OperationCanceledException] {
            $partialThrown = $true
        }
        $remaining = @(Get-ChildItem -LiteralPath $partialCandidate -File -ErrorAction Stop).Count
        $retainedTimestamp = [IO.Directory]::GetLastWriteTimeUtc($partialCandidate)
        if (-not $partialThrown -or $remaining -le 0 -or
            [Math]::Abs(($retainedTimestamp - $old).TotalSeconds) -gt 2) {
            throw 'Partial cancellation must preserve the stale directory age so the next maintenance pass can resume cleanup.'
        }
    } finally {
        $partialCts.Dispose()
    }

    $boundedJsonPath = Join-Path $fixtureRoot 'oversized.json'
    [IO.File]::WriteAllBytes($boundedJsonPath, [byte[]]::new(65))
    $readBoundedMethods = @([Talvora.Shared.JsonFileStore].GetMethods() |
        Where-Object { $_.Name -eq 'ReadBounded' -and $_.IsGenericMethodDefinition })
    if ($readBoundedMethods.Count -ne 1) {
        throw 'Expected exactly one generic JsonFileStore.ReadBounded method.'
    }
    $readBounded = $readBoundedMethods[0]
    $closedRead = $readBounded.MakeGenericMethod([object])
    $oversizedRejected = $false
    try {
        $invokeArgs = [object[]]@([string]$boundedJsonPath, [int]64, $null)
        $null = $closedRead.Invoke($null, $invokeArgs)
    } catch {
        $rootException = $_.Exception
        while ($null -ne $rootException.InnerException) {
            $rootException = $rootException.InnerException
        }
        $oversizedRejected = $rootException -is [System.IO.InvalidDataException]
    }
    if (-not $oversizedRejected) {
        throw 'Bounded JSON reader must reject a snapshot larger than its byte ceiling before deserialization.'
    }

    $boundedTextPath = Join-Path $fixtureRoot 'oversized.txt'
    [IO.File]::WriteAllBytes($boundedTextPath, [byte[]]::new(65))
    $boundedTextRejected = $false
    try {
        [Talvora.Shared.TextFileStore]::ReadBoundedAsync(
            $boundedTextPath,
            64,
            [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
    } catch {
        $rootException = $_.Exception
        while ($null -ne $rootException.InnerException) {
            $rootException = $rootException.InnerException
        }
        $boundedTextRejected = $rootException -is [System.IO.InvalidDataException]
    }
    if (-not $boundedTextRejected) {
        throw 'Bounded text reader must reject a snapshot larger than its byte ceiling before text allocation.'
    }

    Write-Output 'STORAGE_MAINTENANCE_BEHAVIOR_GREEN'
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
