# D108: Dependabot workflow patches are selected and validated inside throwaway repositories.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('talvora-dependabot-actions-' + [Guid]::NewGuid().ToString('N'))
$utf8 = [Text.UTF8Encoding]::new($false)
$oldCheckout = ('a' * 40) + ' # v7.0.1'
$oldDotnet = ('b' * 40) + ' # v6.1.0'
$validCheckout = ('c' * 40) + ' # v7.1.0'
$workflowPaths = @('.github/workflows/windows-ci.yml', '.github/workflows/release.yml')

function Assert-Case {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Write-Text {
    param([string] $Path, [string] $Text)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Invoke-GitChecked {
    param([string] $Repo, [string[]] $Arguments)
    $result = @(& git -C $Repo @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($result -join '; ')"
    }
    return $result
}

function New-Fixture {
    param([string] $CaseName)
    $fixture = Join-Path $work $CaseName
    New-Item -ItemType Directory -Force -Path $fixture | Out-Null
    Invoke-GitChecked $fixture @('init', '-b', 'main') | Out-Null

    Write-Text (Join-Path $fixture '.gitignore') ('artifacts/' + [Environment]::NewLine)
    Write-Text (Join-Path $fixture 'README.md') ('Original readme' + [Environment]::NewLine)
    Write-Text (Join-Path $fixture 'scripts/Apply-DependabotPr.ps1') (
        [IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/Apply-DependabotPr.ps1'))
    )
    foreach ($relative in $workflowPaths) {
        $name = if ($relative -match 'windows-ci') { 'CI' } else { 'Release' }
        $lines = @(
            "name: $name",
            'jobs:',
            '  checks:',
            '    steps:',
            ('      - uses: actions/checkout@' + $oldCheckout),
            ('      - uses: actions/setup-dotnet@' + $oldDotnet),
            ''
        )
        Write-Text (Join-Path $fixture $relative) ($lines -join [Environment]::NewLine)
    }
    Invoke-GitChecked $fixture @('add', '--', '.') | Out-Null
    Invoke-GitChecked $fixture @('-c', 'user.name=Regression', '-c', 'user.email=regression@example.invalid', 'commit', '-m', 'Baseline') | Out-Null
    return $fixture
}

function Invoke-FixtureCase {
    param(
        [string] $Name,
        [string] $NewCheckout,
        [string] $OtherChange,
        [bool] $ShouldPass,
        [string] $ExpectedError
    )
    $fixture = New-Fixture $Name
    $before = [ordered]@{}
    foreach ($relative in @($workflowPaths + 'README.md')) {
        $before[$relative] = [IO.File]::ReadAllText((Join-Path $fixture $relative))
    }
    if ($OtherChange -eq 'readme') {
        Write-Text (Join-Path $fixture 'README.md') ('Updated readme' + [Environment]::NewLine)
    }
    elseif ($OtherChange -eq 'name') {
        $file = Join-Path $fixture '.github/workflows/windows-ci.yml'
        Write-Text $file ([IO.File]::ReadAllText($file).Replace('name: CI', 'name: Changed CI'))
    }
    else {
        foreach ($relative in $workflowPaths) {
            $file = Join-Path $fixture $relative
            Write-Text $file ([IO.File]::ReadAllText($file).Replace(
                ('actions/checkout@' + $oldCheckout),
                ('actions/checkout@' + $NewCheckout)))
        }
    }

    $patchPath = Join-Path $work ($Name + '.patch')
    $diff = @(Invoke-GitChecked $fixture @('diff', '--'))
    Write-Text $patchPath (($diff -join [Environment]::NewLine) + [Environment]::NewLine)
    Invoke-GitChecked $fixture @('checkout', '--', '.') | Out-Null

    $script = Join-Path $fixture 'scripts/Apply-DependabotPr.ps1'
    $stdout = @(& pwsh -NoProfile -File $script -Pr 1 -PatchFile $patchPath -SelectOnly 2>&1)
    $code = $LASTEXITCODE
    $resultPath = Join-Path $fixture 'artifacts/dependabot/pr-1.result'
    Assert-Case (Test-Path -LiteralPath $resultPath -PathType Leaf) "${Name}: no result file (exit $code)."
    $result = [IO.File]::ReadAllText($resultPath).Trim()
    if ($ShouldPass) {
        Assert-Case ($code -eq 0) "${Name}: expected exit 0, got $code; $result; $($stdout -join '; ')"
        Assert-Case ($result -ceq 'SELECTED .github/workflows/release.yml, .github/workflows/windows-ci.yml') "${Name}: unexpected result $result"
        foreach ($relative in $workflowPaths) {
            $now = [IO.File]::ReadAllText((Join-Path $fixture $relative))
            Assert-Case ($now.Contains('actions/checkout@' + $validCheckout)) "${Name}: $relative did not take the selected pin."
            Assert-Case ($now.Contains('actions/setup-dotnet@' + $oldDotnet)) "${Name}: $relative changed the other action."
        }
    }
    else {
        Assert-Case ($code -eq 1) "${Name}: expected exit 1, got $code; $result; $($stdout -join '; ')"
        Assert-Case ($result.StartsWith('FAILED: ')) "${Name}: result did not begin FAILED: $result"
        Assert-Case ($result.Contains($ExpectedError)) "${Name}: missing rejection text '$ExpectedError': $result"
        foreach ($relative in @($workflowPaths + 'README.md')) {
            $now = [IO.File]::ReadAllText((Join-Path $fixture $relative))
            Assert-Case ($now -ceq $before[$relative]) "${Name}: rejected patch changed $relative."
        }
    }
    return $true
}

New-Item -ItemType Directory -Force -Path $work | Out-Null
$cases = 0
try {
    if (Invoke-FixtureCase 'valid' $validCheckout '' $true '') { $cases++ }

    # Every malformed pin is rejected before touching either workflow.
    foreach ($invalid in @(
        @{ Name = 'tag'; Value = 'v7 # v7.1.0' },
        @{ Name = 'short'; Value = 'abc123 # v7.1.0' },
        @{ Name = 'no-comment'; Value = 'c' * 40 },
        @{ Name = 'short-comment'; Value = ('c' * 40) + ' # v7' },
        @{ Name = 'incomplete-comment'; Value = ('c' * 40) + ' # v7.1' }
    )) {
        $line = 'uses: actions/checkout@' + $invalid.Value
        if (Invoke-FixtureCase $invalid.Name $invalid.Value '' $false $line) { $cases++ }
    }

    if (Invoke-FixtureCase 'unrelated-workflow-line' '' 'name' $false 'name: Changed CI') { $cases++ }
    if (Invoke-FixtureCase 'new-major' (('c' * 40) + ' # v8.0.0') '' $false 'PR #1 moves actions/checkout to a new major version; Claude decides majors.') { $cases++ }
    if (Invoke-FixtureCase 'readme-only' '' 'readme' $false 'PR #1 does not change') { $cases++ }
    Write-Output "DEPENDABOT_APPLY_GREEN cases=$cases"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
