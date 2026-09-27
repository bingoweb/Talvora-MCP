param(
    [string]$BaseRef
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$evidenceRelativePath = '.context7/verification.json'

Push-Location $root
try {
    $changedFiles = @()
    if ($env:GITHUB_ACTIONS -eq 'true') {
        if ([string]::IsNullOrWhiteSpace($BaseRef) -and -not [string]::IsNullOrWhiteSpace($env:GITHUB_EVENT_PATH) -and (Test-Path -LiteralPath $env:GITHUB_EVENT_PATH -PathType Leaf)) {
            $event = Get-Content -LiteralPath $env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json
            if ($env:GITHUB_EVENT_NAME -eq 'pull_request' -and $null -ne $event.pull_request) { $BaseRef = [string]$event.pull_request.base.sha }
            elseif ($env:GITHUB_EVENT_NAME -eq 'push') { $BaseRef = [string]$event.before }
        }
        if ([string]::IsNullOrWhiteSpace($BaseRef) -or $BaseRef -match '^0{40}$') { throw 'Context7 gate could not determine a valid GitHub base revision.' }
        & git -c "safe.directory=$root" cat-file -e "$BaseRef^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Context7 gate base revision is unavailable locally: $BaseRef" }
        $changedFiles = @(& git -c "safe.directory=$root" diff --name-only --diff-filter=ACMR $BaseRef HEAD)
        if ($LASTEXITCODE -ne 0) { throw 'Context7 gate could not compute the GitHub change set.' }
    }
    else {
        $trackedChanges = @(& git -c "safe.directory=$root" diff --name-only --diff-filter=ACMR HEAD)
        if ($LASTEXITCODE -ne 0) { throw 'Context7 gate could not inspect local tracked changes.' }
        $untrackedChanges = @(& git -c "safe.directory=$root" ls-files --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw 'Context7 gate could not inspect local untracked changes.' }
        $changedFiles = @($trackedChanges + $untrackedChanges)
    }

    $changedFiles = @($changedFiles | ForEach-Object { ([string]$_).Trim().Replace('\', '/') } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $meaningfulPatterns = @(
        '^src/.+\.(cs|csproj)$',
        '^tests/.+\.(cs|csproj)$',
        '^scripts/.+\.ps1$',
        '^Directory\.(Build|Packages)\.props$',
        '^global\.json$',
        '^\.github/workflows/.+\.ya?ml$'
    )
    $meaningfulChanges = @($changedFiles | Where-Object { $path = $_; $meaningfulPatterns | Where-Object { $path -match $_ } | Select-Object -First 1 })

    if ($meaningfulChanges.Count -eq 0) {
        Write-Host 'CONTEXT7_QUALITY_GATE_GREEN (no development-scope changes)'
        return
    }

    if ($changedFiles -notcontains $evidenceRelativePath) {
        Write-Host 'Development-scope changes:'
        $meaningfulChanges | ForEach-Object { Write-Host " - $_" }
        throw "Context7 evidence must be refreshed in the same change: $evidenceRelativePath"
    }

    $evidencePath = Join-Path $root '.context7\verification.json'
    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { throw "Context7 evidence file is missing: $evidencePath" }
    $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
    if ([int]$evidence.schemaVersion -ne 1) { throw 'Context7 evidence schemaVersion must be 1.' }
    if ([string]::IsNullOrWhiteSpace([string]$evidence.scope)) { throw 'Context7 evidence must describe the development scope.' }

    $verifiedAt = if ($evidence.verifiedAtUtc -is [DateTime]) {
        [DateTimeOffset]$evidence.verifiedAtUtc
    }
    else {
        [DateTimeOffset]::Parse(
            [string]$evidence.verifiedAtUtc,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal)
    }
    $now = [DateTimeOffset]::UtcNow
    if ($verifiedAt -gt $now.AddMinutes(15)) { throw 'Context7 evidence timestamp is unexpectedly in the future.' }
    if ($verifiedAt -lt $now.AddDays(-14)) { throw 'Context7 evidence is older than 14 days; query current documentation again.' }

    $libraries = @($evidence.libraries)
    if ($libraries.Count -eq 0) { throw 'At least one Context7 library verification is required for development-scope changes.' }
    foreach ($library in $libraries) {
        if ([string]::IsNullOrWhiteSpace([string]$library.libraryId) -or -not ([string]$library.libraryId).StartsWith('/')) { throw 'Each Context7 evidence entry must contain a canonical /org/project-style libraryId.' }
        if ([string]::IsNullOrWhiteSpace([string]$library.verified)) { throw "Context7 evidence is missing the verified guidance for $($library.libraryId)." }
    }
    if (@($evidence.vendorSources).Count -eq 0) { throw 'At least one official/vendor source check must accompany Context7 evidence.' }

    Write-Host 'CONTEXT7_QUALITY_GATE_GREEN'
}
finally {
    Pop-Location
}