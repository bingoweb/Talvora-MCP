$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# T3: `-Scope Changed` runs only what a change can affect. It must never drop the always-on policy tests, must
# select everything for dependency/SDK changes, and must see uncommitted and untracked files.
$repoRoot = Split-Path -Parent $PSScriptRoot
$gatePath = Join-Path $repoRoot 'scripts/Invoke-QualityGate.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($gatePath, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) { throw 'Invoke-QualityGate.ps1 does not parse.' }
foreach ($name in @('Get-ChangedFiles', 'Get-ChangedScopePlan')) {
    $function = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $function) { throw "The gate must define $name." }
    . ([ScriptBlock]::Create($function.Extent.Text))
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('TalvoraChangedGate-' + [guid]::NewGuid().ToString('N'))
$testsRoot = Join-Path $root 'tests'
New-Item -ItemType Directory -Path $testsRoot -Force | Out-Null
try {
    $always = @('InstallerRollbackStateRegression.ps1','ModernizationPolicyRegression.ps1',
        'QualityGateRegression.ps1','ThirdPartyNoticesSourceRegression.ps1')
    foreach ($name in $always) { Set-Content -LiteralPath (Join-Path $testsRoot $name) -Value '# always' }
    Set-Content -LiteralPath (Join-Path $testsRoot 'ResetProcessFilterRegression.ps1') -Value "Join-Path `$repoRoot 'scripts/Reset-And-Install.ps1'"
    Set-Content -LiteralPath (Join-Path $testsRoot 'OtherRegression.ps1') -Value '# unrelated'
    $tests = @(Get-ChildItem -LiteralPath $testsRoot -File -Filter '*.ps1' | Sort-Object Name)

    function Assert-Plan {
        param([string[]] $Files, [bool] $Build, [string[]] $Suites, [string[]] $Extra)
        $plan = Get-ChangedScopePlan -ChangedFiles $Files -Tests $tests
        $label = $Files -join ', '
        if ($plan.Build -ne $Build) { throw "Build for [$label] must be $Build." }
        if ((@($plan.Suites) -join ',') -cne (@($Suites) -join ',')) { throw "Suites for [$label]: expected '$($Suites -join ',')', got '$($plan.Suites -join ',')'." }
        $expected = @(@($always) + @($Extra) | Sort-Object)
        $actual = @($plan.Regressions | ForEach-Object Name | Sort-Object)
        if (($actual -join ',') -cne ($expected -join ',')) { throw "Regressions for [$label]: expected '$($expected -join ',')', got '$($actual -join ',')'." }
    }

    Assert-Plan -Files @() -Build $false -Suites @() -Extra @()
    Assert-Plan -Files @('docs/codex/STATUS.md') -Build $false -Suites @() -Extra @()
    Assert-Plan -Files @('scripts/Reset-And-Install.ps1') -Build $false -Suites @() -Extra @('ResetProcessFilterRegression.ps1')
    Assert-Plan -Files @('tests/OtherRegression.ps1') -Build $false -Suites @() -Extra @('OtherRegression.ps1')
    Assert-Plan -Files @('src/Talvora.Tray/Worklog/WorklogTheme.xaml') -Build $true -Suites @('Notification', 'IntegrationPolicy') -Extra @()
    Assert-Plan -Files @('src/Talvora/Tools/ToolchainTools.cs') -Build $true -Suites @('SourceEdit', 'Notification', 'IntegrationPolicy') -Extra @()
    Assert-Plan -Files @('src\Talvora\SourceEditing\RoslynSemanticEditEngine.cs') -Build $true -Suites @('SourceEditSemantic', 'SourceEdit', 'Notification', 'IntegrationPolicy') -Extra @()
    Assert-Plan -Files @('tests/Talvora.Smoke/Program.cs') -Build $true -Suites @('IntegrationPolicy') -Extra @()
    Assert-Plan -Files @('src/Talvora.Installer/Program.cs') -Build $true -Suites @('IntegrationPolicy') -Extra @()
    foreach ($dependency in @('Directory.Packages.props', 'global.json', 'src/Talvora/packages.lock.json')) {
        Assert-Plan -Files @($dependency) -Build $true -Suites @('SourceEditSemantic', 'SourceEdit', 'Notification', 'IntegrationPolicy') -Extra @('OtherRegression.ps1', 'ResetProcessFilterRegression.ps1')
    }

    # Get-ChangedFiles sees committed-range, staged, unstaged and untracked changes.
    $repo = Join-Path $root 'repo'
    New-Item -ItemType Directory -Path $repo -Force | Out-Null
    $git = { param([string[]] $Arguments) & git -C $repo -c user.name=fixture -c user.email=fixture@example.invalid -c commit.gpgsign=false @Arguments 2>&1 | Out-Null; if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed." } }
    & $git @('init', '-q', '-b', 'main')
    Set-Content -LiteralPath (Join-Path $repo 'a.txt') -Value 'a'
    Set-Content -LiteralPath (Join-Path $repo 'b.txt') -Value 'b'
    & $git @('add', '.')
    & $git @('commit', '-q', '-m', 'base')
    $base = (& git -C $repo rev-parse HEAD).Trim()
    Set-Content -LiteralPath (Join-Path $repo 'b.txt') -Value 'b2'
    & $git @('commit', '-q', '-am', 'second')
    Set-Content -LiteralPath (Join-Path $repo 'a.txt') -Value 'a2'
    New-Item -ItemType Directory -Path (Join-Path $repo 'docs') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $repo 'docs/new.md') -Value 'new'
    $repoRoot = $repo
    Push-Location -LiteralPath $repo
    try {
        $sinceHead = @(Get-ChangedFiles -Base '') -join ','
        $sinceBase = @(Get-ChangedFiles -Base $base) -join ','
    }
    finally { Pop-Location }
    if ($sinceHead -cne 'a.txt,docs/new.md') { throw "Changes since HEAD must be the uncommitted and untracked files; got '$sinceHead'." }
    if ($sinceBase -cne 'a.txt,b.txt,docs/new.md') { throw "Changes since the base must include committed files; got '$sinceBase'." }
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'QUALITY_GATE_CHANGED_GREEN'
