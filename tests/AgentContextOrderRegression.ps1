$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# D30: the card comes first, STATUS (open lines only) comes last, and a UI/N card that its
# design spec does not hold (or whose spec file is missing) is read from ROADMAP.
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('talvora-d30-' + [guid]::NewGuid().ToString('N'))
try {
    $scripts = Join-Path $fixture 'scripts'
    $docs = Join-Path $fixture 'docs/codex'
    New-Item -ItemType Directory -Path $scripts, $docs -Force | Out-Null
    $helper = Join-Path $scripts 'Get-CodexContext.ps1'
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/Get-CodexContext.ps1') -Destination $helper
    Set-Content -LiteralPath (Join-Path $docs 'STATUS.md') -Encoding utf8 -Value @(
        '## In progress', '(none)',
        '## Agent queue',
        '- [x] D7 — FINISHED_LINE_MUST_NOT_APPEAR',
        '- [-] D8 — DROPPED_LINE_MUST_NOT_APPEAR',
        '- [ ] D9 — Open card',
        '## User queue', '(none)',
        '## Log',
        '- 2026-11-07 — NEWEST_LOG_SENTINEL')
    Set-Content -LiteralPath (Join-Path $docs 'ROADMAP.md') -Encoding utf8 -Value @(
        '### D9 — Open card', 'D9_CARD_SENTINEL',
        '### UI9 — UI card written in ROADMAP', 'UI9_CARD_SENTINEL',
        '### N9 — Worklog card written in ROADMAP', 'N9_CARD_SENTINEL')
    Set-Content -LiteralPath (Join-Path $docs 'DESIGN-WORKLOG.md') -Encoding utf8 -Value @(
        '## 3. Worklog', 'WORKLOG_SPEC_SENTINEL')

    $context = @(& $helper | Out-String) -join ''
    $first = @($context -split '\r?\n' | Where-Object { $_.Trim() -ne '' })[0]
    if ($first -ne '=== CARD D9 (docs/codex/ROADMAP.md) ===') { throw "D30: the card must come first, got: $first" }
    $statusAt = $context.IndexOf('=== STATUS', [StringComparison]::Ordinal)
    if ($statusAt -lt 0) { throw 'D30: STATUS must still be printed.' }
    foreach ($section in @('=== NEWEST LOG', '=== PENDING QUEUE ADDITIONS', '=== OPEN PROPOSALS', '=== FILE LINE COUNTS')) {
        $at = $context.IndexOf($section, [StringComparison]::Ordinal)
        if ($at -lt 0 -or $at -gt $statusAt) { throw "D30: $section must come before STATUS." }
    }
    foreach ($forbidden in @('FINISHED_LINE_MUST_NOT_APPEAR', 'DROPPED_LINE_MUST_NOT_APPEAR')) {
        if ($context.Contains($forbidden)) { throw "D30: finished queue lines must not reach the brief ($forbidden)." }
    }
    if (-not $context.Contains('- [ ] D9 — Open card') -or -not $context.Contains('NEWEST_LOG_SENTINEL')) {
        throw 'D30: open queue lines and the newest Log entry must stay.'
    }
    # The fixture has no DESIGN-CONTROL-CENTER.md, and its DESIGN-WORKLOG.md has no N9 card.
    foreach ($card in @('UI9', 'N9')) {
        $out = @(& $helper -Card $card | Out-String) -join ''
        if ($out -notmatch "(?m)^=== CARD $card \(docs/codex/ROADMAP\.md\) ===" -or -not $out.Contains("${card}_CARD_SENTINEL")) {
            throw "D30: $card must be read from ROADMAP when its design spec does not hold it."
        }
    }
    Write-Output 'AGENT_CONTEXT_ORDER_GREEN'
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
}
