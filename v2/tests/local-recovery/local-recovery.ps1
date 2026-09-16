param([switch] $Integration)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$module = Join-Path $root 'scripts\Talvora.Local.psm1'
if (-not (Test-Path -LiteralPath $module)) { throw 'Local recovery module is missing; reboot-safe local startup is not implemented.' }
foreach ($name in @('Talvora.Local.psm1','install-local.ps1','run-local.ps1')) {
    $path = Join-Path $root ('scripts\' + $name)
    $tokens = $null; $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw ($parseErrors.Message -join '; ') }
    $text = [IO.File]::ReadAllText($path)
    if ($text -match '(?i)CONTROL_PLANE_API_KEY|OPENAI_API_KEY|tunnel-client|winget|platform\.openai\.com') {
        throw "Local startup unexpectedly depends on a cloud key, tunnel or forbidden package manager: $name"
    }
}
Import-Module $module -Force -DisableNameChecking
function Assert-True($value, [string]$message) { if (-not $value) { throw $message }; Write-Host "PASS $message" }
$original = "model = `"keep-this-model`"`r`n[mcp_servers.other]`r`nurl = `"http://localhost:1234/mcp`"`r`n"
$updated = Get-TalvoraClientConfigText -Content $original
Assert-True ($updated.StartsWith($original, [StringComparison]::Ordinal)) 'Unrelated client configuration remains byte-for-byte intact'
Assert-True ($updated.Contains('[mcp_servers.talvora_local]')) 'A local MCP client entry is added'
Assert-True ($updated.Contains('url = "http://127.0.0.1:7676/mcp"')) 'Client entry uses loopback HTTP'
Assert-True ((Get-TalvoraClientConfigText -Content $updated) -ceq $updated) 'Client configuration is idempotent'
$old = "[mcp_servers.talvora_local]`nurl = `"http://localhost:9999/mcp`"`n`n[mcp_servers.other]`nurl = `"keep-me`"`n"
$fixed = Get-TalvoraClientConfigText -Content $old
Assert-True ($fixed.Contains('url = "keep-me"') -and -not $fixed.Contains(':9999/')) 'Only the Talvora entry is replaced'
Assert-True ((Get-TalvoraClientConfigText -Content '') -match '^\[mcp_servers\.talvora_local\]') 'An empty client configuration is supported'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$task = New-TalvoraLocalTask -Launcher 'C:\Test Folder\run-local.ps1' -ConfigPath 'C:\Test Folder\local.json' -UserId $identity.Name
Assert-True ($task.Actions[0].Arguments.Contains('"C:\Test Folder\run-local.ps1"')) 'Paths with spaces are quoted in task actions'
Assert-True ([int]$task.Principal.RunLevel -eq 1) 'Task requests the highest available Windows user token'
Assert-True ([int]$task.Principal.LogonType -eq 3) 'Task uses the interactive user session, not SYSTEM'
Assert-True ([string]$task.Settings.ExecutionTimeLimit -eq 'PT0S') 'Task has no default three-day execution limit'
Assert-True ($task.Triggers[0].CimClass.CimClassName -eq 'MSFT_TaskLogonTrigger') 'Task is triggered at user logon'
if (-not $Integration) { Write-Host 'Local recovery contract tests GREEN.'; exit 0 }
$tempRoot = Join-Path $env:TEMP ('talvora-local-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
$taskName = 'Talvora local test ' + [Guid]::NewGuid().ToString('N')
$child = $null
try {
    $task | Register-ScheduledTask -TaskName $taskName -Force | Out-Null
    $xml = Export-ScheduledTask -TaskName $taskName
    Assert-True ($xml.Contains('<RunLevel>HighestAvailable</RunLevel>')) 'Windows accepts and persists the task definition'
    Assert-True ($xml.Contains('<LogonTrigger>')) 'Persisted task includes a real logon trigger'
    $state = Join-Path $tempRoot 'local state'
    $clientHome = Join-Path $tempRoot 'client settings'
    & (Join-Path $root 'scripts\install-local.ps1') -RepoRoot ([IO.Path]::GetDirectoryName($root)) -StateRoot $state -ClientHome $clientHome -NoStart
    if (-not $?) { throw 'Local installer failed.' }
    $configPath = Join-Path $state 'current.json'
    Assert-True (Test-Path -LiteralPath $configPath) 'Actual installer creates its deployed state'
    Assert-True (Test-Path -LiteralPath (Join-Path $clientHome 'config.toml')) 'Actual installer writes local client configuration'
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $launcher = Join-Path ([IO.Path]::GetDirectoryName($config.GatewayDll)) 'run-local.ps1'
    $savedTask = Get-ScheduledTask -TaskName 'Talvora Local MCP' -TaskPath '\'
    Assert-True ($savedTask.Actions[0].Arguments.Contains($launcher)) 'Installed task points at the deployed launcher, not a build directory'
    $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $child = Start-Process $ps -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "{0}" -ConfigPath "{1}"' -f $launcher,$configPath) -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(35)
    $health = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        $health = Get-TalvoraLocalHealth
        if ($null -ne $health) { break }
        $child.Refresh(); if ($child.HasExited) { throw "Local launcher exited with $($child.ExitCode)" }
        Start-Sleep -Milliseconds 300
    }
    Assert-True ($null -ne $health) 'Deployed Gateway starts from a path containing spaces'
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($health.processId)"
    Assert-True ($process.CommandLine.Contains($config.GatewayDll)) 'Health response belongs to the deployed local Gateway'
    & dotnet.exe run --project (Join-Path $root 'tests\Talvora.McpSmoke\Talvora.McpSmoke.csproj') -c Release --no-build -- 'http://127.0.0.1:7676/mcp'
    if ($LASTEXITCODE -ne 0) { throw 'Real file lifecycle and process MCP smoke failed.' }
    Write-Host 'Local recovery integration GREEN: task-registration, launcher, 6 tools, Unicode file lifecycle, process execution.'
}
finally {
    Unregister-ScheduledTask -TaskName 'Talvora Local MCP' -TaskPath '\' -Confirm:$false -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    $health = Get-TalvoraLocalHealth
    if ($null -ne $health) {
        $owned = Get-CimInstance Win32_Process -Filter "ProcessId = $($health.processId)" -ErrorAction SilentlyContinue
        if ($null -ne $owned -and $owned.CommandLine -like ('*' + $tempRoot + '*')) { Stop-Process -Id $health.processId -Force -ErrorAction SilentlyContinue }
    }
    if ($null -ne $child) { $child.Refresh(); if (-not $child.HasExited) { Stop-Process -Id $child.Id -Force -ErrorAction SilentlyContinue } }
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
