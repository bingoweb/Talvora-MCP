$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The local evidence gates must reject what ProjectDeliveryGate (System.Text.Json) rejects.
# Before D16 a trailing comma passed scripts/Validate-Context7Gate.ps1 (ConvertFrom-Json is
# lenient) and only talvora_git_run push refused it (audit 2026-10-22 (9)).
$repoRoot = Split-Path -Parent $PSScriptRoot
foreach ($script in @('scripts/Validate-Context7Gate.ps1', 'scripts/Validate-ProjectDeliveryGate.ps1')) {
    $text = [IO.File]::ReadAllText((Join-Path $repoRoot $script))
    if (-not $text.Contains('[System.Text.Json.JsonDocument]::Parse(', [StringComparison]::Ordinal)) {
        throw "$script must parse evidence strictly with System.Text.Json before ConvertFrom-Json."
    }
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('Talvora-StrictEvidence-' + [Guid]::NewGuid().ToString('N'))
$previousGitHubActions = $env:GITHUB_ACTIONS
try {
    [IO.Directory]::CreateDirectory((Join-Path $fixtureRoot 'scripts')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $fixtureRoot 'src')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $fixtureRoot '.context7')) | Out-Null
    $fixtureGate = Join-Path $fixtureRoot 'scripts/Validate-Context7Gate.ps1'
    [IO.File]::Copy((Join-Path $repoRoot 'scripts/Validate-Context7Gate.ps1'), $fixtureGate)
    $env:GITHUB_ACTIONS = 'false'

    Push-Location -LiteralPath $fixtureRoot
    try {
        & git init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Strict evidence fixture git init failed.' }
        & git -c "safe.directory=$fixtureRoot" add -- scripts/Validate-Context7Gate.ps1
        & git -c "safe.directory=$fixtureRoot" -c 'user.name=Talvora Fixture' -c 'user.email=fixture@example.invalid' commit --quiet -m 'Baseline gate'
        if ($LASTEXITCODE -ne 0) { throw 'Strict evidence fixture git commit failed.' }

        [IO.File]::WriteAllText((Join-Path $fixtureRoot 'src/Fixture.cs'), 'class Fixture { }')
        $timestamp = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        [IO.File]::WriteAllText(
            (Join-Path $fixtureRoot '.context7/verification.json'),
            "{ `"schemaVersion`": 1, `"verifiedAtUtc`": `"$timestamp`", `"scope`": `"fixture`", `"libraries`": [ { `"libraryId`": `"/dotnet/docs`", `"verified`": `"fixture`", } ], `"vendorSources`": [ `"fixture`" ] }")

        $failure = $null
        try { & $fixtureGate 2>&1 | Out-Null }
        catch { $failure = $_.Exception }
        if ($null -eq $failure -or -not $failure.Message.Contains('not valid JSON (strict)', [StringComparison]::Ordinal)) {
            throw 'Validate-Context7Gate.ps1 accepted evidence with a trailing comma.'
        }

        function AssertContext7Blocked([string]$expected) {
            $failure = $null
            try { & $fixtureGate *> $null }
            catch { $failure = $_.Exception }
            if ($null -eq $failure -or -not $failure.Message.Contains($expected, [StringComparison]::Ordinal)) {
                throw "CTX2: expected '$expected' but got '$($failure.Message)'."
            }
        }

        $contextPath = Join-Path $fixtureRoot '.context7/verification.json'
        $valid = @{
            schemaVersion = 1
            verifiedAtUtc = $timestamp
            scope = 'CTX2 local proof fixture'
            libraries = @()
            noLookup = 'CTX2: only renames a local variable'
            vendorSources = @()
        }
        [IO.File]::WriteAllText($contextPath, ($valid | ConvertTo-Json -Depth 5))
        & $fixtureGate *> $null
        if ($LASTEXITCODE -ne 0) { throw 'CTX2: valid noLookup evidence failed.' }

        $valid.schemaVersion = '1'
        [IO.File]::WriteAllText($contextPath, ($valid | ConvertTo-Json -Depth 5))
        AssertContext7Blocked 'schemaVersion must be 1'
        $valid.schemaVersion = 1
        $valid.scope = 123
        [IO.File]::WriteAllText($contextPath, ($valid | ConvertTo-Json -Depth 5))
        AssertContext7Blocked 'verified project scope'
        $valid.scope = 'CTX2 local proof fixture'

        $valid.noLookup = 'short'
        [IO.File]::WriteAllText($contextPath, ($valid | ConvertTo-Json -Depth 5))
        AssertContext7Blocked 'noLookup must contain at least 20 characters'
        $valid.noLookup = 'CTX2: only renames a local variable'
        $valid.vendorSources = @('http://example.invalid/doc')
        [IO.File]::WriteAllText($contextPath, ($valid | ConvertTo-Json -Depth 5))
        AssertContext7Blocked 'vendorSources must be 0'

        $valid.Remove('noLookup')
        $valid.vendorSources = @()
        $valid.libraries = @(@{
            libraryId = '/websites/git-scm'
            verified = 'Git diff documentation was checked.'
        })
        [IO.File]::WriteAllText($contextPath, ($valid | ConvertTo-Json -Depth 5))
        AssertContext7Blocked 'verified guidance must include'
        $valid.libraries[0].verified = 'Git diff range (query: "git diff --name-only between a remote-tracking branch and HEAD")'
        [IO.File]::WriteAllText($contextPath, ($valid | ConvertTo-Json -Depth 5))
        & $fixtureGate *> $null
        if ($LASTEXITCODE -ne 0) { throw 'CTX2: valid library evidence failed.' }

        [IO.File]::Delete((Join-Path $fixtureRoot 'src/Fixture.cs'))
        [IO.File]::Delete($contextPath)
        [IO.Directory]::CreateDirectory((Join-Path $fixtureRoot 'docs')) | Out-Null
        [IO.File]::WriteAllText((Join-Path $fixtureRoot 'docs/x.md'), 'docs-only')
        & $fixtureGate *> $null
        if ($LASTEXITCODE -ne 0) { throw 'CTX2: documentation-only change required evidence.' }
    }
    finally {
        Pop-Location
    }
}
finally {
    $env:GITHUB_ACTIONS = $previousGitHubActions
    if (Test-Path -LiteralPath $fixtureRoot -PathType Container) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

Write-Output 'STRICT_EVIDENCE_JSON_GREEN'
