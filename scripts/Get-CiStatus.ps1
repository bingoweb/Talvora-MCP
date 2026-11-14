[CmdletBinding()]
param(
    [string] $Commit = 'HEAD',
    [int] $TimeoutMinutes = 25,
    [int] $PollSeconds = 30
)
# Waits for the windows-ci run of a commit and prints a short, readable verdict.
# Exit codes: 0 = success, 1 = failure (prints the failing lines), 2 = no run / timeout / cancelled with no finished successor.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repoRoot = Split-Path -Parent $PSScriptRoot
$repository = 'bingoweb/Talvora-MCP'
function Format-CiDuration($run) {
    $start = [DateTimeOffset]::Parse([string]$run.startedAt, [Globalization.CultureInfo]::InvariantCulture)
    $end = [DateTimeOffset]::Parse([string]$run.updatedAt, [Globalization.CultureInfo]::InvariantCulture)
    $seconds = [Math]::Max(0, ($end - $start).TotalSeconds)
    if ($seconds -lt 60) {
        return $seconds.ToString('0.0', [Globalization.CultureInfo]::GetCultureInfo('tr-TR')) + ' sn'
    }
    $rounded = [long][Math]::Round($seconds, [MidpointRounding]::AwayFromZero)
    if ($rounded -lt 3600) {
        return [string][long][Math]::Floor($rounded / 60) + ' dk ' + [string]($rounded % 60) + ' sn'
    }
    return [string][long][Math]::Floor($rounded / 3600) + ' sa ' + [string][long][Math]::Floor(($rounded % 3600) / 60) + ' dk'
}
$sha = (& git -C $repoRoot rev-parse $Commit).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') { throw "Unknown commit: $Commit" }

$deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
$run = $null
while ($true) {
    $json = & gh run list --repo $repository --workflow windows-ci.yml --commit $sha `
        --json databaseId,status,conclusion,url,startedAt,updatedAt --limit 5
    if ($LASTEXITCODE -ne 0) { throw 'gh run list failed (is gh signed in for this user?).' }
    $run = @($json | ConvertFrom-Json) | Select-Object -First 1
    if ($run -and $run.status -eq 'completed') { break }
    if ([DateTime]::UtcNow -ge $deadline) { break }
    Start-Sleep -Seconds $PollSeconds
}

if (-not $run) { Write-Host "CI: $($sha.Substring(0, 7)) için windows-ci çalışması bulunamadı."; exit 2 }
if ($run.status -ne 'completed') { Write-Host "CI: süre doldu, çalışma hâlâ '$($run.status)': $($run.url)"; exit 2 }
if ($run.conclusion -eq 'cancelled') {
    # A newer push cancels this run (cancel-in-progress); the newest run that contains this commit decides.
    $runs = @(& gh run list --repo $repository --workflow windows-ci.yml --branch main `
        --json databaseId,headSha,status,conclusion,url,startedAt,updatedAt --limit 10 | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0) { throw 'gh run list failed (is gh signed in for this user?).' }
    $run = $runs | Where-Object { $_.conclusion -ne 'cancelled' } | Where-Object {
        & git -C $repoRoot merge-base --is-ancestor $sha $_.headSha 2>$null; $LASTEXITCODE -eq 0 } | Select-Object -First 1
    if (-not $run) { Write-Host "CI: $($sha.Substring(0, 7)) çalışması iptal edildi; onu içeren yeni bir çalışma yok."; exit 2 }
    if ($run.status -ne 'completed') { Write-Host "CI: çalışma iptal edildi, onu içeren yeni çalışma hâlâ '$($run.status)': $($run.url)"; exit 2 }
}
if ($run.conclusion -eq 'success') { Write-Host "CI başarılı ($(Format-CiDuration $run)): $($run.url)"; exit 0 }

Write-Host "CI başarısız ($($run.conclusion), $(Format-CiDuration $run)): $($run.url)"
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
