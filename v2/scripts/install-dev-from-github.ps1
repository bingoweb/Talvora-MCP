$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoUrl = 'https://github.com/bingoweb/Talvora-MCP.git'
$branch = 'talvora-2/foundation'
$installRoot = Join-Path $env:USERPROFILE 'Talvora-MCP'

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

Ensure-Chocolatey
Ensure-ChocoPackage 'git' 'git.exe'
Ensure-ChocoPackage 'dotnet-10.0-sdk' 'dotnet.exe'
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

$existing = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*Talvora.Gateway*' -and $_.CommandLine -like '*7676*' }
if (-not $existing) {
    Start-Process -FilePath 'dotnet.exe' -ArgumentList @('run','--project',$project,'-c','Release','--no-launch-profile') -WorkingDirectory $v2Root -WindowStyle Hidden
}

$deadline = (Get-Date).AddSeconds(30)
do {
    try {
        $health = Invoke-RestMethod -Uri 'http://127.0.0.1:7676/healthz' -TimeoutSec 2
        if ($health.product -eq 'Talvora') {
            Write-Host "Talvora Gateway is live: http://127.0.0.1:7676/mcp" -ForegroundColor Green
            $health | ConvertTo-Json -Depth 4
            exit 0
        }
    }
    catch { Start-Sleep -Milliseconds 500 }
} while ((Get-Date) -lt $deadline)

throw 'Talvora Gateway did not become healthy within 30 seconds.'
