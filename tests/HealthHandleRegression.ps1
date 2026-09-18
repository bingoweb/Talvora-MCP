param(
    [string] $HealthUrl = 'http://127.0.0.1:7676/healthz',
    [string] $ServiceName = 'Talvora',
    [int] $WarmupRequests = 25,
    [int] $MeasuredRequests = 200,
    [int] $MaxHandleDelta = 25
)

$ErrorActionPreference = 'Stop'

$service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
if ($null -eq $service -or $service.State -ne 'Running') {
    throw "Service '$ServiceName' is not running."
}

1..$WarmupRequests | ForEach-Object {
    Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 5 -Proxy $null | Out-Null
}

$process = Get-Process -Id $service.ProcessId
$before = $process.HandleCount

1..$MeasuredRequests | ForEach-Object {
    Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 5 -Proxy $null | Out-Null
}

Start-Sleep -Milliseconds 250
$process.Refresh()
$after = $process.HandleCount
$delta = $after - $before

[pscustomobject]@{
    Service = $ServiceName
    ProcessId = $service.ProcessId
    Requests = $MeasuredRequests
    HandlesBefore = $before
    HandlesAfter = $after
    HandleDelta = $delta
    MaxAllowedDelta = $MaxHandleDelta
} | Format-List

if ($delta -gt $MaxHandleDelta) {
    throw "Health polling leaked process handles. Delta=$delta MaxAllowed=$MaxHandleDelta"
}

Write-Output 'HEALTH_HANDLE_REGRESSION_GREEN'
