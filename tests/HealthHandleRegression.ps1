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

$handler = [System.Net.Http.SocketsHttpHandler]::new()
$client = [System.Net.Http.HttpClient]::new($handler, $true)
$client.Timeout = [TimeSpan]::FromSeconds(5)

function Invoke-HealthRequest {
    $response = $client.GetAsync($HealthUrl).GetAwaiter().GetResult()
    try {
        $response.EnsureSuccessStatusCode() | Out-Null
        $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult() | Out-Null
    }
    finally {
        $response.Dispose()
    }
}

try {
    1..$WarmupRequests | ForEach-Object {
        Invoke-HealthRequest
    }

    $before = [int](
        Get-CimInstance Win32_Process -Filter "ProcessId=$($service.ProcessId)"
    ).HandleCount

    1..$MeasuredRequests | ForEach-Object {
        Invoke-HealthRequest
    }

    Start-Sleep -Milliseconds 500
    $after = [int](
        Get-CimInstance Win32_Process -Filter "ProcessId=$($service.ProcessId)"
    ).HandleCount
}
finally {
    $client.Dispose()
}

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
