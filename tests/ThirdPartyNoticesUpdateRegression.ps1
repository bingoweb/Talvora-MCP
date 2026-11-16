# scripts/Update-ThirdPartyNotices.ps1 keeps THIRD-PARTY-NOTICES.md in line with the locked runtime
# packages for Apply-DependabotPr.ps1 (P-56: a "### DiffPlex 1.9.0" heading stopped Roslyn 5.12.0).
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$updater = Join-Path $repoRoot 'scripts\Update-ThirdPartyNotices.ps1'
$work = Join-Path ([IO.Path]::GetTempPath()) ("talvora-notice-update-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$utf8Bom = [Text.UTF8Encoding]::new($true)

function Write-Lock {
    param([string] $Path, [hashtable] $Packages)
    $deps = [ordered]@{}
    foreach ($id in ($Packages.Keys | Sort-Object)) {
        $deps[$id] = [ordered]@{ type = 'Transitive'; resolved = $Packages[$id] }
    }
    $lock = [ordered]@{ version = 2; dependencies = [ordered]@{ 'net10.0-windows10.0.26100' = $deps } }
    [IO.File]::WriteAllText($Path, ($lock | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
}

function Write-Nuspec {
    param([string] $Root, [string] $Id, [string] $Version, [string] $License, [string] $Authors = 'Microsoft')
    $folder = Join-Path $Root (Join-Path $Id.ToLowerInvariant() $Version.ToLowerInvariant())
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $xml = '<?xml version="1.0" encoding="utf-8"?>' + "`n" +
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>' +
        "<id>$Id</id><version>$Version</version><authors>$Authors</authors>" +
        "<license type=`"expression`">$License</license></metadata></package>"
    [IO.File]::WriteAllText((Join-Path $folder ($Id.ToLowerInvariant() + '.nuspec')), $xml, [Text.UTF8Encoding]::new($false))
}

function Invoke-Updater {
    param([string] $Notice, [string[]] $Locks, [string] $PackagesRoot)
    try { & $updater -NoticePath $Notice -LockPath $Locks -PackagesRoot $PackagesRoot | Out-Null; return $null }
    catch { return $_.Exception.Message }
}

$cases = 0
try {
    # 1. The repository's own notice already matches its lock files: nothing changes, nothing stops.
    $repoNotice = Join-Path $work 'repo-notice.md'
    Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $repoNotice
    $before = [IO.File]::ReadAllBytes($repoNotice)
    $error1 = Invoke-Updater $repoNotice @(
        (Join-Path $repoRoot 'src\Talvora\packages.lock.json'),
        (Join-Path $repoRoot 'src\Talvora.Tray\packages.lock.json')) (Join-Path $work 'no-packages')
    if ($error1) { throw "Case 1: the repository's own notice stopped the updater: $error1" }
    if ([Convert]::ToBase64String($before) -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes($repoNotice))) {
        throw 'Case 1: the repository notice changed although every locked version is already listed.'
    }
    $cases++

    # 2. Heading, list and table entries take the locked version in place (a longer id sharing the
    #    prefix keeps its own line); a new MIT package under a Microsoft-reserved prefix joins the
    #    Microsoft list in order. BOM and CRLF stay.
    $lines = @(
        '# Notices', '', '### Alpha.Lib 1.0.0', '', '**License:** MIT', '',
        '### Microsoft .NET, Roslyn, data, hosting, and Windows libraries', '',
        'The current locked package set includes:', '',
        '- `Microsoft.CodeAnalysis.CSharp 1.0.0`',
        '- `Microsoft.CodeAnalysis.CSharp.Workspaces 1.0.0`',
        '- `System.Composition 1.0.0`', '',
        'Runtime packs:', '', '- `Microsoft.NETCore.App.Runtime.win-x64 10.0.12`', '',
        '| Package | Version | Upstream attribution | License |', '| --- | ---: | --- | --- |',
        '| `Tbl.Lib` | 1.0.0 | Someone | MIT |', '')
    $notice = Join-Path $work 'notice.md'
    [IO.File]::WriteAllText($notice, ($lines -join "`r`n"), $utf8Bom)
    $packages = Join-Path $work 'packages'
    Write-Nuspec $packages 'Microsoft.Extensions.NewThing' '4.0.0' 'MIT'
    $lockA = Join-Path $work 'a.lock.json'
    $lockB = Join-Path $work 'b.lock.json'
    Write-Lock $lockA @{ 'Alpha.Lib' = '2.0.0'; 'Microsoft.CodeAnalysis.CSharp' = '1.5.0'; 'Tbl.Lib' = '3.0.0' }
    Write-Lock $lockB @{ 'Microsoft.CodeAnalysis.CSharp.Workspaces' = '1.0.0'; 'System.Composition' = '1.0.0'; 'Microsoft.Extensions.NewThing' = '4.0.0' }
    $error2 = Invoke-Updater $notice @($lockA, $lockB) $packages
    if ($error2) { throw "Case 2: the updater stopped: $error2" }
    $expected = $lines.Clone()
    $expected[2] = '### Alpha.Lib 2.0.0'
    $expected[10] = '- `Microsoft.CodeAnalysis.CSharp 1.5.0`'
    $expected[20] = '| `Tbl.Lib` | 3.0.0 | Someone | MIT |'
    $expected = @($expected[0..11]) + '- `Microsoft.Extensions.NewThing 4.0.0`' + @($expected[12..($expected.Count - 1)])
    $bytes = [IO.File]::ReadAllBytes($notice)
    if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) { throw 'Case 2: the BOM was lost.' }
    $actual = [IO.File]::ReadAllText($notice)
    if ($actual -ne ($expected -join "`r`n")) { throw "Case 2: unexpected notice:`n$actual" }
    $cases++

    # 3. A new package outside the Microsoft prefixes needs a license review: the updater stops,
    #    names every such package and leaves the notice untouched.
    [IO.File]::WriteAllText($notice, ($lines -join "`r`n"), $utf8Bom)
    $before = [IO.File]::ReadAllText($notice)
    Write-Nuspec $packages 'Third.Party' '1.0.0' 'MIT' 'Someone'
    Write-Lock $lockB @{ 'Third.Party' = '1.0.0'; 'Other.Unknown' = '2.0.0'; 'Alpha.Lib' = '2.0.0' }
    $error3 = Invoke-Updater $notice @($lockB) $packages
    if (-not $error3 -or $error3 -notmatch 'Third\.Party 1\.0\.0' -or $error3 -notmatch 'Other\.Unknown 2\.0\.0' -or $error3 -notmatch 'license review') {
        throw "Case 3: expected a license-review stop naming both packages, got: $error3"
    }
    if ([IO.File]::ReadAllText($notice) -ne $before) { throw 'Case 3: the notice changed although the updater stopped.' }
    $cases++

    # 4. A Microsoft-prefixed package whose nuspec is not MIT also needs a review.
    Write-Nuspec $packages 'Microsoft.Odd.Thing' '2.0.0' 'Apache-2.0'
    Write-Lock $lockB @{ 'Microsoft.Odd.Thing' = '2.0.0' }
    $error4 = Invoke-Updater $notice @($lockB) $packages
    if (-not $error4 -or $error4 -notmatch 'Microsoft\.Odd\.Thing 2\.0\.0' -or $error4 -notmatch 'Apache-2\.0') {
        throw "Case 4: expected a stop for the Apache-2.0 Microsoft package, got: $error4"
    }
    $cases++

    # 5. An older package under a Microsoft prefix that Microsoft does not own (nuget.org keeps such
    #    packages) is not attributed to Microsoft: it needs a review as well.
    Write-Nuspec $packages 'Microsoft.Community.Thing' '1.2.0' 'MIT' 'Someone Else'
    Write-Lock $lockB @{ 'Microsoft.Community.Thing' = '1.2.0' }
    $error5 = Invoke-Updater $notice @($lockB) $packages
    if (-not $error5 -or $error5 -notmatch 'Microsoft\.Community\.Thing 1\.2\.0' -or $error5 -notmatch 'Someone Else') {
        throw "Case 5: expected a stop for the Microsoft-prefixed package by another author, got: $error5"
    }
    if ([IO.File]::ReadAllText($notice) -ne $before) { throw 'Case 5: the notice changed although the updater stopped.' }
    $cases++
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Output "THIRD_PARTY_NOTICES_UPDATE_GREEN cases=$cases"
