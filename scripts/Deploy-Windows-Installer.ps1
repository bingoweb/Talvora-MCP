param(
    [string] $InstallerPath,
    [switch] $WaitForCompletion,
    [ValidateRange(30, 1800)]
    [int] $TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PSVersionTable.PSEdition -eq 'Core' -and -not $IsWindows) {
    throw 'Talvora canonical deploy launcher requires Windows.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $repoRoot 'artifacts\installer\Talvora-Setup.exe'
}

$installerFullPath = [IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $installerFullPath -PathType Leaf)) {
    throw "Canonical Talvora installer was not found: $installerFullPath"
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$windowsPrincipal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $windowsPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Canonical deploy launcher must run elevated (Administrator or LocalSystem).'
}

$taskName = 'Talvora Canonical Deploy'
$taskLogonServiceAccount = 5
$taskCreateOrUpdate = 6
$taskRunLevelHighest = 1
$taskInstancesIgnoreNew = 2
$taskStateRunning = 4

$scheduler = New-Object -ComObject 'Schedule.Service'
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')

$existing = $null
try {
    $existing = $folder.GetTask("\$taskName")
}
catch {
    $existing = $null
}

if ($null -ne $existing) {
    $runningInstances = @($existing.GetInstances(0))
    if ($runningInstances.Count -gt 0 -or [int]$existing.State -eq $taskStateRunning) {
        throw 'A canonical Talvora deployment is already running.'
    }
}

$definition = $scheduler.NewTask(0)
$definition.RegistrationInfo.Description =
    'Talvora canonical independent LocalSystem installer launcher. On-demand only; no schedule trigger.'
$definition.RegistrationInfo.Source = 'Talvora-MCP/scripts/Deploy-Windows-Installer.ps1'

$definition.Principal.UserId = 'SYSTEM'
$definition.Principal.LogonType = $taskLogonServiceAccount
$definition.Principal.RunLevel = $taskRunLevelHighest

$definition.Settings.Enabled = $true
$definition.Settings.Hidden = $true
$definition.Settings.AllowDemandStart = $true
$definition.Settings.StartWhenAvailable = $false
$definition.Settings.DisallowStartIfOnBatteries = $false
$definition.Settings.StopIfGoingOnBatteries = $false
$definition.Settings.MultipleInstances = $taskInstancesIgnoreNew
$definition.Settings.ExecutionTimeLimit = "PT$($TimeoutSeconds + 120)S"

$action = $definition.Actions.Create(0)
$action.Path = $installerFullPath
$action.Arguments = '--silent'
$action.WorkingDirectory = Split-Path -Parent $installerFullPath

$registered = $folder.RegisterTaskDefinition(
    $taskName,
    $definition,
    $taskCreateOrUpdate,
    'SYSTEM',
    $null,
    $taskLogonServiceAccount,
    $null)

$previousLastRunTime = [DateTime]$registered.LastRunTime
$running = $registered.Run($null)
$taskInstanceGuid = [string]$running.InstanceGuid
if ([string]::IsNullOrWhiteSpace($taskInstanceGuid)) {
    throw 'Task Scheduler accepted the deploy request but did not return a task instance identity.'
}
$sha256 = (Get-FileHash -LiteralPath $installerFullPath -Algorithm SHA256).Hash

if (-not $WaitForCompletion) {
    [pscustomobject]@{
        launched = $true
        waitForCompletion = $false
        taskName = $taskName
        installer = $installerFullPath
        sha256 = $sha256
        taskInstance = $taskInstanceGuid
        note = 'Installer is running outside the Talvora service process tree. Reconnect and validate system_info/version-root after the service switch.'
    } | ConvertTo-Json -Compress
    return
}

$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
$instanceObserved = $false
$completionObserved = $false
while ([DateTimeOffset]::UtcNow -lt $deadline) {
    $instances = @($registered.GetInstances(0))
    $matchingInstances = @(
        $instances | Where-Object {
            [string]$_.InstanceGuid -eq $taskInstanceGuid
        }
    )
    if ($matchingInstances.Count -gt 0) {
        $instanceObserved = $true
    }

    $currentLastRunTime = [DateTime]$registered.LastRunTime
    $lastRunTransitioned = $currentLastRunTime -ne $previousLastRunTime
    $registeredIsRunning = [int]$registered.State -eq $taskStateRunning

    if ($matchingInstances.Count -eq 0 -and
        -not $registeredIsRunning -and
        ($instanceObserved -or $lastRunTransitioned)) {
        $completionObserved = $true
        break
    }

    Start-Sleep -Milliseconds 250
}

if (-not $completionObserved) {
    throw "Talvora installer task instance $taskInstanceGuid did not reach an attributable terminal state within $TimeoutSeconds seconds."
}

$exitCode = [int]$registered.LastTaskResult
if ($exitCode -ne 0) {
    throw "Talvora canonical installer task failed. LastTaskResult=$exitCode"
}

[pscustomobject]@{
    launched = $true
    waitForCompletion = $true
    completed = $true
    taskName = $taskName
    installer = $installerFullPath
    sha256 = $sha256
    taskInstance = $taskInstanceGuid
    lastTaskResult = $exitCode
    lastRunTime = $registered.LastRunTime
} | ConvertTo-Json -Compress
