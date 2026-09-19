$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Join-Path $env:LOCALAPPDATA 'Talvora\PlaywrightMCP'
$runtime = Join-Path $root 'runtime'
$logs = Join-Path $root 'logs'
$output = Join-Path $root 'output'
$profile = Join-Path $root 'profile'
$state = Join-Path $root 'state'
$processStatePath = Join-Path $state 'process-tree.json'
$serverStatePath = Join-Path $state 'server-start.json'
$browserSmokePath = Join-Path $state 'browser-smoke.json'
$bootstrapLog = Join-Path $logs 'bootstrap.log'
$serverLog = Join-Path $logs 'server.log'

New-Item -ItemType Directory -Force -Path $runtime,$logs,$output,$profile,$state | Out-Null

function Rotate-Log([string]$Path,[long]$MaxBytes) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $item = Get-Item -LiteralPath $Path
    if ($item.Length -lt $MaxBytes) { return }

    $backup = "$Path.1"
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    Move-Item -LiteralPath $Path -Destination $backup -Force
}

function Resolve-Executable([string[]]$Candidates,[string]$CommandName) {
    foreach ($candidate in $Candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $command = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace([string]$command.Source)) {
        return [string]$command.Source
    }

    return $null
}

function Stop-OwnedPlaywrightProcesses {
    $markers = @(
        (Join-Path $runtime 'supervisor.mjs'),
        (Join-Path $runtime 'node_modules\@playwright\mcp\cli.js'),
        $profile
    )

    $owned = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            if ($_.ProcessId -eq $PID -or
                [string]::IsNullOrWhiteSpace([string]$_.CommandLine)) {
                return $false
            }

            $name = [string]$_.Name
            if ($name -notin @('node.exe','chrome.exe','msedge.exe','firefox.exe')) {
                return $false
            }

            foreach ($marker in $markers) {
                if (([string]$_.CommandLine).IndexOf(
                        [string]$marker,
                        [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    return $true
                }
            }

            return $false
        }

    foreach ($process in @($owned)) {
        Stop-Process -Id ([int]$process.ProcessId) -Force -ErrorAction SilentlyContinue
    }
}

Rotate-Log $bootstrapLog 2097152

try {
    Stop-OwnedPlaywrightProcesses
}
catch {
    "[$([DateTimeOffset]::UtcNow.ToString('O'))] Owned process cleanup warning: $($_.Exception.Message)" |
        Add-Content -LiteralPath $bootstrapLog
}

Remove-Item -LiteralPath $processStatePath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $browserSmokePath -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 350

$packageJson = @'
{
  "name": "talvora-playwright-mcp-runtime",
  "private": true,
  "type": "module",
  "dependencies": {
    "@playwright/mcp": "latest"
  }
}
'@
Set-Content -LiteralPath (Join-Path $runtime 'package.json') -Value $packageJson -Encoding utf8
Remove-Item -LiteralPath (Join-Path $runtime 'package-lock.json') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $runtime 'node_modules\.package-lock.json') -Force -ErrorAction SilentlyContinue

$node = Resolve-Executable @(
    (Join-Path $env:ProgramFiles 'nodejs\node.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'nodejs\node.exe')
) 'node.exe'
$npm = Resolve-Executable @(
    (Join-Path $env:ProgramFiles 'nodejs\npm.cmd'),
    (Join-Path ${env:ProgramFiles(x86)} 'nodejs\npm.cmd')
) 'npm.cmd'

if ([string]::IsNullOrWhiteSpace($node) -or [string]::IsNullOrWhiteSpace($npm)) {
    throw 'Node.js/npm is required for Playwright MCP.'
}

$cli = Join-Path $runtime 'node_modules\@playwright\mcp\cli.js'
$supervisor = Join-Path $runtime 'supervisor.mjs'
if (-not (Test-Path -LiteralPath $supervisor -PathType Leaf)) {
    throw "Playwright MCP supervisor is missing: $supervisor"
}

Push-Location $runtime
try {
    $dependencyDegraded = $false
    $packageUpdateStatus = 'latest-resolved'

    "[$([DateTimeOffset]::UtcNow.ToString('O'))] Resolving @playwright/mcp@latest." |
        Add-Content -LiteralPath $bootstrapLog

    & $npm install --package-lock=false --omit=dev --no-fund --no-audit *>> $bootstrapLog
    $npmExit = $LASTEXITCODE

    Remove-Item -LiteralPath (Join-Path $runtime 'package-lock.json') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $runtime 'node_modules\.package-lock.json') -Force -ErrorAction SilentlyContinue

    if ($npmExit -ne 0) {
        if (Test-Path -LiteralPath $cli -PathType Leaf) {
            $dependencyDegraded = $true
            $packageUpdateStatus = 'latest-resolution-failed-using-installed'
            "[$([DateTimeOffset]::UtcNow.ToString('O'))] Latest package resolution failed (Exit=$npmExit); using existing verified CLI and retrying latest on next restart." |
                Add-Content -LiteralPath $bootstrapLog
        }
        else {
            "[$([DateTimeOffset]::UtcNow.ToString('O'))] Latest package resolution failed and no installed CLI is available. Exit=$npmExit" |
                Add-Content -LiteralPath $bootstrapLog
            exit $npmExit
        }
    }

    if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
        throw "Playwright MCP CLI is missing after dependency resolution: $cli"
    }

    $packageFile = Join-Path $runtime 'node_modules\@playwright\mcp\package.json'
    if (-not (Test-Path -LiteralPath $packageFile -PathType Leaf)) {
        throw "Playwright MCP package manifest is missing: $packageFile"
    }

    $packageVersion = (Get-Content -LiteralPath $packageFile -Raw | ConvertFrom-Json).version
    $nodeVersion = (& $node --version 2>$null | Select-Object -First 1).Trim()
    $npmVersion = (& $npm --version 2>$null | Select-Object -First 1).Trim()

    $generation = [Guid]::NewGuid().ToString('N')
    $startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $stateTemp = "$serverStatePath.tmp"

    [pscustomobject]@{
        generation = $generation
        launcherPid = $PID
        startedAtUtc = $startedAtUtc
        packageVersion = $packageVersion
        packageUpdateStatus = $packageUpdateStatus
        dependencyDegraded = $dependencyDegraded
        nodeVersion = $nodeVersion
        npmVersion = $npmVersion
        browser = 'chrome'
        profileMode = 'dedicated-persistent-headless'
        profilePath = $profile
        endpoint = 'http://127.0.0.1:8932/mcp'
        backendEndpoint = 'http://127.0.0.1:8931/mcp'
    } | ConvertTo-Json -Compress | Set-Content -LiteralPath $stateTemp -Encoding utf8

    Move-Item -LiteralPath $stateTemp -Destination $serverStatePath -Force

    "[$startedAtUtc] Starting Playwright MCP generation=$generation version=$packageVersion node=$nodeVersion npm=$npmVersion dependencyStatus=$packageUpdateStatus" |
        Add-Content -LiteralPath $serverLog

    $env:TALVORA_PARENT_PID = [string]$PID
    $env:PLAYWRIGHT_MCP_CLI = $cli
    $env:PLAYWRIGHT_MCP_PROFILE = $profile
    $env:PLAYWRIGHT_MCP_OUTPUT = $output
    $env:PLAYWRIGHT_MCP_BACKEND_PORT = '8931'
    $env:PLAYWRIGHT_MCP_PROXY_PORT = '8932'
    $env:PLAYWRIGHT_MCP_PROCESS_STATE = $processStatePath
    $env:PLAYWRIGHT_MCP_SERVER_LOG = $serverLog
    $env:PLAYWRIGHT_MCP_SERVER_LOG_MAX_BYTES = '5242880'
    $env:PLAYWRIGHT_MCP_BACKEND_READY_TIMEOUT_MS = '30000'
    $env:PLAYWRIGHT_MCP_PING_TIMEOUT_MS = '0'

    & $node $supervisor
    $exitCode = $LASTEXITCODE

    "[$([DateTimeOffset]::UtcNow.ToString('O'))] Playwright MCP supervisor exited. Exit=$exitCode" |
        Add-Content -LiteralPath $serverLog
    exit $exitCode
}
finally {
    Pop-Location
}
