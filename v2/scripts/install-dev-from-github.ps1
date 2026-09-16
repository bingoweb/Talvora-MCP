$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoUrl = 'https://github.com/bingoweb/Talvora-MCP.git'
$branch = 'talvora-2/foundation'
$installRoot = Join-Path $env:USERPROFILE 'Talvora-MCP'
$healthUri = 'http://127.0.0.1:7676/healthz'
$mcpUri = 'http://127.0.0.1:7676/mcp'

function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
}

function Ensure-Chocolatey {
    if (Get-Command choco.exe -ErrorAction SilentlyContinue) { return }
    Set-ExecutionPolicy Bypass -Scope Process -Force
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
    Invoke-Expression ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))
    Refresh-Path
    if (-not (Get-Command choco.exe -ErrorAction SilentlyContinue)) { throw 'Chocolatey bootstrap failed.' }
}

function Ensure-ChocoPackage([string] $package, [string] $command) {
    if (Get-Command $command -ErrorAction SilentlyContinue) { return }
    & choco.exe install $package -y --no-progress
    if ($LASTEXITCODE -notin 0, 1641, 3010) { throw "Chocolatey failed installing $package (exit $LASTEXITCODE)." }
    Refresh-Path
}

function Ensure-DotNet10Sdk {
    # An existing dotnet host may contain only a runtime or an older SDK.
    if (Get-Command dotnet.exe -ErrorAction SilentlyContinue) {
        $sdks = @(& dotnet.exe --list-sdks)
        if ($LASTEXITCODE -eq 0 -and @($sdks | Where-Object { $_ -match '^10\.\d+\.\d+\s+\[' }).Count -gt 0) {
            return
        }
    }

    & choco.exe install dotnet-10.0-sdk -y --no-progress
    $installExitCode = $LASTEXITCODE
    if ($installExitCode -notin 0, 1641, 3010) {
        throw "Chocolatey failed installing .NET 10 SDK (exit $installExitCode)."
    }
    Refresh-Path
    if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
        throw '.NET 10 SDK installation did not expose dotnet.exe in PATH.'
    }
    $sdks = @(& dotnet.exe --list-sdks)
    if ($LASTEXITCODE -ne 0 -or @($sdks | Where-Object { $_ -match '^10\.\d+\.\d+\s+\[' }).Count -eq 0) {
        throw '.NET 10 SDK is not available after installation. Restart the terminal and check dotnet --list-sdks.'
    }
    if ($installExitCode -in 1641, 3010) {
        Write-Warning 'The .NET installer reported a Windows restart requirement.'
    }
}

function Get-TalvoraHealth {
    try {
        $health = Invoke-RestMethod -Uri 'http://127.0.0.1:7676/healthz' -TimeoutSec 2
        if ($health.product -eq 'Talvora') { return $health }
    }
    catch { }
    return $null
}

function Write-LiveStatus($health, [string] $prefix = 'Talvora Gateway is live') {
    Write-Host "${prefix}: $mcpUri" -ForegroundColor Green
    $health | ConvertTo-Json -Depth 4
}

Ensure-Chocolatey
Ensure-ChocoPackage 'git' 'git.exe'
Ensure-DotNet10Sdk
Ensure-ChocoPackage 'powershell-core' 'pwsh.exe'

if (Test-Path $installRoot) {
    if (-not (Test-Path (Join-Path $installRoot '.git'))) {
        throw "$installRoot exists but is not a Git repository. Refusing to overwrite it."
    }
    Push-Location $installRoot
    try {
        & git fetch origin $branch
        if ($LASTEXITCODE -ne 0) { throw 'git fetch failed.' }
        & git checkout $branch
        if ($LASTEXITCODE -ne 0) { throw 'git checkout failed.' }
        & git pull --ff-only origin $branch
        if ($LASTEXITCODE -ne 0) { throw 'git pull --ff-only failed.' }
    }
    finally { Pop-Location }
}
else {
    & git clone --branch $branch --single-branch $repoUrl $installRoot
    if ($LASTEXITCODE -ne 0) { throw 'git clone failed.' }
}

$v2Root = Join-Path $installRoot 'v2'
$project = Join-Path $v2Root 'src\Talvora.Gateway\Talvora.Gateway.csproj'

& dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw 'Talvora restore failed.' }
& dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Talvora build failed.' }

$existingHealth = Get-TalvoraHealth
if ($null -ne $existingHealth) {
    Write-LiveStatus $existingHealth 'Talvora Gateway is already live'
    exit 0
}

Start-Process -FilePath 'dotnet.exe' -ArgumentList @('run','--project',$project,'-c','Release','--no-build','--no-launch-profile') -WorkingDirectory $v2Root -WindowStyle Hidden

$deadline = (Get-Date).AddSeconds(30)
do {
    $health = Get-TalvoraHealth
    if ($null -ne $health) {
        Write-LiveStatus $health
        exit 0
    }
    Start-Sleep -Milliseconds 500
} while ((Get-Date) -lt $deadline)

throw 'Talvora Gateway did not become healthy within 30 seconds.'
