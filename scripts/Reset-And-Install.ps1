[CmdletBinding()]
param(
    [string] $RepoRoot = (Join-Path $env:USERPROFILE 'Talvora-MCP'),
    [switch] $FromTemp
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Remove-TalvoraClientTables {
    param([Parameter(Mandatory)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }

    $lines = [IO.File]::ReadAllLines($Path)
    $result = [Collections.Generic.List[string]]::new()
    $skip = $false
    foreach ($line in $lines) {
        if ($line -match '^\s*\[mcp_servers\.talvora[^\]]*\]\s*$') { $skip = $true; continue }
        if ($skip -and $line -match '^\s*\[') { $skip = $false }
        if (-not $skip) { $result.Add($line) }
    }
    [IO.File]::WriteAllLines($Path, $result, [Text.UTF8Encoding]::new($false))
}

if (-not $FromTemp) {
    $tempScript = Join-Path $env:TEMP ('Talvora-reset-' + [Guid]::NewGuid().ToString('N') + '.ps1')
    Copy-Item -LiteralPath $PSCommandPath -Destination $tempScript -Force
    $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $child = Start-Process $ps -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',('"{0}"' -f $tempScript),'-FromTemp','-RepoRoot',('"{0}"' -f $RepoRoot))
    Remove-Item -LiteralPath $tempScript -Force -ErrorAction SilentlyContinue
    exit $child.ExitCode
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator rights are required.' }

Write-Host 'Removing every previous Talvora component...' -ForegroundColor Yellow

Get-CimInstance Win32_Service | Where-Object { $_.Name -like 'Talvora*' -or $_.DisplayName -like 'Talvora*' } | ForEach-Object {
    & "$env:SystemRoot\System32\sc.exe" stop $_.Name | Out-Null
    Start-Sleep -Milliseconds 300
    & "$env:SystemRoot\System32\sc.exe" delete $_.Name | Out-Host
}

Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like '*Talvora*' -or $_.TaskPath -like '*Talvora*' } | ForEach-Object {
    Unregister-ScheduledTask -InputObject $_ -Confirm:$false -ErrorAction SilentlyContinue
}

Get-CimInstance Win32_Process | Where-Object {
    $_.ProcessId -ne $PID -and (
        ([string]$_.ExecutablePath -like '*\Talvora\*') -or
        ([string]$_.CommandLine -match 'Talvora\.(Gateway|SystemBroker|Service|dll|exe)'))
} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

foreach ($path in @(
    (Join-Path $env:LOCALAPPDATA 'Talvora'),
    (Join-Path $env:ProgramData 'Talvora')
)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

$clientHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
Remove-TalvoraClientTables -Path (Join-Path $clientHome 'config.toml')

if (Test-Path -LiteralPath $RepoRoot) { Remove-Item -LiteralPath $RepoRoot -Recurse -Force }

if (-not (Get-Command choco.exe -ErrorAction SilentlyContinue)) {
    Set-ExecutionPolicy Bypass -Scope Process -Force
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
    Invoke-Expression ((New-Object Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))
}
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { choco install git -y --no-progress }
if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) { choco install dotnet-10.0-sdk --version=10.0.400 -y --no-progress }
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')

Write-Host 'Cloning the clean Talvora main branch...'
git clone --branch main --single-branch https://github.com/bingoweb/Talvora-MCP.git $RepoRoot
if ($LASTEXITCODE -ne 0) { throw "git clone failed: $LASTEXITCODE" }

& (Join-Path $RepoRoot 'scripts\Install.ps1') -RepoRoot $RepoRoot -ClientHome $clientHome
if ($LASTEXITCODE -ne 0) { throw "Talvora installation failed: $LASTEXITCODE" }
