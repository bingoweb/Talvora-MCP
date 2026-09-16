param([switch]$Integration)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$module = Join-Path $root 'scripts\Talvora.Maintenance.psm1'
if (-not (Test-Path -LiteralPath $module)) { throw 'Legacy maintenance is missing: no automatic retirement and verification flow.' }
foreach ($file in @($module,(Join-Path $root 'scripts\continue-local.ps1'))) {
    $tokens=$null; $errors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$errors)
    if ($errors.Count) { throw ($errors.Message -join '; ') }
}
Import-Module $module -Force -DisableNameChecking
Import-Module (Join-Path $root 'scripts\Talvora.Local.psm1') -Force -DisableNameChecking
function Assert($condition,[string]$message) { if (-not $condition) { throw $message }; Write-Host "PASS $message" }
$temp = Join-Path $env:TEMP ('talvora-maintenance-' + [Guid]::NewGuid().ToString('N'))
$gateway = Join-Path $temp 'Gateway'
$legacyExe = Join-Path $gateway 'Talvora.Gateway.exe'
$taskName = 'Talvora legacy fixture ' + [Guid]::NewGuid().ToString('N')
$controlName = 'Talvora unrelated fixture ' + [Guid]::NewGuid().ToString('N')
$child=$null
try {
    New-Item -ItemType Directory -Path $gateway,(Join-Path $temp 'Local') -Force | Out-Null
    [IO.File]::WriteAllText($legacyExe,'legacy payload')
    [IO.File]::WriteAllText((Join-Path $temp 'Local\preserve.txt'),'new runtime')
    Assert (Test-TalvoraLegacyAction -Action ([pscustomobject]@{Execute=$legacyExe}) -LegacyExe $legacyExe) 'Exact legacy executable matches'
    Assert (-not (Test-TalvoraLegacyAction -Action ([pscustomobject]@{ClassId='COM'}) -LegacyExe $legacyExe)) 'Non-executable task action is supported'
    Assert (-not (Test-TalvoraLegacyAction -Action ([pscustomobject]@{Execute='powershell.exe';Arguments=$legacyExe}) -LegacyExe $legacyExe)) 'A mention in arguments alone does not prove ownership'
    Assert (-not (Test-TalvoraLegacyAction -Action ([pscustomobject]@{Execute=($legacyExe+'.other')}) -LegacyExe $legacyExe)) 'Prefix match is not accepted'
    $action = New-ScheduledTaskAction -Execute $legacyExe
    Register-ScheduledTask -TaskName $taskName -Action $action -Force | Out-Null
    Register-ScheduledTask -TaskName $controlName -Action (New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/c exit 0') -Force | Out-Null
    $retiredTasks = @(Disable-TalvoraLegacyTasks -LegacyExe $legacyExe -BackupRoot (Join-Path $temp 'task-backup'))
    Assert ($retiredTasks.Count -eq 1) 'Only the exact legacy task is selected'
    Assert ([int](Get-ScheduledTask -TaskName $taskName).State -eq 1) 'Legacy task is actually disabled'
    Assert ([int](Get-ScheduledTask -TaskName $controlName).State -ne 1) 'Unrelated Talvora-named task is untouched'
    Assert (@(Get-ChildItem -LiteralPath (Join-Path $temp 'task-backup') -Filter '*.xml').Count -eq 1) 'Real task XML is backed up'
    $beforeHash = (Get-FileHash -LiteralPath $legacyExe).Hash
    $archive = Move-TalvoraLegacyGateway -GatewayDirectory $gateway -ArchiveRoot (Join-Path $temp 'retired')
    Assert ($archive.Status -eq 'Archived') 'Legacy Gateway is retired'
    Assert (-not (Test-Path -LiteralPath $gateway)) 'Old active directory no longer exists'
    Assert ((Get-FileHash -LiteralPath (Join-Path $archive.ArchivePath 'Talvora.Gateway.exe')).Hash -eq $beforeHash) 'Retirement preserves exact legacy bytes'
    Assert ([IO.File]::ReadAllText((Join-Path $temp 'Local\preserve.txt')) -eq 'new runtime') 'Sibling new Local installation is preserved'
    $again = Move-TalvoraLegacyGateway -GatewayDirectory $gateway -ArchiveRoot (Join-Path $temp 'retired')
    Assert ($again.Status -eq 'Absent') 'Repeated retirement is idempotent'
    $rejected=$false
    try { Move-TalvoraLegacyGateway -GatewayDirectory (Join-Path $temp 'Local') -ArchiveRoot (Join-Path $temp 'retired') | Out-Null } catch { $rejected=$true }
    Assert $rejected 'New Local directory cannot be mistaken for old Gateway'
    if (-not $Integration) { Write-Host 'Legacy maintenance regression GREEN.'; return }

    $state = Join-Path $temp 'local state'
    $clientHome = Join-Path $temp 'client settings'
    & (Join-Path $root 'scripts\install-local.ps1') -RepoRoot ([IO.Path]::GetDirectoryName($root)) -StateRoot $state -ClientHome $clientHome -NoStart
    if (-not $?) { throw 'Base installer failed.' }
    $configPath=Join-Path $state 'current.json'
    $config=Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $launcher=Join-Path ([IO.Path]::GetDirectoryName($config.GatewayDll)) 'run-local.ps1'
    $ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $child=Start-Process $ps -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "{0}" -ConfigPath "{1}"' -f $launcher,$configPath) -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(35)
    $health=$null
    while ([DateTime]::UtcNow -lt $deadline) { $health=Get-TalvoraLocalHealth; if ($null -ne $health) { break }; Start-Sleep -Milliseconds 300 }
    Assert ($null -ne $health) 'Real deployed Gateway is ready'
    $initialPid=$health.processId
    New-Item -ItemType Directory -Path $gateway -Force | Out-Null
    [IO.File]::WriteAllText($legacyExe,'legacy payload')
    foreach ($iteration in 1,2) {
        $arguments='-NoProfile -ExecutionPolicy Bypass -File "{0}" -RepoRoot "{1}" -StateRoot "{2}" -ClientHome "{3}" -LegacyGatewayDirectory "{4}" -NoOpenReport' -f (Join-Path $root 'scripts\continue-local.ps1'),([IO.Path]::GetDirectoryName($root)),$state,$clientHome,$gateway
        $run=Start-Process $ps -ArgumentList $arguments -PassThru -Wait -NoNewWindow
        Assert ($run.ExitCode -eq 0) "Maintenance run $iteration succeeds"
        $report=Get-Content -LiteralPath (Join-Path $state 'maintenance-latest.json') -Raw | ConvertFrom-Json
        Assert ($report.Status -eq 'Ready' -and $report.SmokePassed) 'Report reflects real MCP tools, not only health'
        Assert ((Get-TalvoraLocalHealth).processId -eq $initialPid) 'Healthy installed Gateway is not restarted'
        Assert (-not (Test-Path -LiteralPath $gateway)) 'Legacy active folder remains retired'
    }
    Write-Host 'Local maintenance integration GREEN: live MCP, legacy archive, same PID across repeated runs.'
}
finally {
    foreach ($name in @($taskName,$controlName)) { Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue }
    if ($Integration) {
        Unregister-ScheduledTask -TaskName 'Talvora Local MCP' -TaskPath '\' -Confirm:$false -ErrorAction SilentlyContinue
        $health=Get-TalvoraLocalHealth
        if ($null -ne $health) {
            $owner=Get-CimInstance Win32_Process -Filter "ProcessId = $($health.processId)" -ErrorAction SilentlyContinue
            if ($null -ne $owner -and $owner.CommandLine -like ('*'+$temp+'*')) { Stop-Process -Id $health.processId -Force -ErrorAction SilentlyContinue }
        }
    }
    if ($null -ne $child) { $child.Refresh(); if (-not $child.HasExited) { Stop-Process -Id $child.Id -Force -ErrorAction SilentlyContinue } }
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
