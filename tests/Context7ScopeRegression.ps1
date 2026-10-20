$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'Talvora-Context7Scope-' + [Guid]::NewGuid().ToString('N'))
$scriptDirectory = Join-Path $fixtureRoot 'scripts'
$fixtureGate = Join-Path $scriptDirectory 'Validate-Context7Gate.ps1'
$previousGitHubActions = $env:GITHUB_ACTIONS

function Assert-Context7GateScenario {
    param(
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][bool]$RequiresEvidence
    )

    $fullPath = Join-Path $fixtureRoot $RelativePath
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullPath)) |
        Out-Null
    [IO.File]::WriteAllText($fullPath, 'test fixture')
    try {
        $failure = $null
        try {
            & $fixtureGate 2>&1 | Out-Null
        }
        catch {
            $failure = $_.Exception
        }

        if ($RequiresEvidence) {
            if ($null -eq $failure -or
                -not $failure.Message.Contains(
                    'Context7 evidence must be refreshed',
                    [StringComparison]::Ordinal)) {
                throw "A code-only change must require evidence: $RelativePath"
            }
        }
        elseif ($null -ne $failure) {
            throw "Documentation-only changes must not require evidence: $RelativePath"
        }
    }
    finally {
        [IO.File]::Delete($fullPath)
    }
}

try {
    [IO.Directory]::CreateDirectory($scriptDirectory) | Out-Null
    [IO.File]::Copy(
        (Join-Path $repoRoot 'scripts/Validate-Context7Gate.ps1'),
        $fixtureGate)
    $env:GITHUB_ACTIONS = 'false'

    Push-Location -LiteralPath $fixtureRoot
    try {
        & git init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Context7 fixture git init failed.' }
        & git -c "safe.directory=$fixtureRoot" add -- scripts/Validate-Context7Gate.ps1
        if ($LASTEXITCODE -ne 0) { throw 'Context7 fixture git add failed.' }
        & git -c "safe.directory=$fixtureRoot" -c 'user.name=Talvora Fixture' -c 'user.email=fixture@example.invalid' commit --quiet -m 'Baseline gate'
        if ($LASTEXITCODE -ne 0) { throw 'Context7 fixture git commit failed.' }

        foreach ($path in @(
            'src/Talvora.Tray/ControlCenter.xaml',
            'src/Talvora/Assets/manifest.json',
            'src/Talvora/PenpotAiPlugin/static.js',
            'src/Talvora/PenpotAiPlugin/index.html',
            'tests/OnlyPowerShellRegression.ps1'
        )) {
            Assert-Context7GateScenario -RelativePath $path -RequiresEvidence $true
        }
        Assert-Context7GateScenario -RelativePath 'docs/research.md' -RequiresEvidence $false
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

Write-Output 'CONTEXT7_SCOPE_REGRESSION_GREEN'
