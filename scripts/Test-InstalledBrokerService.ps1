[CmdletBinding()]
param(
    [string]$DotNetPath,
    [string]$Configuration = 'Release',
    [string]$BaseUrl = 'http://127.0.0.1:17678'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$serviceName = 'TalvoraElevatedBroker'
$hostDll = Join-Path $root "src\Talvora.Host\bin\$Configuration\net10.0-windows\Talvora.Host.dll"
$logDirectory = Join-Path $root '.talvora\logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

function Test-Administrator {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
    $DotNetPath = (& (Join-Path $PSScriptRoot 'Resolve-DotNet.ps1') | Select-Object -First 1)
}
if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
    throw '.NET executable bulunamadı.'
}

$service = Get-Service -Name $serviceName -ErrorAction Stop
if ($service.Status -ne 'Running') {
    throw "Talvora Elevated Broker Windows Service çalışmıyor. Status=$($service.Status)"
}

$serviceInfo = Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'"
if ($null -eq $serviceInfo) {
    throw 'Windows Service metadata bulunamadı.'
}
if ($serviceInfo.StartName -ne 'LocalSystem') {
    throw "Elevated Broker beklenen LocalSystem hesabında çalışmıyor. StartName=$($serviceInfo.StartName)"
}

if (-not (Test-Path -LiteralPath $hostDll -PathType Leaf)) {
    $hostProject = Join-Path $root 'src\Talvora.Host\Talvora.Host.csproj'
    & $DotNetPath build $hostProject -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Talvora Host build başarısız oldu. ExitCode=$LASTEXITCODE"
    }
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$stdout = Join-Path $logDirectory "installed-broker-host-$timestamp.stdout.log"
$stderr = Join-Path $logDirectory "installed-broker-host-$timestamp.stderr.log"
$hostProcess = $null

try {
    $hostProcess = Start-Process `
        -FilePath $DotNetPath `
        -ArgumentList @($hostDll, '--urls', $BaseUrl) `
        -WorkingDirectory $root `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -PassThru `
        -WindowStyle Hidden

    $deadline = (Get-Date).AddSeconds(20)
    $hostHealth = $null
    $brokerHealth = $null
    $lastBrokerError = $null
    do {
        if ($hostProcess.HasExited) {
            $errorText = if (Test-Path $stderr) { Get-Content $stderr -Raw } else { '' }
            throw "Talvora Host erken kapandı. ExitCode=$($hostProcess.ExitCode) $errorText"
        }

        try {
            $hostHealth = Invoke-RestMethod -Uri "$BaseUrl/health" -TimeoutSec 2
            $candidate = Invoke-RestMethod -Uri "$BaseUrl/health/broker" -TimeoutSec 3
            if ($candidate.ok) {
                $brokerHealth = $candidate
                $lastBrokerError = $null
            }
            else {
                $lastBrokerError = [string]$candidate.error
                Start-Sleep -Milliseconds 250
            }
        }
        catch {
            $lastBrokerError = $_.Exception.Message
            Start-Sleep -Milliseconds 250
        }
    } until ($null -ne $brokerHealth -or (Get-Date) -ge $deadline)

    if ($null -eq $brokerHealth) {
        $hostError = if (Test-Path $stderr) { Get-Content $stderr -Raw } else { '' }
        throw "Kurulu Elevated Broker doğrulaması zaman aşımına uğradı. BrokerError=$lastBrokerError HostError=$hostError"
    }

    if (-not $brokerHealth.connected -or -not $brokerHealth.ready) {
        throw 'Kurulu broker connected/ready durumunu doğrulamadı.'
    }
    if ($brokerHealth.protocolVersion -ne 2) {
        throw "Beklenmeyen broker protocol version: $($brokerHealth.protocolVersion)"
    }
    if (-not $brokerHealth.isElevated) {
        throw 'Windows Service broker elevated olarak çalışmıyor.'
    }
    if (-not (Test-Administrator) -and $hostHealth.isElevated) {
        throw 'Talvora Host normal kullanıcı doğrulamasında beklenmedik biçimde elevated çalıştı.'
    }

    Write-Host ''
    Write-Host 'Talvora Elevated Broker Windows Service: GREEN' -ForegroundColor Green
    Write-Host '  service:          Running'
    Write-Host "  service account:  $($serviceInfo.StartName)"
    Write-Host '  transport:        gRPC over Windows Named Pipe'
    Write-Host "  protocol:         $($brokerHealth.protocolVersion)"
    Write-Host '  broker elevated:  GREEN'
    if (-not (Test-Administrator)) {
        Write-Host '  host non-elevated: GREEN'
        Write-Host '  cross-elevation:  GREEN'
    }
    else {
        Write-Host '  host non-elevated: SKIPPED (test shell is elevated)'
    }
}
finally {
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue
        $hostProcess.WaitForExit(5000) | Out-Null
    }
}
