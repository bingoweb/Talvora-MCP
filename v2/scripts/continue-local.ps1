param(
    [string]$RepoRoot=(Join-Path $env:USERPROFILE 'Talvora-MCP'),
    [string]$StateRoot=(Join-Path $env:LOCALAPPDATA 'Talvora\Local'),
    [string]$ClientHome=$(if ($env:CODEX_HOME) {$env:CODEX_HOME} else {Join-Path $env:USERPROFILE '.codex'}),
    [string]$LegacyGatewayDirectory=(Join-Path $env:LOCALAPPDATA 'Talvora\Gateway'),
    [string]$OwnerSid=([Security.Principal.WindowsIdentity]::GetCurrent().User.Value),
    [switch]$NoOpenReport
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=[Security.Principal.WindowsPrincipal]::new($identity)
$ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments='-NoProfile -ExecutionPolicy Bypass -File "{0}" -RepoRoot "{1}" -StateRoot "{2}" -ClientHome "{3}" -LegacyGatewayDirectory "{4}" -OwnerSid "{5}"' -f $PSCommandPath,$RepoRoot,$StateRoot,$ClientHome,$LegacyGatewayDirectory,$OwnerSid
    if ($NoOpenReport) {$arguments+=' -NoOpenReport'}
    try { $child=Start-Process $ps -Verb RunAs -ArgumentList $arguments -PassThru -Wait; exit $child.ExitCode }
    catch { Write-Error ('Windows yonetici izni alinamadi: '+$_.Exception.Message); exit 1 }
}
if ($identity.User.Value -ne $OwnerSid) { throw 'Bu islem Talvora sahibinin kendi yonetici oturumunda calismalidir.' }
Import-Module (Join-Path $PSScriptRoot 'Talvora.Local.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'Talvora.Maintenance.psm1') -Force -DisableNameChecking
$RepoRoot=[IO.Path]::GetFullPath($RepoRoot)
$StateRoot=[IO.Path]::GetFullPath($StateRoot)
$configPath=Join-Path $StateRoot 'current.json'
$logRoot=Join-Path $StateRoot 'logs'
$stamp=(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'-'+[Guid]::NewGuid().ToString('N')
$backupRoot=Join-Path $StateRoot ('backups\maintenance-'+$stamp)
New-Item -ItemType Directory -Path $logRoot,$backupRoot -Force | Out-Null
$report=[ordered]@{Status='Failed';TimeUtc=[DateTime]::UtcNow.ToString('o');SmokePassed=$false;GatewayPid=$null;GatewayDll=$null;TaskState=$null;RepairPerformed=$false;LegacyTasks=@();LegacyArchive=$null;OtherServices=@();ClientConnection='Not verified by a local model session';Warnings=@();Error=$null;LogRoot=$logRoot}
$exitCode=1
function Get-InstalledStatus {
    $configuration=$null; $health=$null; $task=$null; $owned=$false; $valid=$false
    if (Test-Path -LiteralPath $configPath -PathType Leaf) {
        $configuration=Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $task=Get-ScheduledTask -TaskName 'Talvora Local MCP' -TaskPath '\' -ErrorAction SilentlyContinue
        if ($null -ne $task) {
            $expectedLauncher=Join-Path ([IO.Path]::GetDirectoryName([string]$configuration.GatewayDll)) 'run-local.ps1'
            $actions=@($task.Actions | Where-Object {
                $_.PSObject.Properties['Execute'] -and $_.PSObject.Properties['Arguments'] -and
                $_.Execute -ieq $ps -and $_.Arguments.Contains('"'+$configPath+'"') -and $_.Arguments.Contains('"'+$expectedLauncher+'"')
            })
            $triggers=@($task.Triggers | Where-Object {$_.CimClass.CimClassName -eq 'MSFT_TaskLogonTrigger'})
            $valid=$actions.Count -eq 1 -and $triggers.Count -gt 0 -and [int]$task.Principal.RunLevel -eq 1 -and [int]$task.Principal.LogonType -eq 3
        }
        $health=Get-TalvoraLocalHealth
        if ($null -ne $health) {
            $owner=Get-CimInstance Win32_Process -Filter "ProcessId = $($health.processId)" -ErrorAction SilentlyContinue
            $owned=Test-TalvoraProcessPath -Process $owner -DllPaths @([string]$configuration.GatewayDll)
        }
    }
    return [pscustomobject]@{Config=$configuration;Health=$health;Task=$task;TaskValid=$valid;Owned=$owned}
}
try {
    Write-Host '1/4 Mevcut yerel Talvora ve otomatik baslangic kontrol ediliyor.'
    $status=Get-InstalledStatus
    if ($status.TaskValid -and -not $status.Owned) {
        Enable-ScheduledTask -InputObject $status.Task | Out-Null
        Start-ScheduledTask -InputObject $status.Task
        $deadline=[DateTime]::UtcNow.AddSeconds(35)
        do {Start-Sleep -Milliseconds 400; $status=Get-InstalledStatus} while (-not $status.Owned -and [DateTime]::UtcNow -lt $deadline)
    }
    if (-not $status.TaskValid -or -not $status.Owned) {
        Write-Host 'Eksik yerel kurulum mevcut dosyalardan onariliyor.'
        $arguments='-NoProfile -ExecutionPolicy Bypass -File "{0}" -RepoRoot "{1}" -StateRoot "{2}" -ClientHome "{3}" -OwnerSid "{4}"' -f (Join-Path $PSScriptRoot 'install-local.ps1'),$RepoRoot,$StateRoot,$ClientHome,$OwnerSid
        $repair=Start-Process $ps -ArgumentList $arguments -Wait -PassThru -NoNewWindow
        if ($repair.ExitCode -ne 0) {throw "Yerel onarim basarisiz: $($repair.ExitCode)"}
        $report.RepairPerformed=$true
        $status=Get-InstalledStatus
    }
    if (-not $status.TaskValid -or -not $status.Owned) {throw 'Yeni kurulumun gorevi ve gercek islem kimligi dogrulanamadi.'}
    if ([int]$status.Task.State -eq 1) {Enable-ScheduledTask -InputObject $status.Task | Out-Null}
    $report.GatewayPid=$status.Health.processId
    $report.GatewayDll=$status.Config.GatewayDll
    $report.TaskState=[string](Get-ScheduledTask -TaskName 'Talvora Local MCP' -TaskPath '\').State
    Write-Host '2/4 Mevcut MCP uzerinde gercek dosya ve komut testleri calistiriliyor.'
    $smoke=Join-Path $RepoRoot 'v2\tests\Talvora.McpSmoke\bin\Release\net10.0\Talvora.McpSmoke.dll'
    if (-not (Test-Path -LiteralPath $smoke)) {throw 'Derlenmis MCP test dosyasi bulunamadi. Calisan Gateway degistirilmedi.'}
    $smokeLog=Join-Path $logRoot ('maintenance-'+$stamp+'.smoke.log')
    $smokeError=Join-Path $logRoot ('maintenance-'+$stamp+'.smoke-error.log')
    # Own the native process handle instead of the PS 5.1 Start-Process wrapper.
    $info=[Diagnostics.ProcessStartInfo]::new()
    $info.FileName=$status.Config.DotNetPath
    $info.Arguments='"{0}" http://127.0.0.1:7676/mcp' -f $smoke
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true
    $info.RedirectStandardError=$true
    $check=[Diagnostics.Process]::new()
    $check.StartInfo=$info
    try {
        if (-not $check.Start()) {throw 'MCP test islemi baslatilamadi.'}
        $stdoutTask=$check.StandardOutput.ReadToEndAsync()
        $stderrTask=$check.StandardError.ReadToEndAsync()
        if (-not $check.WaitForExit(90000)) {$check.Kill(); $check.WaitForExit(); throw 'MCP testi 90 saniyede tamamlanamadi.'}
        $smokeExitCode=$check.ExitCode
        [IO.File]::WriteAllText($smokeLog,$stdoutTask.GetAwaiter().GetResult())
        [IO.File]::WriteAllText($smokeError,$stderrTask.GetAwaiter().GetResult())
    }
    finally {$check.Dispose()}
    Get-Content -LiteralPath $smokeLog | Out-Host
    if ($smokeExitCode -ne 0) {Get-Content -LiteralPath $smokeError | Out-Host; throw "MCP testi basarisiz: $smokeExitCode"}
    $report.SmokePassed=$true
    $clientPath=Join-Path $ClientHome 'config.toml'
    $before=if (Test-Path -LiteralPath $clientPath) {[IO.File]::ReadAllText($clientPath)} else {''}
    $after=Get-TalvoraClientConfigText -Content $before
    if ($before -cne $after) {
        New-Item -ItemType Directory -Path $ClientHome -Force | Out-Null
        if (Test-Path -LiteralPath $clientPath) {Copy-Item -LiteralPath $clientPath -Destination (Join-Path $backupRoot 'client-config.toml')}
        [IO.File]::WriteAllText(($clientPath+'.talvora-new'),$after,[Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath ($clientPath+'.talvora-new') -Destination $clientPath -Force
    }
    Write-Host '3/4 Taninan eski Gateway yeni kurulumdan ayriliyor.'
    $legacyExe=Join-Path $LegacyGatewayDirectory 'Talvora.Gateway.exe'
    $report.LegacyTasks=@(Disable-TalvoraLegacyTasks -LegacyExe $legacyExe -BackupRoot $backupRoot)
    $services=@(Get-CimInstance Win32_Service | Where-Object { $_.Name -match 'Talvora' -or $_.PathName -match 'Talvora' } | Select-Object Name,State,StartMode,PathName)
    $report.OtherServices=$services
    $legacyService=@($services | Where-Object { [string]$_.PathName -match ('^\s*"?'+[regex]::Escape($legacyExe)+'"?(?:\s|$)') })
    if ($legacyService.Count) {
        $report.Warnings+= 'Eski Gateway bir hizmet olarak da kayitli; hizmet otomatik silinmedi ve Gateway klasoru tasinmadi.'
    } else {
        Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath -ieq $legacyExe} | ForEach-Object {
            $process=Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
            if ($null -ne $process -and $process.Path -ieq $legacyExe) {Stop-Process -InputObject $process -Force; [void]$process.WaitForExit(5000)}
        }
        $report.LegacyArchive=Move-TalvoraLegacyGateway -GatewayDirectory $LegacyGatewayDirectory -ArchiveRoot (Join-Path $StateRoot 'retired')
    }
    $final=Get-InstalledStatus
    if (-not $final.Owned) {throw 'Bakim sonrasinda yeni Gateway saglik kontrolu gecmedi.'}
    $report.Status=if ($report.Warnings.Count) {'ReadyWithWarnings'} else {'Ready'}
    $exitCode=0
    Write-Host '4/4 Yerel kontrol raporu kaydediliyor.'
}
catch {$report.Error=$_.Exception.Message; Write-Host ('DURDU: '+$report.Error)}
finally {
    $reportPath=Join-Path $StateRoot 'maintenance-latest.json'
    $textPath=Join-Path $StateRoot 'TALVORA-SONUC.txt'
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Copy-Item -LiteralPath $reportPath -Destination (Join-Path $logRoot ('maintenance-'+$stamp+'.json'))
    $text=@('TALVORA YEREL KONTROL SONUCU',('Durum: '+$report.Status),('MCP arac testi: '+$report.SmokePassed),('Yeni Gateway PID: '+$report.GatewayPid),('Yeni Gateway: '+$report.GatewayDll),('Windows gorevi: '+$report.TaskState),('Onarim gerekti mi: '+$report.RepairPerformed),('Hata: '+$report.Error),('Uyarilar: '+($report.Warnings -join '; ')),('Ayrintili rapor: '+$reportPath),'','Yeni Local klasoru ve kaynak repo korunur. Eski Gateway varsa retired altina tasinir.','Diger Talvora hizmetleri raporda listelenir; tum eski kurulumlarin silindigi iddia edilmez.','Yerel MCP istemci ayari hazirlanir. Tarayicidaki sohbete otomatik erisim saglanmaz.','Bu rapor ve dosyalar disariya gonderilmez.') -join [Environment]::NewLine
    [IO.File]::WriteAllText($textPath,$text,[Text.UTF8Encoding]::new($true))
    Write-Host $text
    if (-not $NoOpenReport) {Start-Process notepad.exe -ArgumentList ('"{0}"' -f $textPath) | Out-Null}
}
exit $exitCode
