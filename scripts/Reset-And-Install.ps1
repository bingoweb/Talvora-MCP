[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
param(
    [string] $RepoRoot = (Join-Path $env:USERPROFILE 'Talvora-MCP'),
    [switch] $FromTemp,
    [switch] $ConfirmReset,
    [switch] $IncludeUserData,
    [Parameter(DontShow = $true)]
    [string] $TestRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Remove-TalvoraClientTables {
    param([Parameter(Mandatory)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }

    $lines = [IO.File]::ReadAllLines($Path)
    $result = [Collections.Generic.List[string]]::new()
    $skip = $false
    foreach ($line in $lines) {
        if ($line -match '^\s*\[mcp_servers\.talvora[^\]]*\]\s*$') { $skip = $true; continue }
        if ($skip -and $line -match '^\s*\[') { $skip = $false }
        if (-not $skip) { $result.Add($line) }
    }
    [IO.File]::WriteAllLines($Path, $result, [Text.UTF8Encoding]::new($false))
}

function Remove-TalvoraRunValues {
    foreach ($runKey in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'
    )) {
        if (-not (Test-Path -LiteralPath $runKey)) { continue }
        $properties = Get-ItemProperty -LiteralPath $runKey
        foreach ($property in $properties.PSObject.Properties) {
            if ($property.Name -match '^PS') { continue }
            if ($property.Name -match '(?i)talvora' -or [string]$property.Value -match '(?i)talvora') {
                Remove-ItemProperty -LiteralPath $runKey -Name $property.Name -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

function Remove-TalvoraEnvironment {
    foreach ($target in @([EnvironmentVariableTarget]::User, [EnvironmentVariableTarget]::Machine)) {
        $variables = [Environment]::GetEnvironmentVariables($target)
        foreach ($name in @($variables.Keys)) {
            if ([string]$name -match '^(?i)TALVORA') {
                [Environment]::SetEnvironmentVariable([string]$name, $null, $target)
            }
        }

        $path = [Environment]::GetEnvironmentVariable('Path', $target)
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $cleanPath = @($path -split ';' | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '(?i)(^|\\)Talvora(?:-MCP)?(\\|$)'
        }) -join ';'
        if ($cleanPath -cne $path) {
            [Environment]::SetEnvironmentVariable('Path', $cleanPath, $target)
        }
    }
}

function Assert-TemporaryTestRoot {
    param([Parameter(Mandatory)][string] $Path)
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $temp = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\', '/')
    if (-not $full.StartsWith($temp + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Directory]::Exists($full)) {
        throw '-TestRoot yalnızca TEMP içinde önceden oluşturulmuş bir test klasörünü kabul eder.'
    }
    $part = [IO.DirectoryInfo]::new($full)
    while ($part -and $part.FullName.Length -ge $temp.Length) {
        if (($part.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw '-TestRoot yeniden yönlendirme noktası içeremez.'
        }
        if ($part.FullName -ieq $temp) { break }
        $part = $part.Parent
    }
    return $full
}

function Assert-ResetPathWithoutReparse {
    param([Parameter(Mandatory)][string] $Path)
    $part = [IO.DirectoryInfo]::new([IO.Path]::GetFullPath($Path))
    while ($null -ne $part) {
        if ([IO.Directory]::Exists($part.FullName) -and
            ($part.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Sıfırlama yolu yeniden yönlendirme noktası içeriyor: $($part.FullName)"
        }
        $part = $part.Parent
    }
}

# Names are literal files in the four shortcut roots, not wildcard matches.
$shortcutNames = @(
    'Talvora.lnk',
    'Talvora Control Center.lnk',
    'Talvora Tray.lnk',
    'Talvora MCP.lnk'
)

function Test-ResetProtectedItem {
    param([IO.FileSystemInfo] $Item, [bool] $IncludeData)
    # Secrets are never entered, read, copied, archived, or removed.
    if ($Item.Name -ieq 'Secrets') { return $true }
    if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return $true }
    if ($IncludeData) { return $false }
    if ($Item -is [IO.DirectoryInfo] -and
        ($Item.Name -ieq 'Backups' -or $Item.Name -ilike 'Penpot*' -or
         $Item.Name -ieq 'ssh' -or $Item.Name -ieq 'state')) { return $true }
    if ($Item -is [IO.FileInfo] -and
        ($Item.Name -imatch '\.dpapi(?:\.|$)' -or
         $Item.Name -imatch '^(?:state|tunnel-state)(?:\.|-|$)')) { return $true }
    return $false
}

function Get-ResetFilePlan {
    param([object[]] $DataRoots, [object[]] $Shortcuts, [bool] $IncludeData)
    $files = [Collections.Generic.List[object]]::new()
    $directories = [Collections.Generic.List[string]]::new()
    foreach ($source in $DataRoots) {
        if (-not [IO.Directory]::Exists($source.Path)) { continue }
        $rootInfo = [IO.DirectoryInfo]::new($source.Path)
        if (($rootInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
        $stack = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
        $stack.Push($rootInfo)
        while ($stack.Count -gt 0) {
            $directory = $stack.Pop()
            $directories.Add($directory.FullName)
            foreach ($entry in $directory.EnumerateFileSystemInfos()) {
                if (Test-ResetProtectedItem -Item $entry -IncludeData $IncludeData) { continue }
                if ($entry -is [IO.DirectoryInfo]) {
                    $stack.Push($entry)
                }
                else {
                    $relative = $entry.FullName.Substring($source.Path.Length).TrimStart('\', '/')
                    $files.Add([pscustomobject]@{
                        Path = $entry.FullName
                        ArchiveEntry = $source.Label + '/' + $relative.Replace('\', '/')
                    })
                }
            }
        }
    }
    foreach ($shortcutRoot in $Shortcuts) {
        if (-not [IO.Directory]::Exists($shortcutRoot.Path)) { continue }
        foreach ($name in $shortcutNames) {
            $itemPath = Join-Path $shortcutRoot.Path $name
            if (-not [IO.File]::Exists($itemPath)) { continue }
            $item = [IO.FileInfo]::new($itemPath)
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            $files.Add([pscustomobject]@{
                Path = $itemPath
                ArchiveEntry = $shortcutRoot.Label + '/' + $name
            })
        }
    }
    return [pscustomobject]@{ Files = $files; Directories = $directories }
}

function Save-ResetArchive {
    param([string] $BackupRoot, [object[]] $Files)
    # Load both ZIP assemblies explicitly for Windows PowerShell compatibility.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Directory]::CreateDirectory($BackupRoot) | Out-Null
    $name = 'Talvora-reset-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' +
        [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.zip'
    $archivePath = Join-Path $BackupRoot $name
    $archive = [IO.Compression.ZipFile]::Open(
        $archivePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $Files) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, [string]$item.Path, [string]$item.ArchiveEntry,
                [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $archive.Dispose() }
    return $archivePath
}

$testing = -not [string]::IsNullOrWhiteSpace($TestRoot)
if ($testing) {
    $root = Assert-TemporaryTestRoot -Path $TestRoot
    $locations = @{
        ProgramData = Join-Path $root 'ProgramData'
        LocalAppData = Join-Path $root 'LocalAppData'
        AppData = Join-Path $root 'AppData'
        ProgramFiles = Join-Path $root 'ProgramFiles'
        ProgramFilesX86 = Join-Path $root 'ProgramFilesX86'
        Desktop = Join-Path $root 'Desktop'
        CommonDesktop = Join-Path $root 'CommonDesktop'
        Programs = Join-Path $root 'StartMenu'
        CommonPrograms = Join-Path $root 'CommonStartMenu'
    }
}
else {
    $locations = @{
        ProgramData = $env:ProgramData
        LocalAppData = $env:LOCALAPPDATA
        AppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
        ProgramFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
        ProgramFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
        Desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
        CommonDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)
        Programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
        CommonPrograms = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)
    }
}

if (-not $ConfirmReset -and -not $WhatIfPreference) {
    throw 'Sıfırlama için -ConfirmReset gerekir. Önce -WhatIf ile silinecekleri inceleyin.'
}

# Elevate before inspecting the protected ProgramData tree. WhatIf stays read-only.
if (-not $testing -and -not $FromTemp -and -not $WhatIfPreference) {
    $tempScript = Join-Path $env:TEMP ('Talvora-reset-' + [Guid]::NewGuid().ToString('N') + '.ps1')
    Copy-Item -LiteralPath $PSCommandPath -Destination $tempScript -Force
    # PowerShell 7 reads the UTF-8 source correctly after UAC elevation.
    $ps = (Get-Command pwsh.exe -ErrorAction Stop).Source
    $args = @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',
        ('"{0}"' -f $tempScript),'-FromTemp','-ConfirmReset','-RepoRoot',
        ('"{0}"' -f $RepoRoot))
    if ($IncludeUserData) { $args += '-IncludeUserData' }
    try {
        $child = Start-Process $ps -Verb RunAs -Wait -PassThru -WorkingDirectory $env:TEMP -ArgumentList $args
        exit $child.ExitCode
    }
    finally { Remove-Item -LiteralPath $tempScript -Force -ErrorAction SilentlyContinue }
}

$dataRoots = @(
    foreach ($key in @('LocalAppData','AppData','ProgramData','ProgramFiles','ProgramFilesX86')) {
        if (-not [string]::IsNullOrWhiteSpace($locations[$key])) {
            $path = Join-Path $locations[$key] 'Talvora'
            [pscustomobject]@{ Label = $key + '/Talvora'; Path = [IO.Path]::GetFullPath($path) }
        }
    }
)
$shortcutRoots = @(
    foreach ($key in @('Desktop','CommonDesktop','Programs','CommonPrograms')) {
        if (-not [string]::IsNullOrWhiteSpace($locations[$key])) {
            [pscustomobject]@{ Label = $key; Path = [IO.Path]::GetFullPath($locations[$key]) }
        }
    }
)
$backupRoot = Join-Path $locations.ProgramData 'TalvoraBackups'
foreach ($rootPath in @($dataRoots.Path) + @($shortcutRoots.Path) + @($backupRoot)) {
    Assert-ResetPathWithoutReparse -Path $rootPath
}
$plan = Get-ResetFilePlan -DataRoots $dataRoots -Shortcuts $shortcutRoots -IncludeData $IncludeUserData.IsPresent
$description = "Talvora sistem dosyaları: $($plan.Files.Count) dosya; korunan Secrets kapsam dışı; yedek: $backupRoot"
if (-not $PSCmdlet.ShouldProcess($description, 'Yedek al ve Talvora bileşenlerini sıfırla')) {
    return
}

if (-not $testing) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Sıfırlama için yönetici yetkisi gerekir.'
    }
}

# The archive must be fully created before deleting files, services, or registry entries.
$backupFile = Save-ResetArchive -BackupRoot $backupRoot -Files $plan.Files
Write-Host "Sıfırlama öncesi yedek: $backupFile"

if (-not $testing) {
    Set-Location $env:TEMP
    Get-CimInstance Win32_Service | Where-Object {
        $_.Name -like 'Talvora*' -or $_.DisplayName -like '*Talvora*'
    } | ForEach-Object {
        & "$env:SystemRoot\System32\sc.exe" stop $_.Name | Out-Null
        Start-Sleep -Milliseconds 300
        & "$env:SystemRoot\System32\sc.exe" delete $_.Name | Out-Host
    }

    Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object {
        $_.TaskName -like '*Talvora*' -or $_.TaskPath -like '*Talvora*'
    } | ForEach-Object {
        Unregister-ScheduledTask -InputObject $_ -Confirm:$false -ErrorAction SilentlyContinue
    }

    Get-CimInstance Win32_Process | Where-Object {
        $_.ProcessId -ne $PID -and (
            ([string]$_.ExecutablePath -match '(?i)\\Talvora(?:-MCP)?\\') -or
            ([string]$_.CommandLine -match '(?i)Talvora(?:\.Gateway|\.SystemBroker|\.Service|\.dll|\.exe|-MCP)')
        )
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like '*Talvora*' -or $_.DisplayName -like '*Talvora*'
    } | Remove-NetFirewallRule -ErrorAction SilentlyContinue

    Remove-TalvoraRunValues
    Remove-TalvoraEnvironment

    foreach ($key in @(
        'HKCU:\Software\Talvora',
        'HKLM:\Software\Talvora',
        'HKLM:\Software\WOW6432Node\Talvora',
        'HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\Application\Talvora'
    )) {
        if (Test-Path -LiteralPath $key) {
            Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

foreach ($file in $plan.Files) {
    Remove-Item -LiteralPath $file.Path -Force -ErrorAction Stop
}
foreach ($directory in @($plan.Directories | Sort-Object { $_.Length } -Descending)) {
    if (-not [IO.Directory]::Exists($directory)) { continue }
    try { [IO.Directory]::Delete($directory, $false) }
    catch [IO.IOException] {
        # A protected child remains; it and its parent must survive.
    }
}

if ($testing) {
    Write-Host 'Test kipi: gerçek servis, kayıt defteri ve kurulum işlemleri yapılmadı.'
    return
}

$clientHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
Remove-TalvoraClientTables -Path (Join-Path $clientHome 'config.toml')

if (-not (Test-Path -LiteralPath $RepoRoot -PathType Container)) {
    throw "Talvora local repository was not found: $RepoRoot"
}

if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot 'src\Talvora\Talvora.csproj') -PathType Leaf)) {
    throw "Talvora local repository is incomplete: $RepoRoot"
}

Write-Host "Talvora yerel depodan mevcut kurulum yöntemiyle kuruluyor: $RepoRoot"
& (Join-Path $RepoRoot 'scripts\Install.ps1') -RepoRoot $RepoRoot
if (-not $?) { throw 'Talvora kurulumu başarısız oldu.' }