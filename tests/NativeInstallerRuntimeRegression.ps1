param()

$ErrorActionPreference = 'Stop'

$service = Get-CimInstance Win32_Service -Filter "Name='Talvora'"
if ($null -eq $service) {
    throw 'Talvora service is missing.'
}

$expectedService = 'C:\Program Files\Talvora\Service\Talvora.exe'
$expectedTray = 'C:\Program Files\Talvora\Tray\Talvora.Tray.exe'

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

$servicePathNormalized = $service.PathName.Trim('"')

$result = [pscustomobject]@{
    ServicePath = $service.PathName
    ServiceUsesProgramFiles = ($servicePathNormalized -ieq $expectedService)
    TrayExecutableExists = (Test-Path -LiteralPath $expectedTray -PathType Leaf)
    TrayStartupRegistered = ([string]$trayRun -match [regex]::Escape($expectedTray))
    LegacyCloudflaredServiceExists = [bool](Get-Service Cloudflared -ErrorAction SilentlyContinue)
    LegacyCloudflaredDataExists = (Test-Path 'C:\ProgramData\cloudflared')
}

$result | Format-List

if (-not $result.ServiceUsesProgramFiles -or
    -not $result.TrayExecutableExists -or
    -not $result.TrayStartupRegistered -or
    $result.LegacyCloudflaredServiceExists -or
    $result.LegacyCloudflaredDataExists) {
    throw 'Native Talvora installer runtime state is not complete.'
}

Write-Output 'NATIVE_INSTALLER_RUNTIME_GREEN'
