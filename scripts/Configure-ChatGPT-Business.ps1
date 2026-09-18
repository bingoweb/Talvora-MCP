[CmdletBinding()]
param(
    [string] $TunnelId,
    [string] $Alias = 'talvora-business',
    [string] $TunnelClientVersion = 'v0.0.14',
    [string] $InstallRoot = $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Talvora\TunnelClient' } else { Join-Path $HOME '.talvora\TunnelClient' }),
    [switch] $Reconnect,
    [switch] $InstallOnly,
    [switch] $SelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$McpUrl = 'http://127.0.0.1:7676/mcp'
$HealthUrl = 'http://127.0.0.1:7676/healthz'
$ConfigPath = Join-Path $InstallRoot 'business.json'
$CredentialPath = Join-Path $InstallRoot 'runtime-key.dpapi'
$StateRoot = Join-Path $InstallRoot 'state'
$BinRoot = Join-Path $InstallRoot 'bin'
$ReleaseMetadataPath = Join-Path $InstallRoot 'release.json'

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool] $Condition,
        [Parameter(Mandatory = $true)][string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Test-TalvoraWindows {
    return [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
}

function Test-TalvoraTunnelId {
    param([Parameter(Mandatory = $true)][string] $Value)

    return $Value -cmatch '^tunnel_[0-9a-f]{32}$'
}

function Get-TunnelClientArchitecture {
    $architecture = $env:PROCESSOR_ARCHITECTURE
    if (-not [string]::IsNullOrWhiteSpace($env:PROCESSOR_ARCHITEW6432)) {
        $architecture = $env:PROCESSOR_ARCHITEW6432
    }

    $normalizedArchitecture = ([string] $architecture).ToUpperInvariant()
    switch ($normalizedArchitecture) {
        'AMD64' { return 'amd64' }
        'ARM64' { return 'arm64' }
        default { throw "Unsupported Windows architecture for OpenAI tunnel-client: $architecture" }
    }
}

function Get-TunnelClientReleaseInfo {
    param(
        [Parameter(Mandatory = $true)][string] $Version,
        [Parameter(Mandatory = $true)][string] $Architecture
    )

    if ($Version -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$') {
        throw "Invalid pinned tunnel-client version: $Version"
    }

    if ($Architecture -notin @('amd64', 'arm64')) {
        throw "Unsupported tunnel-client release architecture: $Architecture"
    }

    $archiveName = "tunnel-client-$Version-windows-$Architecture.zip"
    $releaseBase = "https://github.com/openai/tunnel-client/releases/download/$Version"

    return [pscustomobject]@{
        Version = $Version
        ArchiveName = $archiveName
        ArchiveUrl = "$releaseBase/$archiveName"
        ChecksumsUrl = "$releaseBase/SHA256SUMS.txt"
    }
}

function Get-ExpectedSha256 {
    param(
        [Parameter(Mandatory = $true)][string] $ChecksumsPath,
        [Parameter(Mandatory = $true)][string] $ArchiveName
    )

    $pattern = '^(?<hash>[0-9A-Fa-f]{64})\s+\*?(?<name>.+)$'
    foreach ($line in Get-Content -LiteralPath $ChecksumsPath) {
        if ($line -notmatch $pattern) {
            continue
        }

        $candidateName = [IO.Path]::GetFileName($Matches['name'].Trim())
        if ($candidateName -eq $ArchiveName) {
            return $Matches['hash'].ToLowerInvariant()
        }
    }

    throw "SHA256SUMS.txt does not contain $ArchiveName."
}

function Get-ConnectArguments {
    param(
        [Parameter(Mandatory = $true)][string] $RuntimeAlias,
        [Parameter(Mandatory = $true)][string] $RuntimeTunnelId,
        [Parameter(Mandatory = $true)][string] $ServerUrl
    )

    return @(
        'runtimes',
        'connect',
        '--alias',
        $RuntimeAlias,
        '--tunnel-id',
        $RuntimeTunnelId,
        '--runtime-api-key',
        'env:CONTROL_PLANE_API_KEY',
        '--mcp-server-url',
        $ServerUrl,
        '--json'
    )
}

function Invoke-TunnelClient {
    param(
        [Parameter(Mandatory = $true)][string] $ClientPath,
        [Parameter(Mandatory = $true)][string[]] $Arguments
    )

    $output = @(& $ClientPath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { [string] $_ }) -join [Environment]::NewLine

    if ($exitCode -ne 0) {
        throw "tunnel-client failed with exit code $exitCode.$([Environment]::NewLine)$text"
    }

    return $text
}

function ConvertFrom-TunnelClientJson {
    param([Parameter(Mandatory = $true)][string] $Text)

    try {
        return $Text | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        $firstBrace = $Text.IndexOf('{')
        $lastBrace = $Text.LastIndexOf('}')
        if ($firstBrace -lt 0 -or $lastBrace -le $firstBrace) {
            throw
        }

        $json = $Text.Substring($firstBrace, $lastBrace - $firstBrace + 1)
        return $json | ConvertFrom-Json -ErrorAction Stop
    }
}

function Convert-SecureStringToPlainText {
    param([Parameter(Mandatory = $true)][Security.SecureString] $SecureValue)

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Save-RuntimeCredential {
    param(
        [Parameter(Mandatory = $true)][Security.SecureString] $SecureValue,
        [Parameter(Mandatory = $true)][string] $Path
    )

    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $encrypted = ConvertFrom-SecureString -SecureString $SecureValue
    [IO.File]::WriteAllText($Path, $encrypted, [Text.UTF8Encoding]::new($false))
}

function Load-RuntimeCredential {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Stored tunnel runtime credential was not found: $Path"
    }

    $encrypted = [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false)).Trim()
    if ([string]::IsNullOrWhiteSpace($encrypted)) {
        throw "Stored tunnel runtime credential is empty: $Path"
    }

    return ConvertTo-SecureString -String $encrypted
}

function Install-OpenAITunnelClient {
    param([Parameter(Mandatory = $true)][string] $DestinationRoot)

    if (-not (Test-TalvoraWindows)) {
        throw 'ChatGPT Business tunnel bootstrap is supported only on Windows.'
    }

    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072

    $architecture = Get-TunnelClientArchitecture
    $release = Get-TunnelClientReleaseInfo -Version $TunnelClientVersion -Architecture $architecture
    $tempRoot = Join-Path $env:TEMP ('Talvora-tunnel-client-' + [Guid]::NewGuid().ToString('N'))

    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $archivePath = Join-Path $tempRoot $release.ArchiveName
        $checksumsPath = Join-Path $tempRoot 'SHA256SUMS.txt'

        Invoke-WebRequest -UseBasicParsing -Uri $release.ArchiveUrl -OutFile $archivePath -Headers @{ 'User-Agent' = 'Talvora-Business-Tunnel-Bootstrap' }
        Invoke-WebRequest -UseBasicParsing -Uri $release.ChecksumsUrl -OutFile $checksumsPath -Headers @{ 'User-Agent' = 'Talvora-Business-Tunnel-Bootstrap' }

        $expectedHash = Get-ExpectedSha256 -ChecksumsPath $checksumsPath -ArchiveName $release.ArchiveName
        $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "OpenAI tunnel-client SHA256 mismatch. Expected=$expectedHash Actual=$actualHash"
        }

        $extractRoot = Join-Path $tempRoot 'extract'
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force

        $sourceExe = Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Filter 'tunnel-client.exe' | Select-Object -First 1
        if ($null -eq $sourceExe) {
            throw 'OpenAI tunnel-client archive did not contain tunnel-client.exe.'
        }

        $sourceBinRoot = Split-Path -Parent $sourceExe.FullName
        if (Test-Path -LiteralPath $DestinationRoot) {
            Remove-Item -LiteralPath $DestinationRoot -Recurse -Force
        }

        New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceBinRoot '*') -Destination $DestinationRoot -Recurse -Force

        $installedExe = Join-Path $DestinationRoot 'tunnel-client.exe'
        if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
            throw "Installed tunnel-client is missing: $installedExe"
        }

        $versionOutput = @(& $installedExe --version 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "Installed tunnel-client --version failed: $($versionOutput -join ' ')"
        }

        [pscustomobject]@{
            Product = 'OpenAI Secure MCP Tunnel client'
            ReleaseTag = $TunnelClientVersion
            VersionOutput = ($versionOutput | ForEach-Object { [string] $_ }) -join ' '
            Asset = $release.ArchiveName
            Sha256 = $actualHash
            InstalledAtUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json | Set-Content -LiteralPath $ReleaseMetadataPath -Encoding UTF8

        return $installedExe
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-OrInstallTunnelClient {
    $existing = Join-Path $BinRoot 'tunnel-client.exe'
    if ((Test-Path -LiteralPath $existing -PathType Leaf) -and (Test-Path -LiteralPath $ReleaseMetadataPath -PathType Leaf)) {
        try {
            $metadata = Get-Content -LiteralPath $ReleaseMetadataPath -Raw | ConvertFrom-Json
            if ([string] $metadata.ReleaseTag -eq $TunnelClientVersion) {
                $versionOutput = @(& $existing --version 2>&1)
                if ($LASTEXITCODE -eq 0) {
                    return $existing
                }
            }
        }
        catch {
            Write-Verbose "Existing tunnel-client metadata is not reusable: $($_.Exception.Message)"
        }
    }

    return Install-OpenAITunnelClient -DestinationRoot $BinRoot
}

function Get-TalvoraHealth {
    try {
        $health = Invoke-RestMethod -Uri $HealthUrl -TimeoutSec 5 -Proxy $null
    }
    catch {
        throw "Talvora is not reachable at $HealthUrl. Install/start Talvora first. $($_.Exception.Message)"
    }

    if ($health.product -ne 'Talvora') {
        throw "Unexpected service answered at $HealthUrl."
    }

    return $health
}

function Read-BusinessConfig {
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
        return $null
    }

    return Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
}

function Write-BusinessConfig {
    param(
        [Parameter(Mandatory = $true)][string] $RuntimeAlias,
        [Parameter(Mandatory = $true)][string] $RuntimeTunnelId,
        [Parameter(Mandatory = $true)][string] $ClientPath
    )

    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null

    [pscustomobject]@{
        Alias = $RuntimeAlias
        TunnelId = $RuntimeTunnelId
        McpUrl = $McpUrl
        TunnelClient = $ClientPath
        TunnelClientVersion = $TunnelClientVersion
        StateRoot = $StateRoot
        UpdatedAtUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $ConfigPath -Encoding UTF8
}

function Invoke-SelfTest {
    Assert-True -Condition (Test-TalvoraWindows) -Message 'Windows platform detection failed.'

    $architecture = Get-TunnelClientArchitecture
    Assert-True -Condition ($architecture -in @('amd64', 'arm64')) -Message 'Windows architecture mapping failed.'

    Assert-True -Condition ($TunnelClientVersion -match '^v[0-9]+\.[0-9]+\.[0-9]+$') -Message 'Pinned tunnel-client version is invalid.'

    Assert-True -Condition (Test-TalvoraTunnelId -Value 'tunnel_0123456789abcdef0123456789abcdef') -Message 'Valid tunnel ID was rejected.'
    Assert-True -Condition (-not (Test-TalvoraTunnelId -Value 'not-a-tunnel')) -Message 'Invalid tunnel ID was accepted.'
    Assert-True -Condition (-not (Test-TalvoraTunnelId -Value 'tunnel_0123456789ABCDEF0123456789ABCDEF')) -Message 'Uppercase tunnel ID was accepted.'
    Assert-True -Condition (-not (Test-TalvoraTunnelId -Value 'tunnel_0123456789abcdef')) -Message 'Short tunnel ID was accepted.'

    $release = Get-TunnelClientReleaseInfo -Version 'v9.8.7' -Architecture 'amd64'
    Assert-True -Condition ($release.ArchiveName -eq 'tunnel-client-v9.8.7-windows-amd64.zip') -Message 'Release asset selection failed.'

    $temp = Join-Path ([IO.Path]::GetTempPath()) ('Talvora-business-selftest-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    try {
        $payload = Join-Path $temp 'payload.zip'
        [IO.File]::WriteAllText($payload, 'talvora-business-selftest', [Text.UTF8Encoding]::new($false))
        $hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()

        $sums = Join-Path $temp 'SHA256SUMS.txt'
        [IO.File]::WriteAllText($sums, "$hash  payload.zip", [Text.UTF8Encoding]::new($false))

        $parsed = Get-ExpectedSha256 -ChecksumsPath $sums -ArchiveName 'payload.zip'
        Assert-True -Condition ($parsed -eq $hash) -Message 'Checksum parser failed.'
    }
    finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }

    $connectArgs = Get-ConnectArguments -RuntimeAlias 'talvora-business' -RuntimeTunnelId 'tunnel_0123456789abcdef0123456789abcdef' -ServerUrl $McpUrl
    Assert-True -Condition ($connectArgs -contains 'env:CONTROL_PLANE_API_KEY') -Message 'Runtime key must be passed by environment reference.'
    Assert-True -Condition (-not (($connectArgs -join ' ') -match 'sk-[A-Za-z0-9]')) -Message 'Connect arguments unexpectedly contain a literal API key.'
    Assert-True -Condition (($connectArgs -join ' ') -match [regex]::Escape($McpUrl)) -Message 'Connect arguments do not target Talvora loopback MCP.'

    Write-Output 'TALVORA BUSINESS BOOTSTRAP SELFTEST GREEN'
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

if (-not (Test-TalvoraWindows)) {
    throw 'ChatGPT Business tunnel bootstrap is supported only on Windows.'
}

$clientPath = Get-OrInstallTunnelClient
Write-Host "OpenAI tunnel-client ready: $clientPath" -ForegroundColor Green

if ($InstallOnly) {
    Write-Output 'TALVORA BUSINESS TUNNEL CLIENT INSTALLED'
    return
}

$health = Get-TalvoraHealth
Write-Host "Talvora local MCP healthy. SourceCommit=$($health.sourceCommit)" -ForegroundColor Green

$config = Read-BusinessConfig
if ($Reconnect) {
    if ($null -eq $config) {
        throw "Reconnect requested but Business config is missing: $ConfigPath"
    }

    if (-not $PSBoundParameters.ContainsKey('TunnelId')) {
        $TunnelId = [string] $config.TunnelId
    }

    if (-not $PSBoundParameters.ContainsKey('Alias')) {
        $Alias = [string] $config.Alias
    }
}

if ([string]::IsNullOrWhiteSpace($TunnelId)) {
    Write-Host ''
    Write-Host 'OpenAI Secure MCP Tunnel requires an existing tunnel associated with this ChatGPT Business workspace.' -ForegroundColor Yellow
    Write-Host 'Tunnels: https://platform.openai.com/settings/organization/tunnels'
    Write-Host 'Runtime keys: https://platform.openai.com/settings/organization/api-keys'
    Write-Host 'The runtime principal needs Tunnels Read + Use. The tunnel must include the target ChatGPT workspace.'
    $TunnelId = Read-Host 'Paste tunnel_id'
}

if (-not (Test-TalvoraTunnelId -Value $TunnelId)) {
    throw "Invalid tunnel_id: $TunnelId. Expected tunnel_ followed by 32 lowercase hexadecimal characters."
}

$secureCredential = $null
if ($Reconnect -and (Test-Path -LiteralPath $CredentialPath -PathType Leaf)) {
    $secureCredential = Load-RuntimeCredential -Path $CredentialPath
}
else {
    Write-Host ''
    Write-Host 'Enter the restricted runtime API key for this tunnel.' -ForegroundColor Yellow
    Write-Host 'Use a Runtime API key with Tunnels Read + Use; do not use an admin key.'
    $secureCredential = Read-Host 'Runtime API key' -AsSecureString
    Save-RuntimeCredential -SecureValue $secureCredential -Path $CredentialPath
}

Write-BusinessConfig -RuntimeAlias $Alias -RuntimeTunnelId $TunnelId -ClientPath $clientPath
New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null

$plainCredential = Convert-SecureStringToPlainText -SecureValue $secureCredential
if ([string]::IsNullOrWhiteSpace($plainCredential)) {
    throw 'Runtime API key cannot be empty.'
}

$previousApiKey = [Environment]::GetEnvironmentVariable('CONTROL_PLANE_API_KEY', 'Process')
$previousStateRoot = [Environment]::GetEnvironmentVariable('TUNNEL_CLIENT_STATE_DIR', 'Process')

try {
    $env:CONTROL_PLANE_API_KEY = $plainCredential
    $env:TUNNEL_CLIENT_STATE_DIR = $StateRoot

    $connectArgs = Get-ConnectArguments -RuntimeAlias $Alias -RuntimeTunnelId $TunnelId -ServerUrl $McpUrl
    $connectOutput = Invoke-TunnelClient -ClientPath $clientPath -Arguments $connectArgs
    if (-not [string]::IsNullOrWhiteSpace($connectOutput)) {
        Write-Verbose $connectOutput
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $status = $null
    $statusText = $null

    do {
        try {
            $statusText = Invoke-TunnelClient -ClientPath $clientPath -Arguments @('runtimes', 'status', $Alias, '--json')
            $status = ConvertFrom-TunnelClientJson -Text $statusText

            if ($status.process_running -eq $true -and $status.healthy -eq $true -and $status.ready -eq $true) {
                break
            }
        }
        catch {
            $status = $null
        }

        Start-Sleep -Seconds 2
    }
    while ([DateTime]::UtcNow -lt $deadline)

    if ($null -eq $status) {
        throw 'tunnel-client runtime status did not become available.'
    }

    if ($status.process_running -ne $true) {
        throw "tunnel-client runtime is not running.$([Environment]::NewLine)$statusText"
    }

    if ($status.healthy -ne $true) {
        throw "tunnel-client runtime is not healthy.$([Environment]::NewLine)$statusText"
    }

    if ($status.ready -ne $true) {
        throw "tunnel-client runtime did not become ready.$([Environment]::NewLine)$statusText"
    }

    Write-Host ''
    Write-Host 'TALVORA CHATGPT BUSINESS TUNNEL READY' -ForegroundColor Green
    Write-Host "Alias: $Alias"
    Write-Host "Tunnel: $TunnelId"
    Write-Host "MCP: $McpUrl"

    if ($status.PSObject.Properties.Name -contains 'ui_url') {
        Write-Host "Tunnel UI: $($status.ui_url)"
    }

    if ($status.PSObject.Properties.Name -contains 'health_url') {
        Write-Host "Tunnel health: $($status.health_url)"
    }

    if ($status.PSObject.Properties.Name -contains 'control_plane_poll_health') {
        $pollHealth = $status.control_plane_poll_health | ConvertTo-Json -Compress -Depth 8
        Write-Host "Control-plane poll health: $pollHealth"
    }

    Write-Host ''
    Write-Host 'ChatGPT Business final one-time UI step:'
    Write-Host '1. Enable Developer mode as a Business workspace Admin/Owner.'
    Write-Host '2. Open Workspace settings > Apps > Create.'
    Write-Host '3. Choose Connection: Tunnel and select/paste the tunnel ID above.'
    Write-Host '4. Review the 32 Talvora actions and publish the app to the workspace.'
    Write-Host 'Connector settings: https://chatgpt.com/#settings/Connectors'
}
finally {
    $plainCredential = $null

    if ($null -eq $previousApiKey) {
        Remove-Item Env:CONTROL_PLANE_API_KEY -ErrorAction SilentlyContinue
    }
    else {
        $env:CONTROL_PLANE_API_KEY = $previousApiKey
    }

    if ($null -eq $previousStateRoot) {
        Remove-Item Env:TUNNEL_CLIENT_STATE_DIR -ErrorAction SilentlyContinue
    }
    else {
        $env:TUNNEL_CLIENT_STATE_DIR = $previousStateRoot
    }
}
