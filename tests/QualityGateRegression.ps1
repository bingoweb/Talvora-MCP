$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$gatePath = Join-Path $repoRoot 'scripts\Invoke-QualityGate.ps1'
$contextPath = Join-Path $repoRoot 'scripts\Get-CodexContext.ps1'
$workflowPath = Join-Path $repoRoot '.github\workflows\windows-ci.yml'

if (-not (Test-Path -LiteralPath $gatePath -PathType Leaf)) {
    throw 'Unified quality gate script is missing.'
}
if (-not (Test-Path -LiteralPath $contextPath -PathType Leaf)) {
    throw 'Codex context summary script is missing.'
}

foreach ($path in @($gatePath, $contextPath)) {
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        $path, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) {
        throw "Script does not parse: $path"
    }
}

$tokens = $null
$errors = $null
$qualityAst = [Management.Automation.Language.Parser]::ParseFile(
    $gatePath, [ref]$tokens, [ref]$errors)
$step = $qualityAst.Find({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Invoke-QualityCheck'
}, $true)
if ($null -eq $step) {
    throw 'The shared quality check executor is missing.'
}

$runtimeProbeAst = $qualityAst.Find({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Get-InstalledRuntimeAvailability'
}, $true)
if ($null -eq $runtimeProbeAst) { throw 'The installed-runtime gate probe is missing.' }
Invoke-Expression $runtimeProbeAst.Extent.Text
$interactive = Get-InstalledRuntimeAvailability -Probe {
    [pscustomobject]@{ isWindowsService = $true }
} -GetUserName { 'fixture\interactive' }
if (-not $interactive.HealthHandle -or -not $interactive.NativeInstallerRuntime) {
    throw 'Service and desktop must run both checks.'
}
$headless = Get-InstalledRuntimeAvailability -Probe {
    [pscustomobject]@{ isWindowsService = $true }
} -GetUserName { '' }
if (-not $headless.HealthHandle -or $headless.NativeInstallerRuntime) {
    throw 'Headless service must run only HealthHandle.'
}
$unavailable = Get-InstalledRuntimeAvailability -Probe {
    [pscustomobject]@{ isWindowsService = $false }
} -GetUserName { throw 'Unexpected session probe.' }
if ($unavailable.HealthHandle -or $unavailable.NativeInstallerRuntime) {
    throw 'Non-service response must skip service-dependent checks.'
}
$unreachable = Get-InstalledRuntimeAvailability -Probe {
    throw 'Simulated connection failure'
} -GetUserName { throw 'Unexpected session probe.' }
if ($unreachable.HealthHandle -or $unreachable.NativeInstallerRuntime) {
    throw 'Unreachable service must skip service-dependent checks.'
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'TalvoraQualityGateRegression-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    Invoke-Expression $step.Extent.Text
    $script:GateCaptureRoot = $tempRoot
    $script:GateChecks = 0
    $script:GateFailure = $null
    $script:GateVerbose = $false

    $passed = Invoke-QualityCheck -Name 'FixtureSuccess' -Action {
        Write-Output 'A deterministic success'
    }
    if (-not $passed -or $script:GateChecks -ne 1) {
        throw 'Successful steps must execute and count exactly once.'
    }

    $failed = Invoke-QualityCheck -Name 'SourceEdit' -Action {
        throw 'Injected source-edit regression failure'
    }
    if ($failed -or $script:GateFailure -cne 'SourceEdit' -or
        $script:GateChecks -ne 2) {
        throw 'A source-edit failure must be detected and attributed to SourceEdit.'
    }

    $failedLog = Join-Path $tempRoot '2.log'
    if (-not (Test-Path -LiteralPath $failedLog)) {
        throw 'Failing step diagnostics were not captured.'
    }

    $workflow = [IO.File]::ReadAllText($workflowPath)
    if ($workflow -notmatch 'Invoke-QualityGate\.ps1\s+-Scope\s+Full' -or
        $workflow -notmatch 'NativeInstallerRuntimeRegression\.ps1' -or
        $workflow -notmatch 'HealthHandleRegression\.ps1') {
        throw 'CI must run the full shared gate and defer installed-runtime tests.'
    }

    $installIndex = $workflow.IndexOf('      - name: Install and verify canonical runtime')
    $healthIndex = $workflow.IndexOf('      - name: Validate installed health handle stability')
    if ($installIndex -lt 0 -or $healthIndex -le $installIndex) {
        throw 'HealthHandle regression must run after installing the Windows service.'
    }

    $contextOutput = @(& $contextPath -Card K1 | Out-String) -join ''
    if ($contextOutput -notmatch '### K1\s+[—-]' -or
        $contextOutput -notmatch 'Invoke-QualityGate\.ps1' -or
        $contextOutput -match '### K2\s+[—-]') {
        throw 'The context helper must print the selected card without neighbouring cards.'
    }

    $fixtureRoot = Join-Path $tempRoot 'context-fixture'
    $fixtureScripts = Join-Path $fixtureRoot 'scripts'
    $fixtureDocs = Join-Path $fixtureRoot 'docs/codex'
    New-Item -ItemType Directory -Path $fixtureScripts, $fixtureDocs -Force | Out-Null
    $fixtureHelper = Join-Path $fixtureScripts 'Get-CodexContext.ps1'
    Copy-Item -LiteralPath $contextPath -Destination $fixtureHelper
    @'
## In progress
(none)
## Agent queue
- [ ] N1 — Worklog terminal
- [ ] UI1 — UI theme
## User queue
(none)
## Open questions
(none)
## Log
- 2026-10-18 — NEWEST_LOG_ONLY
- 2026-10-17 — STALE_LOG_MUST_NOT_APPEAR
'@ | Set-Content -LiteralPath (Join-Path $fixtureDocs 'STATUS.md') -Encoding utf8
    @'
### K1 — Baseline
## Unrelated
ROADMAP_UNRELATED_SENTINEL
'@ | Set-Content -LiteralPath (Join-Path $fixtureDocs 'ROADMAP.md') -Encoding utf8
    @'
## 3. Worklog
### 3.1 Code terminal
WORKLOG_TERMINAL_SENTINEL
## 5. Worklog behaviour
### 5.4 Agent jobs
#### 5.4.1 Code display
WORKLOG_AGENT_SENTINEL
## 6. Other
WORKLOG_UNRELATED_SENTINEL
## Implementation cards
### N1 — Current card
**Files:** `src/Example.cs`
**Do:** Follow §3.1 and §5.4.1 only.
### N2 — Next card
NEXT_WORKLOG_CARD_SENTINEL
'@ | Set-Content -LiteralPath (Join-Path $fixtureDocs 'DESIGN-WORKLOG.md') -Encoding utf8
    @'
## 3. UI tokens
### 3.1 Dark colours
UI_COLOUR_SENTINEL
## 7. UI code
### 7.1 Dictionary
UI_DICTIONARY_SENTINEL
### 7.2 Theme switch
UI_SWITCH_SENTINEL
## 8. Other
UI_UNRELATED_SENTINEL
## Implementation cards
### UI1 — Current card
**Files:** `src/Theme.cs`
**Do:** Follow §3.1, §7.1–7.2.
### UI2 — Next card
NEXT_UI_CARD_SENTINEL
'@ | Set-Content -LiteralPath (Join-Path $fixtureDocs 'DESIGN-CONTROL-CENTER.md') -Encoding utf8
    @'
## Pending
- [ ] E8 — PENDING_QUEUE_SENTINEL
## Applied
APPLIED_QUEUE_MUST_NOT_APPEAR
'@ | Set-Content -LiteralPath (Join-Path $fixtureDocs 'QUEUE-ADDITIONS.md') -Encoding utf8
    @'
## Open
### P-1 — OPEN_PROPOSAL_TITLE
PRIVATE_PROPOSAL_BODY_MUST_NOT_APPEAR
## Decided
### P-2 — DECIDED_PROPOSAL_MUST_NOT_APPEAR
'@ | Set-Content -LiteralPath (Join-Path $fixtureDocs 'PROPOSALS.md') -Encoding utf8
    foreach ($spec in @(
        @{ Card='N1'; Required=@('WORKLOG_TERMINAL_SENTINEL','WORKLOG_AGENT_SENTINEL'); Forbidden=@('NEXT_WORKLOG_CARD_SENTINEL','WORKLOG_UNRELATED_SENTINEL') },
        @{ Card='UI1'; Required=@('UI_COLOUR_SENTINEL','UI_DICTIONARY_SENTINEL','UI_SWITCH_SENTINEL'); Forbidden=@('NEXT_UI_CARD_SENTINEL','UI_UNRELATED_SENTINEL') }
    )) {
        $out = @(& $fixtureHelper -Card $spec.Card | Out-String) -join ''
        foreach ($expected in @($spec.Required + @('NEWEST_LOG_ONLY','PENDING_QUEUE_SENTINEL','OPEN_PROPOSAL_TITLE'))) {
            if (-not $out.Contains($expected)) { throw "Missing $expected for $($spec.Card)." }
        }
        foreach ($forbidden in @($spec.Forbidden + @('STALE_LOG_MUST_NOT_APPEAR','APPLIED_QUEUE_MUST_NOT_APPEAR','PRIVATE_PROPOSAL_BODY_MUST_NOT_APPEAR','DECIDED_PROPOSAL_MUST_NOT_APPEAR'))) {
            if ($out.Contains($forbidden)) { throw "Unexpected $forbidden for $($spec.Card)." }
        }
        if (($out -split '\r?\n').Count -gt 250) { throw "Oversized fixture context for $($spec.Card)." }
    }
    foreach ($card in @('UI1','UI2','UI3','UI4','UI5','UI6','UI7','UI8',
            'N1','N2','N3','N3b','N4','N5','N6','N7','N8')) {
        $actual = @(& $contextPath -Card $card | Out-String) -join ''
        # Literal "#### Exact implementation" blocks must reach the agent complete, and the
        # STATUS header is required reading anyway; the 250-line economy budget applies to
        # the card prose, queue/proposal summaries, file counts and cited spec sections.
        $counted = 0
        $insideExact = $false
        $insideStatus = $false
        foreach ($line in ($actual -split '\r?\n')) {
            if ($line -like '=== *') {
                $insideStatus = $line -like '=== STATUS*'
                $insideExact = $false
            }
            if ($line -like '#### Exact implementation*') { $insideExact = $true; continue }
            if (-not $insideExact -and -not $insideStatus) { $counted++ }
        }
        if ($counted -gt 250) {
            throw "Real context for $card exceeds 250 lines outside STATUS and exact-implementation blocks."
        }
    }

    $quietOutput = @(& {
        Invoke-QualityCheck -Name 'FixtureQuiet' -Action { Write-Output 'Quiet successful step' } -QuietSuccess
    } 6>&1)
    if ($quietOutput.Count -gt 1 -or ($quietOutput.Count -eq 1 -and
            [string]$quietOutput[0] -match 'PASS|FixtureQuiet')) {
        throw 'Successful regression substeps must be silent when grouped.'
    }
    if ([IO.File]::ReadAllText($gatePath) -notmatch '-Tail\s+60') {
        throw 'Failure diagnostics must be limited to the last 60 lines.'
    }
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'QUALITY_GATE_REGRESSION_GREEN'