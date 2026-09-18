param()

$ErrorActionPreference = 'Stop'

$service = Get-CimInstance Win32_Service -Filter "Name='Talvora'"
if ($null -eq $service) {
    throw 'Talvora service is missing.'
}

$versionedServicePattern = '^"?C:\\Program Files\\Talvora\\Versions\\[0-9a-f]{40}-[0-9]{17}\\Service\\Talvora\.exe"?$'
$versionedTrayPattern = '^"?C:\\Program Files\\Talvora\\Versions\\[0-9a-f]{40}-[0-9]{17}\\Tray\\Talvora\.Tray\.exe"?$'

$interactiveUser = (Get-CimInstance Win32_ComputerSystem).UserName
if ([string]::IsNullOrWhiteSpace($interactiveUser)) {
    throw 'No interactive Windows user is logged on.'
}

$sid = ([Security.Principal.NTAccount]$interactiveUser).Translate([Security.Principal.SecurityIdentifier]).Value
$runKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Run"
$trayRun = if (Test-Path $runKey) {
    (Get-ItemProperty $runKey -Name 'TalvoraTray' -ErrorAction SilentlyContinue).TalvoraTray
} else {
    $null
}

$serviceUsesVersionedPath = [string]$service.PathName -match $versionedServicePattern
$trayStartupRegistered = [string]$trayRun -match $versionedTrayPattern
$trayExecutable = if ($trayStartupRegistered) { ([string]$trayRun).Trim('"') } else { $null }

$result = [pscustomobject]@{
    ServicePath = $service.PathName
    ServiceUsesVersionedPath = $serviceUsesVersionedPath
    TrayExecutableExists = ($trayStartupRegistered -and (Test-Path -LiteralPath $trayExecutable -PathType Leaf))
    TrayStartupRegistered = $trayStartupRegistered
    LegacyCloudflaredServiceExists = [bool](Get-Service Cloudflared -ErrorAction SilentlyContinue)
    LegacyCloudflaredDataExists = (Test-Path 'C:\ProgramData\cloudflared')
    LegacyProgramDataServiceExists = (Test-Path 'C:\ProgramData\Talvora\Service')
    LegacyProgramFilesServiceExists = (Test-Path 'C:\Program Files\Talvora\Service')
    LegacyProgramFilesTrayExists = (Test-Path 'C:\Program Files\Talvora\Tray')
}

$result | Format-List

if (-not $result.ServiceUsesVersionedPath -or
    -not $result.TrayExecutableExists -or
    -not $result.TrayStartupRegistered -or
    $result.LegacyCloudflaredServiceExists -or
    $result.LegacyCloudflaredDataExists -or
    $result.LegacyProgramDataServiceExists -or
    $result.LegacyProgramFilesServiceExists -or
    $result.LegacyProgramFilesTrayExists) {
    throw 'Native Talvora installer runtime state is not complete.'
}

Write-Output 'NATIVE_INSTALLER_RUNTIME_GREEN'
