[CmdletBinding()]
param(
    [ValidateSet('Fast','Full','Changed')]
    [string] $Scope = 'Full',
    # T3, -Scope Changed: compare the working tree (plus untracked files) with this revision; default HEAD,
    # i.e. exactly the uncommitted card work. CI passes the previous pushed commit.
    [string] $Base = ''
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$script:GateChecks = 0
$script:GateFailure = $null
$script:GateFailureCount = 0
$script:GateVerbose = $PSBoundParameters.ContainsKey('Verbose')
$script:GateCaptureRoot = Join-Path ([IO.Path]::GetTempPath()) ('TalvoraQualityGate-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:GateCaptureRoot -Force | Out-Null
Write-Host ("Kimlik: " + [Security.Principal.WindowsIdentity]::GetCurrent().Name)

function Invoke-QualityCheck {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][scriptblock] $Action,
        [switch] $QuietSuccess
    )
    $script:GateChecks++
    $logPath = Join-Path $script:GateCaptureRoot ([string]$script:GateChecks + '.log')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action *> $logPath
        if (-not $?) { throw 'The step reported an unsuccessful PowerShell status.' }
        $timer.Stop()
        if ($script:GateVerbose) { Get-Content -LiteralPath $logPath | ForEach-Object { Write-Host $_ } }
        if (-not $QuietSuccess) {
            Write-Host ("PASS {0} {1:0.0} sn" -f $Name, $timer.Elapsed.TotalSeconds)
        }
        return $true
    }
    catch {
        $timer.Stop()
        $script:GateFailure = $Name
        Write-Host "FAIL $Name"
        if (Test-Path -LiteralPath $logPath) {
            Get-Content -LiteralPath $logPath -Tail 60 | ForEach-Object { Write-Host $_ }
        }
        Write-Host ("Reason: " + $_.Exception.Message)
        return $false
    }
}
# T2: independent suites are separate processes that share no state, so they run side by side. Each check
# still gets its own numbered log, its own PASS/FAIL line (in the order listed) and the 60-line failure tail.
function Invoke-ParallelQualityChecks {
    param(
        [Parameter(Mandatory)][object[]] $Checks,
        [int] $ThrottleLimit = [Math]::Max(2, [Environment]::ProcessorCount)
    )
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $running = foreach ($check in $Checks) {
        $script:GateChecks++
        $logPath = Join-Path $script:GateCaptureRoot ([string]$script:GateChecks + '.log')
        $job = Start-ThreadJob -ThrottleLimit $ThrottleLimit -ArgumentList @(
            $check.Executable, $check.Arguments, $logPath, $repoRoot) -ScriptBlock {
            param([string] $Executable, [string[]] $Arguments, [string] $LogPath, [string] $Root)
            $ErrorActionPreference = 'Stop'
            $PSNativeCommandUseErrorActionPreference = $false
            Set-Location -LiteralPath $Root
            $watch = [Diagnostics.Stopwatch]::StartNew()
            & $Executable @Arguments *> $LogPath
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Seconds = $watch.Elapsed.TotalSeconds }
        }
        [pscustomobject]@{ Check = $check; LogPath = $logPath; Job = $job }
    }
    $null = Wait-Job -Job @($running | ForEach-Object Job)
    $allPassed = $true
    foreach ($entry in $running) {
        $reason = $null
        $seconds = 0.0
        try {
            $result = @(Receive-Job -Job $entry.Job -ErrorAction Stop) | Select-Object -Last 1
            if ($null -eq $result) { $reason = 'The check returned no result.' }
            else {
                $seconds = [double]$result.Seconds
                if ($null -eq $result.ExitCode -or [int]$result.ExitCode -ne 0) { $reason = "$($entry.Check.Executable) returned exit code $($result.ExitCode)." }
            }
        }
        catch { $reason = $_.Exception.Message }
        finally { Remove-Job -Job $entry.Job -Force }
        if ($null -eq $reason) {
            if ($script:GateVerbose) { Get-Content -LiteralPath $entry.LogPath | ForEach-Object { Write-Host $_ } }
            if (-not $entry.Check.Quiet) { Write-Host ("PASS {0} {1:0.0} sn" -f $entry.Check.Name, $seconds) }
            continue
        }
        $allPassed = $false
        if ($null -eq $script:GateFailure) { $script:GateFailure = $entry.Check.Name }
        $script:GateFailureCount++
        Write-Host "FAIL $($entry.Check.Name)"
        if (Test-Path -LiteralPath $entry.LogPath) {
            Get-Content -LiteralPath $entry.LogPath -Tail 60 | ForEach-Object { Write-Host $_ }
        }
        Write-Host ("Reason: " + $reason)
    }
    $timer.Stop()
    if ($allPassed) {
        Write-Host ("PASS ParallelPhase ({0} checks) {1:0.0} sn" -f $Checks.Count, $timer.Elapsed.TotalSeconds)
    }
    return $allPassed
}
function Invoke-ExternalChecked {
    param([Parameter(Mandatory)][string] $Executable, [Parameter(Mandatory)][string[]] $Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable returned exit code $LASTEXITCODE." }
}
function Get-InstalledRuntimeAvailability {
    param(
        [scriptblock] $Probe = {
            Invoke-RestMethod -Uri 'http://127.0.0.1:7676/healthz' -NoProxy -ConnectionTimeoutSeconds 2 -OperationTimeoutSeconds 2 -ErrorAction Stop
        },
        [scriptblock] $GetUserName = {
            (Get-CimInstance -ClassName Win32_ComputerSystem -Property UserName -ErrorAction Stop).UserName
        }
    )
    $serviceReady = $false
    try {
        $health = & $Probe
        $serviceReady = $null -ne $health -and $health.isWindowsService -eq $true
    }
    catch {
        $serviceReady = $false
    }
    $userName = ''
    if ($serviceReady) {
        try { $userName = [string](& $GetUserName) }
        catch { $userName = '' }
    }
    [pscustomobject]@{
        HealthHandle = $serviceReady
        NativeInstallerRuntime = ($serviceReady -and -not [string]::IsNullOrWhiteSpace($userName))
    }
}

function Assert-ScriptSyntax {
    $files = @(
        Get-ChildItem -LiteralPath (Join-Path $repoRoot 'scripts') -File -Filter '*.ps1'
        Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests') -File -Filter '*.ps1'
    )
    foreach ($file in $files) {
        $tokens = $null
        $errors = $null
        [Management.Automation.Language.Parser]::ParseFile(
            $file.FullName,[ref]$tokens,[ref]$errors) | Out-Null
        if ($errors.Count) { throw "PowerShell parse failed: $($file.Name): $($errors[0].Message)" }
    }
}
function Assert-PackageManagerPolicy {
    # Split the forbidden command name so the scanner cannot match its own test.
    $expression = '(^|[^A-Za-z0-9_])win' + 'get(\.exe)?([^A-Za-z0-9_]|$)'
    $gitSafe = 'safe.directory=' + $repoRoot.Replace([char]92,[char]47)
    $hits = @(& git -c $gitSafe grep -n -I -i -E $expression -- src scripts TALVORA-KUR.cmd TALVORA-BUSINESS-KUR.cmd)
    $code = $LASTEXITCODE
    if ($code -notin 0,1) { throw "Package manager scan returned $code." }
    if ($hits.Count) { $hits | ForEach-Object { Write-Output $_ }; throw 'Forbidden package manager found.' }
}
# T3: the files that differ from the base revision, with forward slashes.
function Get-ChangedFiles {
    param([string] $Base)
    $gitSafe = 'safe.directory=' + $repoRoot.Replace([char]92,[char]47)
    $revision = if ([string]::IsNullOrWhiteSpace($Base)) { 'HEAD' } else { $Base }
    $tracked = @(& git -c $gitSafe diff --name-only --no-renames $revision --)
    if ($LASTEXITCODE -ne 0) { throw "git diff against $revision failed." }
    $untracked = @(& git -c $gitSafe ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
    @($tracked + $untracked | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
}
# T3: which suites and regressions a change can affect. A dependency or SDK change selects everything; a
# regression runs when it is always-on, when it changed itself, or when its text names a changed file.
function Get-ChangedScopePlan {
    param([string[]] $ChangedFiles, [IO.FileInfo[]] $Tests)
    $files = @($ChangedFiles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Replace([char]92, [char]47) })
    $any = { param([string] $Pattern) @($files | Where-Object { $_ -match $Pattern }).Count -gt 0 }
    $everything = & $any '(^|/)(Directory\.[^/]+\.(props|targets)|global\.json|NuGet\.config|Talvora\.slnx|packages\.lock\.json)$'
    $build = $everything -or (& $any '^(src/|tests/[^/]+/)')
    $suites = [Collections.Generic.List[string]]::new()
    if ($everything -or (& $any '^(src/Talvora/SourceEditing/|tests/Talvora\.SourceEdit\.Regression/)')) { $suites.Add('SourceEditSemantic') }
    if ($everything -or (& $any '^(src/Talvora/|src/Talvora\.Shared/|tests/Talvora\.SourceEdit\.Regression/)')) { $suites.Add('SourceEdit') }
    if ($everything -or (& $any '^(src/Talvora/|src/Talvora\.Shared/|src/Talvora\.Tray/|tests/Talvora\.Notification\.Regression/)')) { $suites.Add('Notification') }
    if ($build) { $suites.Add('IntegrationPolicy') }
    $always = @('InstallerRollbackStateRegression.ps1','ModernizationPolicyRegression.ps1',
        'QualityGateRegression.ps1','ThirdPartyNoticesSourceRegression.ps1')
    $names = @($files | ForEach-Object { [IO.Path]::GetFileName($_) } | Where-Object { $_ } | Sort-Object -Unique)
    $regressions = foreach ($test in $Tests) {
        if ($everything -or $test.Name -in $always -or $files -contains ('tests/' + $test.Name)) { $test; continue }
        $text = [IO.File]::ReadAllText($test.FullName)
        foreach ($name in $names) {
            if ($text.IndexOf($name, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $test; break }
        }
    }
    [pscustomobject]@{ Files = $files; Build = $build; Suites = $suites.ToArray(); Regressions = @($regressions) }
}
# These regressions build the tray or restore/publish fixture projects; they must not run while other suites
# execute the same build output, so they run after the parallel phase.
$serialRegressions = @(
    'GiteaProtocolReadinessRegression.ps1', 'GiteaStopChainRegression.ps1',
    'InstallerDependencyProvenanceRegression.ps1')
$serviceRegressions = @('HealthHandleRegression.ps1', 'NativeInstallerRuntimeRegression.ps1')
$canContinue = $true
Push-Location -LiteralPath $repoRoot
try {
    $plan = $null
    if ($Scope -eq 'Changed') {
        $plan = Get-ChangedScopePlan -ChangedFiles (Get-ChangedFiles -Base $Base) -Tests @(
            Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests') -File -Filter '*.ps1' | Sort-Object Name)
        Write-Host ("Değişen dosya: {0} · derleme: {1} · paketler: {2} · PS testleri: {3}" -f $plan.Files.Count,
            $(if ($plan.Build) { 'evet' } else { 'hayır' }),
            $(if ($plan.Suites.Count) { $plan.Suites -join ', ' } else { '-' }), $plan.Regressions.Count)
    }
    if ($canContinue -and ($null -eq $plan -or $plan.Build)) {
        $canContinue = Invoke-QualityCheck 'Restore' {
            Invoke-ExternalChecked dotnet @('restore','Talvora.slnx','--locked-mode','--nologo','--verbosity','quiet')
            Invoke-ExternalChecked dotnet @('restore','src/Talvora.Installer/Talvora.Installer.csproj','--locked-mode','--nologo','--verbosity','quiet')
        }
    }
    if ($canContinue -and ($null -eq $plan -or $plan.Build)) {
        $canContinue = Invoke-QualityCheck 'Build' {
            Invoke-ExternalChecked dotnet @('build','Talvora.slnx','-c','Release','--no-restore','--nologo','--verbosity','quiet')
        }
    }
    if ($canContinue) { $canContinue = Invoke-QualityCheck 'PowerShellParse' { Assert-ScriptSyntax } }
    if ($canContinue) { $canContinue = Invoke-QualityCheck 'PackageManager' { Assert-PackageManagerPolicy } }
    if ($canContinue) {
        $canContinue = Invoke-QualityCheck 'Context7' {
            Invoke-ExternalChecked pwsh @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $repoRoot 'scripts/Validate-Context7Gate.ps1'))
        }
    }
    if ($canContinue) {
        $canContinue = Invoke-QualityCheck 'Delivery' {
            Invoke-ExternalChecked pwsh @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $repoRoot 'scripts/Validate-ProjectDeliveryGate.ps1'))
        }
    }
    $selected = if ($null -ne $plan) {
        $plan.Regressions
    }
    elseif ($Scope -eq 'Full') {
        @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests') -File -Filter '*.ps1' | Sort-Object Name)
    }
    else {
        @('InstallerRollbackStateRegression.ps1','ModernizationPolicyRegression.ps1',
          'QualityGateRegression.ps1','ThirdPartyNoticesSourceRegression.ps1') |
            ForEach-Object { Get-Item -LiteralPath (Join-Path $repoRoot ('tests/' + $_)) }
    }
    $regressions = 0
    if ($canContinue) {
        # The slowest checks are listed first so they start first.
        $parallel = [Collections.Generic.List[object]]::new()
        foreach ($suite in @(
            @{ Name = 'SourceEditSemantic'; Project = 'tests/Talvora.SourceEdit.Regression/Talvora.SourceEdit.Regression.csproj'; Arguments = @('--semantic-only') },
            @{ Name = 'SourceEdit'; Project = 'tests/Talvora.SourceEdit.Regression/Talvora.SourceEdit.Regression.csproj'; Arguments = @('--without-semantic') },
            @{ Name = 'Notification'; Project = 'tests/Talvora.Notification.Regression/Talvora.Notification.Regression.csproj'; Arguments = @() },
            @{ Name = 'IntegrationPolicy'; Project = 'tests/Talvora.Smoke/Talvora.Smoke.csproj'; Arguments = @('--codex-integration-policy-only') })) {
            if ($null -ne $plan -and $suite.Name -notin $plan.Suites) { continue }
            $runArguments = @('run','--project',$suite.Project,'-c','Release','--no-build')
            if ($suite.Arguments.Count -gt 0) { $runArguments += @('--') + $suite.Arguments }
            $parallel.Add([pscustomobject]@{ Name = $suite.Name; Executable = 'dotnet'; Arguments = $runArguments; Quiet = $false })
        }
        foreach ($test in $selected) {
            if ($test.Name -in $serialRegressions -or $test.Name -in $serviceRegressions) { continue }
            $parallel.Add([pscustomobject]@{
                Name = 'PS:' + $test.BaseName
                Executable = 'pwsh'
                Arguments = @('-NoLogo','-NoProfile','-NonInteractive','-File',$test.FullName)
                Quiet = $true
            })
            $regressions++
        }
        if ($parallel.Count -gt 0) { $canContinue = Invoke-ParallelQualityChecks -Checks $parallel.ToArray() }
    }
    if ($canContinue) {
        $availability = if ($Scope -eq 'Full') {
            Get-InstalledRuntimeAvailability
        }
        else {
            [pscustomobject]@{ HealthHandle = $false; NativeInstallerRuntime = $false }
        }
        $regressionTimer = [Diagnostics.Stopwatch]::StartNew()
        foreach ($test in $selected) {
            if ($test.Name -notin $serialRegressions -and $test.Name -notin $serviceRegressions) { continue }
            if ($test.Name -eq 'HealthHandleRegression.ps1' -and
                -not $availability.HealthHandle) {
                Write-Host 'SKIP HealthHandle: requires an installed, running Talvora service.'
                continue
            }
            if ($test.Name -eq 'NativeInstallerRuntimeRegression.ps1' -and
                -not $availability.NativeInstallerRuntime) {
                Write-Host 'SKIP NativeInstallerRuntime: requires installed service and signed-in desktop.'
                continue
            }
            $path = $test.FullName
            $serviceDependent = $test.Name -in $serviceRegressions
            $checkName = if ($serviceDependent) {
                $test.BaseName.Replace('Regression','')
            } else { 'PS:' + $test.BaseName }
            $canContinue = Invoke-QualityCheck $checkName {
                Invoke-ExternalChecked pwsh @('-NoLogo','-NoProfile','-NonInteractive','-File',$path)
            } -QuietSuccess:(-not $serviceDependent)
            if (-not $canContinue) { break }
            $regressions++
        }
        $regressionTimer.Stop()
        if ($canContinue -and $regressions -gt 0) {
            Write-Host ("PASS PowerShellRegressions ({0} tests) serial part {1:0.0} sn" -f
                $regressions, $regressionTimer.Elapsed.TotalSeconds)
        }
    }
    if ($canContinue -and $Scope -eq 'Full') {
        if ($availability.NativeInstallerRuntime -and [Environment]::UserInteractive) {
            $canContinue = Invoke-QualityCheck 'ControlCenter' {
                $previousPreview = $env:TALVORA_CONTROL_CENTER_PREVIEW_PNG
                try {
                    $env:TALVORA_CONTROL_CENTER_PREVIEW_PNG = Join-Path $script:GateCaptureRoot 'control-center.png'
                    $tray = Join-Path $repoRoot 'src/Talvora.Tray/bin/Release/net10.0-windows/win-x64/Talvora.Tray.exe'
                    $smoke = Start-Process -FilePath $tray -ArgumentList @('--control-center-smoke') -Wait -PassThru -WindowStyle Hidden
                    if ($smoke.ExitCode -ne 0) { throw "Control Center smoke returned exit code $($smoke.ExitCode)." }
                    foreach ($name in @('control-center.png', 'control-center-loaded.png')) {
                        if (-not (Test-Path -LiteralPath (Join-Path $script:GateCaptureRoot $name) -PathType Leaf)) {
                            throw "Control Center preview was not written: $name"
                        }
                    }
                }
                finally { $env:TALVORA_CONTROL_CENTER_PREVIEW_PNG = $previousPreview }
            }
        }
        else { Write-Host 'SKIP ControlCenter: requires an installed service and signed-in interactive desktop.' }
    }
    if ($canContinue -and $Scope -eq 'Full') {
        $canContinue = Invoke-QualityCheck 'BusinessBootstrap' {
            $selfTest = Join-Path $repoRoot 'scripts/Configure-ChatGPT-Business.ps1'
            Invoke-ExternalChecked pwsh @('-NoLogo','-NoProfile','-NonInteractive','-File',$selfTest,'-SelfTest')
            $winPs = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
            Invoke-ExternalChecked $winPs @('-NoLogo','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$selfTest,'-SelfTest')
        }
    }
}
catch {
    if (-not $script:GateFailure) {
        $script:GateFailure = 'Runner'
        Write-Host ("Reason: " + $_.Exception.Message)
    }
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $script:GateCaptureRoot -Force -Recurse -ErrorAction SilentlyContinue
}
if ($script:GateFailure) {
    # A serial failure stops the gate; only the parallel phase can report several failures.
    Write-Host "$($script:GateChecks) kontrol, $([Math]::Max(1, $script:GateFailureCount)) hata"
    Write-Host "Kontrol başarısız: $($script:GateFailure)"
    exit 1
}
Write-Host "$($script:GateChecks) kontrol, 0 hata"
Write-Host 'Hepsi geçti.'
exit 0
