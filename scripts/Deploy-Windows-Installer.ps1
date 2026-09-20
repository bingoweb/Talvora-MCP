param(
    [string] $InstallerPath,
    [string] $ManifestPath,
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

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path (Split-Path -Parent $installerFullPath) 'Talvora-Setup.manifest.json'
}
$manifestFullPath = [IO.Path]::GetFullPath($ManifestPath)
if (-not (Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "Canonical Talvora installer identity manifest was not found: $manifestFullPath"
}

function Get-RequiredManifestValue {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Manifest,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $property = $Manifest.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        throw "Installer identity manifest is missing required field '$Name'."
    }

    return $property.Value
}

function Assert-InstallerArtifactIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $ExpectedFileName,

        [Parameter(Mandatory = $true)]
        [long] $ExpectedSizeBytes,

        [Parameter(Mandatory = $true)]
        [string] $ExpectedSha256
    )

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if (-not [string]::Equals($item.Name, $ExpectedFileName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer file name does not match the canonical build manifest. Expected=$ExpectedFileName Actual=$($item.Name)"
    }

    if ([long]$item.Length -ne $ExpectedSizeBytes) {
        throw "Installer size does not match the canonical build manifest. Expected=$ExpectedSizeBytes Actual=$($item.Length)"
    }

    $actualSha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not [string]::Equals($actualSha256, $ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer SHA-256 does not match the canonical build manifest. Expected=$ExpectedSha256 Actual=$actualSha256"
    }

    return $actualSha256
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$windowsPrincipal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $windowsPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Canonical deploy launcher must run elevated (Administrator or LocalSystem).'
}

$buildMutexName = 'Global\Talvora.BuildWindowsInstaller.v2'
$buildMutex = [Threading.Mutex]::new($false, $buildMutexName)
$buildMutexOwned = $false
try {
    try {
        $buildMutexOwned = $buildMutex.WaitOne(0)
    }
    catch [Threading.AbandonedMutexException] {
        $buildMutexOwned = $true
    }

    if (-not $buildMutexOwned) {
        throw 'Canonical installer build is still running; deploy will not consume a mutable artifact.'
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Canonical installer identity manifest is invalid JSON: $manifestFullPath"
    }

    $schemaVersion = [int](Get-RequiredManifestValue -Manifest $manifest -Name 'schemaVersion')
    if ($schemaVersion -ne 1) {
        throw "Unsupported installer identity manifest schema: $schemaVersion"
    }

    $expectedFileName = [string](Get-RequiredManifestValue -Manifest $manifest -Name 'installerFileName')
    $expectedSizeBytes = [long](Get-RequiredManifestValue -Manifest $manifest -Name 'sizeBytes')
    $expectedSha256 = ([string](Get-RequiredManifestValue -Manifest $manifest -Name 'sha256')).ToLowerInvariant()
    $sourceCommit = [string](Get-RequiredManifestValue -Manifest $manifest -Name 'sourceCommit')
    $sourceHeadCommit = [string](Get-RequiredManifestValue -Manifest $manifest -Name 'sourceHeadCommit')
    $sourceIndexTree = [string](Get-RequiredManifestValue -Manifest $manifest -Name 'sourceIndexTree')
    $runtimeInputsSha256 = ([string](Get-RequiredManifestValue -Manifest $manifest -Name 'runtimeInputsSha256')).ToLowerInvariant()

    if ($expectedSizeBytes -le 0 -or
        $expectedSha256 -notmatch '^[0-9a-f]{64}$' -or
        $runtimeInputsSha256 -notmatch '^[0-9a-f]{64}$' -or
        [string]::IsNullOrWhiteSpace($sourceCommit) -or
        [string]::IsNullOrWhiteSpace($sourceHeadCommit) -or
        [string]::IsNullOrWhiteSpace($sourceIndexTree)) {
        throw 'Canonical installer identity manifest contains invalid identity fields.'
    }

    $sha256 = Assert-InstallerArtifactIdentity -Path $installerFullPath -ExpectedFileName $expectedFileName -ExpectedSizeBytes $expectedSizeBytes -ExpectedSha256 $expectedSha256

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
$sha256 = Assert-InstallerArtifactIdentity -Path $installerFullPath -ExpectedFileName $expectedFileName -ExpectedSizeBytes $expectedSizeBytes -ExpectedSha256 $expectedSha256
$running = $registered.Run($null)
$taskInstanceGuid = [string]$running.InstanceGuid
if ([string]::IsNullOrWhiteSpace($taskInstanceGuid)) {
    throw 'Task Scheduler accepted the deploy request but did not return a task instance identity.'
}

if (-not $WaitForCompletion) {
    [pscustomobject]@{
        launched = $true
        waitForCompletion = $false
        taskName = $taskName
        installer = $installerFullPath
        manifest = $manifestFullPath
        sha256 = $sha256
        sourceCommit = $sourceCommit
        sourceHeadCommit = $sourceHeadCommit
        sourceIndexTree = $sourceIndexTree
        runtimeInputsSha256 = $runtimeInputsSha256
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
    manifest = $manifestFullPath
    sha256 = $sha256
    sourceCommit = $sourceCommit
    sourceHeadCommit = $sourceHeadCommit
    sourceIndexTree = $sourceIndexTree
    runtimeInputsSha256 = $runtimeInputsSha256
    taskInstance = $taskInstanceGuid
    lastTaskResult = $exitCode
    lastRunTime = $registered.LastRunTime
} | ConvertTo-Json -Compress
}
finally {
    try {
        if ($buildMutexOwned) {
            $buildMutex.ReleaseMutex()
        }
    }
    finally {
        $buildMutex.Dispose()
    }
}
