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
