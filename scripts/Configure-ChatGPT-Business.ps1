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
$ReleaseApi = "https://api.github.com/repos/openai/tunnel-client/releases/tags/$TunnelClientVersion"
$ConfigPath = Join-Path $InstallRoot 'business.json'
$CredentialPath = Join-Path $InstallRoot 'runtime-key.dpapi'
$StateRoot = Join-Path $InstallRoot 'state'
$BinRoot = Join-Path $InstallRoot 'bin'

function Assert-True {
    param(
        [Parameter(Mandatory)][bool] $Condition,
        [Parameter(Mandatory)][string] $Message
    )
    if (-not $Condition) { throw $Message }
}

function Test-TalvoraTunnelId {
    param([Parameter(Mandatory)][string] $Value)
    return $Value -match '^tunnel_[0-9a-f]{32}$'
}

function Test-TalvoraWindows {
    return [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
}

function Get-TunnelClientArchitecture {
    $architecture = if (-not [string]::IsNullOrWhiteSpace($env:PROCESSOR_ARCHITEW6432)) {
        $env:PROCESSOR_ARCHITEW6432
    }
    else {
        $env:PROCESSOR_ARCHITECTURE
    }

    $normalizedArchitecture = ([string] $architecture).ToUpperInvariant()
    switch ($normalizedArchitecture) {
        'AMD64' { return 'amd64' }
        'ARM64' { return 'arm64' }
        default { throw "Unsupported Windows architecture for OpenAI tunnel-client: $architecture" }
    }
}

function Select-TunnelClientReleaseAssets {
    param(
        [Parameter(Mandatory)] $Release,
        [Parameter(Mandatory)][string] $Architecture
    )

    $tag = [string] $Release.tag_name
    if ([string]::IsNullOrWhiteSpace($tag)) { throw 'OpenAI tunnel-client release did not include tag_name.' }

    $archiveName = "tunnel-client-$tag-windows-$Architecture.zip"
    $archive = @($Release.assets | Where-Object { $_.name -eq $archiveName }) | Select-Object -First 1
    $checksums = @($Release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' }) | Select-Object -First 1
    if ($null -eq $archive) { throw "OpenAI tunnel-client release is missing $archiveName." }
    if ($null -eq $checksums) { throw 'OpenAI tunnel-client release is missing SHA256SUMS.txt.' }

    return [pscustomobject]@{
        Tag = $tag
        ArchiveName = $archiveName
        ArchiveUrl = [string] $archive.browser_download_url
        ChecksumsUrl = [string] $checksums.browser_download_url
    }
}

function Get-ExpectedSha256 {
    param(
        [Parameter(Mandatory)][string] $ChecksumsPath,
        [Parameter(Mandatory)][string] $ArchiveName
    )

    $line = Get-Content -LiteralPath $ChecksumsPath | Where-Object {
        $_ -match '^[0-9A-Fa-f]{64}\s+\*?.+$' -and
        [IO.Path]::GetFileName(($_ -replace '^[0-9A-Fa-f]{64}\s+\*?', '').Trim()) -eq $ArchiveName
    } | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($line)) { throw "SHA256SUMS.txt does not contain $ArchiveName." }
    if ($line -notmatch '^(?<hash>[0-9A-Fa-f]{64})\s+\*?(?<name>.+)$') { throw "Invalid checksum line for $ArchiveName." }
    return $Matches['hash'].ToLowerInvariant()
}

function Get-ConnectArguments {
    param(
        [Parameter(Mandatory)][string] $RuntimeAlias,
        [Parameter(Mandatory)][string] $RuntimeTunnelId,
        [Parameter(Mandatory)][string] $ServerUrl
    )

    return @(
        'runtimes', 'connect',
        '--alias', $RuntimeAlias,
        '--tunnel-id', $RuntimeTunnelId,
        '--runtime-api-key', 'env:CONTROL_PLANE_API_KEY',
        '--mcp-server-url', $ServerUrl,
        '--json'
    )
}

function Invoke-TunnelClient {
    param(
        [Parameter(Mandatory)][string] $ClientPath,
        [Parameter(Mandatory)][string[]] $Arguments
    )

    $output = @(& $ClientPath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { [string] $_ }) -join [Environment]::NewLine
    if ($exitCode -ne 0) {
        throw "tunnel-client failed with exit code $exitCode.$([Environment]::NewLine)$text"
    }
    return $text
}

function Convert-SecureStringToPlainText {
    param([Parameter(Mandatory)][Security.SecureString] $SecureValue)

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
        [Parameter(Mandatory)][Security.SecureString] $SecureValue,
        [Parameter(Mandatory)][string] $Path
    )

    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $encrypted = ConvertFrom-SecureString -SecureString $SecureValue
    [IO.File]::WriteAllText($Path, $encrypted, [Text.UTF8Encoding]::new($false))
}

function Load-RuntimeCredential {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Stored tunnel runtime credential was not found: $Path"
    }

    $encrypted = [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false)).Trim()
    if ([string]::IsNullOrWhiteSpace($encrypted)) { throw "Stored tunnel runtime credential is empty: $Path" }
    return ConvertTo-SecureString -String $encrypted
}

function Install-OpenAITunnelClient {
    param([Parameter(Mandatory)][string] $DestinationRoot)

    if (-not (Test-TalvoraWindows)) { throw 'ChatGPT Business tunnel bootstrap is supported only on Windows.' }

    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
    $release = Invoke-RestMethod -Uri $ReleaseApi -Headers @{ 'User-Agent' = 'Talvora-Business-Tunnel-Bootstrap' }
    $architecture = Get-TunnelClientArchitecture
    $selected = Select-TunnelClientReleaseAssets -Release $release -Architecture $architecture

    $tempRoot = Join-Path $env:TEMP ('Talvora-tunnel-client-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $archivePath = Join-Path $tempRoot $selected.ArchiveName
        $checksumsPath = Join-Path $tempRoot 'SHA256SUMS.txt'
        Invoke-WebRequest -UseBasicParsing -Uri $selected.ArchiveUrl -OutFile $archivePath -Headers @{ 'User-Agent' = 'Talvora-Business-Tunnel-Bootstrap' }
        Invoke-WebRequest -UseBasicParsing -Uri $selected.ChecksumsUrl -OutFile $checksumsPath -Headers @{ 'User-Agent' = 'Talvora-Business-Tunnel-Bootstrap' }

        $expectedHash = Get-ExpectedSha256 -ChecksumsPath $checksumsPath -ArchiveName $selected.ArchiveName
        $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "OpenAI tunnel-client SHA256 mismatch. Expected=$expectedHash Actual=$actualHash"
        }

        $extractRoot = Join-Path $tempRoot 'extract'
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
        $sourceExe = Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Filter 'tunnel-client.exe' | Select-Object -First 1
        if ($null -eq $sourceExe) { throw 'OpenAI tunnel-client archive did not contain tunnel-client.exe.' }

        $sourceBinRoot = Split-Path -Parent $sourceExe.FullName
        if (Test-Path -LiteralPath $DestinationRoot) { Remove-Item -LiteralPath $DestinationRoot -Recurse -Force }
        New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceBinRoot '*') -Destination $DestinationRoot -Recurse -Force

        $installedExe = Join-Path $DestinationRoot 'tunnel-client.exe'
        if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) { throw "Installed tunnel-client is missing: $installedExe" }

        $versionOutput = @(& $installedExe --version 2>&1)
        if ($LASTEXITCODE -ne 0) { throw "Installed tunnel-client --version failed: $($versionOutput -join ' ')" }

        New-Item -ItemType Directory -Path (Split-Path -Parent $DestinationRoot) -Force | Out-Null
        [pscustomobject]@{
            Product = 'OpenAI Secure MCP Tunnel client'
            Version = ($versionOutput | ForEach-Object { [string] $_ }) -join ' '
            ReleaseTag = $selected.Tag
            Asset = $selected.ArchiveName
            Sha256 = $actualHash
            InstalledAtUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $DestinationRoot) 'release.json') -Encoding UTF8

        return $installedExe
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-OrInstallTunnelClient {
    $existing = Join-Path $BinRoot 'tunnel-client.exe'
    if (Test-Path -LiteralPath $existing -PathType Leaf) {
        $versionOutput = @(& $existing --version 2>&1)
        if ($LASTEXITCODE -eq 0) { return $existing }
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

    if ($health.product -ne 'Talvora') { throw "Unexpected service answered at $HealthUrl." }
    return $health
}

function Read-BusinessConfig {
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
}

function Write-BusinessConfig {
    param(
        [Parameter(Mandatory)][string] $RuntimeAlias,
        [Parameter(Mandatory)][string] $RuntimeTunnelId,
        [Parameter(Mandatory)][string] $ClientPath
    )

    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    [pscustomobject]@{
        Alias = $RuntimeAlias
        TunnelId = $RuntimeTunnelId
        McpUrl = $McpUrl
        TunnelClient = $ClientPath
        StateRoot = $StateRoot
        UpdatedAtUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $ConfigPath -Encoding UTF8
}

function Invoke-SelfTest {
    Assert-True -Condition (Test-TalvoraWindows) -Message 'Windows platform detection failed.'
    $selfTestArchitecture = Get-TunnelClientArchitecture
    Assert-True -Condition ($selfTestArchitecture -in @('amd64', 'arm64')) -Message 'Windows architecture mapping failed.'
    Assert-True -Condition ($TunnelClientVersion -match '^v[0-9]+\.[0-9]+\.[0-9]+$') -Message 'pinned tunnel-client version is invalid.'
    Assert-True -Condition (Test-TalvoraTunnelId -Value 'tunnel_0123456789abcdef0123456789abcdef') -Message 'valid tunnel ID was rejected.'
    Assert-True -Condition (-not (Test-TalvoraTunnelId -Value 'not-a-tunnel')) -Message 'invalid tunnel ID was accepted.'
    Assert-True -Condition (-not (Test-TalvoraTunnelId -Value 'tunnel_0123456789ABCDEF0123456789ABCDEF')) -Message 'uppercase tunnel ID was accepted.'
    Assert-True -Condition (-not (Test-TalvoraTunnelId -Value 'tunnel_0123456789abcdef')) -Message 'short tunnel ID was accepted.'

    $fakeRelease = [pscustomobject]@{
        tag_name = 'v9.8.7'
        assets = @(
            [pscustomobject]@{ name = 'tunnel-client-v9.8.7-windows-amd64.zip'; browser_download_url = 'https://example.invalid/tunnel.zip' },
            [pscustomobject]@{ name = 'SHA256SUMS.txt'; browser_download_url = 'https://example.invalid/SHA256SUMS.txt' }
        )
    }
    $selected = Select-TunnelClientReleaseAssets -Release $fakeRelease -Architecture 'amd64'
    Assert-True -Condition ($selected.ArchiveName -eq 'tunnel-client-v9.8.7-windows-amd64.zip') -Message 'release asset selection failed.'

    $temp = Join-Path ([IO.Path]::GetTempPath()) ('Talvora-business-selftest-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    try {
        $payload = Join-Path $temp 'payload.zip'
        [IO.File]::WriteAllText($payload, 'talvora-business-selftest', [Text.UTF8Encoding]::new($false))
        $hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
        $sums = Join-Path $temp 'SHA256SUMS.txt'
        [IO.File]::WriteAllText($sums, "$hash  payload.zip", [Text.UTF8Encoding]::new($false))
        $parsed = Get-ExpectedSha256 -ChecksumsPath $sums -ArchiveName 'payload.zip'
        Assert-True -Condition ($parsed -eq $hash) -Message 'checksum parser failed.'
    }
    finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }

    $args = Get-ConnectArguments -RuntimeAlias 'talvora-business' -RuntimeTunnelId 'tunnel_0123456789abcdef0123456789abcdef' -ServerUrl $McpUrl
    Assert-True -Condition ($args -contains 'env:CONTROL_PLANE_API_KEY') -Message 'runtime key must be passed by environment reference.'
    Assert-True -Condition (-not (($args -join ' ') -match 'sk-[A-Za-z0-9]')) -Message 'connect arguments unexpectedly contain a literal API key.'
    Assert-True -Condition (($args -join ' ') -match [regex]::Escape($McpUrl)) -Message 'connect arguments do not target Talvora loopback MCP.'

    Write-Output 'TALVORA BUSINESS BOOTSTRAP SELFTEST GREEN'
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

if (-not (Test-TalvoraWindows)) { throw 'ChatGPT Business tunnel bootstrap is supported only on Windows.' }

$health = Get-TalvoraHealth
Write-Host "Talvora local MCP healthy. SourceCommit=$($health.sourceCommit)" -ForegroundColor Green

$clientPath = Get-OrInstallTunnelClient
Write-Host "OpenAI tunnel-client ready: $clientPath" -ForegroundColor Green

if ($InstallOnly) {
    Write-Output 'TALVORA BUSINESS TUNNEL CLIENT INSTALLED'
    return
}

$config = Read-BusinessConfig
if ($Reconnect) {
    if ($null -eq $config) { throw "Reconnect requested but Business config is missing: $ConfigPath" }
    if ([string]::IsNullOrWhiteSpace($TunnelId)) { $TunnelId = [string] $config.TunnelId }
    if ([string]::IsNullOrWhiteSpace($Alias)) { $Alias = [string] $config.Alias }
}

if ([string]::IsNullOrWhiteSpace($TunnelId)) {
    Write-Host ''
    Write-Host 'OpenAI Secure MCP Tunnel requires an existing tunnel associated with this ChatGPT Business workspace.' -ForegroundColor Yellow
    Write-Host 'Create/select it at: https://platform.openai.com/settings/organization/tunnels'
    Write-Host 'The runtime principal needs Tunnels Read + Use. The tunnel must include the target ChatGPT workspace.'
    $TunnelId = Read-Host 'Paste tunnel_id'
}

if (-not (Test-TalvoraTunnelId -Value $TunnelId)) {
    throw "Invalid tunnel_id: $TunnelId"
}

$secureCredential = $null
if ($Reconnect -and (Test-Path -LiteralPath $CredentialPath -PathType Leaf)) {
    $secureCredential = Load-RuntimeCredential -Path $CredentialPath
}
else {
    Write-Host ''
    Write-Host 'Enter the restricted runtime API key for this tunnel. It is used only by OpenAI tunnel-client transport.' -ForegroundColor Yellow
    Write-Host 'Create it with Tunnels Read + Use; do not use an admin key.'
    $secureCredential = Read-Host 'Runtime API key' -AsSecureString
    Save-RuntimeCredential -SecureValue $secureCredential -Path $CredentialPath
}

Write-BusinessConfig -RuntimeAlias $Alias -RuntimeTunnelId $TunnelId -ClientPath $clientPath
New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null

$plainCredential = Convert-SecureStringToPlainText -SecureValue $secureCredential
if ([string]::IsNullOrWhiteSpace($plainCredential)) { throw 'Runtime API key cannot be empty.' }

$previousApiKey = [Environment]::GetEnvironmentVariable('CONTROL_PLANE_API_KEY', 'Process')
$previousStateRoot = [Environment]::GetEnvironmentVariable('TUNNEL_CLIENT_STATE_DIR', 'Process')
try {
    $env:CONTROL_PLANE_API_KEY = $plainCredential
    $env:TUNNEL_CLIENT_STATE_DIR = $StateRoot

    $connectArgs = Get-ConnectArguments -RuntimeAlias $Alias -RuntimeTunnelId $TunnelId -ServerUrl $McpUrl
    $connectOutput = Invoke-TunnelClient -ClientPath $clientPath -Arguments $connectArgs
    if (-not [string]::IsNullOrWhiteSpace($connectOutput)) { Write-Verbose $connectOutput }

    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $status = $null
    $statusText = $null
    do {
        try {
            $statusText = Invoke-TunnelClient -ClientPath $clientPath -Arguments @('runtimes', 'status', $Alias, '--json')
            $status = $statusText | ConvertFrom-Json
            if ($status.process_running -eq $true -and $status.healthy -eq $true -and $status.ready -eq $true) { break }
        }
        catch {
            $status = $null
        }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($null -eq $status) { throw 'tunnel-client runtime status did not become available.' }
    if ($status.process_running -ne $true) { throw "tunnel-client runtime is not running.$([Environment]::NewLine)$statusText" }
    if ($status.healthy -ne $true) { throw "tunnel-client runtime is not healthy.$([Environment]::NewLine)$statusText" }
    if ($status.ready -ne $true) { throw "tunnel-client runtime did not become ready.$([Environment]::NewLine)$statusText" }

    Write-Host ''
    Write-Host 'TALVORA CHATGPT BUSINESS TUNNEL READY' -ForegroundColor Green
    Write-Host "Alias: $Alias"
    Write-Host "Tunnel: $TunnelId"
    Write-Host "MCP: $McpUrl"
    if ($status.PSObject.Properties.Name -contains 'ui_url') { Write-Host "Tunnel UI: $($status.ui_url)" }
    if ($status.PSObject.Properties.Name -contains 'health_url') { Write-Host "Tunnel health: $($status.health_url)" }
    if ($status.PSObject.Properties.Name -contains 'control_plane_poll_health') {
        Write-Host "Control-plane poll: $($status.control_plane_poll_health)"
    }
    Write-Host ''
    Write-Host 'ChatGPT Business final one-time UI step:'
    Write-Host '1. Enable Developer mode as a Business workspace Admin/Owner.'
    Write-Host '2. Open ChatGPT Apps/Connectors and create a custom MCP app.'
    Write-Host '3. Choose Connection: Tunnel and select/paste the tunnel above.'
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

}

function Test-TalvoraWindows {
    return [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
}

function Get-TunnelClientArchitecture {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    switch ($architecture) {
        'X64' { return 'amd64' }
        'Arm64' { return 'arm64' }
        default { throw "Unsupported Windows architecture for OpenAI tunnel-client: $architecture" }
    }
}

function Select-TunnelClientReleaseAssets {
    param(
        [Parameter(Mandatory)] $Release,
        [Parameter(Mandatory)][string] $Architecture
    )

    $tag = [string] $Release.tag_name
    if ([string]::IsNullOrWhiteSpace($tag)) { throw 'OpenAI tunnel-client release did not include tag_name.' }

    $archiveName = "tunnel-client-$tag-windows-$Architecture.zip"
    $archive = @($Release.assets | Where-Object { $_.name -eq $archiveName }) | Select-Object -First 1
    $checksums = @($Release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' }) | Select-Object -First 1
    if ($null -eq $archive) { throw "OpenAI tunnel-client release is missing $archiveName." }
    if ($null -eq $checksums) { throw 'OpenAI tunnel-client release is missing SHA256SUMS.txt.' }

    return [pscustomobject]@{
        Tag = $tag
        ArchiveName = $archiveName
        ArchiveUrl = [string] $archive.browser_download_url
        ChecksumsUrl = [string] $checksums.browser_download_url
    }
}

function Get-ExpectedSha256 {
    param(
        [Parameter(Mandatory)][string] $ChecksumsPath,
        [Parameter(Mandatory)][string] $ArchiveName
    )

    $line = Get-Content -LiteralPath $ChecksumsPath | Where-Object {
        $_ -match '^[0-9A-Fa-f]{64}\s+\*?.+$' -and
        [IO.Path]::GetFileName(($_ -replace '^[0-9A-Fa-f]{64}\s+\*?', '').Trim()) -eq $ArchiveName
    } | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($line)) { throw "SHA256SUMS.txt does not contain $ArchiveName." }
    if ($line -notmatch '^(?<hash>[0-9A-Fa-f]{64})\s+\*?(?<name>.+)$') { throw "Invalid checksum line for $ArchiveName." }
    return $Matches['hash'].ToLowerInvariant()
}

function Get-ConnectArguments {
    param(
        [Parameter(Mandatory)][string] $RuntimeAlias,
        [Parameter(Mandatory)][string] $RuntimeTunnelId,
        [Parameter(Mandatory)][string] $ServerUrl
    )

    return @(
        'runtimes', 'connect',
        '--alias', $RuntimeAlias,
        '--tunnel-id', $RuntimeTunnelId,
        '--runtime-api-key', 'env:CONTROL_PLANE_API_KEY',
        '--mcp-server-url', $ServerUrl,
        '--json'
    )
}

function Invoke-TunnelClient {
    param(
        [Parameter(Mandatory)][string] $ClientPath,
        [Parameter(Mandatory)][string[]] $Arguments
    )

    $output = @(& $ClientPath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { [string] $_ }) -join [Environment]::NewLine
    if ($exitCode -ne 0) {
        throw "tunnel-client failed with exit code $exitCode.$([Environment]::NewLine)$text"
    }
    return $text
}

function Convert-SecureStringToPlainText {
    param([Parameter(Mandatory)][Security.SecureString] $SecureValue)

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
        [Parameter(Mandatory)][Security.SecureString] $SecureValue,
        [Parameter(Mandatory)][string] $Path
    )

    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $encrypted = ConvertFrom-SecureString -SecureString $SecureValue
    [IO.File]::WriteAllText($Path, $encrypted, [Text.UTF8Encoding]::new($false))
}

function Load-RuntimeCredential {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Stored tunnel runtime credential was not found: $Path"
    }

    $encrypted = [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false)).Trim()
    if ([string]::IsNullOrWhiteSpace($encrypted)) { throw "Stored tunnel runtime credential is empty: $Path" }
    return ConvertTo-SecureString -String $encrypted
}

function Install-OpenAITunnelClient {
    param([Parameter(Mandatory)][string] $DestinationRoot)

    if (-not (Test-TalvoraWindows)) { throw 'ChatGPT Business tunnel bootstrap is supported only on Windows.' }

    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
    $release = Invoke-RestMethod -Uri $ReleaseApi -Headers @{ 'User-Agent' = 'Talvora-Business-Tunnel-Bootstrap' }
    $architecture = Get-TunnelClientArchitecture
    $selected = Select-TunnelClientReleaseAssets -Release $release -Architecture $architecture

    $tempRoot = Join-Path $env:TEMP ('Talvora-tunnel-client-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $archivePath = Join-Path $tempRoot $selected.ArchiveName
        $checksumsPath = Join-Path $tempRoot 'SHA256SUMS.txt'
        Invoke-WebRequest -Uri $selected.ArchiveUrl -OutFile $archivePath -Headers @{ 'User-Agent' = 'Talvora-Business-Tunnel-Bootstrap' }
        Invoke-WebRequest -Uri $selected.ChecksumsUrl -OutFile $checksumsPath -Headers @{ 'User-Agent' = 'Talvora-Business-Tunnel-Bootstrap' }

        $expectedHash = Get-ExpectedSha256 -ChecksumsPath $checksumsPath -ArchiveName $selected.ArchiveName
        $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "OpenAI tunnel-client SHA256 mismatch. Expected=$expectedHash Actual=$actualHash"
        }

        $extractRoot = Join-Path $tempRoot 'extract'
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
        $sourceExe = Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Filter 'tunnel-client.exe' | Select-Object -First 1
        if ($null -eq $sourceExe) { throw 'OpenAI tunnel-client archive did not contain tunnel-client.exe.' }

        $sourceBinRoot = Split-Path -Parent $sourceExe.FullName
        if (Test-Path -LiteralPath $DestinationRoot) { Remove-Item -LiteralPath $DestinationRoot -Recurse -Force }
        New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceBinRoot '*') -Destination $DestinationRoot -Recurse -Force

        $installedExe = Join-Path $DestinationRoot 'tunnel-client.exe'
        if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) { throw "Installed tunnel-client is missing: $installedExe" }

        $versionOutput = @(& $installedExe --version 2>&1)
        if ($LASTEXITCODE -ne 0) { throw "Installed tunnel-client --version failed: $($versionOutput -join ' ')" }

        New-Item -ItemType Directory -Path (Split-Path -Parent $DestinationRoot) -Force | Out-Null
        [pscustomobject]@{
            Product = 'OpenAI Secure MCP Tunnel client'
            Version = ($versionOutput | ForEach-Object { [string] $_ }) -join ' '
            ReleaseTag = $selected.Tag
            Asset = $selected.ArchiveName
            Sha256 = $actualHash
            InstalledAtUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $DestinationRoot) 'release.json') -Encoding UTF8

        return $installedExe
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-OrInstallTunnelClient {
    $existing = Join-Path $BinRoot 'tunnel-client.exe'
    if (Test-Path -LiteralPath $existing -PathType Leaf) {
        $versionOutput = @(& $existing --version 2>&1)
        if ($LASTEXITCODE -eq 0) { return $existing }
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

    if ($health.product -ne 'Talvora') { throw "Unexpected service answered at $HealthUrl." }
    return $health
}

function Read-BusinessConfig {
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
}

function Write-BusinessConfig {
    param(
        [Parameter(Mandatory)][string] $RuntimeAlias,
        [Parameter(Mandatory)][string] $RuntimeTunnelId,
        [Parameter(Mandatory)][string] $ClientPath
    )

    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    [pscustomobject]@{
        Alias = $RuntimeAlias
        TunnelId = $RuntimeTunnelId
        McpUrl = $McpUrl
        TunnelClient = $ClientPath
        StateRoot = $StateRoot
        UpdatedAtUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $ConfigPath -Encoding UTF8
}

function Invoke-SelfTest {
    Assert-True -Condition (Test-TalvoraTunnelId -Value 'tunnel_0123456789abcdef0123456789abcdef') -Message 'valid tunnel ID was rejected.'
    Assert-True -Condition (-not (Test-TalvoraTunnelId -Value 'not-a-tunnel')) -Message 'invalid tunnel ID was accepted.'

    $fakeRelease = [pscustomobject]@{
        tag_name = 'v9.8.7'
        assets = @(
            [pscustomobject]@{ name = 'tunnel-client-v9.8.7-windows-amd64.zip'; browser_download_url = 'https://example.invalid/tunnel.zip' },
            [pscustomobject]@{ name = 'SHA256SUMS.txt'; browser_download_url = 'https://example.invalid/SHA256SUMS.txt' }
        )
    }
    $selected = Select-TunnelClientReleaseAssets -Release $fakeRelease -Architecture 'amd64'
    Assert-True -Condition ($selected.ArchiveName -eq 'tunnel-client-v9.8.7-windows-amd64.zip') -Message 'release asset selection failed.'

    $temp = Join-Path ([IO.Path]::GetTempPath()) ('Talvora-business-selftest-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    try {
        $payload = Join-Path $temp 'payload.zip'
        [IO.File]::WriteAllText($payload, 'talvora-business-selftest', [Text.UTF8Encoding]::new($false))
        $hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
        $sums = Join-Path $temp 'SHA256SUMS.txt'
        [IO.File]::WriteAllText($sums, "$hash  payload.zip", [Text.UTF8Encoding]::new($false))
        $parsed = Get-ExpectedSha256 -ChecksumsPath $sums -ArchiveName 'payload.zip'
        Assert-True -Condition ($parsed -eq $hash) -Message 'checksum parser failed.'
    }
    finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }

    $args = Get-ConnectArguments -RuntimeAlias 'talvora-business' -RuntimeTunnelId 'tunnel_0123456789abcdef' -ServerUrl $McpUrl
    Assert-True -Condition ($args -contains 'env:CONTROL_PLANE_API_KEY') -Message 'runtime key must be passed by environment reference.'
    Assert-True -Condition (-not (($args -join ' ') -match 'sk-[A-Za-z0-9]')) -Message 'connect arguments unexpectedly contain a literal API key.'
    Assert-True -Condition (($args -join ' ') -match [regex]::Escape($McpUrl)) -Message 'connect arguments do not target Talvora loopback MCP.'

    Write-Output 'TALVORA BUSINESS BOOTSTRAP SELFTEST GREEN'
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

if (-not (Test-TalvoraWindows)) { throw 'ChatGPT Business tunnel bootstrap is supported only on Windows.' }

$health = Get-TalvoraHealth
Write-Host "Talvora local MCP healthy. SourceCommit=$($health.sourceCommit)" -ForegroundColor Green

$clientPath = Get-OrInstallTunnelClient
Write-Host "OpenAI tunnel-client ready: $clientPath" -ForegroundColor Green

if ($InstallOnly) {
    Write-Output 'TALVORA BUSINESS TUNNEL CLIENT INSTALLED'
    return
}

$config = Read-BusinessConfig
if ($Reconnect) {
    if ($null -eq $config) { throw "Reconnect requested but Business config is missing: $ConfigPath" }
    if ([string]::IsNullOrWhiteSpace($TunnelId)) { $TunnelId = [string] $config.TunnelId }
    if ([string]::IsNullOrWhiteSpace($Alias)) { $Alias = [string] $config.Alias }
}

if ([string]::IsNullOrWhiteSpace($TunnelId)) {
    Write-Host ''
    Write-Host 'OpenAI Secure MCP Tunnel requires an existing tunnel associated with this ChatGPT Business workspace.' -ForegroundColor Yellow
    Write-Host 'Create/select it at: https://platform.openai.com/settings/organization/tunnels'
    Write-Host 'The runtime principal needs Tunnels Read + Use. The tunnel must include the target ChatGPT workspace.'
    $TunnelId = Read-Host 'Paste tunnel_id'
}

if (-not (Test-TalvoraTunnelId -Value $TunnelId)) {
    throw "Invalid tunnel_id: $TunnelId"
}

$secureCredential = $null
if ($Reconnect -and (Test-Path -LiteralPath $CredentialPath -PathType Leaf)) {
    $secureCredential = Load-RuntimeCredential -Path $CredentialPath
}
else {
    Write-Host ''
    Write-Host 'Enter the restricted runtime API key for this tunnel. It is used only by OpenAI tunnel-client transport.' -ForegroundColor Yellow
    Write-Host 'Create it with Tunnels Read + Use; do not use an admin key.'
    $secureCredential = Read-Host 'Runtime API key' -AsSecureString
    Save-RuntimeCredential -SecureValue $secureCredential -Path $CredentialPath
}

Write-BusinessConfig -RuntimeAlias $Alias -RuntimeTunnelId $TunnelId -ClientPath $clientPath
New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null

$plainCredential = Convert-SecureStringToPlainText -SecureValue $secureCredential
if ([string]::IsNullOrWhiteSpace($plainCredential)) { throw 'Runtime API key cannot be empty.' }

$previousApiKey = [Environment]::GetEnvironmentVariable('CONTROL_PLANE_API_KEY', 'Process')
$previousStateRoot = [Environment]::GetEnvironmentVariable('TUNNEL_CLIENT_STATE_DIR', 'Process')
try {
    $env:CONTROL_PLANE_API_KEY = $plainCredential
    $env:TUNNEL_CLIENT_STATE_DIR = $StateRoot

    $connectArgs = Get-ConnectArguments -RuntimeAlias $Alias -RuntimeTunnelId $TunnelId -ServerUrl $McpUrl
    $connectOutput = Invoke-TunnelClient -ClientPath $clientPath -Arguments $connectArgs
    if (-not [string]::IsNullOrWhiteSpace($connectOutput)) { Write-Verbose $connectOutput }

    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $status = $null
    $statusText = $null
    do {
        try {
            $statusText = Invoke-TunnelClient -ClientPath $clientPath -Arguments @('runtimes', 'status', $Alias, '--json')
            $status = $statusText | ConvertFrom-Json
            if ($status.process_running -eq $true -and $status.healthy -eq $true -and $status.ready -eq $true) { break }
        }
        catch {
            $status = $null
        }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($null -eq $status) { throw 'tunnel-client runtime status did not become available.' }
    if ($status.process_running -ne $true) { throw "tunnel-client runtime is not running.$([Environment]::NewLine)$statusText" }
    if ($status.healthy -ne $true) { throw "tunnel-client runtime is not healthy.$([Environment]::NewLine)$statusText" }
    if ($status.ready -ne $true) { throw "tunnel-client runtime did not become ready.$([Environment]::NewLine)$statusText" }

    Write-Host ''
    Write-Host 'TALVORA CHATGPT BUSINESS TUNNEL READY' -ForegroundColor Green
    Write-Host "Alias: $Alias"
    Write-Host "Tunnel: $TunnelId"
    Write-Host "MCP: $McpUrl"
    if ($status.PSObject.Properties.Name -contains 'ui_url') { Write-Host "Tunnel UI: $($status.ui_url)" }
    if ($status.PSObject.Properties.Name -contains 'health_url') { Write-Host "Tunnel health: $($status.health_url)" }
    if ($status.PSObject.Properties.Name -contains 'control_plane_poll_health') {
        Write-Host "Control-plane poll: $($status.control_plane_poll_health)"
    }
    Write-Host ''
    Write-Host 'ChatGPT Business final one-time UI step:'
    Write-Host '1. Enable Developer mode as a Business workspace Admin/Owner.'
    Write-Host '2. Open ChatGPT Apps/Connectors and create a custom MCP app.'
    Write-Host '3. Choose Connection: Tunnel and select/paste the tunnel above.'
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
) -Message 'pinned tunnel-client version is invalid.'
    Assert-True -Condition (Test-TalvoraTunnelId -Value 'tunnel_0123456789abcdef0123456789abcdef') -Message 'valid tunnel ID was rejected.'
    Assert-True -Condition (-not (Test-TalvoraTunnelId -Value 'not-a-tunnel')) -Message 'invalid tunnel ID was accepted.'
    Assert-True -Condition (-not (Test-TalvoraTunnelId -Value 'tunnel_0123456789ABCDEF0123456789ABCDEF')) -Message 'uppercase tunnel ID was accepted.'
    Assert-True -Condition (-not (Test-TalvoraTunnelId -Value 'tunnel_0123456789abcdef')) -Message 'short tunnel ID was accepted.'

    $fakeRelease = [pscustomobject]@{
        tag_name = 'v9.8.7'
        assets = @(
            [pscustomobject]@{ name = 'tunnel-client-v9.8.7-windows-amd64.zip'; browser_download_url = 'https://example.invalid/tunnel.zip' },
            [pscustomobject]@{ name = 'SHA256SUMS.txt'; browser_download_url = 'https://example.invalid/SHA256SUMS.txt' }
        )
    }
    $selected = Select-TunnelClientReleaseAssets -Release $fakeRelease -Architecture 'amd64'
    Assert-True -Condition ($selected.ArchiveName -eq 'tunnel-client-v9.8.7-windows-amd64.zip') -Message 'release asset selection failed.'

    $temp = Join-Path ([IO.Path]::GetTempPath()) ('Talvora-business-selftest-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    try {
        $payload = Join-Path $temp 'payload.zip'
        [IO.File]::WriteAllText($payload, 'talvora-business-selftest', [Text.UTF8Encoding]::new($false))
        $hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
        $sums = Join-Path $temp 'SHA256SUMS.txt'
        [IO.File]::WriteAllText($sums, "$hash  payload.zip", [Text.UTF8Encoding]::new($false))
        $parsed = Get-ExpectedSha256 -ChecksumsPath $sums -ArchiveName 'payload.zip'
        Assert-True -Condition ($parsed -eq $hash) -Message 'checksum parser failed.'
    }
    finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }

    $args = Get-ConnectArguments -RuntimeAlias 'talvora-business' -RuntimeTunnelId 'tunnel_0123456789abcdef' -ServerUrl $McpUrl
    Assert-True -Condition ($args -contains 'env:CONTROL_PLANE_API_KEY') -Message 'runtime key must be passed by environment reference.'
    Assert-True -Condition (-not (($args -join ' ') -match 'sk-[A-Za-z0-9]')) -Message 'connect arguments unexpectedly contain a literal API key.'
    Assert-True -Condition (($args -join ' ') -match [regex]::Escape($McpUrl)) -Message 'connect arguments do not target Talvora loopback MCP.'

    Write-Output 'TALVORA BUSINESS BOOTSTRAP SELFTEST GREEN'
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

if (-not (Test-TalvoraWindows)) { throw 'ChatGPT Business tunnel bootstrap is supported only on Windows.' }

$health = Get-TalvoraHealth
Write-Host "Talvora local MCP healthy. SourceCommit=$($health.sourceCommit)" -ForegroundColor Green

$clientPath = Get-OrInstallTunnelClient
Write-Host "OpenAI tunnel-client ready: $clientPath" -ForegroundColor Green

if ($InstallOnly) {
    Write-Output 'TALVORA BUSINESS TUNNEL CLIENT INSTALLED'
    return
}

$config = Read-BusinessConfig
if ($Reconnect) {
    if ($null -eq $config) { throw "Reconnect requested but Business config is missing: $ConfigPath" }
    if ([string]::IsNullOrWhiteSpace($TunnelId)) { $TunnelId = [string] $config.TunnelId }
    if ([string]::IsNullOrWhiteSpace($Alias)) { $Alias = [string] $config.Alias }
}

if ([string]::IsNullOrWhiteSpace($TunnelId)) {
    Write-Host ''
    Write-Host 'OpenAI Secure MCP Tunnel requires an existing tunnel associated with this ChatGPT Business workspace.' -ForegroundColor Yellow
    Write-Host 'Create/select it at: https://platform.openai.com/settings/organization/tunnels'
    Write-Host 'The runtime principal needs Tunnels Read + Use. The tunnel must include the target ChatGPT workspace.'
    $TunnelId = Read-Host 'Paste tunnel_id'
}

if (-not (Test-TalvoraTunnelId -Value $TunnelId)) {
    throw "Invalid tunnel_id: $TunnelId"
}

$secureCredential = $null
if ($Reconnect -and (Test-Path -LiteralPath $CredentialPath -PathType Leaf)) {
    $secureCredential = Load-RuntimeCredential -Path $CredentialPath
}
else {
    Write-Host ''
    Write-Host 'Enter the restricted runtime API key for this tunnel. It is used only by OpenAI tunnel-client transport.' -ForegroundColor Yellow
    Write-Host 'Create it with Tunnels Read + Use; do not use an admin key.'
    $secureCredential = Read-Host 'Runtime API key' -AsSecureString
    Save-RuntimeCredential -SecureValue $secureCredential -Path $CredentialPath
}

Write-BusinessConfig -RuntimeAlias $Alias -RuntimeTunnelId $TunnelId -ClientPath $clientPath
New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null

$plainCredential = Convert-SecureStringToPlainText -SecureValue $secureCredential
if ([string]::IsNullOrWhiteSpace($plainCredential)) { throw 'Runtime API key cannot be empty.' }

$previousApiKey = [Environment]::GetEnvironmentVariable('CONTROL_PLANE_API_KEY', 'Process')
$previousStateRoot = [Environment]::GetEnvironmentVariable('TUNNEL_CLIENT_STATE_DIR', 'Process')
try {
    $env:CONTROL_PLANE_API_KEY = $plainCredential
    $env:TUNNEL_CLIENT_STATE_DIR = $StateRoot

    $connectArgs = Get-ConnectArguments -RuntimeAlias $Alias -RuntimeTunnelId $TunnelId -ServerUrl $McpUrl
    $connectOutput = Invoke-TunnelClient -ClientPath $clientPath -Arguments $connectArgs
    if (-not [string]::IsNullOrWhiteSpace($connectOutput)) { Write-Verbose $connectOutput }

    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $status = $null
    $statusText = $null
    do {
        try {
            $statusText = Invoke-TunnelClient -ClientPath $clientPath -Arguments @('runtimes', 'status', $Alias, '--json')
            $status = $statusText | ConvertFrom-Json
            if ($status.process_running -eq $true -and $status.healthy -eq $true -and $status.ready -eq $true) { break }
        }
        catch {
            $status = $null
        }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($null -eq $status) { throw 'tunnel-client runtime status did not become available.' }
    if ($status.process_running -ne $true) { throw "tunnel-client runtime is not running.$([Environment]::NewLine)$statusText" }
    if ($status.healthy -ne $true) { throw "tunnel-client runtime is not healthy.$([Environment]::NewLine)$statusText" }
    if ($status.ready -ne $true) { throw "tunnel-client runtime did not become ready.$([Environment]::NewLine)$statusText" }

    Write-Host ''
    Write-Host 'TALVORA CHATGPT BUSINESS TUNNEL READY' -ForegroundColor Green
    Write-Host "Alias: $Alias"
    Write-Host "Tunnel: $TunnelId"
    Write-Host "MCP: $McpUrl"
    if ($status.PSObject.Properties.Name -contains 'ui_url') { Write-Host "Tunnel UI: $($status.ui_url)" }
    if ($status.PSObject.Properties.Name -contains 'health_url') { Write-Host "Tunnel health: $($status.health_url)" }
    if ($status.PSObject.Properties.Name -contains 'control_plane_poll_health') {
        Write-Host "Control-plane poll: $($status.control_plane_poll_health)"
    }
    Write-Host ''
    Write-Host 'ChatGPT Business final one-time UI step:'
    Write-Host '1. Enable Developer mode as a Business workspace Admin/Owner.'
    Write-Host '2. Open ChatGPT Apps/Connectors and create a custom MCP app.'
    Write-Host '3. Choose Connection: Tunnel and select/paste the tunnel above.'
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

}

function Test-TalvoraWindows {
    return [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
}

function Get-TunnelClientArchitecture {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    switch ($architecture) {
        'X64' { return 'amd64' }
        'Arm64' { return 'arm64' }
        default { throw "Unsupported Windows architecture for OpenAI tunnel-client: $architecture" }
    }
}

function Select-TunnelClientReleaseAssets {
    param(
        [Parameter(Mandatory)] $Release,
        [Parameter(Mandatory)][string] $Architecture
    )

    $tag = [string] $Release.tag_name
    if ([string]::IsNullOrWhiteSpace($tag)) { throw 'OpenAI tunnel-client release did not include tag_name.' }

    $archiveName = "tunnel-client-$tag-windows-$Architecture.zip"
    $archive = @($Release.assets | Where-Object { $_.name -eq $archiveName }) | Select-Object -First 1
    $checksums = @($Release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' }) | Select-Object -First 1
    if ($null -eq $archive) { throw "OpenAI tunnel-client release is missing $archiveName." }
    if ($null -eq $checksums) { throw 'OpenAI tunnel-client release is missing SHA256SUMS.txt.' }

    return [pscustomobject]@{
        Tag = $tag
        ArchiveName = $archiveName
        ArchiveUrl = [string] $archive.browser_download_url
        ChecksumsUrl = [string] $checksums.browser_download_url
    }
}

function Get-ExpectedSha256 {
    param(
        [Parameter(Mandatory)][string] $ChecksumsPath,
        [Parameter(Mandatory)][string] $ArchiveName
    )

    $line = Get-Content -LiteralPath $ChecksumsPath | Where-Object {
        $_ -match '^[0-9A-Fa-f]{64}\s+\*?.+$' -and
        [IO.Path]::GetFileName(($_ -replace '^[0-9A-Fa-f]{64}\s+\*?', '').Trim()) -eq $ArchiveName
    } | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($line)) { throw "SHA256SUMS.txt does not contain $ArchiveName." }
    if ($line -notmatch '^(?<hash>[0-9A-Fa-f]{64})\s+\*?(?<name>.+)$') { throw "Invalid checksum line for $ArchiveName." }
    return $Matches['hash'].ToLowerInvariant()
}

function Get-ConnectArguments {
    param(
        [Parameter(Mandatory)][string] $RuntimeAlias,
        [Parameter(Mandatory)][string] $RuntimeTunnelId,
        [Parameter(Mandatory)][string] $ServerUrl
    )

    return @(
        'runtimes', 'connect',
        '--alias', $RuntimeAlias,
        '--tunnel-id', $RuntimeTunnelId,
        '--runtime-api-key', 'env:CONTROL_PLANE_API_KEY',
        '--mcp-server-url', $ServerUrl,
        '--json'
    )
}

function Invoke-TunnelClient {
    param(
        [Parameter(Mandatory)][string] $ClientPath,
        [Parameter(Mandatory)][string[]] $Arguments
    )

    $output = @(& $ClientPath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = ($output | ForEach-Object { [string] $_ }) -join [Environment]::NewLine
    if ($exitCode -ne 0) {
        throw "tunnel-client failed with exit code $exitCode.$([Environment]::NewLine)$text"
    }
    return $text
}

function Convert-SecureStringToPlainText {
    param([Parameter(Mandatory)][Security.SecureString] $SecureValue)

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
        [Parameter(Mandatory)][Security.SecureString] $SecureValue,
        [Parameter(Mandatory)][string] $Path
    )

    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $encrypted = ConvertFrom-SecureString -SecureString $SecureValue
    [IO.File]::WriteAllText($Path, $encrypted, [Text.UTF8Encoding]::new($false))
}

function Load-RuntimeCredential {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Stored tunnel runtime credential was not found: $Path"
    }

    $encrypted = [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false)).Trim()
    if ([string]::IsNullOrWhiteSpace($encrypted)) { throw "Stored tunnel runtime credential is empty: $Path" }
    return ConvertTo-SecureString -String $encrypted
}

function Install-OpenAITunnelClient {
    param([Parameter(Mandatory)][string] $DestinationRoot)

    if (-not (Test-TalvoraWindows)) { throw 'ChatGPT Business tunnel bootstrap is supported only on Windows.' }

    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
    $release = Invoke-RestMethod -Uri $ReleaseApi -Headers @{ 'User-Agent' = 'Talvora-Business-Tunnel-Bootstrap' }
    $architecture = Get-TunnelClientArchitecture
    $selected = Select-TunnelClientReleaseAssets -Release $release -Architecture $architecture

    $tempRoot = Join-Path $env:TEMP ('Talvora-tunnel-client-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $archivePath = Join-Path $tempRoot $selected.ArchiveName
        $checksumsPath = Join-Path $tempRoot 'SHA256SUMS.txt'
        Invoke-WebRequest -Uri $selected.ArchiveUrl -OutFile $archivePath -Headers @{ 'User-Agent' = 'Talvora-Business-Tunnel-Bootstrap' }
        Invoke-WebRequest -Uri $selected.ChecksumsUrl -OutFile $checksumsPath -Headers @{ 'User-Agent' = 'Talvora-Business-Tunnel-Bootstrap' }

        $expectedHash = Get-ExpectedSha256 -ChecksumsPath $checksumsPath -ArchiveName $selected.ArchiveName
        $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "OpenAI tunnel-client SHA256 mismatch. Expected=$expectedHash Actual=$actualHash"
        }

        $extractRoot = Join-Path $tempRoot 'extract'
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
        $sourceExe = Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Filter 'tunnel-client.exe' | Select-Object -First 1
        if ($null -eq $sourceExe) { throw 'OpenAI tunnel-client archive did not contain tunnel-client.exe.' }

        $sourceBinRoot = Split-Path -Parent $sourceExe.FullName
        if (Test-Path -LiteralPath $DestinationRoot) { Remove-Item -LiteralPath $DestinationRoot -Recurse -Force }
        New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceBinRoot '*') -Destination $DestinationRoot -Recurse -Force

        $installedExe = Join-Path $DestinationRoot 'tunnel-client.exe'
        if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) { throw "Installed tunnel-client is missing: $installedExe" }

        $versionOutput = @(& $installedExe --version 2>&1)
        if ($LASTEXITCODE -ne 0) { throw "Installed tunnel-client --version failed: $($versionOutput -join ' ')" }

        New-Item -ItemType Directory -Path (Split-Path -Parent $DestinationRoot) -Force | Out-Null
        [pscustomobject]@{
            Product = 'OpenAI Secure MCP Tunnel client'
            Version = ($versionOutput | ForEach-Object { [string] $_ }) -join ' '
            ReleaseTag = $selected.Tag
            Asset = $selected.ArchiveName
            Sha256 = $actualHash
            InstalledAtUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $DestinationRoot) 'release.json') -Encoding UTF8

        return $installedExe
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-OrInstallTunnelClient {
    $existing = Join-Path $BinRoot 'tunnel-client.exe'
    if (Test-Path -LiteralPath $existing -PathType Leaf) {
        $versionOutput = @(& $existing --version 2>&1)
        if ($LASTEXITCODE -eq 0) { return $existing }
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

    if ($health.product -ne 'Talvora') { throw "Unexpected service answered at $HealthUrl." }
    return $health
}

function Read-BusinessConfig {
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
}

function Write-BusinessConfig {
    param(
        [Parameter(Mandatory)][string] $RuntimeAlias,
        [Parameter(Mandatory)][string] $RuntimeTunnelId,
        [Parameter(Mandatory)][string] $ClientPath
    )

    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    [pscustomobject]@{
        Alias = $RuntimeAlias
        TunnelId = $RuntimeTunnelId
        McpUrl = $McpUrl
        TunnelClient = $ClientPath
        StateRoot = $StateRoot
        UpdatedAtUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $ConfigPath -Encoding UTF8
}

function Invoke-SelfTest {
    Assert-True -Condition (Test-TalvoraTunnelId -Value 'tunnel_0123456789abcdef0123456789abcdef') -Message 'valid tunnel ID was rejected.'
    Assert-True -Condition (-not (Test-TalvoraTunnelId -Value 'not-a-tunnel')) -Message 'invalid tunnel ID was accepted.'

    $fakeRelease = [pscustomobject]@{
        tag_name = 'v9.8.7'
        assets = @(
            [pscustomobject]@{ name = 'tunnel-client-v9.8.7-windows-amd64.zip'; browser_download_url = 'https://example.invalid/tunnel.zip' },
            [pscustomobject]@{ name = 'SHA256SUMS.txt'; browser_download_url = 'https://example.invalid/SHA256SUMS.txt' }
        )
    }
    $selected = Select-TunnelClientReleaseAssets -Release $fakeRelease -Architecture 'amd64'
    Assert-True -Condition ($selected.ArchiveName -eq 'tunnel-client-v9.8.7-windows-amd64.zip') -Message 'release asset selection failed.'

    $temp = Join-Path ([IO.Path]::GetTempPath()) ('Talvora-business-selftest-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    try {
        $payload = Join-Path $temp 'payload.zip'
        [IO.File]::WriteAllText($payload, 'talvora-business-selftest', [Text.UTF8Encoding]::new($false))
        $hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
        $sums = Join-Path $temp 'SHA256SUMS.txt'
        [IO.File]::WriteAllText($sums, "$hash  payload.zip", [Text.UTF8Encoding]::new($false))
        $parsed = Get-ExpectedSha256 -ChecksumsPath $sums -ArchiveName 'payload.zip'
        Assert-True -Condition ($parsed -eq $hash) -Message 'checksum parser failed.'
    }
    finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }

    $args = Get-ConnectArguments -RuntimeAlias 'talvora-business' -RuntimeTunnelId 'tunnel_0123456789abcdef' -ServerUrl $McpUrl
    Assert-True -Condition ($args -contains 'env:CONTROL_PLANE_API_KEY') -Message 'runtime key must be passed by environment reference.'
    Assert-True -Condition (-not (($args -join ' ') -match 'sk-[A-Za-z0-9]')) -Message 'connect arguments unexpectedly contain a literal API key.'
    Assert-True -Condition (($args -join ' ') -match [regex]::Escape($McpUrl)) -Message 'connect arguments do not target Talvora loopback MCP.'

    Write-Output 'TALVORA BUSINESS BOOTSTRAP SELFTEST GREEN'
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

if (-not (Test-TalvoraWindows)) { throw 'ChatGPT Business tunnel bootstrap is supported only on Windows.' }

$health = Get-TalvoraHealth
Write-Host "Talvora local MCP healthy. SourceCommit=$($health.sourceCommit)" -ForegroundColor Green

$clientPath = Get-OrInstallTunnelClient
Write-Host "OpenAI tunnel-client ready: $clientPath" -ForegroundColor Green

if ($InstallOnly) {
    Write-Output 'TALVORA BUSINESS TUNNEL CLIENT INSTALLED'
    return
}

$config = Read-BusinessConfig
if ($Reconnect) {
    if ($null -eq $config) { throw "Reconnect requested but Business config is missing: $ConfigPath" }
    if ([string]::IsNullOrWhiteSpace($TunnelId)) { $TunnelId = [string] $config.TunnelId }
    if ([string]::IsNullOrWhiteSpace($Alias)) { $Alias = [string] $config.Alias }
}

if ([string]::IsNullOrWhiteSpace($TunnelId)) {
    Write-Host ''
    Write-Host 'OpenAI Secure MCP Tunnel requires an existing tunnel associated with this ChatGPT Business workspace.' -ForegroundColor Yellow
    Write-Host 'Create/select it at: https://platform.openai.com/settings/organization/tunnels'
    Write-Host 'The runtime principal needs Tunnels Read + Use. The tunnel must include the target ChatGPT workspace.'
    $TunnelId = Read-Host 'Paste tunnel_id'
}

if (-not (Test-TalvoraTunnelId -Value $TunnelId)) {
    throw "Invalid tunnel_id: $TunnelId"
}

$secureCredential = $null
if ($Reconnect -and (Test-Path -LiteralPath $CredentialPath -PathType Leaf)) {
    $secureCredential = Load-RuntimeCredential -Path $CredentialPath
}
else {
    Write-Host ''
    Write-Host 'Enter the restricted runtime API key for this tunnel. It is used only by OpenAI tunnel-client transport.' -ForegroundColor Yellow
    Write-Host 'Create it with Tunnels Read + Use; do not use an admin key.'
    $secureCredential = Read-Host 'Runtime API key' -AsSecureString
    Save-RuntimeCredential -SecureValue $secureCredential -Path $CredentialPath
}

Write-BusinessConfig -RuntimeAlias $Alias -RuntimeTunnelId $TunnelId -ClientPath $clientPath
New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null

$plainCredential = Convert-SecureStringToPlainText -SecureValue $secureCredential
if ([string]::IsNullOrWhiteSpace($plainCredential)) { throw 'Runtime API key cannot be empty.' }

$previousApiKey = [Environment]::GetEnvironmentVariable('CONTROL_PLANE_API_KEY', 'Process')
$previousStateRoot = [Environment]::GetEnvironmentVariable('TUNNEL_CLIENT_STATE_DIR', 'Process')
try {
    $env:CONTROL_PLANE_API_KEY = $plainCredential
    $env:TUNNEL_CLIENT_STATE_DIR = $StateRoot

    $connectArgs = Get-ConnectArguments -RuntimeAlias $Alias -RuntimeTunnelId $TunnelId -ServerUrl $McpUrl
    $connectOutput = Invoke-TunnelClient -ClientPath $clientPath -Arguments $connectArgs
    if (-not [string]::IsNullOrWhiteSpace($connectOutput)) { Write-Verbose $connectOutput }

    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $status = $null
    $statusText = $null
    do {
        try {
            $statusText = Invoke-TunnelClient -ClientPath $clientPath -Arguments @('runtimes', 'status', $Alias, '--json')
            $status = $statusText | ConvertFrom-Json
            if ($status.process_running -eq $true -and $status.healthy -eq $true -and $status.ready -eq $true) { break }
        }
        catch {
            $status = $null
        }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($null -eq $status) { throw 'tunnel-client runtime status did not become available.' }
    if ($status.process_running -ne $true) { throw "tunnel-client runtime is not running.$([Environment]::NewLine)$statusText" }
    if ($status.healthy -ne $true) { throw "tunnel-client runtime is not healthy.$([Environment]::NewLine)$statusText" }
    if ($status.ready -ne $true) { throw "tunnel-client runtime did not become ready.$([Environment]::NewLine)$statusText" }

    Write-Host ''
    Write-Host 'TALVORA CHATGPT BUSINESS TUNNEL READY' -ForegroundColor Green
    Write-Host "Alias: $Alias"
    Write-Host "Tunnel: $TunnelId"
    Write-Host "MCP: $McpUrl"
    if ($status.PSObject.Properties.Name -contains 'ui_url') { Write-Host "Tunnel UI: $($status.ui_url)" }
    if ($status.PSObject.Properties.Name -contains 'health_url') { Write-Host "Tunnel health: $($status.health_url)" }
    if ($status.PSObject.Properties.Name -contains 'control_plane_poll_health') {
        Write-Host "Control-plane poll: $($status.control_plane_poll_health)"
    }
    Write-Host ''
    Write-Host 'ChatGPT Business final one-time UI step:'
    Write-Host '1. Enable Developer mode as a Business workspace Admin/Owner.'
    Write-Host '2. Open ChatGPT Apps/Connectors and create a custom MCP app.'
    Write-Host '3. Choose Connection: Tunnel and select/paste the tunnel above.'
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
