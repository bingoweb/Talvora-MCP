param([Parameter(Mandatory)][string] $ConfigPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Talvora.Local.psm1') -Force -DisableNameChecking
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $config.LogRoot -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$stdout = Join-Path $config.LogRoot "gateway-$stamp.stdout.log"
$stderr = Join-Path $config.LogRoot "gateway-$stamp.stderr.log"
$mutex = [Threading.Mutex]::new($false, 'Local\Talvora.Local.Gateway')
$locked = $false
try {
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { exit 0 }
    $existing = Get-TalvoraLocalHealth
    if ($null -ne $existing) {
        $owner = Get-CimInstance Win32_Process -Filter "ProcessId = $($existing.processId)"
        if (-not (Test-TalvoraProcessPath -Process $owner -DllPaths @($config.GatewayDll))) {
            throw 'Port 7676 belongs to a different Talvora deployment. Run TALVORA-KUR.cmd to switch deployments.'
        }
        Wait-Process -Id $existing.processId -ErrorAction SilentlyContinue
        exit 1
    }
    $start = @{
        FilePath = [string]$config.DotNetPath
        ArgumentList = ('"{0}"' -f $config.GatewayDll)
        WorkingDirectory = [IO.Path]::GetDirectoryName([string]$config.GatewayDll)
        RedirectStandardOutput = $stdout
        RedirectStandardError = $stderr
        WindowStyle = 'Hidden'
        PassThru = $true
    }
    $child = Start-Process @start
    [IO.File]::WriteAllText((Join-Path $config.LogRoot 'latest-process.txt'), [string]$child.Id)
    $child.WaitForExit()
    # Any unrequested exit should be retried by Task Scheduler.
    $code = $child.ExitCode
    if ($code -eq 0) { $code = 1 }
    exit $code
}
catch {
    ($_ | Out-String) | Add-Content -LiteralPath $stderr
    Write-Error $_ -ErrorAction Continue
    exit 1
}
finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
