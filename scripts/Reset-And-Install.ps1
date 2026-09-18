[CmdletBinding()]
param(
    [string] $RepoRoot = (Join-Path $env:USERPROFILE 'Talvora-MCP'),
    [switch] $FromTemp
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Remove-TalvoraClientTables {
    param([Parameter(Mandatory)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }

    $lines = [IO.File]::ReadAllLines($Path)
    $result = [Collections.Generic.List[string]]::new()
    $skip = $false
    foreach ($line in $lines) {
        if ($line -match '^\s*\[mcp_servers\.talvora[^\]]*\]\s*$') { $skip = $true; continue }
        if ($skip -and $line -match '^\s*\[') { $skip = $false }
        if (-not $skip) { $result.Add($line) }
    }
    [IO.File]::WriteAllLines($Path, $result, [Text.UTF8Encoding]::new($false))
}

function Remove-TalvoraRunValues {
    foreach ($runKey in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'
    )) {
        if (-not (Test-Path -LiteralPath $runKey)) { continue }
        $properties = Get-ItemProperty -LiteralPath $runKey
        foreach ($property in $properties.PSObject.Properties) {
            if ($property.Name -match '^PS') { continue }
            if ($property.Name -match '(?i)talvora' -or [string]$property.Value -match '(?i)talvora') {
                Remove-ItemProperty -LiteralPath $runKey -Name $property.Name -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

function Remove-TalvoraEnvironment {
    foreach ($target in @([EnvironmentVariableTarget]::User, [EnvironmentVariableTarget]::Machine)) {
        $variables = [Environment]::GetEnvironmentVariables($target)
        foreach ($name in @($variables.Keys)) {
            if ([string]$name -match '^(?i)TALVORA') {
                [Environment]::SetEnvironmentVariable([string]$name, $null, $target)
            }
        }

        $path = [Environment]::GetEnvironmentVariable('Path', $target)
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $cleanPath = @($path -split ';' | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '(?i)(^|\\)Talvora(?:-MCP)?(\\|$)'
        }) -join ';'
        if ($cleanPath -cne $path) {
            [Environment]::SetEnvironmentVariable('Path', $cleanPath, $target)
        }
    }
}

if (-not $FromTemp) {
    $tempScript = Join-Path $env:TEMP ('Talvora-reset-' + [Guid]::NewGuid().ToString('N') + '.ps1')
    Copy-Item -LiteralPath $PSCommandPath -Destination $tempScript -Force
    $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $child = Start-Process $ps -Verb RunAs -Wait -PassThru -WorkingDirectory $env:TEMP -ArgumentList @(
        '-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',('"{0}"' -f $tempScript),'-FromTemp','-RepoRoot',('"{0}"' -f $RepoRoot))
    Remove-Item -LiteralPath $tempScript -Force -ErrorAction SilentlyContinue
    exit $child.ExitCode
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator rights are required.' }
Set-Location $env:TEMP

Write-Host 'Removing every previous Talvora component...' -ForegroundColor Yellow

Get-CimInstance Win32_Service | Where-Object { $_.Name -like 'Talvora*' -or $_.DisplayName -like '*Talvora*' } | ForEach-Object {
    & "$env:SystemRoot\System32\sc.exe" stop $_.Name | Out-Null
    Start-Sleep -Milliseconds 300
    & "$env:SystemRoot\System32\sc.exe" delete $_.Name | Out-Host
}

Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object {
    $_.TaskName -like '*Talvora*' -or $_.TaskPath -like '*Talvora*'
} | ForEach-Object {
    Unregister-ScheduledTask -InputObject $_ -Confirm:$false -ErrorAction SilentlyContinue
}

Get-CimInstance Win32_Process | Where-Object {
    $_.ProcessId -ne $PID -and (
        ([string]$_.ExecutablePath -match '(?i)\\Talvora(?:-MCP)?\\') -or
        ([string]$_.CommandLine -match '(?i)Talvora(?:\.Gateway|\.SystemBroker|\.Service|\.dll|\.exe|-MCP)')
    )
} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object {
    $_.Name -like '*Talvora*' -or $_.DisplayName -like '*Talvora*'
} | Remove-NetFirewallRule -ErrorAction SilentlyContinue

Remove-TalvoraRunValues
Remove-TalvoraEnvironment

foreach ($key in @(
    'HKCU:\Software\Talvora',
    'HKLM:\Software\Talvora',
    'HKLM:\Software\WOW6432Node\Talvora',
    'HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\Application\Talvora'
)) {
    if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction SilentlyContinue }
}

$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
$paths = @(
    (Join-Path $env:LOCALAPPDATA 'Talvora'),
    (Join-Path $appData 'Talvora'),
    (Join-Path $env:ProgramData 'Talvora'),
    $(if ($programFiles) { Join-Path $programFiles 'Talvora' }),
    $(if ($programFilesX86) { Join-Path $programFilesX86 'Talvora' })
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
foreach ($path in $paths) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue }
}

foreach ($shortcutRoot in @(
    [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory),
    [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory),
    [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs),
    [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)
)) {
    if ([string]::IsNullOrWhiteSpace($shortcutRoot) -or -not (Test-Path -LiteralPath $shortcutRoot)) { continue }
    Get-ChildItem -LiteralPath $shortcutRoot -Recurse -Force -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like '*Talvora*'
    } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

Get-ChildItem -LiteralPath $env:TEMP -Directory -Filter 'Talvora*' -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

$clientHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
Remove-TalvoraClientTables -Path (Join-Path $clientHome 'config.toml')

if (-not (Test-Path -LiteralPath $RepoRoot -PathType Container)) {
    throw "Talvora local repository was not found: $RepoRoot"
}

if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot 'src\Talvora\Talvora.csproj') -PathType Leaf)) {
    throw "Talvora local repository is incomplete: $RepoRoot"
}

if (-not (Get-Command choco.exe -ErrorAction SilentlyContinue)) {
    Set-ExecutionPolicy Bypass -Scope Process -Force
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
    Invoke-Expression ((New-Object Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))
}
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { choco install git -y --no-progress }
if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) { choco install dotnet-10.0-sdk --version=10.0.400 -y --no-progress }
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')

Write-Host "Installing Talvora from local repository: $RepoRoot"

& (Join-Path $RepoRoot 'scripts\Install.ps1') -RepoRoot $RepoRoot -ClientHome $clientHome
if ($LASTEXITCODE -ne 0) { throw "Talvora installation failed: $LASTEXITCODE" }