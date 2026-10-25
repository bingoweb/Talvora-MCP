[CmdletBinding()]
param([string] $Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = [IO.Path]::GetFullPath($Root)
$rootMarkdown = @(Get-ChildItem -LiteralPath $Root -File -Filter '*.md')
$rootBytes = ($rootMarkdown | Measure-Object -Property Length -Sum).Sum
if ($rootBytes -ge 120000) { throw "Root Markdown budget exceeded: $rootBytes bytes, expected <120000." }
$handoff = Join-Path $Root 'HANDOFF.md'
if ([IO.File]::ReadAllLines($handoff).Count -gt 150) { throw 'HANDOFF.md exceeds 150 lines.' }

# Frozen archives keep their original bytes and historical relative-link context.
$documents = @($rootMarkdown) + @(Get-ChildItem -LiteralPath (Join-Path $Root 'docs') -File -Filter '*.md' -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]docs[\\/]archive[\\/]' })
$fence = '(?ms)^[ \t]*(?:' + [char]96 + '{3,}|~{3,})[^\r\n]*\r?\n.*?^[ \t]*(?:' + [char]96 + '{3,}|~{3,})[ \t]*\r?$'
$patterns = @(
    '!?\[[^\]\r\n]*\]\(\s*(?<target><[^>]+>|[^\s)]+)(?:\s+"[^"]*")?\s*\)',
    '(?mi)(?:href|src)\s*=\s*["''](?<target>[^"'']+)["'']',
    '(?m)^[ \t]{0,3}\[[^\]\r\n]+\]:\s*(?<target><[^>]+>|\S+)'
)
$broken = [Collections.Generic.List[string]]::new()
$checked = 0
foreach ($document in $documents) {
    $text = [regex]::Replace([IO.File]::ReadAllText($document.FullName), $fence, '')
    foreach ($pattern in $patterns) {
        foreach ($link in [regex]::Matches($text, $pattern)) {
            $target = $link.Groups['target'].Value.Trim([char[]]'<>')
            if ($target -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|#)' -or -not $target) { continue }
            $targetPath = [Uri]::UnescapeDataString(($target -split '[?#]', 2)[0])
            if (-not $targetPath) { continue }
            $base = if ($targetPath.StartsWith('/')) { $Root } else { $document.DirectoryName }
            $resolved = [IO.Path]::GetFullPath((Join-Path $base $targetPath.TrimStart('/')))
            $checked++
            if (-not (Test-Path -LiteralPath $resolved)) {
                $broken.Add(([IO.Path]::GetRelativePath($Root, $document.FullName) + ' -> ' + $target))
            }
        }
    }
}
if ($broken.Count -gt 0) { throw ('Broken local documentation links:' + [Environment]::NewLine + ($broken -join [Environment]::NewLine)) }
Write-Host ("Documentation: {0} root bytes, {1} files, {2} local links." -f $rootBytes, $documents.Count, $checked)
Write-Host 'DOCUMENTATION_LINKS_GREEN'
