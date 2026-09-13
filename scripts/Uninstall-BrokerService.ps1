[CmdletBinding()]
param([switch]$ElevatedChild)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$serviceName = 'TalvoraElevatedBroker'
$installDirectory = Join-Path $env:ProgramFiles 'Talvora\Broker'
$scExe = Join-Path $env:SystemRoot 'System32\sc.exe'

function Test-Administrator {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    if ($ElevatedChild) {
        throw 'Elevated Broker kaldırma çocuğu yönetici yetkisi olmadan başlatıldı.'
    }

    Write-Host 'Elevated Broker Windows Service kaldırmak için bir kez yönetici izni istenecek.' -ForegroundColor Yellow
    $powerShellPath = (Get-Process -Id $PID -ErrorAction Stop).Path
    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-ElevatedChild'
    )
    $child = Start-Process -FilePath $powerShellPath -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $child.ExitCode
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction Stop
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
    }

    & $scExe delete $serviceName | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe delete başarısız oldu. ExitCode=$LASTEXITCODE"
    }

    $deadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    } until ($null -eq $service -or (Get-Date) -ge $deadline)

    if ($null -ne $service) {
        throw 'Windows Service silme işlemi zaman aşımına uğradı.'
    }
}

if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

Write-Host ''
Write-Host 'Talvora Elevated Broker Windows Service kaldırma: GREEN' -ForegroundColor Green
