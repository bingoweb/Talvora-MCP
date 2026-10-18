[CmdletBinding()]
param(
    [string] $Card
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$statusPath = Join-Path $repoRoot 'docs/codex/STATUS.md'
$status = [IO.File]::ReadAllText($statusPath)
$logMatch = [regex]::Match($status, '(?m)^## Log[ \t]*\r?$')
if (-not $logMatch.Success) { throw 'STATUS.md is missing its Log heading.' }
$statusHeader = $status.Substring(0, $logMatch.Index).TrimEnd()
$recent = [regex]::Match($status.Substring($logMatch.Index + $logMatch.Length), '(?m)^- .+$')

if ([string]::IsNullOrWhiteSpace($Card)) {
    $progress = [regex]::Match($statusHeader, '(?ms)^## In progress\s*\r?\n(.*?)(?=^## )')
    if ($progress.Success) {
        $current = [regex]::Match($progress.Groups[1].Value, '(?m)^\s*(?<id>[A-Z]+[0-9]+[a-z]?)\s+[-—–]')
        if ($current.Success) { $Card = $current.Groups['id'].Value }
    }
    if ([string]::IsNullOrWhiteSpace($Card)) {
        $queue = [regex]::Match($statusHeader, '(?ms)^## Agent queue[^\r\n]*\r?\n(.*?)(?=^## )')
        if (-not $queue.Success) { throw 'STATUS.md has no Agent queue.' }
        $next = [regex]::Match($queue.Groups[1].Value, '(?m)^- \[ \] (?<id>[A-Z]+[0-9]+[a-z]?)\b')
        if (-not $next.Success) { throw 'Agent queue has no actionable unchecked card.' }
        $Card = $next.Groups['id'].Value
    }
}
$source = if ($Card -match '^UI') {
    'docs/codex/DESIGN-CONTROL-CENTER.md'
}
elseif ($Card -match '^N[0-9]') {
    'docs/codex/DESIGN-WORKLOG.md'
}
else {
    'docs/codex/ROADMAP.md'
}
$roadmap = [IO.File]::ReadAllText((Join-Path $repoRoot $source))
$cardPattern = '(?ms)^###\s+' + [regex]::Escape($Card) + '\s+[—–-].*?(?=^###\s+|\z)'
$cardMatch = [regex]::Match($roadmap, $cardPattern)
if (-not $cardMatch.Success) { throw "Card $Card could not be found in $source." }
$cardText = $cardMatch.Value.TrimEnd()

Write-Output '=== STATUS (without historical Log) ==='
Write-Output $statusHeader
Write-Output '=== NEWEST LOG ==='
if ($recent.Success) { Write-Output $recent.Value.TrimEnd() } else { Write-Output '(none)' }
Write-Output "=== CARD $Card ($source) ==="
Write-Output $cardText
Write-Output '=== FILE LINE COUNTS ==='
$filesLine = [regex]::Match($cardText, '(?m)^\*\*Files:\*\*\s*(.+)$')
if (-not $filesLine.Success) {
    Write-Output '(card does not declare files)'
    return
}
$paths = @([regex]::Matches($filesLine.Groups[1].Value, '\x60([^\x60]+)\x60') |
    ForEach-Object { $_.Groups[1].Value })
$paths = @($paths | Select-Object -Unique)
foreach ($relative in $paths) {
    $full = Join-Path $repoRoot $relative
    if ($relative.Contains('*')) {
        Write-Output "$relative : (pattern)"
    }
    elseif (Test-Path -LiteralPath $full -PathType Leaf) {
        Write-Output "$relative : $([IO.File]::ReadAllLines($full).Length) lines"
    }
    elseif (Test-Path -LiteralPath $full -PathType Container) {
        Write-Output "$relative : (directory)"
    }
    else {
        Write-Output "$relative : (new or unspecified path)"
    }
}
