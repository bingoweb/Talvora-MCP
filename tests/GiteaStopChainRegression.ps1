$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$trayProject = Join-Path $repoRoot 'src\Talvora.Tray\Talvora.Tray.csproj'
$trayOutput = Join-Path $repoRoot 'src\Talvora.Tray\bin\Release\net10.0-windows\win-x64'
$sourcePath = Join-Path $repoRoot 'src\Talvora.Tray\GiteaTrayClient.cs'

function Assert-Contract {
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

& dotnet build $trayProject -c Release -p:UseSharedCompilation=false --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Talvora.Tray Release build failed with exit code $LASTEXITCODE."
}

$source = Get-Content -LiteralPath $sourcePath -Raw
$stopStart = $source.IndexOf(
    'public static async Task<GiteaStatus> StopAsync(',
    [StringComparison]::Ordinal)
$restartStart = $source.IndexOf(
    'public static async Task<GiteaStatus> RestartAsync(',
    $stopStart,
    [StringComparison]::Ordinal)
Assert-Contract (
    $stopStart -ge 0 -and
    $restartStart -gt $stopStart
) 'Gitea StopAsync source range was not found.'
$stopBody = $source.Substring(
    $stopStart,
    $restartStart - $stopStart)

Assert-Contract (
    $stopBody.Contains('ProbeStoppedChainAsync(')
) 'Gitea stop must use the dedicated full-chain terminal probe.'
Assert-Contract (
    -not $stopBody.Contains('GetStatusAsync(')
) 'Gitea stop must not reuse normal status early-return semantics.'

$probeStart = $source.IndexOf(
    'private static async Task<(bool Success, string Detail)> ProbeStoppedChainAsync(',
    [StringComparison]::Ordinal)
$runScriptStart = $source.IndexOf(
    'private static async Task RunPrivilegedScriptAsync(',
    $probeStart,
    [StringComparison]::Ordinal)
Assert-Contract (
    $probeStart -ge 0 -and
    $runScriptStart -gt $probeStart
) 'Gitea full-chain stop probe source range was not found.'
$probeBody = $source.Substring(
    $probeStart,
    $runScriptStart - $probeStart)

foreach ($required in @(
    'BackendHealthUrl',
    'ProxyHealthUrl',
    'McpHealthUrl',
    'ProbeTunnelAsync('
)) {
    Assert-Contract (
        $probeBody.Contains($required)
    ) "Gitea full-chain stop probe is missing '$required'."
}

$assemblyPath = Join-Path $trayOutput 'Talvora.Tray.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType(
    'Talvora.Tray.GiteaTrayClient',
    $true)
$method = $type.GetMethod(
    'BuildLifecycleScript',
    [Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $method) {
    throw 'Gitea BuildLifecycleScript method was not found.'
}

$stopScript = [string]$method.Invoke(
    $null,
    @('stop'))

$tokens = $null
$parseErrors = $null
[void][Management.Automation.Language.Parser]::ParseInput(
    $stopScript,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw "Embedded Gitea stop script parse failed: $($parseErrors[0].Message)"
}

foreach ($required in @(
    'Add-OwnedTaskProcesses',
    '$process.ExecutablePath',
    '$action.Execute',
    '$action.Arguments',
    '$_.ParentProcessId',
    'Stop-ScheduledTask',
    'Stop-Process -Id $processId -Force',
    '$ownedAlive.Count -eq 0',
    'OwnedProcesses=',
    "Stop-ServiceIfRunning -Name 'caddy'",
    "Stop-ServiceIfRunning -Name 'gitea'"
)) {
    Assert-Contract (
        $stopScript.Contains($required)
    ) "Embedded Gitea stop script is missing '$required'."
}

$captureIndex = $stopScript.IndexOf(
    'Add-OwnedTaskProcesses',
    [StringComparison]::Ordinal)
$taskStopIndex = $stopScript.IndexOf(
    'Stop-ScheduledTask',
    $captureIndex,
    [StringComparison]::Ordinal)
$processStopIndex = $stopScript.IndexOf(
    'Stop-Process -Id $processId -Force',
    $taskStopIndex,
    [StringComparison]::Ordinal)
$terminalIndex = $stopScript.IndexOf(
    '$ownedAlive.Count -eq 0',
    $processStopIndex,
    [StringComparison]::Ordinal)

Assert-Contract (
    $captureIndex -ge 0 -and
    $taskStopIndex -gt $captureIndex -and
    $processStopIndex -gt $taskStopIndex -and
    $terminalIndex -gt $processStopIndex
) 'Gitea task stop ordering must capture owned roots/children before task stop, terminate tracked processes, and verify no owned process remains.'

Write-Output 'GITEA_STOP_CHAIN_TERMINAL_GREEN'
