[CmdletBinding()]
param(
    [string] $Card
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
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
$cardPattern = '(?ms)^#{2,3}[ \t]+' + [regex]::Escape($Card) + '[ \t]+[—–-].*?(?=^#{2,3}[ \t]+|\z)'
$cardMatch = [regex]::Match($roadmap, $cardPattern)
if (-not $cardMatch.Success) { throw "Card $Card could not be found in $source." }
$cardText = $cardMatch.Value.TrimEnd()

$script:ContextLines = [Collections.Generic.List[string]]::new()
function Add-ContextText {
    param([string] $Value)
    foreach ($line in ($Value.TrimEnd() -split '\r?\n')) {
        $script:ContextLines.Add($line)
    }
}
function Get-SpecSection {
    param([string] $Document, [string] $Number)
    $headings = [regex]::Matches($Document,
        '(?m)^(?<level>#{2,6})[ \t]+(?<number>\d+(?:\.\d+)*)(?=[ \t.:—–-])[^\r\n]*')
    for ($i = 0; $i -lt $headings.Count; $i++) {
        $heading = $headings[$i]
        if ($heading.Groups['number'].Value -ne $Number) { continue }
        $level = $heading.Groups['level'].Length
        $end = $Document.Length
        for ($j = $i + 1; $j -lt $headings.Count; $j++) {
            if ($headings[$j].Groups['level'].Length -le $level) {
                $end = $headings[$j].Index
                break
            }
        }
        return $Document.Substring($heading.Index, $end - $heading.Index).TrimEnd()
    }
    return "(Section §$Number is not present in the selected specification.)"
}
function Get-CitedSections {
    param([string] $Text)
    $cited = [Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($Text,
        '§\s*(?<first>\d+(?:\.\d+)*)(?:\s*[—–-]\s*(?<last>\d+(?:\.\d+)*))?')) {
        $first = $match.Groups['first'].Value
        $range = @($first)
        if ($match.Groups['last'].Success) {
            $last = $match.Groups['last'].Value
            $prefix = $first.Substring(0, $first.LastIndexOf('.') + 1)
            if ($prefix -and $last.StartsWith($prefix, [StringComparison]::Ordinal)) {
                $begin = [int]$first.Substring($prefix.Length)
                $finish = [int]$last.Substring($prefix.Length)
                if ($finish -ge $begin -and $finish - $begin -le 20) {
                    $range = @($begin..$finish | ForEach-Object { "$prefix$_" })
                }
            }
        }
        foreach ($number in $range) {
            if (-not $cited.Contains($number)) { $cited.Add($number) }
        }
    }
    foreach ($number in $cited) {
        $containedInParent = @($cited | Where-Object {
            $_ -ne $number -and $number.StartsWith(($_ + '.'), [StringComparison]::Ordinal)
        }).Count -gt 0
        if (-not $containedInParent) { Write-Output $number }
    }
}

Add-ContextText '=== STATUS (without historical Log) ==='
Add-ContextText $statusHeader
Add-ContextText '=== NEWEST LOG ==='
if ($recent.Success) { Add-ContextText $recent.Value.TrimEnd() } else { Add-ContextText '(none)' }
Add-ContextText "=== CARD $Card ($source) ==="
Add-ContextText $cardText

$additionsPath = Join-Path $repoRoot 'docs/codex/QUEUE-ADDITIONS.md'
$pending = if (Test-Path -LiteralPath $additionsPath -PathType Leaf) {
    [regex]::Match([IO.File]::ReadAllText($additionsPath),
        '(?ms)^## Pending[ \t]*\r?\n(?<body>.*?)(?=^## |\z)').Groups['body'].Value.Trim()
}
Add-ContextText '=== PENDING QUEUE ADDITIONS ==='
if ($pending -and $pending -ne '(none)') { Add-ContextText $pending } else { Add-ContextText '(none)' }

$proposalsPath = Join-Path $repoRoot 'docs/codex/PROPOSALS.md'
$openTitles = @()
if (Test-Path -LiteralPath $proposalsPath -PathType Leaf) {
    $openBody = [regex]::Match([IO.File]::ReadAllText($proposalsPath),
        '(?ms)^## Open[ \t]*\r?\n(?<body>.*?)(?=^## |\z)').Groups['body'].Value
    $openTitles = @([regex]::Matches($openBody, '(?m)^### P-\d+[^\r\n]*') |
        ForEach-Object { $_.Value })
}
Add-ContextText '=== OPEN PROPOSALS (titles only) ==='
if ($openTitles.Count) { foreach ($title in $openTitles) { Add-ContextText $title } }
else { Add-ContextText '(none)' }

Add-ContextText '=== FILE LINE COUNTS ==='
$filesLine = [regex]::Match($cardText, '(?m)^\*\*Files:\*\*\s*(.+)$')
if ($filesLine.Success) {
    $paths = @([regex]::Matches($filesLine.Groups[1].Value, '\x60([^\x60]+)\x60') |
        ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    foreach ($relative in $paths) {
        $full = Join-Path $repoRoot $relative
        if ($relative.Contains('*')) { Add-ContextText "$relative : (pattern)" }
        elseif (Test-Path -LiteralPath $full -PathType Leaf) {
            Add-ContextText "$relative : $([IO.File]::ReadAllLines($full).Length) lines"
        }
        elseif (Test-Path -LiteralPath $full -PathType Container) {
            Add-ContextText "$relative : (directory)"
        }
        else { Add-ContextText "$relative : (new or unspecified path)" }
    }
}
else { Add-ContextText '(card does not declare files)' }

if ($Card -match '^(UI|N[0-9])') {
    $sections = [Collections.Generic.List[object]]::new()
    foreach ($number in @(Get-CitedSections $cardText)) {
        $text = Get-SpecSection -Document $roadmap -Number $number
        $sections.Add([pscustomobject]@{
            Number = $number
            Lines = @($text -split '\r?\n')
        })
    }
    if ($sections.Count) {
        Add-ContextText "=== REFERENCED SPEC SECTIONS ($source) ==="
        # Reserve one heading per section and keep the entire report under 250 lines.
        $remaining = [Math]::Max(0, 248 - $script:ContextLines.Count - $sections.Count)
        for ($i = 0; $i -lt $sections.Count; $i++) {
            $section = $sections[$i]
            Add-ContextText "=== §$($section.Number) ==="
            $allowance = [Math]::Max(1,
                [int][Math]::Floor($remaining / ($sections.Count - $i)))
            $truncated = $section.Lines.Count -gt $allowance
            $take = [Math]::Min($section.Lines.Count, $allowance)
            if ($truncated -and $take -gt 1) { $take-- }
            for ($j = 0; $j -lt $take; $j++) {
                Add-ContextText $section.Lines[$j]
            }
            $remaining -= $take
            if ($truncated -and $remaining -gt 0) {
                Add-ContextText "… ($($section.Lines.Count - $take) more lines in $source §$($section.Number))"
                $remaining--
            }
        }
    }
}
$script:ContextLines
