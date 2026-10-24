$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# T4: a push made only of Markdown documents under docs/ runs the changed-scope gate; every other push still
# runs the full gate, the installer and the installed-runtime checks.
$repoRoot = Split-Path -Parent $PSScriptRoot
$workflow = [IO.File]::ReadAllText((Join-Path $repoRoot '.github/workflows/windows-ci.yml')).Replace("`r`n", "`n")

$expressionMatch = [regex]::Match($workflow, '(?ms)^\s*(\$docsOnly = \$changedFiles\.Count -gt 0 -and\s*\n\s*@\(.+?\)\.Count -eq 0)\s*$')
if (-not $expressionMatch.Success) { throw 'The workflow must classify documentation-only changes with $docsOnly.' }
$classify = [ScriptBlock]::Create('param([string[]] $changedFiles) ' + $expressionMatch.Groups[1].Value + '; $docsOnly')
foreach ($case in @(
    @{ Files = @('docs/codex/STATUS.md', 'docs/codex/ROADMAP.md'); Expected = $true },
    @{ Files = @('docs/codex/STATUS.md', 'src/Talvora/Program.cs'); Expected = $false },
    @{ Files = @('docs/codex/reference/t2/notes.md'); Expected = $false },
    @{ Files = @('README.md'); Expected = $false },
    @{ Files = @('AGENTS.md'); Expected = $false },
    @{ Files = @('docs/codex/reference/worklog-phosphor/previews/01.png'); Expected = $false },
    @{ Files = @('docs/diagram.png'); Expected = $false },
    @{ Files = @(); Expected = $false }
)) {
    $actual = & $classify -changedFiles $case.Files
    if ($actual -ne $case.Expected) { throw "Docs-only classification of [$($case.Files -join ', ')] must be $($case.Expected)." }
}

foreach ($required in @('id: changes', '"docs_only=', '"base_ref=', 'TALVORA_CI_BASE: ${{ steps.changes.outputs.base_ref }}',
        '-Scope Changed -Base $env:TALVORA_CI_BASE', '-Scope Full')) {
    if (-not $workflow.Contains($required)) { throw "The workflow must contain '$required'." }
}

$steps = [regex]::Split($workflow, '(?m)^(?=      - (?:name|uses): )') | Where-Object { $_ -match '^      - ' }
$docsIndex = -1
for ($index = 0; $index -lt $steps.Count; $index++) {
    if ($steps[$index] -match '^      - name: Validate documentation-only change\n') { $docsIndex = $index }
}
if ($docsIndex -lt 0) { throw 'The documentation-only step is missing.' }
if ($steps[$docsIndex] -notmatch "\n        if: steps\.changes\.outputs\.docs_only == 'true'\n") { throw 'The documentation-only step must run only for documentation-only changes.' }
if ($docsIndex + 1 -ge $steps.Count) { throw 'The full build and runtime steps must follow the documentation-only step.' }
foreach ($step in $steps[($docsIndex + 1)..($steps.Count - 1)]) {
    if ($step -notmatch "\n        if: (?:always\(\) && )?steps\.changes\.outputs\.docs_only != 'true'\n") {
        throw "Every build or runtime step must be skipped only for documentation-only changes: $(($step -split "`n")[0].Trim())"
    }
}

Write-Output 'CI_DOCS_ONLY_GREEN'
