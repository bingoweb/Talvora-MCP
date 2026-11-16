[CmdletBinding()]
param(
    [string] $NoticePath,
    [string[]] $LockPath,
    [string] $PackagesRoot
)
# Brings THIRD-PARTY-NOTICES.md in line with the locked runtime packages, the inventory that
# tests/ThirdPartyNoticesSourceRegression.ps1 checks. Apply-DependabotPr.ps1 (DEP1) calls it.
# A listed package takes its locked version in place, in each of the notice's three formats:
# a list line "- `Id 1.2.3`", a table row "| `Id` | 1.2.3 |" or a heading "### Id 1.2.3" (P-56:
# the DiffPlex heading stopped every Dependabot update). A package that is not listed yet joins
# the Microsoft list by itself only when its id is under a prefix nuget.org reserves for Microsoft
# (Microsoft.*, System.*), its nuspec names Microsoft as author (older packages under a reserved
# prefix may belong to someone else) and declares the MIT license expression. Any other new
# package needs a license review: the script stops before writing and names all of them.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $NoticePath) { $NoticePath = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md' }
if (-not $LockPath) {
    $LockPath = @('src\Talvora\packages.lock.json', 'src\Talvora.Tray\packages.lock.json') | ForEach-Object { Join-Path $repoRoot $_ }
}
$microsoftHeading = '### Microsoft .NET, Roslyn, data, hosting, and Windows libraries'

# Same inventory as the regression test: every package with a resolved version, one entry per id.
$locked = [ordered]@{}
foreach ($lockFile in $LockPath) {
    $lock = Get-Content -LiteralPath $lockFile -Raw | ConvertFrom-Json
    foreach ($tfm in $lock.dependencies.PSObject.Properties) {
        foreach ($package in $tfm.Value.PSObject.Properties) {
            $resolved = $package.Value.PSObject.Properties['resolved']
            if ($null -eq $resolved -or [string]::IsNullOrWhiteSpace([string]$resolved.Value)) { continue }
            $locked[$package.Name.ToLowerInvariant()] = [pscustomobject]@{ Id = $package.Name; Version = [string]$resolved.Value }
        }
    }
}

function Get-NuspecFacts {
    param([string] $Id, [string] $Version)
    $facts = [pscustomobject]@{ License = 'unknown'; Authors = 'unknown' }
    if (-not $script:PackagesRoot) {
        $line = @(& dotnet nuget locals global-packages --list) | Where-Object { $_ -match '^global-packages:\s*(.+)$' } | Select-Object -First 1
        if (-not $line) { $facts.License = 'unknown (NuGet packages folder not found)'; return $facts }
        $script:PackagesRoot = ([regex]::Match($line, '^global-packages:\s*(.+)$').Groups[1].Value).Trim()
    }
    $lowerId = $Id.ToLowerInvariant()
    $nuspec = Join-Path $script:PackagesRoot (Join-Path $lowerId (Join-Path $Version.ToLowerInvariant() ($lowerId + '.nuspec')))
    if (-not (Test-Path -LiteralPath $nuspec -PathType Leaf)) { $facts.License = 'unknown (no nuspec in the NuGet cache)'; return $facts }
    $document = [xml]::new()
    $document.Load($nuspec)
    $metadata = "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='{0}']"
    $authors = $document.SelectSingleNode(($metadata -f 'authors'))
    if ($null -ne $authors) { $facts.Authors = $authors.InnerText.Trim() }
    $license = $document.SelectSingleNode(($metadata -f 'license'))
    if ($null -eq $license) { $facts.License = 'unknown (no license element)' }
    elseif ($license.GetAttribute('type') -ne 'expression') { $facts.License = "unknown (license type '$($license.GetAttribute('type'))')" }
    else { $facts.License = $license.InnerText.Trim() }
    return $facts
}

$noticeBytes = [IO.File]::ReadAllBytes($NoticePath)
$hasBom = $noticeBytes.Length -ge 3 -and $noticeBytes[0] -eq 0xEF -and $noticeBytes[1] -eq 0xBB -and $noticeBytes[2] -eq 0xBF
$notice = [IO.File]::ReadAllText($NoticePath)
$newline = if ($notice.Contains("`r`n")) { "`r`n" } else { "`n" }
$updated = $notice
$additions = [Collections.Generic.List[object]]::new()
$review = [Collections.Generic.List[string]]::new()
foreach ($package in $locked.Values) {
    $id = [regex]::Escape($package.Id)
    $patterns = @(
        ('(?im)^(- `' + $id + ' )[^`\r\n]+(`)'),
        ('(?im)^(\| `' + $id + '` \| )[^|\r\n]+?( \|)'),
        ('(?im)^(#{2,6} ' + $id + ' )[0-9][^\s]*()'))
    $found = $false
    foreach ($pattern in $patterns) {
        if ([regex]::IsMatch($updated, $pattern)) {
            $found = $true
            $replaced = [regex]::Replace($updated, $pattern, '${1}' + $package.Version + '${2}')
            if ($replaced -ne $updated) { Write-Output "THIRD-PARTY-NOTICES.md: $($package.Id) -> $($package.Version)" }
            $updated = $replaced
        }
    }
    if ($found) { continue }
    $facts = Get-NuspecFacts -Id $package.Id -Version $package.Version
    $byMicrosoft = @($facts.Authors -split ',' | ForEach-Object { $_.Trim() }) -contains 'Microsoft'
    if ($package.Id -match '^(Microsoft|System)\.' -and $facts.License -ceq 'MIT' -and $byMicrosoft) { $additions.Add($package) }
    else { $review.Add("$($package.Id) $($package.Version) (license: $($facts.License), authors: $($facts.Authors))") }
}
if ($review.Count -gt 0) {
    throw ("THIRD-PARTY-NOTICES.md has no line for " + ($review -join ', ') +
        "; a new package that is not Microsoft's own MIT package needs a license review (Findings for Claude).")
}

if ($additions.Count -gt 0) {
    $lines = [Collections.Generic.List[string]]::new([string[]]($updated -split '\r?\n', 0))
    $heading = $lines.IndexOf($microsoftHeading)
    if ($heading -lt 0) { throw "THIRD-PARTY-NOTICES.md has no '$microsoftHeading' section for new Microsoft packages." }
    $first = -1
    for ($i = $heading + 1; $i -lt $lines.Count -and -not $lines[$i].StartsWith('#'); $i++) {
        if ($lines[$i].StartsWith('- `')) { $first = $i; break }
    }
    if ($first -lt 0) { throw "The '$microsoftHeading' section of THIRD-PARTY-NOTICES.md has no package list." }
    foreach ($package in ($additions | Sort-Object Id)) {
        $at = $first
        while ($at -lt $lines.Count -and $lines[$at].StartsWith('- `') -and
            [string]::Compare(([regex]::Match($lines[$at], '^- `(\S+)').Groups[1].Value), $package.Id, [StringComparison]::OrdinalIgnoreCase) -lt 0) { $at++ }
        $lines.Insert($at, "- ``$($package.Id) $($package.Version)``")
        Write-Output "THIRD-PARTY-NOTICES.md: added $($package.Id) $($package.Version) (Microsoft, MIT)"
    }
    $updated = $lines -join $newline
}
if ($updated -ne $notice) { [IO.File]::WriteAllText($NoticePath, $updated, [Text.UTF8Encoding]::new($hasBom)) }
