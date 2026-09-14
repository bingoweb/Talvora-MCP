[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DotNetPath,

    [string]$Configuration = 'Debug',

    [string]$BaseUrl = 'http://127.0.0.1:17676'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$hostDllRelative = "src\Talvora.Host\bin\$Configuration\net10.0-windows\Talvora.Host.dll"
$hostDll = Join-Path $root $hostDllRelative
$logDir = Join-Path $root '.talvora\logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

if (-not (Test-Path -LiteralPath $hostDll)) {
    throw "Talvora.Host bulunamadı: $hostDll"
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$stdoutLog = Join-Path $logDir "mcp-smoke-$timestamp.stdout.log"
$stderrLog = Join-Path $logDir "mcp-smoke-$timestamp.stderr.log"

function ConvertFrom-McpEventStream {
    param([Parameter(Mandatory)][string]$Content)

    $dataLines = @(
        $Content -split "`r?`n" |
            Where-Object { $_ -like 'data:*' } |
            ForEach-Object { $_.Substring(5).TrimStart() }
    )

    if ($dataLines.Count -eq 0) {
        throw "MCP yanıtında data satırı bulunamadı. Yanıt: $Content"
    }

    return ($dataLines -join "`n") | ConvertFrom-Json
}

function Invoke-McpRequest {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][hashtable]$Params,
        [string]$Name,
        [Parameter(Mandatory)][string]$Id
    )

    $headers = @{
        Accept                 = 'application/json, text/event-stream'
        'MCP-Protocol-Version' = '2026-07-28'
        'Mcp-Method'           = $Method
    }

    if (-not [string]::IsNullOrWhiteSpace($Name)) {
        $headers['Mcp-Name'] = $Name
    }

    $payload = @{
        jsonrpc = '2.0'
        id      = $Id
        method  = $Method
        params  = $Params
    } | ConvertTo-Json -Depth 20 -Compress

    $response = Invoke-WebRequest `
        -Uri "$BaseUrl/mcp" `
        -Method Post `
        -ContentType 'application/json' `
        -UseBasicParsing `
        -Headers $headers `
        -Body $payload

    return ConvertFrom-McpEventStream -Content $response.Content
}

$meta = @{
    'io.modelcontextprotocol/protocolVersion' = '2026-07-28'
    'io.modelcontextprotocol/clientInfo' = @{
        name = 'Talvora-Smoke'
        version = '0.1'
    }
    'io.modelcontextprotocol/clientCapabilities' = @{}
}

$process = $null
try {
    $process = Start-Process `
        -FilePath $DotNetPath `
        -ArgumentList @($hostDllRelative, '--urls', $BaseUrl) `
        -WorkingDirectory $root `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError $stderrLog `
        -PassThru `
        -WindowStyle Hidden

    $deadline = (Get-Date).AddSeconds(20)
    $health = $null
    do {
        if ($process.HasExited) {
            $stderr = if (Test-Path $stderrLog) { Get-Content $stderrLog -Raw } else { '' }
            throw "Talvora Host smoke test sırasında erken kapandı. ExitCode=$($process.ExitCode) $stderr"
        }

        try {
            $health = Invoke-RestMethod -Uri "$BaseUrl/health" -TimeoutSec 2
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    } until ($null -ne $health -or (Get-Date) -ge $deadline)

    if ($null -eq $health -or -not $health.ok) {
        throw 'Talvora /health smoke testi zaman aşımına uğradı.'
    }

    $discover = Invoke-McpRequest -Method 'server/discover' -Params @{ _meta = $meta } -Id 'discover-1'
    if ($discover.result.supportedVersions -notcontains '2026-07-28') {
        throw 'MCP server/discover 2026-07-28 desteğini bildirmedi.'
    }

    $tools = Invoke-McpRequest -Method 'tools/list' -Params @{ _meta = $meta } -Id 'tools-1'
    $toolNames = @($tools.result.tools | ForEach-Object { $_.name })
    if ($toolNames -notcontains 'get_system_info') {
        throw 'MCP tools/list get_system_info aracını döndürmedi.'
    }
    if ($toolNames -notcontains 'read_registry_value') {
        throw 'MCP tools/list read_registry_value aracını döndürmedi.'
    }

    $call = Invoke-McpRequest `
        -Method 'tools/call' `
        -Name 'get_system_info' `
        -Params @{ _meta = $meta; name = 'get_system_info'; arguments = @{} } `
        -Id 'call-1'

    $textBlock = @($call.result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1)
    if ($textBlock.Count -eq 0) {
        throw 'MCP tools/call metin sonucu döndürmedi.'
    }

    $toolResult = $textBlock[0].text | ConvertFrom-Json
    if (-not $toolResult.ok) {
        throw 'get_system_info sonucu ok=false döndürdü.'
    }

    Write-Host 'Talvora MCP smoke test: GREEN'
    Write-Host "  health:          GREEN"
    Write-Host "  server/discover: GREEN"
    Write-Host "  tools/list:      GREEN ($($toolNames.Count) araç, Registry dahil)"
    Write-Host "  tools/call:      GREEN (get_system_info)"
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        $process.WaitForExit(5000) | Out-Null
    }
}
