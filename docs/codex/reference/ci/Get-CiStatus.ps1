[CmdletBinding()]
param(
    [string] $Commit = 'HEAD',
    [int] $TimeoutMinutes = 25,
    [int] $PollSeconds = 30
)
# Waits for the windows-ci run of a commit and prints a short, readable verdict.
# Exit codes: 0 = success, 1 = failure (prints the failing lines), 2 = no run / timeout.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repoRoot = Split-Path -Parent $PSScriptRoot
$repository = 'bingoweb/Talvora-MCP'
$sha = (& git -C $repoRoot rev-parse $Commit).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') { throw "Unknown commit: $Commit" }

$deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
$run = $null
while ($true) {
    $json = & gh run list --repo $repository --workflow windows-ci.yml --commit $sha `
        --json databaseId,status,conclusion,url --limit 5
    if ($LASTEXITCODE -ne 0) { throw 'gh run list failed (is gh signed in for this user?).' }
    $run = @($json | ConvertFrom-Json) | Select-Object -First 1
    if ($run -and $run.status -eq 'completed') { break }
    if ([DateTime]::UtcNow -ge $deadline) { break }
    Start-Sleep -Seconds $PollSeconds
}

if (-not $run) { Write-Host "CI: $($sha.Substring(0, 7)) için windows-ci çalışması bulunamadı."; exit 2 }
if ($run.status -ne 'completed') { Write-Host "CI: süre doldu, çalışma hâlâ '$($run.status)': $($run.url)"; exit 2 }
if ($run.conclusion -eq 'success') { Write-Host "CI başarılı: $($run.url)"; exit 0 }

Write-Host "CI başarısız ($($run.conclusion)): $($run.url)"
$log = & gh run view $run.databaseId --repo $repository --log-failed 2>&1
$lines = @($log |
    Where-Object { $_ -notmatch '\x1b\[36;1m' } |
    Where-Object { $_ -match 'FAIL |Kontrol başarısız|Reason:|Unhandled exception|regression failures|error [A-Z]{2,}\d+|Exception \(|##\[error\]' } |
    Select-Object -First 25)
foreach ($line in $lines) {
    $text = [string]$line
    $at = $text.IndexOf('Z ')
    Write-Host ($(if ($at -ge 0) { $text.Substring($at + 2) } else { $text }))
}
exit 1
