[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $InstallRoot = (Join-Path $env:ProgramData 'Talvora\Service'),
    [string] $ClientHome = $(if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' })
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-Chocolatey {
    if (Get-Command choco.exe -ErrorAction SilentlyContinue) { return }
    Set-ExecutionPolicy Bypass -Scope Process -Force
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
    Invoke-Expression ((New-Object Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))
    $env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
}

function Set-TalvoraClientConfig {
    param([Parameter(Mandatory)][string] $Path)

    $lines = if (Test-Path -LiteralPath $Path) { [IO.File]::ReadAllLines($Path) } else { @() }
    $result = [Collections.Generic.List[string]]::new()
    $skip = $false
    foreach ($line in $lines) {
        if ($line -match '^\s*\[mcp_servers\.talvora_local\]\s*$') { $skip = $true; continue }
        if ($skip -and $line -match '^\s*\[') { $skip = $false }
        if (-not $skip) { $result.Add($line) }
    }
    while ($result.Count -gt 0 -and [string]::IsNullOrWhiteSpace($result[$result.Count - 1])) { $result.RemoveAt($result.Count - 1) }
    if ($result.Count -gt 0) { $result.Add('') }
    $result.Add('[mcp_servers.talvora_local]')
    $result.Add('url = "http://127.0.0.1:7676/mcp"')
    $result.Add('enabled = true')
    $result.Add('startup_timeout_sec = 20')
    $result.Add('tool_timeout_sec = 300')
    $result.Add('default_tools_approval_mode = "approve"')
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllLines($Path, $result, [Text.UTF8Encoding]::new($false))
}

if (-not (Test-Administrator)) {
    $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $child = Start-Process $ps -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',('"{0}"' -f $PSCommandPath),
        '-RepoRoot',('"{0}"' -f $RepoRoot),'-InstallRoot',('"{0}"' -f $InstallRoot),'-ClientHome',('"{0}"' -f $ClientHome))
    exit $child.ExitCode
}

Ensure-Chocolatey
if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { choco install git -y --no-progress }
if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) { choco install dotnet-10.0-sdk --version=10.0.400 -y --no-progress }
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')

$project = Join-Path $RepoRoot 'src\Talvora\Talvora.csproj'
$smokeProject = Join-Path $RepoRoot 'tests\Talvora.Smoke\Talvora.Smoke.csproj'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Talvora project not found: $project" }
if (-not (Test-Path -LiteralPath $smokeProject -PathType Leaf)) { throw "Talvora smoke project not found: $smokeProject" }

$sourceCommit = (& git.exe -C $RepoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-fA-F]{40}
try {
    dotnet publish $project -c Release -r win-x64 --self-contained true -o $stage --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

    $service = Get-Service -Name Talvora -ErrorAction SilentlyContinue
    if ($null -ne $service) {
        if ($service.Status -ne 'Stopped') {
            Stop-Service Talvora -Force
            $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
        }
        & "$env:SystemRoot\System32\sc.exe" delete Talvora | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Failed to delete previous Talvora service: $LASTEXITCODE" }
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 300
            $service = Get-Service -Name Talvora -ErrorAction SilentlyContinue
        } while ($null -ne $service -and [DateTime]::UtcNow -lt $deadline)
        if ($null -ne $service) { throw 'Previous Talvora service was not removed.' }
    }

    $listeners = @(Get-NetTCPConnection -LocalPort 7676 -State Listen -ErrorAction SilentlyContinue)
    if ($listeners.Count) {
        $owners = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
        throw "Port 7676 is already in use by PID(s): $($owners -join ','). Reset installer must remove previous Talvora first."
    }

    if (Test-Path -LiteralPath $InstallRoot) { Remove-Item -LiteralPath $InstallRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $stage '*') -Destination $InstallRoot -Recurse -Force

    [pscustomobject]@{
        SourceCommit = $sourceCommit
        InstalledAtUtc = $installedAtUtc
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $InstallRoot 'talvora-runtime.json') -Encoding UTF8

    $exe = Join-Path $InstallRoot 'Talvora.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published executable missing: $exe" }

    New-Service -Name Talvora -BinaryPathName ('"{0}"' -f $exe) -DisplayName 'Talvora' -Description 'Talvora LocalSystem MCP service' -StartupType Automatic | Out-Null
    $definition = Get-CimInstance Win32_Service -Filter "Name='Talvora'"
    if ($definition.StartName -ne 'LocalSystem') { throw "Talvora service account is not LocalSystem: $($definition.StartName)" }

    & "$env:SystemRoot\System32\sc.exe" failure Talvora reset= 60 actions= restart/1000/restart/3000/restart/10000 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Failed to configure Talvora recovery: $LASTEXITCODE" }

    Start-Service Talvora

    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $health = $null
    do {
        try { $health = Invoke-RestMethod -Uri 'http://127.0.0.1:7676/healthz' -TimeoutSec 2 -Proxy $null } catch { $health = $null }
        if ($null -ne $health) { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($null -eq $health) { throw 'Talvora health endpoint did not become ready.' }
    if ($health.product -ne 'Talvora') { throw 'Unexpected service answered on port 7676.' }
    if ($health.sid -ne 'S-1-5-18') { throw "Talvora is not running as LocalSystem. SID=$($health.sid)" }
    if ($health.sourceCommit -ne $sourceCommit) { throw "Talvora runtime source commit mismatch. Expected=$sourceCommit Actual=$($health.sourceCommit)" }

    Push-Location $RepoRoot
    try {
        $smokeOutput = @(dotnet run --project $smokeProject -c Release -- 'http://127.0.0.1:7676/mcp' 2>&1)
        $smokeExitCode = $LASTEXITCODE
    }
    finally { Pop-Location }

    $smokeOutput | ForEach-Object { Write-Output $_ }
    if ($smokeExitCode -ne 0) { throw "Talvora MCP smoke failed: $smokeExitCode" }

    $toolsLine = $smokeOutput |
        ForEach-Object { [string]$_ } |
        Where-Object { $_ -match '^tools=' } |
        Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($toolsLine)) { throw 'Talvora MCP smoke did not report its tool manifest.' }

    $toolNames = @(
        $toolsLine.Substring('tools='.Length).Split(',') |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($toolNames.Count -eq 0) { throw 'Talvora MCP smoke returned an empty tool manifest.' }

    Write-Output 'MCP_SMOKE_OK'

    $clientPath = Join-Path $ClientHome 'config.toml'
    Set-TalvoraClientConfig -Path $clientPath

    $stateRoot = Join-Path $env:LOCALAPPDATA 'Talvora'
    New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
    [pscustomobject]@{
        Product = 'Talvora'
        Version = $health.version
        SourceCommit = $sourceCommit
        Service = 'Talvora'
        ServiceSid = $health.sid
        ProcessId = $health.processId
        McpUrl = 'http://127.0.0.1:7676/mcp'
        ClientConfig = $clientPath
        ToolCount = $toolNames.Count
        ToolNames = $toolNames
        InstalledAtUtc = $installedAtUtc
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stateRoot 'current.json') -Encoding UTF8

    Write-Host ''
    Write-Host 'TALVORA READY' -ForegroundColor Green
    Write-Host 'Service account: LocalSystem (S-1-5-18)'
    Write-Host 'MCP smoke: GREEN'
    Write-Host 'MCP: http://127.0.0.1:7676/mcp'
    Write-Host "Client config: $clientPath"
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
) {
    throw "Unable to resolve Talvora source commit from $RepoRoot"
}
$sourceCommit = $sourceCommit.ToLowerInvariant()
$installedAtUtc = [DateTime]::UtcNow.ToString('o')

$stage = Join-Path $env:TEMP ('Talvora-publish-' + [Guid]::NewGuid().ToString('N'))
try {
    dotnet publish $project -c Release -r win-x64 --self-contained true -o $stage --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

    $service = Get-Service -Name Talvora -ErrorAction SilentlyContinue
    if ($null -ne $service) {
        if ($service.Status -ne 'Stopped') {
            Stop-Service Talvora -Force
            $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
        }
        & "$env:SystemRoot\System32\sc.exe" delete Talvora | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Failed to delete previous Talvora service: $LASTEXITCODE" }
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 300
            $service = Get-Service -Name Talvora -ErrorAction SilentlyContinue
        } while ($null -ne $service -and [DateTime]::UtcNow -lt $deadline)
        if ($null -ne $service) { throw 'Previous Talvora service was not removed.' }
    }

    $listeners = @(Get-NetTCPConnection -LocalPort 7676 -State Listen -ErrorAction SilentlyContinue)
    if ($listeners.Count) {
        $owners = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
        throw "Port 7676 is already in use by PID(s): $($owners -join ','). Reset installer must remove previous Talvora first."
    }

    if (Test-Path -LiteralPath $InstallRoot) { Remove-Item -LiteralPath $InstallRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $stage '*') -Destination $InstallRoot -Recurse -Force

    $exe = Join-Path $InstallRoot 'Talvora.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published executable missing: $exe" }

    New-Service -Name Talvora -BinaryPathName ('"{0}"' -f $exe) -DisplayName 'Talvora' -Description 'Talvora LocalSystem MCP service' -StartupType Automatic | Out-Null
    $definition = Get-CimInstance Win32_Service -Filter "Name='Talvora'"
    if ($definition.StartName -ne 'LocalSystem') { throw "Talvora service account is not LocalSystem: $($definition.StartName)" }

    & "$env:SystemRoot\System32\sc.exe" failure Talvora reset= 60 actions= restart/1000/restart/3000/restart/10000 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Failed to configure Talvora recovery: $LASTEXITCODE" }

    Start-Service Talvora

    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $health = $null
    do {
        try { $health = Invoke-RestMethod -Uri 'http://127.0.0.1:7676/healthz' -TimeoutSec 2 -Proxy $null } catch { $health = $null }
        if ($null -ne $health) { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($null -eq $health) { throw 'Talvora health endpoint did not become ready.' }
    if ($health.product -ne 'Talvora') { throw 'Unexpected service answered on port 7676.' }
    if ($health.sid -ne 'S-1-5-18') { throw "Talvora is not running as LocalSystem. SID=$($health.sid)" }

    Push-Location $RepoRoot
    try {
        dotnet run --project $smokeProject -c Release -- 'http://127.0.0.1:7676/mcp'
        if ($LASTEXITCODE -ne 0) { throw "Talvora MCP smoke failed: $LASTEXITCODE" }
    }
    finally { Pop-Location }
    Write-Output 'MCP_SMOKE_OK'

    $clientPath = Join-Path $ClientHome 'config.toml'
    Set-TalvoraClientConfig -Path $clientPath

    $stateRoot = Join-Path $env:LOCALAPPDATA 'Talvora'
    New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
    [pscustomobject]@{
        Product = 'Talvora'
        Version = $health.version
        Service = 'Talvora'
        ServiceSid = $health.sid
        ProcessId = $health.processId
        McpUrl = 'http://127.0.0.1:7676/mcp'
        ClientConfig = $clientPath
        InstalledAtUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stateRoot 'current.json') -Encoding UTF8

    Write-Host ''
    Write-Host 'TALVORA READY' -ForegroundColor Green
    Write-Host 'Service account: LocalSystem (S-1-5-18)'
    Write-Host 'MCP smoke: GREEN'
    Write-Host 'MCP: http://127.0.0.1:7676/mcp'
    Write-Host "Client config: $clientPath"
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
