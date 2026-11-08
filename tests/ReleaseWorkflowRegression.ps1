$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root '.github/workflows/release.yml'
if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw 'RL4: release.yml is missing; tag releases have no workflow.'
}
$release = [IO.File]::ReadAllText($path).Replace([Environment]::NewLine, [string][char]10)
$windowsCi = [IO.File]::ReadAllText(
    (Join-Path $root '.github/workflows/windows-ci.yml')).Replace([Environment]::NewLine, [string][char]10)

function Assert-Pattern([string] $Pattern, [string] $Description) {
    if (-not [regex]::IsMatch(
            $release,
            $Pattern,
            [Text.RegularExpressions.RegexOptions]::Multiline -bor
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw "RL4: $Description"
    }
}

Assert-Pattern "(?s)on:\n\s+push:\n\s+tags:\s*\[\s*'v0\.0\.\*\.\*'\s*\]" 'v0.0.*.* tag trigger is missing.'
Assert-Pattern "workflow_dispatch:\s*\n\s+inputs:" 'Manual dispatch is missing.'
Assert-Pattern "version:\s*\n(?:.*\n){0,5}\s+type:\s+string" 'Manual version must be a string.'
Assert-Pattern "dry_run:\s*\n(?:.*\n){0,5}\s+type:\s+boolean" 'Manual dry_run must be boolean.'
Assert-Pattern 'dry_run:[\s\S]{0,180}default:\s*true' 'Manual dispatch must default to dry run.'
Assert-Pattern '(?s)permissions:\s*\n\s+contents:\s*write\s*\n\s+actions:\s*read' 'Minimal release permissions must be declared.'
Assert-Pattern 'concurrency:\s*\n\s+group: release-' 'Per-tag release concurrency is required.'
Assert-Pattern 'runs-on:\s*windows-latest' 'The release must run on Windows.'
Assert-Pattern 'fetch-depth:\s*0' 'Full checkout history is needed to verify main ancestry.'
Assert-Pattern '\^v0\\\.0\\\.\\d\{1,5\}\\\.\\d\{1,5\}\$' 'Tag-name validation must use ^v0\.0\.\d{1,5}\.\d{1,5}$.'
Assert-Pattern '\^0\\\.0\\\.\\d\{1,5\}\\\.\\d\{1,5\}\$' 'The selected version must have 0.0.x.x format.'
Assert-Pattern 'git\s+fetch\s+origin\s+main' 'The workflow must fetch main.'
Assert-Pattern 'git\s+merge-base\s+--is-ancestor' 'The released commit must be an ancestor of origin/main.'
Assert-Pattern 'origin/main' 'The ancestry check must be against origin/main.'
Assert-Pattern 'gh\s+run\s+list\s+--workflow\s+windows-ci\.yml\s+--commit' 'The workflow must inspect windows-ci on this commit.'
Assert-Pattern "conclusion[\s\S]{0,700}success" 'A successful CI conclusion is required.'
Assert-Pattern 'GH_TOKEN:' 'GitHub CLI must receive the Actions token.'
Assert-Pattern 'docs/releases/' 'Release notes must be present before publishing.'
Assert-Pattern 'Sürüm notu yok:' 'Missing release notes must fail in Turkish.'
Assert-Pattern 'Build-Windows-Installer\.ps1\s+-ReleaseVersion' 'The canonical installer must receive its release version.'
Assert-Pattern 'SHA256SUMS\.txt' 'The release must contain a SHA256SUMS file.'
Assert-Pattern 'Get-FileHash[\s\S]{0,130}SHA256' 'Checksum must be SHA-256 of the generated installer.'
Assert-Pattern 'ToLowerInvariant\(\)' 'Release checksum must be lowercase.'
Assert-Pattern 'gh\s+release\s+create' 'The release command is missing.'
Assert-Pattern '--title' 'The release must name the version.'
Assert-Pattern '--notes-file' 'The release must use committed release notes.'
Assert-Pattern '--latest' 'The release must be marked latest.'
Assert-Pattern 'if:\s+steps\.release\.outputs\.dry_run\s*!=\s*''true''' 'Real publishing must be skipped in dry runs.'
Assert-Pattern 'github\.event_name.*workflow_dispatch' 'Manual dispatch must have an explicit dry-run safety gate.'

foreach ($pin in @(
    'actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1',
    'actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73 # v6.1.0'
)) {
    if (-not $windowsCi.Contains($pin) -or -not $release.Contains($pin)) {
        throw "RL4: release and CI must use the same pinned action: $pin"
    }
}

if ([regex]::Matches($release, 'gh\s+release\s+create').Count -ne 1) {
    throw 'RL4: exactly one release command must exist.'
}
if ([regex]::Matches($release, 'SHA256SUMS\.txt').Count -lt 2) {
    throw 'RL4: checksum file must be built and uploaded.'
}
Write-Output 'RELEASE_WORKFLOW_GREEN'
