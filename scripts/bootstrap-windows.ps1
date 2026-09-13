[CmdletBinding()]
param(
    [switch]$ElevatedChild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

$logDirectory = Join-Path $root '.talvora\logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logRole = if ($ElevatedChild) { 'elevated' } else { 'bootstrap' }
$logPath = Join-Path $logDirectory ("{0}-{1}-{2}.log" -f $logRole, (Get-Date -Format 'yyyyMMdd-HHmmss'), $PID)
Start-Transcript -Path $logPath -Force | Out-Null

function Refresh-Path {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($machine, $user) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $env:Path = $parts -join ';'
}

function Resolve-FirstExecutable {
    param(
        [Parameter(Mandatory)][string[]]$Candidates,
        [string[]]$CommandNames = @()
    )

    foreach ($candidate in $Candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    foreach ($commandName in $CommandNames) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
            return $command.Source
        }
    }

    return $null
}

function Resolve-DotNetExecutable {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        $(if ($env:ProgramW6432) { Join-Path $env:ProgramW6432 'dotnet\dotnet.exe' }),
        $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe' })
    ) | Where-Object { $_ }

    $resolved = Resolve-FirstExecutable -Candidates $candidates -CommandNames @('dotnet.exe', 'dotnet')
    return $resolved
}

function Resolve-PowerShellExecutable {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'),
        $(if ($env:ProgramW6432) { Join-Path $env:ProgramW6432 'PowerShell\7\pwsh.exe' }),
        $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\pwsh.exe' })
    ) | Where-Object { $_ }

    $resolved = Resolve-FirstExecutable -Candidates $candidates -CommandNames @('pwsh.exe', 'pwsh')
    return $resolved
}

function Resolve-GitExecutable {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Git\cmd\git.exe'),
        (Join-Path $env:ProgramFiles 'Git\bin\git.exe'),
        $(if ($env:ProgramW6432) { Join-Path $env:ProgramW6432 'Git\cmd\git.exe' }),
        $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Programs\Git\cmd\git.exe' })
    ) | Where-Object { $_ }

    $resolved = Resolve-FirstExecutable -Candidates $candidates -CommandNames @('git.exe', 'git')
    return $resolved
}

function Resolve-WingetExecutable {
    $candidates = @(
        $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe' })
    ) | Where-Object { $_ }

    $resolved = Resolve-FirstExecutable -Candidates $candidates -CommandNames @('winget.exe', 'winget')
    return $resolved
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-DotNet10 {
    param([string]$DotNetPath = (Resolve-DotNetExecutable))

    if ([string]::IsNullOrWhiteSpace($DotNetPath)) { return $false }
    $sdks = & $DotNetPath --list-sdks 2>$null
    return [bool]($sdks | Where-Object { $_ -match '^10\.0\.' })
}

function Show-LatestElevatedLog {
    $latest = Get-ChildItem -LiteralPath $logDirectory -Filter 'elevated-*.log' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) { return }

    Write-Host ''
    Write-Host "Elevated bootstrap log: $($latest.FullName)" -ForegroundColor Yellow
    Write-Host '--- son log satirlari ---' -ForegroundColor DarkYellow
    Get-Content -LiteralPath $latest.FullName -Tail 100 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
    Write-Host '--- log sonu ---' -ForegroundColor DarkYellow
}

function Ensure-ElevatedWhenInstallNeeded {
    param([bool]$NeedsInstall)

    if (-not $NeedsInstall -or (Test-Administrator)) {
        return $null
    }

    Write-Host 'Eksik geliştirme araçları için bir kez yönetici izni istenecek.' -ForegroundColor Yellow

    $currentPowerShell = (Get-Process -Id $PID -ErrorAction Stop).Path
    if ([string]::IsNullOrWhiteSpace($currentPowerShell)) {
        throw 'Çalışan PowerShell executable yolu belirlenemedi.'
    }

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-ElevatedChild'
    )

    $process = Start-Process -FilePath $currentPowerShell -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        Show-LatestElevatedLog
    }

    return [int]$process.ExitCode
}

function Install-WingetPackage {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$WingetPath
    )

    Write-Host "$Label kuruluyor ($Id)..." -ForegroundColor Yellow
    & $WingetPath install --id $Id --exact --source winget --accept-package-agreements --accept-source-agreements --silent
    if ($LASTEXITCODE -ne 0) {
        throw "$Label kurulumu başarısız oldu. ExitCode=$LASTEXITCODE"
    }
}

try {
    Refresh-Path

    Write-Host ''
    Write-Host '=== TALVORA / WINDOWS BOOTSTRAP ===' -ForegroundColor Cyan
    Write-Host "Project: $root"
    Write-Host "Log:     $logPath" -ForegroundColor DarkGray

    $dotnetPath = Resolve-DotNetExecutable
    $pwshPath = Resolve-PowerShellExecutable
    $gitPath = Resolve-GitExecutable

    $needsDotNet = -not (Test-DotNet10 -DotNetPath $dotnetPath)
    $needsPowerShell = [string]::IsNullOrWhiteSpace($pwshPath)
    $needsGit = [string]::IsNullOrWhiteSpace($gitPath)
    $needsInstall = $needsDotNet -or $needsPowerShell -or $needsGit

    $wingetPath = Resolve-WingetExecutable
    if ($needsInstall -and [string]::IsNullOrWhiteSpace($wingetPath)) {
        throw 'WinGet bulunamadı. Windows 11 App Installer / WinGet kurulumu gerekli.'
    }

    $delegatedExitCode = Ensure-ElevatedWhenInstallNeeded -NeedsInstall $needsInstall
    if ($null -ne $delegatedExitCode) {
        if ($delegatedExitCode -ne 0) {
            throw "Yükseltilmiş bootstrap başarısız oldu. ExitCode=$delegatedExitCode"
        }

        Write-Host 'Yükseltilmiş bootstrap başarıyla tamamlandı.' -ForegroundColor Green
        exit 0
    }

    if ($needsDotNet) {
        Install-WingetPackage -Id 'Microsoft.DotNet.SDK.10' -Label '.NET 10 SDK' -WingetPath $wingetPath
    }

    if ($needsPowerShell) {
        Install-WingetPackage -Id 'Microsoft.PowerShell' -Label 'PowerShell 7' -WingetPath $wingetPath
    }

    if ($needsGit) {
        Install-WingetPackage -Id 'Git.Git' -Label 'Git for Windows' -WingetPath $wingetPath
    }

    Refresh-Path
    $dotnetPath = Resolve-DotNetExecutable
    $pwshPath = Resolve-PowerShellExecutable
    $gitPath = Resolve-GitExecutable

    if (-not (Test-DotNet10 -DotNetPath $dotnetPath)) {
        throw '.NET 10 SDK kurulumdan sonra bulunamadı.'
    }
    if ([string]::IsNullOrWhiteSpace($pwshPath)) {
        throw 'PowerShell 7 kurulumdan sonra bulunamadı.'
    }
    if ([string]::IsNullOrWhiteSpace($gitPath)) {
        throw 'Git for Windows kurulumdan sonra bulunamadı.'
    }

    Write-Host "dotnet: $(& $dotnetPath --version) [$dotnetPath]" -ForegroundColor DarkGray
    Write-Host "pwsh:   $(& $pwshPath -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()') [$pwshPath]" -ForegroundColor DarkGray
    Write-Host "git:    $(& $gitPath --version) [$gitPath]" -ForegroundColor DarkGray

    if (-not (Test-Path (Join-Path $root '.git'))) {
        & $gitPath init -b main
        if ($LASTEXITCODE -ne 0) { throw "git init başarısız. ExitCode=$LASTEXITCODE" }
    }

    & (Join-Path $PSScriptRoot 'Test-Architecture.ps1')

    Write-Host 'NuGet restore...' -ForegroundColor Cyan
    & $dotnetPath restore Talvora.slnx
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore başarısız. ExitCode=$LASTEXITCODE" }

    Write-Host 'Build...' -ForegroundColor Cyan
    & $dotnetPath build Talvora.slnx -c Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build başarısız. ExitCode=$LASTEXITCODE" }

    Write-Host 'Tests...' -ForegroundColor Cyan
    & $dotnetPath test --solution Talvora.slnx -c Debug --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test başarısız. ExitCode=$LASTEXITCODE" }

    Write-Host ''
    Write-Host 'Talvora bootstrap doğrulaması GREEN.' -ForegroundColor Green
    Write-Host 'Başlatmak için TALVORA-BASLAT.bat dosyasını çift tıklayabilirsin.'
}
catch {
    Write-Host ''
    Write-Host 'TALVORA BOOTSTRAP HATASI' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ("Hata türü: {0}" -f $_.Exception.GetType().FullName) -ForegroundColor DarkRed
    Write-Host ("Konum: {0}" -f $_.InvocationInfo.PositionMessage) -ForegroundColor DarkRed
    Write-Host "Tam log: $logPath" -ForegroundColor Yellow
    exit 1
}
finally {
    try { Stop-Transcript | Out-Null } catch { }
}
