[CmdletBinding()]
param(
    [string]$TunnelId = $env:CONTROL_PLANE_TUNNEL_ID,
    [string]$ProfileName = 'talvora-local'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$talvoraHealthUrl = 'http://127.0.0.1:7676/health'
$talvoraMcpUrl = 'http://127.0.0.1:7676/mcp'
$profileDirectory = Join-Path $root '.talvora\tunnel-profiles'
$toolRoot = Join-Path $root '.talvora\tools\tunnel-client'
$version = 'v0.0.14'

function Read-SecretPlainText {
    param([Parameter(Mandatory)][string]$Prompt)

    $secure = Read-Host $Prompt -AsSecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    }
}

function Assert-TalvoraHostReady {
    try {
        $response = Invoke-RestMethod -Uri $talvoraHealthUrl -Method Get -TimeoutSec 3
        if ($null -eq $response -or $response.ok -ne $true) {
            throw 'Talvora health yanıtı hazır değil.'
        }
    }
    catch {
        throw "Talvora Host erişilemiyor: $talvoraHealthUrl`nÖnce TALVORA-BASLAT.bat ile Host'u başlat. Ayrıntı: $($_.Exception.Message)"
    }
}

function Resolve-TunnelClientPackage {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    switch ($architecture) {
        'X64' {
            return [pscustomobject]@{
                Asset = "tunnel-client-$version-windows-amd64.zip"
                Sha256 = '784ab8da7b5a88f0109f1fd8aaf0a1c86067430b896dddf307ef7e3cc49fa1a5'
                Platform = 'windows-amd64'
            }
        }
        'Arm64' {
            return [pscustomobject]@{
                Asset = "tunnel-client-$version-windows-arm64.zip"
                Sha256 = 'fa775db8897df543dd4ba66404f69492a2acfbc6a291f10df27aced064a16568'
                Platform = 'windows-arm64'
            }
        }
        default {
            throw "Desteklenmeyen Windows mimarisi: $architecture"
        }
    }
}

function Resolve-TunnelClientExecutable {
    $package = Resolve-TunnelClientPackage
    $installDirectory = Join-Path $toolRoot "$version\$($package.Platform)"
    $existing = Get-ChildItem -LiteralPath $installDirectory -Filter 'tunnel-client.exe' -File -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $existing) {
        return $existing.FullName
    }

    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    $archivePath = Join-Path $installDirectory $package.Asset
    $downloadUrl = "https://github.com/openai/tunnel-client/releases/download/$version/$($package.Asset)"

    Write-Host "OpenAI tunnel-client $version indiriliyor..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath

    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $package.Sha256) {
        Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
        throw "tunnel-client SHA256 doğrulaması başarısız. Beklenen=$($package.Sha256) Gerçek=$actualHash"
    }

    $extractDirectory = Join-Path $installDirectory 'package'
    if (Test-Path -LiteralPath $extractDirectory) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDirectory -Force

    $executable = Get-ChildItem -LiteralPath $extractDirectory -Filter 'tunnel-client.exe' -File -Recurse |
        Select-Object -First 1
    if ($null -eq $executable) {
        throw "tunnel-client.exe arşiv içinde bulunamadı: $archivePath"
    }

    return $executable.FullName
}

Assert-TalvoraHostReady

if ([string]::IsNullOrWhiteSpace($TunnelId)) {
    $TunnelId = Read-Host 'OpenAI Tunnel ID (tunnel_...)'
}
if ($TunnelId -notmatch '^tunnel_[0-9a-f]{32}$') {
    throw 'Tunnel ID biçimi geçersiz. Beklenen: tunnel_ + 32 küçük hexadecimal karakter.'
}

if ([string]::IsNullOrWhiteSpace($env:CONTROL_PLANE_API_KEY)) {
    $env:CONTROL_PLANE_API_KEY = Read-SecretPlainText -Prompt 'OpenAI Runtime API Key'
}
if ([string]::IsNullOrWhiteSpace($env:CONTROL_PLANE_API_KEY)) {
    throw 'CONTROL_PLANE_API_KEY boş olamaz.'
}

$tunnelClient = Resolve-TunnelClientExecutable
New-Item -ItemType Directory -Path $profileDirectory -Force | Out-Null

Write-Host ''
Write-Host 'Talvora MCP tünel profili hazırlanıyor...' -ForegroundColor Cyan
& $tunnelClient init `
    --sample sample_mcp_remote_no_auth `
    --profile $ProfileName `
    --profile-dir $profileDirectory `
    --force `
    --tunnel-id $TunnelId `
    --mcp-server-url $talvoraMcpUrl
if ($LASTEXITCODE -ne 0) {
    throw "tunnel-client init başarısız. ExitCode=$LASTEXITCODE"
}

Write-Host ''
Write-Host 'Talvora MCP tüneli doğrulanıyor...' -ForegroundColor Cyan
& $tunnelClient doctor --profile $ProfileName --profile-dir $profileDirectory --explain
if ($LASTEXITCODE -ne 0) {
    throw "tunnel-client doctor başarısız. ExitCode=$LASTEXITCODE"
}

Write-Host ''
Write-Host 'Talvora MCP tüneli başlatılıyor.' -ForegroundColor Green
Write-Host "MCP:      $talvoraMcpUrl" -ForegroundColor DarkGray
Write-Host "Profile:  $profileDirectory\$ProfileName.yaml" -ForegroundColor DarkGray
Write-Host 'Bu pencere açık kaldığı sürece tünel aktif kalır. Durdurmak için Ctrl+C.' -ForegroundColor Yellow
Write-Host ''

& $tunnelClient run --profile $ProfileName --profile-dir $profileDirectory --log.level=info --log.format=struct-text
exit $LASTEXITCODE
