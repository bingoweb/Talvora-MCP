param()

$ErrorActionPreference = 'Stop'

$service = Get-CimInstance Win32_Service -Filter "Name='Talvora'"
if ($null -eq $service) { throw 'Talvora service is missing.' }
$serviceController = Get-Service Talvora

$qfailure = (& sc.exe qfailure Talvora | Out-String)
$qfailureFlag = (& sc.exe qfailureflag Talvora | Out-String)
$qSidType = (& sc.exe qsidtype Talvora | Out-String)

$versionedServicePattern = '^"?C:\\Program Files\\Talvora\\Versions\\[0-9a-f]{40}-[0-9]{17}\\Service\\Talvora\.exe"?$'
$versionedTrayPattern = '^"?C:\\Program Files\\Talvora\\Versions\\[0-9a-f]{40}-[0-9]{17}\\Tray\\Talvora\.Tray\.exe"?$'

$interactiveUser = (Get-CimInstance Win32_ComputerSystem).UserName
if ([string]::IsNullOrWhiteSpace($interactiveUser)) { throw 'No interactive Windows user is logged on.' }

$sid = ([Security.Principal.NTAccount]$interactiveUser).Translate([Security.Principal.SecurityIdentifier]).Value
$userProfile = Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $sid } | Select-Object -First 1
$currentStatePath = if ($null -ne $userProfile) { Join-Path $userProfile.LocalPath 'AppData\Local\Talvora\current.json' } else { $null }
$currentState = if ($null -ne $currentStatePath -and (Test-Path $currentStatePath)) { Get-Content $currentStatePath -Raw | ConvertFrom-Json } else { $null }
$runKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Run"
$trayRun = if (Test-Path $runKey) { (Get-ItemProperty $runKey -Name 'TalvoraTray' -ErrorAction SilentlyContinue).TalvoraTray } else { $null }

$serviceUsesVersionedPath = [string]$service.PathName -match $versionedServicePattern
$trayStartupRegistered = [string]$trayRun -match $versionedTrayPattern
$trayExecutable = if ($trayStartupRegistered) { ([string]$trayRun).Trim('"') } else { $null }

$legacyTrayPath = 'C:\Program Files\Talvora\Tray'
$sessionManager = Get-ItemProperty 'Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager' -Name 'PendingFileRenameOperations' -ErrorAction SilentlyContinue
$pendingDeletes = @($sessionManager.PendingFileRenameOperations)
$legacyTrayPendingDelete = @($pendingDeletes | Where-Object { ([string]$_) -match [regex]::Escape($legacyTrayPath) }).Count -gt 0

$result = [pscustomobject]@{
    ServiceState = $service.State
    ServiceStartMode = $service.StartMode
    ServiceStartName = $service.StartName
    ServiceCanStop = $serviceController.CanStop
    ServiceCanPauseAndContinue = $serviceController.CanPauseAndContinue
    FailureRestart1000 = ($qfailure -match '1000 milliseconds')
    FailureRestart3000 = ($qfailure -match '3000 milliseconds')
    FailureRestart10000 = ($qfailure -match '10000 milliseconds')
    FailureRestart30000 = ($qfailure -match '30000 milliseconds')
    FailureRestart60000 = ($qfailure -match '60000 milliseconds')
    FailureActionsOnNonCrash = ($qfailureFlag -match 'TRUE')
    ServiceSidUnrestricted = ($qSidType -match 'UNRESTRICTED')
    ToolCount = if ($null -ne $currentState) { [int]$currentState.ToolCount } else { -1 }
    ServicePath = $service.PathName
    ServiceUsesVersionedPath = $serviceUsesVersionedPath
    TrayExecutableExists = ($trayStartupRegistered -and (Test-Path -LiteralPath $trayExecutable -PathType Leaf))
    TrayStartupRegistered = $trayStartupRegistered
    LegacyCloudflaredServiceExists = [bool](Get-Service Cloudflared -ErrorAction SilentlyContinue)
    LegacyCloudflaredDataExists = (Test-Path 'C:\ProgramData\cloudflared')
    LegacyProgramDataServiceExists = (Test-Path 'C:\ProgramData\Talvora\Service')
    LegacyProgramFilesServiceExists = (Test-Path 'C:\Program Files\Talvora\Service')
    LegacyProgramFilesTrayExists = (Test-Path $legacyTrayPath)
    LegacyProgramFilesTrayPendingDelete = $legacyTrayPendingDelete
}

$result | Format-List

if ($result.ServiceState -ne 'Running' -or
    $result.ServiceStartMode -ne 'Auto' -or
    $result.ServiceStartName -ne 'LocalSystem' -or
    $result.ServiceCanStop -or
    $result.ServiceCanPauseAndContinue -or
    -not $result.FailureRestart1000 -or
    -not $result.FailureRestart3000 -or
    -not $result.FailureRestart10000 -or
    -not $result.FailureRestart30000 -or
    -not $result.FailureRestart60000 -or
    -not $result.FailureActionsOnNonCrash -or
    -not $result.ServiceSidUnrestricted -or
    $result.ToolCount -ne 32 -or
    -not $result.ServiceUsesVersionedPath -or
    -not $result.TrayExecutableExists -or
    -not $result.TrayStartupRegistered -or
    $result.LegacyCloudflaredServiceExists -or
    $result.LegacyCloudflaredDataExists -or
    $result.LegacyProgramDataServiceExists -or
    $result.LegacyProgramFilesServiceExists -or
    ($result.LegacyProgramFilesTrayExists -and -not $result.LegacyProgramFilesTrayPendingDelete)) {
    throw 'Native Talvora installer runtime state is not complete.'
}

Write-Output 'NATIVE_INSTALLER_RUNTIME_GREEN'