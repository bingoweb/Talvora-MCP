[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DotNetPath,

    [string]$Configuration = 'Debug',

    [string]$BaseUrl = 'http://127.0.0.1:17677'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$brokerDllRelative = "src\Talvora.ElevatedBroker\bin\$Configuration\net10.0-windows\Talvora.ElevatedBroker.dll"
$hostDllRelative = "src\Talvora.Host\bin\$Configuration\net10.0-windows\Talvora.Host.dll"
$brokerDll = Join-Path $root $brokerDllRelative
$hostDll = Join-Path $root $hostDllRelative
$logDir = Join-Path $root '.talvora\logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

if (-not (Test-Path -LiteralPath $brokerDll)) {
    throw "Talvora.ElevatedBroker bulunamadı: $brokerDll"
}
if (-not (Test-Path -LiteralPath $hostDll)) {
    throw "Talvora.Host bulunamadı: $hostDll"
}

$currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$pipeName = "Talvora.ElevatedBroker.Smoke.$PID.$([Guid]::NewGuid().ToString('N'))"
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$brokerStdout = Join-Path $logDir "broker-smoke-$timestamp.stdout.log"
$brokerStderr = Join-Path $logDir "broker-smoke-$timestamp.stderr.log"
$hostStdout = Join-Path $logDir "broker-host-smoke-$timestamp.stdout.log"
$hostStderr = Join-Path $logDir "broker-host-smoke-$timestamp.stderr.log"

$brokerProcess = $null
$hostProcess = $null
try {
    $brokerProcess = Start-Process `
        -FilePath $DotNetPath `
        -ArgumentList @(
            $brokerDllRelative,
            "--Broker:PipeName=$pipeName",
            "--Broker:AllowedUserSid=$currentSid"
        ) `
        -WorkingDirectory $root `
        -RedirectStandardOutput $brokerStdout `
        -RedirectStandardError $brokerStderr `
        -PassThru `
        -WindowStyle Hidden

    Start-Sleep -Milliseconds 500
    if ($brokerProcess.HasExited) {
        $stderr = if (Test-Path $brokerStderr) { Get-Content $brokerStderr -Raw } else { '' }
        throw "Talvora Elevated Broker erken kapandı. ExitCode=$($brokerProcess.ExitCode) $stderr"
    }

    $hostProcess = Start-Process `
        -FilePath $DotNetPath `
        -ArgumentList @(
            $hostDllRelative,
            '--urls',
            $BaseUrl,
            "--Broker:PipeName=$pipeName"
        ) `
        -WorkingDirectory $root `
        -RedirectStandardOutput $hostStdout `
        -RedirectStandardError $hostStderr `
        -PassThru `
        -WindowStyle Hidden

    $deadline = (Get-Date).AddSeconds(20)
    $brokerHealth = $null
    $lastBrokerError = $null
    do {
        if ($brokerProcess.HasExited) {
            $stderr = if (Test-Path $brokerStderr) { Get-Content $brokerStderr -Raw } else { '' }
            throw "Talvora Elevated Broker smoke test sırasında kapandı. ExitCode=$($brokerProcess.ExitCode) $stderr"
        }
        if ($hostProcess.HasExited) {
            $stderr = if (Test-Path $hostStderr) { Get-Content $hostStderr -Raw } else { '' }
            throw "Talvora Host broker smoke test sırasında kapandı. ExitCode=$($hostProcess.ExitCode) $stderr"
        }

        try {
            $hostHealth = Invoke-RestMethod -Uri "$BaseUrl/health" -TimeoutSec 2
            if ($hostHealth.ok) {
                $candidate = Invoke-RestMethod -Uri "$BaseUrl/health/broker" -TimeoutSec 3
                if ($candidate.ok) {
                    $brokerHealth = $candidate
                }
                else {
                    $lastBrokerError = $candidate.error
                    Start-Sleep -Milliseconds 250
                }
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    } until ($null -ne $brokerHealth -or (Get-Date) -ge $deadline)

    if ($null -eq $brokerHealth) {
        $brokerError = if (Test-Path $brokerStderr) { Get-Content $brokerStderr -Raw } else { '' }
        $hostError = if (Test-Path $hostStderr) { Get-Content $hostStderr -Raw } else { '' }
        throw "Elevated Broker named-pipe smoke testi zaman aşımına uğradı. LastError=$lastBrokerError Broker: $brokerError Host: $hostError"
    }

    if ($brokerHealth.protocolVersion -ne 1) {
        throw "Beklenmeyen broker protokol sürümü: $($brokerHealth.protocolVersion)"
    }
    if (-not $brokerHealth.connected -or -not $brokerHealth.ready) {
        throw 'Broker health endpoint connected/ready durumunu doğrulamadı.'
    }

    Write-Host 'Talvora Elevated Broker IPC smoke test: GREEN'
    Write-Host "  transport:        gRPC over Windows Named Pipe"
    Write-Host "  pipe:             $pipeName"
    Write-Host "  protocol:         $($brokerHealth.protocolVersion)"
    Write-Host "  connected:        GREEN"
    Write-Host "  broker health:    GREEN"
}
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue
        $hostProcess.WaitForExit(5000) | Out-Null
    }
    if ($null -ne $brokerProcess -and -not $brokerProcess.HasExited) {
        Stop-Process -Id $brokerProcess.Id -Force -ErrorAction SilentlyContinue
        $brokerProcess.WaitForExit(5000) | Out-Null
    }
}
