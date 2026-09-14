[CmdletBinding()]
param(
    [switch]$ElevatedChild,
    [string]$TunnelToken,
    [switch]$ReplaceExistingCloudflaredService
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$serviceName = 'cloudflared'
$originUrl = 'http://127.0.0.1:7676'
$releaseApi = 'https://api.github.com/repos/cloudflare/cloudflared/releases/latest'
$installDirectory = Join-Path $env:ProgramFiles 'Talvora\Cloudflare'
$cloudflaredExe = Join-Path $installDirectory 'cloudflared.exe'

function Test-Administrator {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-CloudflaredArchitecture {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    switch ($architecture) {
        'X64' { return 'amd64' }
        'Arm64' { return 'arm64' }
        default { throw "Desteklenmeyen Windows mimarisi: $architecture" }
    }
}

function Resolve-TunnelToken {
    param([string]$ExplicitToken)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitToken)) {
        return $ExplicitToken.Trim()
    }

    $environmentToken = $env:TALVORA_CLOUDFLARE_TUNNEL_TOKEN
    if (-not [string]::IsNullOrWhiteSpace($environmentToken)) {
        return $environmentToken.Trim()
    }

    Write-Host ''
    Write-Host 'Cloudflare Dashboard > Networking > Tunnels > tunnel > Add a replica ekranındaki token gerekli.' -ForegroundColor Yellow
    $secureToken = Read-Host 'Cloudflare Tunnel token' -AsSecureString
    $plainToken = [System.Net.NetworkCredential]::new('', $secureToken).Password
    if ([string]::IsNullOrWhiteSpace($plainToken)) {
        throw 'Cloudflare Tunnel token boş bırakılamaz.'
    }

    return $plainToken.Trim()
}

function Get-LatestCloudflaredBinary {
    param(
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)][string]$Architecture
    )

    Write-Host 'Cloudflare cloudflared son sürümü sorgulanıyor...' -ForegroundColor Cyan
    $headers = @{
        'User-Agent' = 'Talvora-Cloudflare-Installer'
        'Accept' = 'application/vnd.github+json'
    }
    $release = Invoke-RestMethod -Uri $releaseApi -Headers $headers -Method Get
    $assetName = "cloudflared-windows-$Architecture.exe"
    $asset = @($release.assets | Where-Object { $_.name -eq $assetName }) | Select-Object -First 1
    if ($null -eq $asset) {
        throw "Cloudflare release içinde $assetName bulunamadı."
    }

    $digestProperty = $asset.PSObject.Properties['digest']
    if ($null -eq $digestProperty -or [string]::IsNullOrWhiteSpace([string]$digestProperty.Value)) {
        throw "Cloudflare release SHA-256 digest sağlamadı: $assetName"
    }

    $digest = [string]$digestProperty.Value
    if (-not $digest.StartsWith('sha256:', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Beklenmeyen Cloudflare digest biçimi: $digest"
    }

    Write-Host "cloudflared sürümü: $($release.tag_name)" -ForegroundColor DarkGray
    Invoke-WebRequest -Uri ([string]$asset.browser_download_url) -Headers $headers -OutFile $DestinationPath

    $expectedHash = $digest.Substring('sha256:'.Length).ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not [string]::Equals($expectedHash, $actualHash, [System.StringComparison]::Ordinal)) {
        throw "cloudflared SHA-256 doğrulaması başarısız. Expected=$expectedHash Actual=$actualHash"
    }

    Write-Host 'cloudflared SHA-256 doğrulaması: GREEN' -ForegroundColor Green
}

function Remove-ExistingTalvoraCloudflaredService {
    param([Parameter(Mandatory)][string]$InstallerExecutable)

    $existing = Get-Service -Name 'cloudflared' -ErrorAction SilentlyContinue
    if ($null -eq $existing) {
        return
    }

    $serviceInfo = Get-CimInstance -ClassName Win32_Service -Filter "Name='cloudflared'" -ErrorAction Stop
    $existingPath = [string]$serviceInfo.PathName
    $isTalvoraService = $existingPath.IndexOf($installDirectory, [System.StringComparison]::OrdinalIgnoreCase) -ge 0

    if (-not $isTalvoraService -and -not $ReplaceExistingCloudflaredService) {
        throw @"
Bu makinede Talvora dışında bir cloudflared Windows servisi zaten var.
Mevcut servis otomatik olarak ezilmedi: $existingPath
Bilinçli olarak değiştirmek istiyorsan Install-CloudflareTunnel.ps1 dosyasını -ReplaceExistingCloudflaredService ile çalıştır.
"@
    }

    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction Stop
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
    }

    Write-Host 'Mevcut cloudflared servisi kaldırılıyor...' -ForegroundColor DarkYellow
    & $InstallerExecutable service uninstall | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "cloudflared service uninstall başarısız. ExitCode=$LASTEXITCODE"
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        $remaining = Get-Service -Name 'cloudflared' -ErrorAction SilentlyContinue
        if ($null -eq $remaining) {
            return
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    throw 'cloudflared servisi zamanında kaldırılamadı.'
}

function Test-TalvoraOrigin {
    try {
        $response = Invoke-WebRequest -Uri "$originUrl/health" -Method Get -TimeoutSec 3
        if ($response.StatusCode -eq 200) {
            Write-Host 'Talvora local health: GREEN' -ForegroundColor Green
            return
        }
    }
    catch {
        Write-Warning "Talvora şu anda $originUrl üzerinde yanıt vermiyor. Tunnel kurulacak; TALVORA-BASLAT.bat ile Host'u başlatınca origin erişilebilir olacak."
    }
}

if (-not (Test-Administrator)) {
    if ($ElevatedChild) {
        throw 'Cloudflare tunnel elevated child yönetici yetkisi olmadan başlatıldı.'
    }

    Write-Host 'Cloudflare Tunnel Windows Service kurulumu için bir kez yönetici izni istenecek.' -ForegroundColor Yellow
    Write-Host 'Tunnel token komut satırıyla UAC çocuğuna aktarılmayacak; yükseltilmiş pencerede ortamdan okunacak veya güvenli istemle alınacak.' -ForegroundColor DarkGray

    $powerShellPath = (Get-Process -Id $PID -ErrorAction Stop).Path
    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-ElevatedChild'
    )
    if ($ReplaceExistingCloudflaredService) {
        $arguments += '-ReplaceExistingCloudflaredService'
    }

    $child = Start-Process -FilePath $powerShellPath -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $child.ExitCode
}

$resolvedToken = Resolve-TunnelToken -ExplicitToken $TunnelToken
$architecture = Resolve-CloudflaredArchitecture
$stagingDirectory = Join-Path $env:TEMP ("Talvora-Cloudflare-{0}" -f [Guid]::NewGuid().ToString('N'))
$stagedExecutable = Join-Path $stagingDirectory 'cloudflared.exe'

try {
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    Get-LatestCloudflaredBinary -DestinationPath $stagedExecutable -Architecture $architecture

    Remove-ExistingTalvoraCloudflaredService -InstallerExecutable $stagedExecutable

    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -LiteralPath $stagedExecutable -Destination $cloudflaredExe -Force

    & $cloudflaredExe --version | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "cloudflared çalıştırılamadı. ExitCode=$LASTEXITCODE"
    }

    Test-TalvoraOrigin

    Write-Host ''
    Write-Host 'Cloudflare remotely-managed tunnel Windows servisi kuruluyor...' -ForegroundColor Cyan
    # Cloudflare resmi Windows akışı: cloudflared service install <TUNNEL_TOKEN>
    & $cloudflaredExe service install $resolvedToken | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "cloudflared service install başarısız. ExitCode=$LASTEXITCODE"
    }

    $service = Get-Service -Name 'cloudflared' -ErrorAction Stop
    if ($service.Status -ne 'Running') {
        Start-Service -Name $serviceName
        $service = Get-Service -Name 'cloudflared' -ErrorAction Stop
        $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
    }

    if ($service.Status -ne 'Running') {
        throw "cloudflared servisi Running durumuna geçmedi. Status=$($service.Status)"
    }

    Write-Host ''
    Write-Host 'Talvora Cloudflare Tunnel Windows Service: GREEN' -ForegroundColor Green
    Write-Host "  service:  $serviceName"
    Write-Host "  binary:   $cloudflaredExe"
    Write-Host "  origin:   $originUrl"
    Write-Host "  MCP:      $originUrl/mcp"
    Write-Host ''
    Write-Host 'Cloudflare Dashboard üzerinde bu tunnel için Published application route ekle:' -ForegroundColor Yellow
    Write-Host '  Service URL: http://127.0.0.1:7676'
    Write-Host '  Route path:  /mcp*  (yalnız MCP yüzeyini yayınlamak için önerilir)'
    Write-Host '  Hostname:    Cloudflare üzerindeki kendi alan adından seçtiğin sabit hostname'
    Write-Host ''
    Write-Host 'Uzaktan otomasyon için Cloudflare Access Service Auth kullanırsan istemci şu header çiftini göndermelidir:' -ForegroundColor DarkYellow
    Write-Host '  CF-Access-Client-Id'
    Write-Host '  CF-Access-Client-Secret'
    Write-Host ''
    Write-Host 'Not: Tunnel token repoya veya Talvora config dosyasına yazılmaz. Cloudflare service install komutu tokenı Windows servis tanımında kendi standart biçiminde yönetir.' -ForegroundColor DarkGray
}
finally {
    $resolvedToken = $null
    $TunnelToken = $null
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
