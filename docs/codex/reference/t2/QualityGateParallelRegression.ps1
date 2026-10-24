$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# T2: the quality gate runs independent suites side by side. A parallel run must still report every failure
# in the listed order, keep each check's own log and 60-line tail, and really overlap the checks.
$repoRoot = Split-Path -Parent $PSScriptRoot
$gatePath = Join-Path $repoRoot 'scripts/Invoke-QualityGate.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($gatePath, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) { throw 'Invoke-QualityGate.ps1 does not parse.' }
$function = $ast.Find({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-ParallelQualityChecks'
}, $true)
if ($null -eq $function) { throw 'The gate must run independent suites with Invoke-ParallelQualityChecks.' }
. ([ScriptBlock]::Create($function.Extent.Text))

$source = [IO.File]::ReadAllText($gatePath)
foreach ($required in @("'--semantic-only'", "'--without-semantic'", "'GiteaProtocolReadinessRegression.ps1'",
        "'GiteaStopChainRegression.ps1'", "'InstallerDependencyProvenanceRegression.ps1'")) {
    if (-not $source.Contains($required)) { throw "The gate must contain $required." }
}

$repoRoot = Join-Path ([IO.Path]::GetTempPath()) ('TalvoraParallelGate-' + [guid]::NewGuid().ToString('N'))
$script:GateCaptureRoot = Join-Path $repoRoot 'logs'
New-Item -ItemType Directory -Path $script:GateCaptureRoot -Force | Out-Null
try {
    $script:GateChecks = 0
    $script:GateFailure = $null
    $script:GateFailureCount = 0
    $script:GateVerbose = $false
    $sleep = { param($Seconds, $Code, $Text) @('-NoLogo','-NoProfile','-NonInteractive','-Command',"Start-Sleep -Seconds $Seconds; Write-Output '$Text'; exit $Code") }
    $checks = @(
        [pscustomobject]@{ Name = 'SlowPass'; Executable = 'pwsh'; Arguments = (& $sleep 3 0 'SLOW_PASS'); Quiet = $false },
        [pscustomobject]@{ Name = 'FirstFail'; Executable = 'pwsh'; Arguments = (& $sleep 1 3 'FIRST_FAIL_TAIL'); Quiet = $true },
        [pscustomobject]@{ Name = 'QuietPass'; Executable = 'pwsh'; Arguments = (& $sleep 3 0 'QUIET_PASS'); Quiet = $true },
        [pscustomobject]@{ Name = 'SecondFail'; Executable = 'pwsh'; Arguments = (& $sleep 1 4 'SECOND_FAIL_TAIL'); Quiet = $false },
        [pscustomobject]@{ Name = 'Missing'; Executable = 'talvora-no-such-program'; Arguments = @('x'); Quiet = $false }
    )
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $output = @(Invoke-ParallelQualityChecks -Checks $checks -ThrottleLimit 5 6>&1)
    $timer.Stop()
    $result = $output | Where-Object { $_ -is [bool] } | Select-Object -Last 1
    $text = ($output | Where-Object { $_ -isnot [bool] } | ForEach-Object { [string]$_ }) -join "`n"

    if ($result -ne $false) { throw 'A failing parallel check must fail the phase.' }
    if ($timer.Elapsed.TotalSeconds -ge 7) { throw "Checks did not overlap ($($timer.Elapsed.TotalSeconds) s for 3+1+3+1 s of work)." }
    if ($script:GateChecks -ne 5) { throw 'Every parallel check must count once.' }
    if ($script:GateFailure -cne 'FirstFail') { throw "The first failure in listed order must be named; got $($script:GateFailure)." }
    if ($script:GateFailureCount -ne 3) { throw "All three failures must be counted; got $($script:GateFailureCount)." }
    foreach ($expected in @('PASS SlowPass', 'FAIL FirstFail', 'FIRST_FAIL_TAIL', 'FAIL SecondFail', 'SECOND_FAIL_TAIL', 'FAIL Missing', 'exit code 3', 'exit code 4')) {
        if (-not $text.Contains($expected)) { throw "Parallel phase output is missing '$expected'." }
    }
    if ($text.Contains('QuietPass') -or $text.Contains('ParallelPhase')) { throw 'Quiet passes and a failed phase must not print PASS lines.' }
    if ($text.IndexOf('FAIL FirstFail') -gt $text.IndexOf('FAIL SecondFail')) { throw 'Results must be reported in the listed order.' }
    foreach ($index in 1..4) {
        if (-not (Test-Path -LiteralPath (Join-Path $script:GateCaptureRoot "$index.log"))) { throw "Check $index has no log." }
    }

    $script:GateChecks = 0
    $script:GateFailure = $null
    $script:GateFailureCount = 0
    $green = @(Invoke-ParallelQualityChecks -Checks @($checks[0], $checks[2]) 6>&1)
    if (($green | Where-Object { $_ -is [bool] } | Select-Object -Last 1) -ne $true -or
        -not (($green | ForEach-Object { [string]$_ }) -join "`n").Contains('PASS ParallelPhase (2 checks)')) {
        throw 'A green parallel phase must pass and print its summary.'
    }
}
finally {
    Remove-Item -LiteralPath $repoRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'QUALITY_GATE_PARALLEL_GREEN'
