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
        '^src/.+\.(xaml|json|js|html)$',
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
    $evidenceText = Get-Content -LiteralPath $evidencePath -Raw
    # Same strict parser as ProjectDeliveryGate (System.Text.Json): ConvertFrom-Json accepts trailing
    # commas, so this gate passed evidence that talvora_git_run push then refused (audit 2026-10-22 (9)).
    try { [System.Text.Json.JsonDocument]::Parse($evidenceText).Dispose() }
    catch { throw "Context7 evidence is not valid JSON (strict): $($_.Exception.InnerException.Message)" }
    $evidence = $evidenceText | ConvertFrom-Json
    if (($evidence.schemaVersion -isnot [long] -and $evidence.schemaVersion -isnot [int]) -or
        $evidence.schemaVersion -ne 1) { throw 'Context7 evidence schemaVersion must be 1.' }
    if ($evidence.scope -isnot [string] -or
        [string]::IsNullOrWhiteSpace($evidence.scope)) { throw 'Context7 evidence must describe the verified project scope.' }

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
    if ($verifiedAt.UtcDateTime.Date -ne $now.UtcDateTime.Date) { throw 'Context7 evidence verifiedAtUtc must be today (UTC).' }

    if ($evidence.libraries -isnot [array]) { throw 'Context7 evidence libraries must be an array.' }
    $libraries = @($evidence.libraries)
    if ($libraries.Count -gt 12) { throw 'Context7 evidence must list at most 12 libraries.' }
    foreach ($library in $libraries) {
        if ($null -eq $library -or $library -isnot [pscustomobject] -or
            [string]::IsNullOrWhiteSpace([string]$library.libraryId) -or
            [string]$library.libraryId -cnotmatch '^/[^/\s]+/[^/\s]+(/[^/\s]+)?$' -or
            ([string]$library.libraryId).Contains('\') -or
            [string]::IsNullOrWhiteSpace([string]$library.verified)) {
            throw 'Every Context7 library entry must contain a canonical /org/project libraryId and verified guidance.'
        }
        if ([string]$library.verified -cnotmatch '\(query: "([^"]+)"\)') {
            throw 'Context7 evidence verified guidance must include (query: "question").'
        }
    }
    if ($libraries.Count -eq 0 -and (
        $evidence.noLookup -isnot [string] -or
        ([string]$evidence.noLookup).Trim().Length -lt 20)) {
        throw 'Context7 evidence noLookup must contain at least 20 characters.'
    }
    if ($null -ne $evidence.PSObject.Properties['vendorSources']) {
        if ($evidence.vendorSources -isnot [array] -or
            @($evidence.vendorSources).Count -gt 12) {
            throw 'Context7 evidence vendorSources must be 0–12 HTTPS URLs.'
        }
        foreach ($source in @($evidence.vendorSources)) {
            if ($source -isnot [string] -or
                -not $source.StartsWith('https://', [StringComparison]::Ordinal)) {
                throw 'Context7 evidence vendorSources must be 0–12 HTTPS URLs.'
            }
        }
    }

    Write-Host 'CONTEXT7_QUALITY_GATE_GREEN'
}
finally {
    Pop-Location
}