[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $Pr,
    [switch] $DryRun
)
# Prepares a Dependabot PR's version change on local main the Talvora way: only the version
# files are taken from the PR, then EVERY packages.lock.json is regenerated (Dependabot cannot
# do this for transitively pinned packages → NU1004) and checked with the Full gate. It never
# commits, pushes or closes: the agent does that with talvora_git_run / talvora_gh_run (P-25),
# so the delivery gates of those tools apply. The PR is closed, never merged on GitHub.
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
    # Only STATUS (queue/Log) and the refreshed Context7 evidence may be pre-edited; both go into the DEP commit.
    $allowed = @('docs/codex/STATUS.md', '.context7/verification.json')
    $dirty = @(& git status --porcelain | Where-Object { $allowed -notcontains $_.Substring(3) })
    if ($dirty.Count -gt 0) { throw 'Working tree must be clean (except docs/codex/STATUS.md and .context7/verification.json) before applying a Dependabot PR.' }
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

    # THIRD-PARTY-NOTICES.md lists every locked runtime package with its exact version
    # (tests/ThirdPartyNoticesSourceRegression.ps1). A listed package gets its new version in
    # place; a package that is not listed yet needs a license review, so the script stops.
    $noticePath = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
    $noticeBytes = [IO.File]::ReadAllBytes($noticePath)
    $hasBom = $noticeBytes.Length -ge 3 -and $noticeBytes[0] -eq 0xEF -and $noticeBytes[1] -eq 0xBB -and $noticeBytes[2] -eq 0xBF
    $notice = [IO.File]::ReadAllText($noticePath)
    $updated = $notice
    foreach ($lockPath in @('src/Talvora/packages.lock.json', 'src/Talvora.Tray/packages.lock.json')) {
        $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
        foreach ($tfm in $lock.dependencies.PSObject.Properties) {
            foreach ($package in $tfm.Value.PSObject.Properties) {
                $resolved = $package.Value.PSObject.Properties['resolved']
                if ($null -eq $resolved -or [string]::IsNullOrWhiteSpace([string]$resolved.Value)) { continue }
                $id = [regex]::Escape($package.Name)
                # Two notice formats: list lines "- `Id 1.2.3`" and table rows "| `Id` | 1.2.3 | ...".
                $patterns = @(
                    ('(?im)^(- `' + $id + ' )[^`\r\n]+(`)'),
                    ('(?im)^(\| `' + $id + '` \| )[^|\r\n]+?( \|)'))
                $found = $false
                foreach ($pattern in $patterns) {
                    if ([regex]::IsMatch($updated, $pattern)) {
                        $found = $true
                        $updated = [regex]::Replace($updated, $pattern, '${1}' + [string]$resolved.Value + '${2}')
                    }
                }
                if (-not $found) {
                    throw "THIRD-PARTY-NOTICES.md has no line for $($package.Name); a new runtime package needs a license review (Findings for Claude)."
                }
            }
        }
    }
    if ($updated -ne $notice) { [IO.File]::WriteAllText($noticePath, $updated, [Text.UTF8Encoding]::new($hasBom)) }


    $changed = @(& git status --porcelain | ForEach-Object { $_.Substring(3) })
    Write-Host ("Changed files: " + ($changed -join ', '))
    if ($DryRun) {
        Write-Host 'DRY RUN: reverting; nothing committed or pushed.'
        Invoke-Checked git @('restore', '--staged', '--worktree', '--', '.', ':!docs/codex/STATUS.md', ':!.context7/verification.json')
        exit 0
    }

    Invoke-Checked pwsh @('-NoProfile', '-File', 'scripts/Invoke-QualityGate.ps1', '-Scope', 'Full')
    Write-Host "READY DEP$($Pr): commit these files with talvora_git_run (Card: DEP$Pr), push both remotes, then close PR #$Pr with talvora_gh_run."
    exit 0
}
finally {
    & git update-ref -d "refs/talvora/dependabot-pr-$Pr" 2>$null
    Pop-Location
}
