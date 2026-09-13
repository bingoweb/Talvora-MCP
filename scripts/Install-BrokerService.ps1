[CmdletBinding()]
param(
    [switch]$ElevatedChild,
    [string]$AllowedUserSid
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$serviceName = 'TalvoraElevatedBroker'
$displayName = 'Talvora Elevated Broker'
$description = 'Talvora privileged Windows broker over local gRPC named pipes.'
$installDirectory = Join-Path $env:ProgramFiles 'Talvora\Broker'
$brokerProject = Join-Path $root 'src\Talvora.ElevatedBroker\Talvora.ElevatedBroker.csproj'
$hostProject = Join-Path $root 'src\Talvora.Host\Talvora.Host.csproj'
$verifyScript = Join-Path $PSScriptRoot 'Test-InstalledBrokerService.ps1'
$scExe = Join-Path $env:SystemRoot 'System32\sc.exe'

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

function Invoke-Sc {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & $scExe @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe başarısız oldu. ExitCode=$LASTEXITCODE Args=$($Arguments -join ' ')"
    }
}

function Stop-BrokerServiceIfPresent {
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($null -eq $service -or $service.Status -eq 'Stopped') {
        return
    }

    Stop-Service -Name $serviceName -Force -ErrorAction Stop
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
}

function Write-BrokerConfiguration {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$UserSid
    )

    $configuration = [ordered]@{
        Broker = [ordered]@{
            PipeName = 'Talvora.ElevatedBroker.v2'
            AllowedUserSid = $UserSid
        }
        Logging = [ordered]@{
            EventLog = [ordered]@{
                LogLevel = [ordered]@{
                    Default = 'Information'
                }
            }
        }
    }

    $json = $configuration | ConvertTo-Json -Depth 8
    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText((Join-Path $Directory 'appsettings.json'), $json, $encoding)
}

function Install-ServiceDefinition {
    param([Parameter(Mandatory)][string]$ExecutablePath)

    $quotedExecutable = '"' + $ExecutablePath + '"'
    $existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

    if ($null -eq $existing) {
        Invoke-Sc -Arguments @(
            'create', $serviceName,
            'binPath=', $quotedExecutable,
            'start=', 'auto',
            'obj=', 'LocalSystem',
            'DisplayName=', $displayName
        )
    }
    else {
        Invoke-Sc -Arguments @(
            'config', $serviceName,
            'binPath=', $quotedExecutable,
            'start=', 'auto',
            'obj=', 'LocalSystem',
            'DisplayName=', $displayName
        )
    }

    Invoke-Sc -Arguments @('description', $serviceName, $description)
    Invoke-Sc -Arguments @(
        'failure', $serviceName,
        'reset=', '86400',
        'actions=', 'restart/2000/restart/5000/restart/10000'
    )
}

$dotnetPath = Resolve-DotNetPath
$isAdministrator = Test-Administrator
if ($isAdministrator -and -not $ElevatedChild) {
    Write-Host 'Bu kurulum oturumu zaten yönetici yetkisinde; ayrı bir UAC penceresi açılmayacak.' -ForegroundColor DarkYellow
}

if ([string]::IsNullOrWhiteSpace($AllowedUserSid)) {
    $AllowedUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
}

if (-not $isAdministrator) {
    if ($ElevatedChild) {
        throw 'Elevated Broker kurulum çocuğu yönetici yetkisi olmadan başlatıldı.'
    }

    Write-Host 'Talvora Host Release build hazırlanıyor...' -ForegroundColor Cyan
    & $dotnetPath build $hostProject -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Talvora Host Release build başarısız oldu. ExitCode=$LASTEXITCODE"
    }

    Write-Host ''
    Write-Host 'Elevated Broker Windows Service kurulumu için bir kez yönetici izni istenecek.' -ForegroundColor Yellow
    $powerShellPath = (Get-Process -Id $PID -ErrorAction Stop).Path
    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-ElevatedChild',
        '-AllowedUserSid', $AllowedUserSid
    )

    $child = Start-Process -FilePath $powerShellPath -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    if ($child.ExitCode -ne 0) {
        throw "Elevated Broker yönetici kurulumu başarısız oldu. ExitCode=$($child.ExitCode)"
    }

    & $verifyScript -DotNetPath $dotnetPath -Configuration Release
    exit $LASTEXITCODE
}

Write-Host ''
Write-Host '=== TALVORA / ELEVATED BROKER SERVICE INSTALL ===' -ForegroundColor Cyan
Write-Host "Allowed user SID: $AllowedUserSid" -ForegroundColor DarkGray
Write-Host "Install directory: $installDirectory" -ForegroundColor DarkGray

Stop-BrokerServiceIfPresent

$stagingDirectory = Join-Path $env:TEMP ("Talvora-Broker-Publish-{0}" -f [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

    & $dotnetPath publish $brokerProject -c Release -o $stagingDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Elevated Broker publish başarısız oldu. ExitCode=$LASTEXITCODE"
    }

    if (Test-Path -LiteralPath $installDirectory) {
        Remove-Item -LiteralPath $installDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $stagingDirectory '*') -Destination $installDirectory -Recurse -Force
    Write-BrokerConfiguration -Directory $installDirectory -UserSid $AllowedUserSid

    $brokerExecutable = Join-Path $installDirectory 'Talvora.ElevatedBroker.exe'
    if (-not (Test-Path -LiteralPath $brokerExecutable -PathType Leaf)) {
        throw "Published broker executable bulunamadı: $brokerExecutable"
    }

    Install-ServiceDefinition -ExecutablePath $brokerExecutable

    Start-Service -Name $serviceName
    $service = Get-Service -Name $serviceName -ErrorAction Stop
    $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))

    if ($service.Status -ne 'Running') {
        throw "Elevated Broker servisi Running durumuna geçmedi. Status=$($service.Status)"
    }

    Write-Host ''
    Write-Host 'Talvora Elevated Broker Windows Service kurulumu: GREEN' -ForegroundColor Green
    Write-Host "  service:      $serviceName"
    Write-Host '  account:      LocalSystem'
    Write-Host '  startup:      Automatic'
    Write-Host '  recovery:     restart / restart / restart'
    Write-Host '  pipe:         Talvora.ElevatedBroker.v2'
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not $ElevatedChild) {
    & $verifyScript -DotNetPath $dotnetPath -Configuration Release
    exit $LASTEXITCODE
}
