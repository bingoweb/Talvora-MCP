param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^tunnel_[0-9a-f]{32}$')]
    [string] $TunnelId,

    [string] $Profile = 'talvora-local',
    [string] $McpServerUrl = 'http://127.0.0.1:7676/mcp',
    [string] $HealthListenAddress = '127.0.0.1:17676',
    [string] $InstallRoot = (Join-Path $env:LOCALAPPDATA 'Talvora\Tools\TunnelClient')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$releaseApi = 'https://api.github.com/repos/openai/tunnel-client/releases/latest'
$gatewayHealthUri = 'http://127.0.0.1:7676/healthz'
$tunnelReadyUri = "http://$HealthListenAddress/readyz"
$runtimeKeyReference = 'env:CONTROL_PLANE_API_KEY'

function Assert-TalvoraGateway {
    try {
        $health = Invoke-RestMethod -Uri $gatewayHealthUri -TimeoutSec 3
    }
    catch {
        throw "Talvora Gateway is not reachable at $gatewayHealthUri. Start Talvora before configuring Secure MCP Tunnel."
    }

    if ($health.product -ne 'Talvora') {
        throw "Unexpected service at $gatewayHealthUri. Expected product 'Talvora'."
    }
}

function Get-WindowsArchitectureToken {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    switch ($architecture) {
        'X64' { return 'amd64' }
        'Arm64' { return 'arm64' }
        default { throw "Unsupported Windows architecture for tunnel-client: $architecture" }
    }
}

function Get-LatestTunnelClientAsset([string] $architectureToken) {
    $release = Invoke-RestMethod -Uri $releaseApi -Headers @{ 'User-Agent' = 'Talvora' } -TimeoutSec 30
    if ($release.draft -or $release.prerelease) {
        throw "Latest tunnel-client release '$($release.tag_name)' is not a stable release."
    }

    $pattern = "^tunnel-client-v.+-windows-$architectureToken\.zip$"
    $assets = @($release.assets | Where-Object { $_.name -match $pattern })
    if ($assets.Count -ne 1) {
        throw "Expected exactly one Windows $architectureToken tunnel-client asset in release '$($release.tag_name)', found $($assets.Count)."
    }

    $asset = $assets[0]
    if ([string]::IsNullOrWhiteSpace([string] $asset.digest) -or -not ([string] $asset.digest).StartsWith('sha256:', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release asset '$($asset.name)' does not expose a SHA-256 digest."
    }

    return [pscustomobject]@{
        Tag = [string] $release.tag_name
        Name = [string] $asset.name
        Url = [string] $asset.browser_download_url
        Sha256 = ([string] $asset.digest).Substring('sha256:'.Length).ToLowerInvariant()
    }
}

function Install-TunnelClient($asset) {
    New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null

    $versionRoot = Join-Path $InstallRoot $asset.Tag
    $pathFile = Join-Path $InstallRoot 'current-path.txt'

    if (Test-Path -LiteralPath $versionRoot) {
        $existing = Get-ChildItem -LiteralPath $versionRoot -Filter 'tunnel-client.exe' -File -Recurse | Sort-Object { $_.FullName.Length } | Select-Object -First 1
        if ($null -ne $existing) {
            Set-Content -LiteralPath $pathFile -Value $existing.FullName -Encoding utf8NoBOM
            return $existing.FullName
        }
    }

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('talvora-tunnel-' + [Guid]::NewGuid().ToString('N'))
    $archive = Join-Path $tempRoot $asset.Name
    $extractRoot = Join-Path $tempRoot 'extract'

    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    try {
        Invoke-WebRequest -Uri $asset.Url -OutFile $archive -Headers @{ 'User-Agent' = 'Talvora' } -TimeoutSec 120
        $actualSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualSha256 -ne $asset.Sha256) {
            throw "SHA-256 mismatch for '$($asset.Name)'. Expected $($asset.Sha256), got $actualSha256."
        }

        Expand-Archive -LiteralPath $archive -DestinationPath $extractRoot -Force
        $binary = Get-ChildItem -LiteralPath $extractRoot -Filter 'tunnel-client.exe' -File -Recurse | Sort-Object { $_.FullName.Length } | Select-Object -First 1
        if ($null -eq $binary) {
            throw "Downloaded tunnel-client archive '$($asset.Name)' did not contain tunnel-client.exe."
        }

        if (Test-Path -LiteralPath $versionRoot) {
            Remove-Item -LiteralPath $versionRoot -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $versionRoot | Out-Null
        Copy-Item -LiteralPath (Join-Path $extractRoot '*') -Destination $versionRoot -Recurse -Force

        $installedBinary = Get-ChildItem -LiteralPath $versionRoot -Filter 'tunnel-client.exe' -File -Recurse | Sort-Object { $_.FullName.Length } | Select-Object -First 1
        if ($null -eq $installedBinary) {
            throw 'Tunnel-client copy completed without an executable.'
        }

        Set-Content -LiteralPath $pathFile -Value $installedBinary.FullName -Encoding utf8NoBOM
        return $installedBinary.FullName
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-TunnelClient([string] $binary, [string[]] $arguments) {
    & $binary @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "tunnel-client failed with exit code $LASTEXITCODE: $($arguments -join ' ')"
    }
}

function Test-TunnelReady {
    try {
        Invoke-RestMethod -Uri $tunnelReadyUri -TimeoutSec 2 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

if ([string]::IsNullOrWhiteSpace($env:CONTROL_PLANE_API_KEY)) {
    throw 'CONTROL_PLANE_API_KEY is not set. Create a Tunnel runtime API key in OpenAI Platform and set it only in the local environment before running this script.'
}

Assert-TalvoraGateway

$architectureToken = Get-WindowsArchitectureToken
$asset = Get-LatestTunnelClientAsset $architectureToken
$binary = Install-TunnelClient $asset

Write-Host "OpenAI tunnel-client $($asset.Tag): $binary" -ForegroundColor Cyan

$initArguments = @(
    'init',
    '--sample', 'sample_mcp_remote_no_auth',
    '--profile', $Profile,
    '--force',
    '--tunnel-id', $TunnelId,
    '--mcp-server-url', $McpServerUrl,
    '--control-plane-api-key-ref', $runtimeKeyReference,
    '--health-listen-addr', $HealthListenAddress
)
Invoke-TunnelClient $binary $initArguments
Invoke-TunnelClient $binary @('doctor', '--profile', $Profile, '--explain')

if (Test-TunnelReady) {
    Write-Host "Talvora Secure MCP Tunnel is already ready: $tunnelReadyUri" -ForegroundColor Green
    exit 0
}

$logRoot = Join-Path $env:LOCALAPPDATA 'Talvora\Logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$stdout = Join-Path $logRoot 'tunnel-client.stdout.log'
$stderr = Join-Path $logRoot 'tunnel-client.stderr.log'

$process = Start-Process -FilePath $binary -ArgumentList @('run', '--profile', $Profile) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr

$deadline = [DateTime]::UtcNow.AddSeconds(45)
do {
    if ($process.HasExited) {
        $errorTail = if (Test-Path -LiteralPath $stderr) { (Get-Content -LiteralPath $stderr -Tail 60) -join [Environment]::NewLine } else { '' }
        throw "tunnel-client exited before becoming ready (exit $($process.ExitCode)). $errorTail"
    }

    if (Test-TunnelReady) {
        Write-Host "Talvora Secure MCP Tunnel is ready." -ForegroundColor Green
        Write-Host "Tunnel: $TunnelId"
        Write-Host "MCP: $McpServerUrl"
        Write-Host "Ready: $tunnelReadyUri"
        Write-Host "Admin UI: http://$HealthListenAddress/ui"
        Write-Host "PID: $($process.Id)"
        Write-Host 'Next in ChatGPT Business: create a developer-mode custom app, choose Connection = Tunnel, select this tunnel, Scan Tools, then Create.'
        exit 0
    }

    Start-Sleep -Milliseconds 500
} while ([DateTime]::UtcNow -lt $deadline)

$errorTail = if (Test-Path -LiteralPath $stderr) { (Get-Content -LiteralPath $stderr -Tail 60) -join [Environment]::NewLine } else { '' }
throw "tunnel-client did not become ready at $tunnelReadyUri within 45 seconds. $errorTail"
