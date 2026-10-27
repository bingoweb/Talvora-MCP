$ErrorActionPreference = 'Stop'
# SDK2: every locked runtime package already named in THIRD-PARTY-NOTICES.md gets its locked version
# (rule of tests/ThirdPartyNoticesSourceRegression.ps1). Attributions and licences stay untouched.
$noticePath = (Resolve-Path 'THIRD-PARTY-NOTICES.md').Path
$bytes = [IO.File]::ReadAllBytes($noticePath)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$notice = [IO.File]::ReadAllText($noticePath)
$changed = 0
foreach ($lockPath in @('src/Talvora/packages.lock.json', 'src/Talvora.Tray/packages.lock.json')) {
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    foreach ($tfm in $lock.dependencies.PSObject.Properties) {
        foreach ($package in $tfm.Value.PSObject.Properties) {
            $resolved = $package.Value.PSObject.Properties['resolved']
            if ($null -eq $resolved -or [string]::IsNullOrWhiteSpace([string]$resolved.Value)) { continue }
            $version = [string]$resolved.Value
            $id = [regex]::Escape($package.Name)
            if ([regex]::IsMatch($notice, $id + '[^\r\n]{0,120}' + [regex]::Escape($version), 'IgnoreCase')) { continue }
            $pattern = '(?im)(' + $id + '(?![\w.])[^\r\n]{0,120}?)\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?'
            if (-not [regex]::IsMatch($notice, $pattern)) {
                throw "THIRD-PARTY-NOTICES.md has no line for $($package.Name); a new package needs a license review (proposal)."
            }
            $notice = [regex]::Replace($notice, $pattern, '${1}' + $version)
            $changed++
        }
    }
}
[IO.File]::WriteAllText($noticePath, $notice, [Text.UTF8Encoding]::new($hasBom))
Write-Output "SDK2_NOTICES_UPDATED $changed"
