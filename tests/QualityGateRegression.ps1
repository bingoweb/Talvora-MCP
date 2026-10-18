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
        $workflow -notmatch 'NativeInstallerRuntimeRegression\.ps1') {
        throw 'CI must run the full shared gate and defer installed-runtime tests.'
    }

    $contextOutput = @(& $contextPath -Card K1 | Out-String) -join ''
    if ($contextOutput -notmatch '### K1\s+[—-]' -or
        $contextOutput -notmatch 'Invoke-QualityGate\.ps1' -or
        $contextOutput -match '### K2\s+[—-]') {
        throw 'The context helper must print the selected card without neighbouring cards.'
    }
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'QUALITY_GATE_REGRESSION_GREEN'