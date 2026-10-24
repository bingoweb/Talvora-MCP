$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$script = Join-Path $repoRoot 'scripts/Reset-And-Install.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('Talvora-R3-' + [Guid]::NewGuid().ToString('N'))

function New-FixtureFile {
    param([string]$RelativePath, [string]$Text)
    $path = Join-Path $fixture $RelativePath
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $Text, [Text.UTF8Encoding]::new($false))
    return $path
}
function Assert-Path {
    param([string]$RelativePath, [bool]$Expected)
    $exists = [IO.File]::Exists((Join-Path $fixture $RelativePath)) -or
        [IO.Directory]::Exists((Join-Path $fixture $RelativePath))
    if ($exists -ne $Expected) {
        throw "R3 fixture assertion failed: $RelativePath exists=$exists; expected=$Expected"
    }
}

[IO.Directory]::CreateDirectory($fixture) | Out-Null
try {
    $null = New-FixtureFile 'ProgramData/Talvora/Service/old-service.txt' 'OLD_SERVICE'
    $null = New-FixtureFile 'ProgramData/Talvora/Secrets/private.key' 'NEVER_READ_OR_BACKUP'
    $null = New-FixtureFile 'ProgramData/Talvora/Backups/old-backup.dat' 'USER_BACKUP'
    $null = New-FixtureFile 'ProgramData/Talvora/PenpotSite/project.json' 'PENPOT'
    $null = New-FixtureFile 'ProgramData/Talvora/ssh/id_rsa' 'SSH_PRIVATE'
    $null = New-FixtureFile 'LocalAppData/Talvora/tunnels/state/connection.json' 'TUNNEL_STATE'
    $null = New-FixtureFile 'LocalAppData/Talvora/tunnels/runtime-key.dpapi' 'TUNNEL_DPAPI'
    $null = New-FixtureFile 'LocalAppData/Talvora/cache/cache.txt' 'CACHE'
    $null = New-FixtureFile 'AppData/Talvora/UI/settings.json' 'APP_SETTINGS'
    $null = New-FixtureFile 'ProgramFiles/Talvora/Service/Talvora.exe' 'BIN'
    $null = New-FixtureFile 'Desktop/Talvora.lnk' 'OWN_SHORTCUT'
    $null = New-FixtureFile 'Desktop/Talvora notlarım/benim.txt' 'PERSONAL_NOTES'
    $null = New-FixtureFile 'Desktop/Talvora özel.lnk' 'NOT_OUR_SHORTCUT'
    $null = New-FixtureFile 'StartMenu/Talvora.lnk' 'START_SHORTCUT'
    $null = New-FixtureFile 'ProgramData/Outside/untouched.txt' 'OUTSIDE'

    $outsideRootDenied = $false
    try { & $script -TestRoot $env:USERPROFILE -WhatIf *> $null }
    catch { $outsideRootDenied = $_.Exception.Message -match '-TestRoot' }
    if (-not $outsideRootDenied) { throw '-TestRoot accepted a non-TEMP directory.' }

    & $script -TestRoot $fixture -WhatIf *> $null
    Assert-Path 'ProgramData/Talvora/Service/old-service.txt' $true
    Assert-Path 'LocalAppData/Talvora/cache/cache.txt' $true
    Assert-Path 'Desktop/Talvora.lnk' $true
    Assert-Path 'ProgramData/TalvoraBackups' $false
    $denied = $false
    try {
        & $script -TestRoot $fixture *> $null
    }
    catch { $denied = $true }
    if (-not $denied) { throw 'Reset proceeded without -ConfirmReset.' }
    Assert-Path 'ProgramData/Talvora/Service/old-service.txt' $true

    & $script -TestRoot $fixture -ConfirmReset *> $null
    Assert-Path 'ProgramData/Talvora/Service/old-service.txt' $false
    Assert-Path 'LocalAppData/Talvora/cache/cache.txt' $false
    Assert-Path 'AppData/Talvora/UI/settings.json' $false
    Assert-Path 'ProgramFiles/Talvora/Service/Talvora.exe' $false
    Assert-Path 'Desktop/Talvora.lnk' $false
    Assert-Path 'StartMenu/Talvora.lnk' $false
    Assert-Path 'Desktop/Talvora notlarım/benim.txt' $true
    Assert-Path 'Desktop/Talvora özel.lnk' $true
    Assert-Path 'ProgramData/Outside/untouched.txt' $true
    foreach ($keep in @(
        'ProgramData/Talvora/Secrets/private.key',
        'ProgramData/Talvora/Backups/old-backup.dat',
        'ProgramData/Talvora/PenpotSite/project.json',
        'ProgramData/Talvora/ssh/id_rsa',
        'LocalAppData/Talvora/tunnels/state/connection.json',
        'LocalAppData/Talvora/tunnels/runtime-key.dpapi'
    )) { Assert-Path $keep $true }

    $backups = @(Get-ChildItem -LiteralPath (Join-Path $fixture 'ProgramData/TalvoraBackups') -Filter '*.zip' -File)
    if ($backups.Count -ne 1) { throw "Expected exactly one safe backup zip; got $($backups.Count)." }
    $archive = [IO.Compression.ZipFile]::OpenRead($backups[0].FullName)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName })
        if (-not ($names -match 'old-service.txt') -or
            @($names -match 'Secrets/|private.key|id_rsa|runtime-key.dpapi').Count -ne 0) {
            throw 'Backup did not contain disposed files exclusively; a protected path leaked.'
        }
    }
    finally { $archive.Dispose() }

    & $script -TestRoot $fixture -ConfirmReset -IncludeUserData *> $null
    foreach ($gone in @(
        'ProgramData/Talvora/Backups/old-backup.dat',
        'ProgramData/Talvora/PenpotSite/project.json',
        'ProgramData/Talvora/ssh/id_rsa',
        'LocalAppData/Talvora/tunnels/state/connection.json',
        'LocalAppData/Talvora/tunnels/runtime-key.dpapi'
    )) { Assert-Path $gone $false }
    Assert-Path 'ProgramData/Talvora/Secrets/private.key' $true
    Assert-Path 'Desktop/Talvora notlarım/benim.txt' $true
    $allBackups = @(Get-ChildItem -LiteralPath (Join-Path $fixture 'ProgramData/TalvoraBackups') -Filter '*.zip' -File)
    if ($allBackups.Count -ne 2) { throw 'Opt-in reset did not retain both archives outside reset scope.' }
    foreach ($zip in $allBackups) {
        $entries = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
        try {
            if (@($entries.Entries | Where-Object { $_.FullName -match 'Secrets/|private.key' }).Count -ne 0) {
                throw 'Secrets appeared in a backup archive.'
            }
        }
        finally { $entries.Dispose() }
    }
    'R3_SAFE_RESET_GREEN'
}
finally {
    if ([IO.Directory]::Exists($fixture)) {
        [IO.Directory]::Delete($fixture, $true)
    }
}
