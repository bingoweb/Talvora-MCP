[CmdletBinding()]
param(
    [switch]$ElevatedChild,
    [string]$TargetUser,
    [string]$TargetLocalAppData,
    [string]$PowerShellPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$taskName = 'Talvora Host'
$healthUrl = 'http://127.0.0.1:7676/health'
$hostProject = Join-Path $root 'src\Talvora.Host\Talvora.Host.csproj'

function Test-Administrator {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-DotNetPath {
    $resolved = (& (Join-Path $PSScriptRoot 'Resolve-DotNet.ps1') | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($resolved)) {
        throw '.NET executable bulunamadı. Önce TALVORA-KUR.bat çalıştır.'
    }

    return $resolved
}

function Test-TalvoraHealth {
    try {
        $response = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 3
        return ($response.ok -eq $true -and [string]$response.service -eq 'talvora')
    }
    catch {
        return $false
    }
}

function Wait-TalvoraHealth {
    param([int]$TimeoutSeconds = 30)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (Test-TalvoraHealth) {
            return $true
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    return $false
}

if (-not (Test-Administrator)) {
    if ($ElevatedChild) {
        throw 'Talvora otomatik başlatma kurulum çocuğu yönetici yetkisi olmadan başlatıldı.'
    }

    if ([string]::IsNullOrWhiteSpace($TargetUser)) {
        $TargetUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    }
    if ([string]::IsNullOrWhiteSpace($TargetLocalAppData)) {
        $TargetLocalAppData = $env:LOCALAPPDATA
    }
    if ([string]::IsNullOrWhiteSpace($PowerShellPath)) {
        $PowerShellPath = (Get-Process -Id $PID -ErrorAction Stop).Path
    }

    Write-Host 'Talvora otomatik başlatma kurulumu için bir kez yönetici izni istenecek.' -ForegroundColor Yellow

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-ElevatedChild',
        '-TargetUser', ('"{0}"' -f $TargetUser),
        '-TargetLocalAppData', ('"{0}"' -f $TargetLocalAppData),
        '-PowerShellPath', ('"{0}"' -f $PowerShellPath)
    )

    $child = Start-Process -FilePath $PowerShellPath -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $child.ExitCode
}

if ([string]::IsNullOrWhiteSpace($TargetUser)) {
    $TargetUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
}
if ([string]::IsNullOrWhiteSpace($TargetLocalAppData)) {
    $TargetLocalAppData = $env:LOCALAPPDATA
}
if ([string]::IsNullOrWhiteSpace($PowerShellPath)) {
    $PowerShellPath = (Get-Process -Id $PID -ErrorAction Stop).Path
}

$dotnetPath = Resolve-DotNetPath
$installDirectory = Join-Path $TargetLocalAppData 'Talvora\Host'
$stagingDirectory = Join-Path $env:TEMP ("Talvora-Host-Publish-{0}" -f [Guid]::NewGuid().ToString('N'))
$wasHealthy = Test-TalvoraHealth

try {
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

    Write-Host 'Talvora Host hazırlanıyor...' -ForegroundColor Cyan
    & $dotnetPath publish $hostProject -c Release -o $stagingDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Talvora Host publish başarısız oldu. ExitCode=$LASTEXITCODE"
    }

    $existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($null -ne $existingTask -and $existingTask.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction Stop
        Start-Sleep -Seconds 1
    }

    if (Test-Path -LiteralPath $installDirectory) {
        Remove-Item -LiteralPath $installDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $stagingDirectory '*') -Destination $installDirectory -Recurse -Force

    $hostExecutable = Join-Path $installDirectory 'Talvora.Host.exe'
    if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
        throw "Talvora Host executable bulunamadı: $hostExecutable"
    }

    $runnerPath = Join-Path $installDirectory 'Run-TalvoraHost.ps1'
    $runnerContent = @'
$ErrorActionPreference = 'Stop'
try {
    $response = Invoke-RestMethod -Uri 'http://127.0.0.1:7676/health' -Method Get -TimeoutSec 3
    if ($response.ok -eq $true -and [string]$response.service -eq 'talvora') {
        exit 0
    }
}
catch {
}

Set-Location $PSScriptRoot
& (Join-Path $PSScriptRoot 'Talvora.Host.exe')
exit $LASTEXITCODE
'@
    [System.IO.File]::WriteAllText($runnerPath, $runnerContent, [System.Text.UTF8Encoding]::new($false))

    $taskArguments = '-NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}"' -f $runnerPath
    $action = New-ScheduledTaskAction -Execute $PowerShellPath -Argument $taskArguments -WorkingDirectory $installDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $TargetUser
    $principal = New-ScheduledTaskPrincipal -UserId $TargetUser -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet `
        -StartWhenAvailable `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -MultipleInstances IgnoreNew `
        -RestartCount 999 `
        -RestartInterval (New-TimeSpan -Minutes 1) `
        -ExecutionTimeLimit ([TimeSpan]::Zero)

    Register-ScheduledTask `
        -TaskName $taskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description 'Starts Talvora Host automatically for the signed-in user.' `
        -Force | Out-Null

    $registeredTask = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
    if ([string]$registeredTask.Principal.UserId -ne $TargetUser) {
        throw "Talvora Host task kullanıcı eşleşmesi başarısız. Expected=$TargetUser Actual=$($registeredTask.Principal.UserId)"
    }
    if ([string]$registeredTask.Principal.RunLevel -ne 'Limited') {
        throw "Talvora Host task normal kullanıcı bağlamında değil. RunLevel=$($registeredTask.Principal.RunLevel)"
    }

    Start-ScheduledTask -TaskName $taskName

    if (-not $wasHealthy) {
        if (-not (Wait-TalvoraHealth -TimeoutSeconds 30)) {
            throw 'Talvora Host otomatik başlatma testi zamanında GREEN olmadı.'
        }
    }

    Write-Host ''
    Write-Host 'Talvora otomatik başlatma: GREEN' -ForegroundColor Green
    Write-Host "  görev:     $taskName"
    Write-Host "  kullanıcı: $TargetUser"
    Write-Host "  konum:     $installDirectory"
    Write-Host "  health:    $healthUrl"
    Write-Host ''
    if ($wasHealthy) {
        Write-Host 'Talvora zaten çalışıyordu. Bir sonraki Windows oturumunda otomatik başlayacak.' -ForegroundColor Yellow
    }
    else {
        Write-Host 'Talvora otomatik görev üzerinden çalışıyor.' -ForegroundColor Green
    }
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
