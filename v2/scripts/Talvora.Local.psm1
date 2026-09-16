Set-StrictMode -Version Latest

function Get-TalvoraLocalHealth {
    Add-Type -AssemblyName System.Net.Http
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(2)
    $response = $null
    try {
        $response = $client.GetAsync('http://127.0.0.1:7676/healthz').GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { return $null }
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        if ($body.product -eq 'Talvora' -and $body.version -eq '2.0-dev' -and $body.mcp -eq '/mcp' -and [int]$body.processId -gt 0) { return $body }
    }
    catch { return $null }
    finally { if ($null -ne $response) { $response.Dispose() }; $client.Dispose() }
    return $null
}

function Get-TalvoraClientConfigText {
    param([AllowEmptyString()][string] $Content = '')
    $nl = if ($Content.Contains("`r`n")) { "`r`n" } else { "`n" }
    $block = (@(
        '[mcp_servers.talvora_local]',
        'url = "http://127.0.0.1:7676/mcp"',
        'enabled = true',
        'startup_timeout_sec = 20',
        'tool_timeout_sec = 300',
        'default_tools_approval_mode = "approve"',
        '', ''
    ) -join $nl)
    $pattern = '(?ms)^\[mcp_servers\.talvora_local\][ \t]*(?:\r?\n|\z).*?(?=^\[|\z)'
    $rx = [regex]::new($pattern)
    if ($rx.Matches($Content).Count -gt 1) { throw 'Duplicate talvora_local tables in client configuration.' }
    if ($rx.IsMatch($Content)) {
        # Replace the Talvora-owned table only. Other servers and model settings stay intact.
        return $rx.Replace($Content, [Text.RegularExpressions.MatchEvaluator]{ param($match) $block }, 1)
    }
    if ($Content -match '(?m)^\[.*talvora_local') { throw 'An alternate talvora_local table already exists; client configuration was left unchanged.' }
    if ($Content.Length -eq 0) { return $block }
    $separator = if ($Content.EndsWith("`n")) { $nl } else { $nl + $nl }
    return $Content + $separator + $block
}

function New-TalvoraLocalTask {
    param([Parameter(Mandatory)][string] $Launcher, [Parameter(Mandatory)][string] $ConfigPath, [Parameter(Mandatory)][string] $UserId)
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -ConfigPath "{1}"' -f $Launcher,$ConfigPath
    $action = New-ScheduledTaskAction -Execute $powershell -Argument $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($Launcher))
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $UserId
    $principal = New-ScheduledTaskPrincipal -UserId $UserId -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval ([TimeSpan]::FromMinutes(1)) -MultipleInstances IgnoreNew -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    return New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Talvora local MCP. Managed local startup; no external connection is required.'
}

function Test-TalvoraProcessPath {
    param($Process, [string[]] $DllPaths = @(), [string[]] $ExePaths = @())
    if ($null -eq $Process) { return $false }
    foreach ($path in $ExePaths) {
        if ($path -and [string]$Process.ExecutablePath -ieq $path) { return $true }
    }
    foreach ($path in $DllPaths) {
        if ($path -and [string]$Process.CommandLine -match ('(?:^|[\s"])' + [regex]::Escape($path) + '(?:[\s"]|$)')) { return $true }
    }
    return $false
}

Export-ModuleMember -Function Get-TalvoraLocalHealth,Get-TalvoraClientConfigText,New-TalvoraLocalTask,Test-TalvoraProcessPath
