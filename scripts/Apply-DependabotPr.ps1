[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int] $Pr,
    [switch] $DryRun,
    [string] $PatchFile,
    [switch] $SelectOnly
)
# Prepares a Dependabot PR's version change on local main the Talvora way: only the version
# files are taken from the PR, then EVERY packages.lock.json is regenerated (Dependabot cannot
# do this for transitively pinned packages → NU1004) and checked with the Full gate. It never
# commits, pushes or closes: the agent does that with talvora_git_run / talvora_gh_run (P-25),
# so the delivery gates of those tools apply. The PR is closed, never merged on GitHub.
# Long run (~6 min): start it detached with talvora_user_process_start and poll
# artifacts/dependabot/pr-<n>.result (RUNNING / READY / DRY RUN OK / FAILED) and pr-<n>.log (D14).
# Only one run at a time: a second start exits 3 at once and changes nothing.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repoRoot = Split-Path -Parent $PSScriptRoot
$repository = 'bingoweb/Talvora-MCP'
$versionFiles = @('Directory.Packages.props', 'global.json')
$workflowPathPattern = '^\.github/workflows/[A-Za-z0-9_.-]+\.yml$'
$resultDirectory = Join-Path $repoRoot 'artifacts\dependabot'
$logPath = Join-Path $resultDirectory "pr-$Pr.log"
$resultPath = Join-Path $resultDirectory "pr-$Pr.result"

function Write-Log {
    param([string] $Text)
    Write-Host $Text
    Add-Content -LiteralPath $logPath -Value $Text -Encoding utf8
}

function Invoke-Checked {
    param([string] $Executable, [string[]] $Arguments)
    Write-Log "> $Executable $($Arguments -join ' ')"
    & $Executable @Arguments 2>&1 | ForEach-Object { Write-Log "$_" }
    if ($LASTEXITCODE -ne 0) { throw "$Executable $($Arguments -join ' ') failed ($LASTEXITCODE)." }
}

function Assert-WorkflowPatch {
    param([string] $Section, [string] $Path)

    $removed = [Collections.Generic.List[string]]::new()
    $added = [Collections.Generic.List[string]]::new()
    foreach ($line in ($Section -split '\r?\n')) {
        if ($line.StartsWith('--- ') -or $line.StartsWith('+++ ')) { continue }
        if ($line.StartsWith('-')) { $removed.Add($line.Substring(1)) }
        if ($line.StartsWith('+')) { $added.Add($line.Substring(1)) }
    }
    $pinPattern = '^\s*(?:-\s*)?uses:\s+(?<action>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*)@(?<sha>[0-9a-f]{40}) # v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$'
    if ($removed.Count -ne $added.Count -or $added.Count -eq 0) {
        throw "Workflow update has unmatched uses lines: $Path"
    }
    for ($i = 0; $i -lt $added.Count; $i++) {
        $before = [regex]::Match($removed[$i], $pinPattern)
        $after = [regex]::Match($added[$i], $pinPattern)
        if (-not $after.Success) { throw "Invalid workflow update line: $($added[$i])" }
        if (-not $before.Success) { throw "Invalid workflow update line: $($removed[$i])" }
        $action = $after.Groups['action'].Value
        if ($before.Groups['action'].Value -cne $action) {
            throw "Workflow update changes the action identity: $($added[$i])"
        }
        if ($before.Groups['major'].Value -cne $after.Groups['major'].Value) {
            throw "PR #$Pr moves $action to a new major version; Claude decides majors."
        }
    }
}

New-Item -ItemType Directory -Force -Path $resultDirectory | Out-Null
try {
    $runLock = [IO.File]::Open((Join-Path $resultDirectory 'apply.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
}
catch {
    Write-Host 'Another Apply-DependabotPr.ps1 run is already active; this run changed nothing.'
    exit 3
}

Push-Location $repoRoot
try {
    Set-Content -LiteralPath $logPath -Value "Apply-DependabotPr -Pr $Pr started $([DateTime]::UtcNow.ToString('u')) pid $PID" -Encoding utf8
    Set-Content -LiteralPath $resultPath -Value "RUNNING pid $PID" -Encoding utf8
    # Only STATUS (queue/Log) and the refreshed Context7 evidence may be pre-edited; both go into the DEP commit.
    $allowed = @('docs/codex/STATUS.md', '.context7/verification.json')
    $dirty = @(& git status --porcelain | Where-Object { $allowed -notcontains $_.Substring(3) })
    if ($dirty.Count -gt 0) { throw "Working tree must be clean (except docs/codex/STATUS.md and .context7/verification.json) before applying a Dependabot PR. Unexpected: $($dirty -join '; ')" }
    if ((& git rev-parse --abbrev-ref HEAD).Trim() -ne 'main') { throw 'Run this on main.' }

    if ($PatchFile) {
        if (-not (Test-Path -LiteralPath $PatchFile -PathType Leaf)) {
            throw "Patch file was not found: $PatchFile"
        }
        $rawPatch = [IO.File]::ReadAllText([IO.Path]::GetFullPath($PatchFile))
    }
    else {
        $info = & gh pr view $Pr --repo $repository --json state,author,headRefOid,title | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { throw "Cannot read PR #$Pr." }
        if ($info.state -ne 'OPEN') { throw "PR #$Pr is $($info.state)." }
        if ($info.author.login -notlike '*dependabot*') { throw "PR #$Pr is not a Dependabot PR." }

        $ref = "refs/talvora/dependabot-pr-$Pr"
        Invoke-Checked git @('fetch', 'github', "+pull/$Pr/head:$ref")
        $prHead = (& git rev-parse $ref).Trim()
        $base = (& git merge-base HEAD $prHead).Trim()
        $rawPatch = @(& git diff $base $prHead) -join [string][char]10
        if ($LASTEXITCODE -ne 0) { throw "Cannot read PR #$Pr diff." }
    }

    # The same path selection is applied to PRs fetched from GitHub and fixture patches.
    $rawPatch = [regex]::Replace($rawPatch, '\r\n?', [string][char]10)
    $headers = [regex]::Matches($rawPatch, '(?m)^diff --git a/(?<from>[^\n]+) b/(?<to>[^\n]+)$')
    $selectedSections = [Collections.Generic.List[string]]::new()
    $selectedPaths = [Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $headers.Count; $i++) {
        $header = $headers[$i]
        $path = $header.Groups['to'].Value
        if ($path -cne $header.Groups['from'].Value) { continue }
        if ($versionFiles -cnotcontains $path -and $path -cnotmatch $workflowPathPattern) { continue }
        $end = if ($i + 1 -lt $headers.Count) { $headers[$i + 1].Index } else { $rawPatch.Length }
        $section = $rawPatch.Substring($header.Index, $end - $header.Index)
        if ($path -cmatch $workflowPathPattern) { Assert-WorkflowPatch -Section $section -Path $path }
        $selectedSections.Add($section)
        $selectedPaths.Add($path)
    }
    if ($selectedSections.Count -eq 0) {
        throw "PR #$Pr does not change Directory.Packages.props, global.json or .github/workflows/*.yml."
    }
    $selectedPatch = [string]::Concat($selectedSections.ToArray())
    if (-not $selectedPatch.EndsWith([string][char]10)) { $selectedPatch += [string][char]10 }
    $patchToApply = Join-Path ([IO.Path]::GetTempPath()) "talvora-dependabot-$Pr.patch"
    [IO.File]::WriteAllText($patchToApply, $selectedPatch, [Text.UTF8Encoding]::new($false))
    Invoke-Checked git @('apply', '--3way', $patchToApply)
    Remove-Item -LiteralPath $patchToApply

    if ($SelectOnly) {
        $pathsText = @($selectedPaths | Sort-Object -Unique) -join ', '
        $selected = "SELECTED $pathsText"
        Write-Log $selected
        Set-Content -LiteralPath $resultPath -Value $selected -Encoding utf8
        exit 0
    }

    $hasPackageFiles = @($selectedPaths | Where-Object { $versionFiles -ccontains $_ }).Count -gt 0
    if ($hasPackageFiles) {
        if ($selectedPaths -ccontains 'global.json') {
            $sdkVersion = [string]((Get-Content -LiteralPath (Join-Path $repoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version)
            Write-Log "> scripts/Update-DotnetSdkPatch.ps1 -Version $sdkVersion"
            & (Join-Path $PSScriptRoot 'Update-DotnetSdkPatch.ps1') -Version $sdkVersion |
                ForEach-Object { Write-Log "$_" }
        }

        Invoke-Checked dotnet @('restore', 'Talvora.slnx', '--force-evaluate', '--nologo', '-v', 'q')
        Invoke-Checked dotnet @('restore', 'src/Talvora.Installer/Talvora.Installer.csproj', '--force-evaluate', '--nologo', '-v', 'q')
        Invoke-Checked pwsh @('-NoProfile', '-File', 'tests/PlatformPackageLockstepRegression.ps1')
        Invoke-Checked dotnet @('restore', 'Talvora.slnx', '--locked-mode', '--nologo', '-v', 'q')
        Invoke-Checked dotnet @('restore', 'src/Talvora.Installer/Talvora.Installer.csproj', '--locked-mode', '--nologo', '-v', 'q')
        Invoke-Checked dotnet @('build', 'Talvora.slnx', '-c', 'Release', '--no-restore', '-v', 'q', '-nologo')

        # THIRD-PARTY-NOTICES.md lists every locked runtime package with its exact version
        # (tests/ThirdPartyNoticesSourceRegression.ps1). Update-ThirdPartyNotices.ps1 writes the new
        # versions in place and stops when a new package needs a license review (P-56).
        & (Join-Path $PSScriptRoot 'Update-ThirdPartyNotices.ps1') | ForEach-Object { Write-Log "$_" }
    }

    $changed = @(& git status --porcelain | ForEach-Object { $_.Substring(3) })
    Write-Log ("Changed files: " + ($changed -join ', '))
    if ($DryRun) {
        Write-Log 'DRY RUN: reverting; nothing committed or pushed.'
        Invoke-Checked git @('restore', '--staged', '--worktree', '--', '.', ':!docs/codex/STATUS.md', ':!.context7/verification.json')
        Set-Content -LiteralPath $resultPath -Value 'DRY RUN OK' -Encoding utf8
        exit 0
    }

    Invoke-Checked pwsh @('-NoProfile', '-File', 'scripts/Invoke-QualityGate.ps1', '-Scope', 'Full')
    $ready = "READY DEP$($Pr): commit these files with talvora_git_run (Card: DEP$Pr), push both remotes, then close PR #$Pr with talvora_gh_run."
    Write-Log $ready
    Set-Content -LiteralPath $resultPath -Value $ready -Encoding utf8
    exit 0
}
catch {
    $failure = "FAILED: $($_.Exception.Message)"
    Write-Log $failure
    Set-Content -LiteralPath $resultPath -Value $failure -Encoding utf8
    exit 1
}
finally {
    & git update-ref -d "refs/talvora/dependabot-pr-$Pr" 2>$null
    Pop-Location
    $runLock.Dispose()
}
