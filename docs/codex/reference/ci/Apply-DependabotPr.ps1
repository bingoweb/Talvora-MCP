[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $Pr,
    [switch] $DryRun
)
# Applies a Dependabot PR's version change to local main the Talvora way: only the version
# files are taken from the PR, then EVERY packages.lock.json is regenerated (Dependabot cannot
# do this for transitively pinned packages → NU1004), checked, committed once and pushed to both
# remotes. The PR itself is closed, never merged on GitHub (that would fork GitHub from Gitea).
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repoRoot = Split-Path -Parent $PSScriptRoot
$repository = 'bingoweb/Talvora-MCP'
$versionFiles = @('Directory.Packages.props', 'global.json')

function Invoke-Checked {
    param([string] $Executable, [string[]] $Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable $($Arguments -join ' ') failed ($LASTEXITCODE)." }
}

Push-Location $repoRoot
try {
    # Only docs/codex/STATUS.md may be pre-edited (queue tick + Log line); it is committed with the update.
    $dirty = @(& git status --porcelain | Where-Object { $_.Substring(3) -ne 'docs/codex/STATUS.md' })
    if ($dirty.Count -gt 0) { throw 'Working tree must be clean (except docs/codex/STATUS.md) before applying a Dependabot PR.' }
    if ((& git rev-parse --abbrev-ref HEAD).Trim() -ne 'main') { throw 'Run this on main.' }

    $info = & gh pr view $Pr --repo $repository --json state,author,headRefOid,title | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "Cannot read PR #$Pr." }
    if ($info.state -ne 'OPEN') { throw "PR #$Pr is $($info.state)." }
    if ($info.author.login -notlike '*dependabot*') { throw "PR #$Pr is not a Dependabot PR." }

    $ref = "refs/talvora/dependabot-pr-$Pr"
    Invoke-Checked git @('fetch', 'github', "+pull/$Pr/head:$ref")
    $prHead = (& git rev-parse $ref).Trim()
    $base = (& git merge-base HEAD $prHead).Trim()

    $patch = & git diff $base $prHead -- @versionFiles
    if (-not $patch) { throw "PR #$Pr does not change $($versionFiles -join ' or ')." }
    $patchFile = Join-Path ([IO.Path]::GetTempPath()) "talvora-dependabot-$Pr.patch"
    [IO.File]::WriteAllText($patchFile, (($patch -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
    Invoke-Checked git @('apply', '--3way', $patchFile)
    Remove-Item -LiteralPath $patchFile

    Invoke-Checked dotnet @('restore', 'Talvora.slnx', '--force-evaluate', '--nologo', '-v', 'q')
    Invoke-Checked dotnet @('restore', 'src/Talvora.Installer/Talvora.Installer.csproj', '--force-evaluate', '--nologo', '-v', 'q')
    Invoke-Checked pwsh @('-NoProfile', '-File', 'tests/PlatformPackageLockstepRegression.ps1')
    Invoke-Checked dotnet @('restore', 'Talvora.slnx', '--locked-mode', '--nologo', '-v', 'q')
    Invoke-Checked dotnet @('restore', 'src/Talvora.Installer/Talvora.Installer.csproj', '--locked-mode', '--nologo', '-v', 'q')
    Invoke-Checked dotnet @('build', 'Talvora.slnx', '-c', 'Release', '--no-restore', '-v', 'q', '-nologo')

    $changed = @(& git status --porcelain | ForEach-Object { $_.Substring(3) })
    Write-Host ("Changed files: " + ($changed -join ', '))
    if ($DryRun) {
        Write-Host 'DRY RUN: reverting; nothing committed or pushed.'
        Invoke-Checked git @('restore', '--staged', '--worktree', '--', '.', ':!docs/codex/STATUS.md')
        exit 0
    }

    Invoke-Checked pwsh @('-NoProfile', '-File', 'scripts/Invoke-QualityGate.ps1', '-Scope', 'Full')
    Invoke-Checked git @('add', '--', 'Directory.Packages.props', 'global.json', '*packages.lock.json', 'docs/codex/STATUS.md')
    $message = "build(deps): apply Dependabot PR #$Pr`n`n$($info.title)`nAll lock files regenerated with --force-evaluate.`n`nCard: DEP$Pr"
    Invoke-Checked git @('commit', '-m', $message)
    Invoke-Checked git @('push', 'origin', 'main')
    Invoke-Checked git @('push', 'github', 'main')
    Invoke-Checked gh @('pr', 'close', "$Pr", '--repo', $repository, '--comment',
        "Applied on main as $((& git rev-parse --short HEAD).Trim()) with all lock files regenerated (scripts/Apply-DependabotPr.ps1).")
    & pwsh -NoProfile -File scripts/Get-CiStatus.ps1
    exit $LASTEXITCODE
}
finally {
    & git update-ref -d "refs/talvora/dependabot-pr-$Pr" 2>$null
    Pop-Location
}
