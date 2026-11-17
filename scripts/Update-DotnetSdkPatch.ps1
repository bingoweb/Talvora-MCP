[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $FromVersion,
    [switch] $SkipRestore,
    [string] $DotnetRoot
)
# Moves an installed .NET SDK servicing patch and its bundled runtime as one locked graph.
# Feature-band and major changes are decisions for Claude; this script installs nothing.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$globalPath = Join-Path $repoRoot 'global.json'
$packagesPath = Join-Path $repoRoot 'Directory.Packages.props'
$noticesPath = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
$globalBytes = [IO.File]::ReadAllBytes($globalPath)
$hasBom = $globalBytes.Length -ge 3 -and $globalBytes[0] -eq 0xef -and
    $globalBytes[1] -eq 0xbb -and $globalBytes[2] -eq 0xbf
$globalText = [IO.File]::ReadAllText($globalPath)
$global = $globalText | ConvertFrom-Json
if ($global.sdk.rollForward -cne 'disable') {
    throw 'global.json must keep rollForward: disable.'
}
$oldSdk = [string]$global.sdk.version
if ($oldSdk -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "Invalid pinned SDK version: $oldSdk" }
$fromSdk = if ($FromVersion) { $FromVersion } else { $oldSdk }
if ($fromSdk -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "Invalid pinned SDK version: $fromSdk" }
$major = [int]$Matches[1]
$minor = [int]$Matches[2]
$feature = [int][Math]::Floor([int]$Matches[3] / 100)

if ([string]::IsNullOrWhiteSpace($DotnetRoot)) {
    $dotnetExecutable = (Get-Command -Name dotnet -CommandType Application -ErrorAction Stop).Source
    $DotnetRoot = Split-Path -Parent $dotnetExecutable
}
$sdkRoot = Join-Path $DotnetRoot 'sdk'
if (-not (Test-Path -LiteralPath $sdkRoot -PathType Container)) {
    throw "No installed .NET SDKs under $sdkRoot. Install a matching patch from https://dotnet.microsoft.com/download/dotnet/$major.$minor."
}

$installed = [Collections.Generic.List[object]]::new()
foreach ($folder in @(Get-ChildItem -LiteralPath $sdkRoot -Directory)) {
    if ($folder.Name -notmatch '^(\d+)\.(\d+)\.(\d+)$') { continue }
    $installed.Add([pscustomobject]@{
        Version = $folder.Name
        Major = [int]$Matches[1]
        Minor = [int]$Matches[2]
        Patch = [int]$Matches[3]
        Feature = [int][Math]::Floor([int]$Matches[3] / 100)
    })
}
$inBand = @($installed | Where-Object {
    $_.Major -eq $major -and $_.Minor -eq $minor -and $_.Feature -eq $feature
} | Sort-Object Patch -Descending)
if ($Version) {
    if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "Invalid SDK version: $Version" }
    if ([int]$Matches[1] -ne $major -or [int]$Matches[2] -ne $minor -or
        [int][Math]::Floor([int]$Matches[3] / 100) -ne $feature) {
        throw 'Claude decides feature bands and majors. The SDK patch updater only changes patches in the current band.'
    }
    if (-not @($inBand | Where-Object { $_.Version -ceq $Version }).Count) {
        throw "SDK $Version is not installed. Download it from https://dotnet.microsoft.com/download/dotnet/$major.$minor."
    }
    $chosen = $Version
}
else {
    if ($inBand.Count -eq 0) {
        throw "No installed SDK matches the pinned feature band. Claude decides feature bands and majors. Install a matching patch from https://dotnet.microsoft.com/download/dotnet/$major.$minor."
    }
    $chosen = $inBand[0].Version
}
if ([version]$chosen -lt [version]$fromSdk) {
    throw "SDK $chosen is older than the pinned $fromSdk; the updater never moves the pin back."
}

$bundledPath = Join-Path $sdkRoot (Join-Path $chosen 'Microsoft.NETCoreSdk.BundledVersions.props')
if (-not (Test-Path -LiteralPath $bundledPath -PathType Leaf)) {
    throw "Installed SDK $chosen is missing Microsoft.NETCoreSdk.BundledVersions.props."
}
$bundled = [xml][IO.File]::ReadAllText($bundledPath)
$runtime = [string]$bundled.Project.PropertyGroup.BundledNETCoreAppPackageVersion
if ($runtime -notmatch '^\d+\.\d+\.\d+$') { throw "SDK $chosen has no valid BundledNETCoreAppPackageVersion." }

$packagesText = [IO.File]::ReadAllText($packagesPath)
$platformPattern = '(<PackageVersion Include="(?:Microsoft\.Extensions\.(?!AI\.)[^"]+|Microsoft\.Data\.Sqlite|System\.Composition[^"]*)" Version=")([^"]+)(")'
$platform = [regex]::Matches($packagesText, $platformPattern)
if ($platform.Count -lt 10) { throw 'The platform package family in Directory.Packages.props is incomplete.' }
$oldRuntime = $platform[0].Groups[2].Value

$noticeText = [IO.File]::ReadAllText($noticesPath)
$runtimePacks = @(
    'Microsoft.NETCore.App.Runtime.win-x64',
    'Microsoft.WindowsDesktop.App.Runtime.win-x64',
    'Microsoft.AspNetCore.App.Runtime.win-x64'
)
$noticesAligned = $true
foreach ($pack in $runtimePacks) {
    $pattern = '(?m)^- `' + [regex]::Escape($pack) + ' (\d+\.\d+\.\d+)`(?=\r?$)'
    $found = [regex]::Match($noticeText, $pattern)
    if (-not $found.Success) { throw "THIRD-PARTY-NOTICES.md is missing the runtime pack: $pack." }
    if ($found.Groups[1].Value -cne $runtime) { $noticesAligned = $false }
}
$platformAligned = @($platform | Where-Object { $_.Groups[2].Value -cne $runtime }).Count -eq 0
if ($oldSdk -ceq $chosen -and $platformAligned -and $noticesAligned) {
    if (-not $SkipRestore) {
        $platformUpdater = Join-Path $PSScriptRoot 'Update-DotnetPlatformPackages.ps1'
        & $platformUpdater -Version $runtime
    }
    Write-Output "SDK $chosen is already the newest patch of its band"
    return
}

if ($oldSdk -cne $chosen) {
    $versionPattern = '("version"\s*:\s*")\d+\.\d+\.\d+(")'
    $updated = [regex]::Replace($globalText, $versionPattern, {
        param($m)
        $m.Groups[1].Value + $chosen + $m.Groups[2].Value
    })
    if ($updated -ceq $globalText) { throw 'Cannot update sdk.version in global.json.' }
    [IO.File]::WriteAllText($globalPath, $updated, [Text.UTF8Encoding]::new($hasBom))
}

$platformUpdater = Join-Path $PSScriptRoot 'Update-DotnetPlatformPackages.ps1'
& $platformUpdater -Version $runtime -SkipRestore:$SkipRestore.IsPresent
$noticeUpdater = Join-Path $PSScriptRoot 'Update-ThirdPartyNotices.ps1'
& $noticeUpdater -RuntimeVersion $runtime
Write-Output "SDK $oldSdk -> $chosen, runtime $oldRuntime -> $runtime"
