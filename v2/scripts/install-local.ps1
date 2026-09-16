param(
    [string] $RepoRoot = (Join-Path $env:USERPROFILE 'Talvora-MCP'),
    [string] $StateRoot = (Join-Path $env:LOCALAPPDATA 'Talvora\Local'),
    [string] $ClientHome = $(if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }),
    [string] $OwnerSid = ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value),
    [switch] $NoStart
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $argsText = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -RepoRoot "{1}" -StateRoot "{2}" -ClientHome "{3}" -OwnerSid "{4}"' -f $PSCommandPath,$RepoRoot,$StateRoot,$ClientHome,$OwnerSid
    if ($NoStart) { $argsText += ' -NoStart' }
    Write-Host 'Windows yonetici izni isteniyor. Acilan ekranda Evet secin.'
    $elevated = Start-Process $ps -Verb RunAs -ArgumentList $argsText -PassThru -Wait
    exit $elevated.ExitCode
}
if ($identity.User.Value -ne $OwnerSid) { throw 'Kurulum, Talvora sahibinin kendi yonetici oturumunda calistirilmalidir.' }
Import-Module (Join-Path $PSScriptRoot 'Talvora.Local.psm1') -Force -DisableNameChecking
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
$StateRoot = [IO.Path]::GetFullPath($StateRoot)
$v2 = Join-Path $RepoRoot 'v2'
$source = Join-Path $v2 'src\Talvora.Gateway\bin\Release\net10.0'
$sourceDll = Join-Path $source 'Talvora.Gateway.dll'
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
foreach ($name in @('Talvora.Gateway.dll','Talvora.Gateway.deps.json','Talvora.Gateway.runtimeconfig.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
        throw "Hazir yerel Gateway bulunamadi: $source\$name. Bu paket mevcut derlenmis Talvora kurulumunu duzeltir."
    }
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$release = Join-Path $StateRoot "releases\$stamp"
$logRoot = Join-Path $StateRoot 'logs'
$backup = Join-Path $StateRoot "backups\$stamp"
$configPath = Join-Path $StateRoot 'current.json'
$taskName = 'Talvora Local MCP'
New-Item -ItemType Directory -Path $release,$logRoot,$backup -Force | Out-Null
Start-Transcript -Path (Join-Path $logRoot "install-$stamp.log") -Force | Out-Null
try {
    Write-Host '1/5 Hazir Gateway ayri calisma klasorune kopyalaniyor.'
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $release -Recurse -Force
    foreach ($name in @('Talvora.Local.psm1','run-local.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $release -Force
    }
    $config = @{
        DotNetPath = $dotnet
        GatewayDll = (Join-Path $release 'Talvora.Gateway.dll')
        LogRoot = $logRoot
        RepoRoot = $RepoRoot
        TaskName = $taskName
    }
    $task = New-TalvoraLocalTask -Launcher (Join-Path $release 'run-local.ps1') -ConfigPath $configPath -UserId $identity.Name
    $clientPath = Join-Path $ClientHome 'config.toml'
    $clientBefore = if (Test-Path -LiteralPath $clientPath) { [IO.File]::ReadAllText($clientPath) } else { '' }
    $clientAfter = Get-TalvoraClientConfigText -Content $clientBefore
    $oldConfig = $null
    if (Test-Path -LiteralPath $configPath) {
        Copy-Item -LiteralPath $configPath -Destination $backup
        $oldConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    }
    $oldExe = Join-Path $env:LOCALAPPDATA 'Talvora\Gateway\Talvora.Gateway.exe'
    $ownedDlls = @($sourceDll)
    if ($null -ne $oldConfig) { $ownedDlls += [string]$oldConfig.GatewayDll }
    $listeners = @(Get-NetTCPConnection -State Listen | Where-Object LocalPort -eq 7676)
    foreach ($ownerId in @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)) {
        $owner = Get-CimInstance Win32_Process -Filter "ProcessId = $ownerId"
        if (-not (Test-TalvoraProcessPath -Process $owner -DllPaths $ownedDlls -ExePaths @($oldExe))) {
            throw "7676 baska bir uygulamada: PID=$ownerId; $($owner.ExecutablePath). Hicbir ilgisiz uygulama kapatilmadi."
        }
    }
    Write-Host '2/5 Eski Talvora gorevi ile port cakismasi gideriliyor.'
    $previousTask = Get-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction SilentlyContinue
    if ($null -ne $previousTask) {
        Export-ScheduledTask -TaskName $taskName -TaskPath '\' | Set-Content -LiteralPath (Join-Path $backup 'task.xml') -Encoding Unicode
        Stop-ScheduledTask -InputObject $previousTask
    }
    foreach ($legacy in (Get-ScheduledTask)) {
        $direct = @($legacy.Actions | Where-Object {
            [Environment]::ExpandEnvironmentVariables([string]$_.Execute).Trim().Trim('"') -ieq $oldExe
        })
        if ($direct.Count) {
            Export-ScheduledTask -TaskName $legacy.TaskName -TaskPath $legacy.TaskPath | Set-Content -LiteralPath (Join-Path $backup ('legacy-' + [Guid]::NewGuid().ToString('N') + '.xml')) -Encoding Unicode
            Disable-ScheduledTask -InputObject $legacy | Out-Null
            Stop-ScheduledTask -InputObject $legacy
        }
    }
    foreach ($ownerId in @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)) {
        $owner = Get-CimInstance Win32_Process -Filter "ProcessId = $ownerId" -ErrorAction SilentlyContinue
        if (Test-TalvoraProcessPath -Process $owner -DllPaths $ownedDlls -ExePaths @($oldExe)) {
            Stop-Process -Id $ownerId -Force -ErrorAction SilentlyContinue
        }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $occupied = @(Get-NetTCPConnection -State Listen | Where-Object LocalPort -eq 7676)
        if (-not $occupied.Count) { break }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($occupied.Count) { throw '7676 serbest birakilmadi. Baska bir baslatma gorevi olabilir. Kurulum gunlugunu paylasin.' }
    Write-Host '3/5 Oturum acilisinda yonetici yetkisiyle calisacak yerel gorev kaydediliyor.'
    $config | ConvertTo-Json | Set-Content -LiteralPath ($configPath + '.new') -Encoding UTF8
    Move-Item -LiteralPath ($configPath + '.new') -Destination $configPath -Force
    Register-ScheduledTask -TaskName $taskName -TaskPath '\' -InputObject $task -Force | Out-Null
    Write-Host '4/5 Yerel istemci ayari hazirlaniyor.'
    if ($clientAfter -cne $clientBefore) {
        New-Item -ItemType Directory -Path $ClientHome -Force | Out-Null
        if (Test-Path -LiteralPath $clientPath) { Copy-Item -LiteralPath $clientPath -Destination (Join-Path $backup 'client-config.toml') }
        [IO.File]::WriteAllText(($clientPath + '.talvora-new'), $clientAfter, [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath ($clientPath + '.talvora-new') -Destination $clientPath -Force
    }
    Write-Host "Yerel istemci ayari: $clientPath"
    if ($NoStart) { Write-Host 'Kurulum kaydedildi. Baslatma istenmedigi icin Gateway baslatilmadi.'; return }
    Write-Host '5/5 Yerel MCP baslatiliyor ve gercek arac testi yapiliyor.'
    Start-ScheduledTask -TaskName $taskName -TaskPath '\'
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    $ready = $false
    do {
        $health = Get-TalvoraLocalHealth
        if ($null -ne $health) {
            $owner = Get-CimInstance Win32_Process -Filter "ProcessId = $($health.processId)"
            if (Test-TalvoraProcessPath -Process $owner -DllPaths @($config.GatewayDll)) { $ready = $true; break }
        }
        Start-Sleep -Milliseconds 400
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $ready) {
        Get-ScheduledTaskInfo -TaskName $taskName -TaskPath '\' | Format-List | Out-Host
        Get-ChildItem -LiteralPath $logRoot -Filter '*.stderr.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -Tail 40 | Out-Host
        throw 'Yerel Gateway baslangici dogrulanamadi. Yukaridaki gorev sonucu ve hata gunlugu gerekli.'
    }
    $health | ConvertTo-Json | Out-Host
    Push-Location $v2
    try {
        & $dotnet run --project 'tests\Talvora.McpSmoke\Talvora.McpSmoke.csproj' -c Release -- 'http://127.0.0.1:7676/mcp'
        if ($LASTEXITCODE -ne 0) { throw "MCP testi basarisiz: $LASTEXITCODE" }
    }
    finally { Pop-Location }
    Write-Host ''
    Write-Host 'TALVORA LOCAL READY' -ForegroundColor Green
    Write-Host 'MCP: http://127.0.0.1:7676/mcp'
    Write-Host 'Windows oturum acilisi: otomatik, en yuksek kullanici yetkisi'
    Write-Host "Gunlukler: $logRoot"
    Write-Host 'Yerel ChatGPT/Codex istemcisini yeniden acin; MCP adi: talvora_local'
    Write-Host 'Bu kurulum web sohbetine dogrudan erisim saglamaz; istemci Windows bilgisayarda calismalidir.'
}
catch { Write-Error $_ -ErrorAction Continue; exit 1 }
finally { Stop-Transcript | Out-Null }
