# D107: an SDK patch updates the pinned SDK, platform packages and runtime notices together.
# Every case uses a temporary repository, not this repository's tracked files.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('talvora-sdk-patch-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $work 'fixture'
$fakeDotnet = Join-Path $work 'dotnet'
$utf8 = [Text.UTF8Encoding]::new($false)
$utf8Bom = [Text.UTF8Encoding]::new($true)
$platformIds = @(
    'Microsoft.Data.Sqlite',
    'Microsoft.Extensions.Caching.Abstractions',
    'Microsoft.Extensions.Configuration.Abstractions',
    'Microsoft.Extensions.DependencyInjection.Abstractions',
    'Microsoft.Extensions.Diagnostics.Abstractions',
    'Microsoft.Extensions.FileProviders.Abstractions',
    'Microsoft.Extensions.Hosting.Abstractions',
    'Microsoft.Extensions.Hosting.WindowsServices',
    'Microsoft.Extensions.Logging.Abstractions',
    'Microsoft.Extensions.Options',
    'Microsoft.Extensions.Primitives',
    'System.Composition'
)
$runtimePacks = @(
    'Microsoft.NETCore.App.Runtime.win-x64',
    'Microsoft.WindowsDesktop.App.Runtime.win-x64',
    'Microsoft.AspNetCore.App.Runtime.win-x64'
)

function Assert-Case {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Write-LockFixture {
    param([string] $Path)
    $lock = [ordered]@{
        version = 2
        dependencies = [ordered]@{
            'net10.0-windows10.0.26100' = [ordered]@{
                'Fixture.Known' = [ordered]@{ type = 'Transitive'; resolved = '1.0.0' }
            }
        }
    }
    [IO.File]::WriteAllText($Path, ($lock | ConvertTo-Json -Depth 6), $utf8)
}

function Reset-Fixture {
    param([string] $SdkVersion = '10.0.404', [string] $RuntimeVersion = '10.0.15')
    New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'scripts') | Out-Null
    foreach ($file in @('Update-DotnetSdkPatch.ps1', 'Update-DotnetPlatformPackages.ps1', 'Update-ThirdPartyNotices.ps1')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot ('scripts\' + $file)) -Destination (Join-Path $fixture ('scripts\' + $file)) -Force
    }
    $globalText = @(
        '{',
        '  "sdk": {',
        ('    "version": "{0}",' -f $SdkVersion),
        '    "rollForward": "disable",',
        '    "allowPrerelease": false',
        '  }',
        '}',
        ''
    ) -join "`r`n"
    [IO.File]::WriteAllText((Join-Path $fixture 'global.json'), $globalText, $utf8Bom)
    $versions = @($platformIds | ForEach-Object { '    <PackageVersion Include="' + $_ + '" Version="' + $RuntimeVersion + '" />' })
    $packages = (@('<Project>', '  <ItemGroup>') + $versions + @('  </ItemGroup>', '</Project>', '')) -join "`r`n"
    [IO.File]::WriteAllText((Join-Path $fixture 'Directory.Packages.props'), $packages, $utf8Bom)
    $noticeLines = @(
        '# Fixture notices', '',
        '### Microsoft .NET, Roslyn, data, hosting, and Windows libraries', '',
        'The current locked package set includes:', '',
        '- `Fixture.Known 1.0.0`', '',
        'Runtime packs:', ''
    ) + @($runtimePacks | ForEach-Object { '- `' + $_ + ' ' + $RuntimeVersion + '`' }) + @('')
    [IO.File]::WriteAllText((Join-Path $fixture 'THIRD-PARTY-NOTICES.md'), ($noticeLines -join "`r`n"), $utf8Bom)
    foreach ($relative in @('src\Talvora\packages.lock.json', 'src\Talvora.Tray\packages.lock.json')) {
        $path = Join-Path $fixture $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
        Write-LockFixture $path
    }
}

function Assert-Alignment {
    param([string] $SdkVersion, [string] $RuntimeVersion)
    $globalPath = Join-Path $fixture 'global.json'
    $global = [IO.File]::ReadAllText($globalPath) | ConvertFrom-Json
    Assert-Case ($global.sdk.version -ceq $SdkVersion) "Wrong sdk.version: $($global.sdk.version)"
    Assert-Case ($global.sdk.rollForward -ceq 'disable') 'rollForward changed.'
    $bytes = [IO.File]::ReadAllBytes($globalPath)
    Assert-Case ($bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and $bytes[2] -eq 0xbf) 'global.json BOM lost.'
    Assert-Case ([IO.File]::ReadAllText($globalPath).Contains("`r`n")) 'global.json CRLF lost.'
    $props = [IO.File]::ReadAllText((Join-Path $fixture 'Directory.Packages.props'))
    foreach ($id in $platformIds) {
        Assert-Case ($props.Contains('<PackageVersion Include="' + $id + '" Version="' + $RuntimeVersion + '" />')) "Platform package wrong: $id"
    }
    $notices = [IO.File]::ReadAllText((Join-Path $fixture 'THIRD-PARTY-NOTICES.md'))
    foreach ($name in $runtimePacks) {
        Assert-Case ($notices.Contains('- `' + $name + ' ' + $RuntimeVersion + '`')) "Runtime pack wrong: $name"
    }
}

function Assert-Refused {
    param([string] $Version, [string] $ExpectedMessage)
    Reset-Fixture
    $errorText = ''
    try {
        & (Join-Path $fixture 'scripts\Update-DotnetSdkPatch.ps1') -Version $Version -SkipRestore -DotnetRoot $fakeDotnet | Out-Null
    }
    catch { $errorText = $_.Exception.Message }
    Assert-Case ($errorText -like ('*' + $ExpectedMessage + '*')) "Expected refusal for $Version naming $ExpectedMessage; got: $errorText"
    Assert-Alignment '10.0.404' '10.0.15'
}

function Get-FixtureSignature {
    @(
        Get-ChildItem -LiteralPath $fixture -File -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                $_.FullName.Substring($fixture.Length) + ':' +
                    [Convert]::ToBase64String([IO.File]::ReadAllBytes($_.FullName))
            }
    ) -join '|'
}

New-Item -ItemType Directory -Force -Path $work | Out-Null
$cases = 0
try {
    foreach ($entry in @(
        @{ Version = '10.0.404'; Runtime = '10.0.15' },
        @{ Version = '10.0.405'; Runtime = '10.0.16' },
        @{ Version = '10.0.500'; Runtime = '10.0.20' },
        @{ Version = '11.0.100'; Runtime = '11.0.0' }
    )) {
        $sdkFolder = Join-Path $fakeDotnet ('sdk\' + $entry.Version)
        New-Item -ItemType Directory -Force -Path $sdkFolder | Out-Null
        $xml = '<Project><PropertyGroup><BundledNETCoreAppPackageVersion>' + $entry.Runtime +
            '</BundledNETCoreAppPackageVersion></PropertyGroup></Project>'
        [IO.File]::WriteAllText((Join-Path $sdkFolder 'Microsoft.NETCoreSdk.BundledVersions.props'), $xml, $utf8)
    }

    # (a, b): implicit selection stays within 10.0.4xx and takes the bundled runtime.
    Reset-Fixture
    $output = @(& (Join-Path $fixture 'scripts\Update-DotnetSdkPatch.ps1') -SkipRestore -DotnetRoot $fakeDotnet)
    Assert-Alignment '10.0.405' '10.0.16'
    Assert-Case (($output -join ' ') -match 'SDK 10\.0\.404 -> 10\.0\.405, runtime 10\.0\.15 -> 10\.0\.16') 'Upgrade receipt is missing.'
    $cases += 2

    # (c): a different feature band or major is Claude's decision, never auto-applied.
    Assert-Refused '10.0.500' 'Claude decides feature bands and majors'
    Assert-Refused '11.0.100' 'Claude decides feature bands and majors'
    $cases++

    # (d): a requested SDK patch must actually be installed.
    Assert-Refused '10.0.406' 'https://dotnet.microsoft.com/download/dotnet/10.0'
    $cases++

    # (e): an already pinned SDK still reconciles package family and notices, then does nothing.
    Reset-Fixture -SdkVersion '10.0.405'
    $reconciled = @(& (Join-Path $fixture 'scripts\Update-DotnetSdkPatch.ps1') -SkipRestore -DotnetRoot $fakeDotnet)
    Assert-Alignment '10.0.405' '10.0.16'
    Assert-Case (($reconciled -join ' ') -match 'SDK 10\.0\.405 -> 10\.0\.405, runtime 10\.0\.15 -> 10\.0\.16') 'Already-pinned SDK did not reconcile runtime.'
    $paths = @('global.json', 'Directory.Packages.props', 'THIRD-PARTY-NOTICES.md')
    $before = @($paths | ForEach-Object { [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $fixture $_))) })
    $again = @(& (Join-Path $fixture 'scripts\Update-DotnetSdkPatch.ps1') -SkipRestore -DotnetRoot $fakeDotnet)
    $after = @($paths | ForEach-Object { [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $fixture $_))) })
    Assert-Case (($before -join '|') -ceq ($after -join '|')) 'Idempotent run changed fixture bytes.'
    Assert-Case (($again -join ' ') -match 'SDK 10\.0\.405 is already the newest patch of its band') 'Idempotent receipt missing.'
    $cases++
    # D110 (a, b): an installed older patch can never downgrade the current pin.
    $olderDotnet = Join-Path $work 'older-dotnet'
    $olderSdkPath = Join-Path $olderDotnet 'sdk\10.0.404'
    New-Item -ItemType Directory -Force -Path $olderSdkPath | Out-Null
    Copy-Item -LiteralPath (Join-Path $fakeDotnet 'sdk\10.0.404\Microsoft.NETCoreSdk.BundledVersions.props') -Destination (Join-Path $olderSdkPath 'Microsoft.NETCoreSdk.BundledVersions.props')
    foreach ($explicit in @($false, $true)) {
        Reset-Fixture -SdkVersion '10.0.405' -RuntimeVersion '10.0.16'
        $argsForSdk = @{ SkipRestore = $true; DotnetRoot = $olderDotnet }
        if ($explicit) { $argsForSdk.Version = '10.0.404' }
        $beforeSignature = Get-FixtureSignature
        $errorText = ''
        try { & (Join-Path $fixture 'scripts\Update-DotnetSdkPatch.ps1') @argsForSdk | Out-Null }
        catch { $errorText = $_.Exception.Message }
        Assert-Case ($errorText -ceq 'SDK 10.0.404 is older than the pinned 10.0.405; the updater never moves the pin back.') "Downgrade was not refused: $errorText"
        Assert-Case ((Get-FixtureSignature) -ceq $beforeSignature) 'Rejected downgrade changed fixture files.'
        Assert-Alignment '10.0.405' '10.0.16'
        $cases++
    }

    # D110 (c): -FromVersion is the original pin, even after a proposed diff changed global.json.
    Reset-Fixture -SdkVersion '10.0.500' -RuntimeVersion '10.0.20'
    $beforeSignature = Get-FixtureSignature
    $errorText = ''
    try {
        & (Join-Path $fixture 'scripts\Update-DotnetSdkPatch.ps1') -FromVersion '10.0.404' -Version '10.0.500' -SkipRestore -DotnetRoot $fakeDotnet | Out-Null
    }
    catch { $errorText = $_.Exception.Message }
    Assert-Case ($errorText -like '*Claude decides feature bands and majors.*') "FromVersion did not refuse a new feature band: $errorText"
    Assert-Case ((Get-FixtureSignature) -ceq $beforeSignature) 'Rejected band change modified fixture files.'
    Assert-Alignment '10.0.500' '10.0.20'
    $cases++

    # D110 (d): aligned pins only skip the updater when -SkipRestore is present.
    Reset-Fixture -SdkVersion '10.0.405' -RuntimeVersion '10.0.16'
    $markerPath = Join-Path $fixture 'platform-update-called.txt'
    $stub = @'
[CmdletBinding()]
param([string] $Version, [switch] $SkipRestore)
$marker = Join-Path (Split-Path -Parent $PSScriptRoot) 'platform-update-called.txt'
[IO.File]::WriteAllText($marker, $Version)
'@
    [IO.File]::WriteAllText((Join-Path $fixture 'scripts\Update-DotnetPlatformPackages.ps1'), $stub, $utf8)
    $skipped = @(& (Join-Path $fixture 'scripts\Update-DotnetSdkPatch.ps1') -SkipRestore -DotnetRoot $fakeDotnet)
    Assert-Case (-not (Test-Path -LiteralPath $markerPath)) 'SkipRestore must return before platform update.'
    Assert-Case (($skipped -join ' ') -match 'SDK 10\.0\.405 is already the newest patch of its band') 'SkipRestore aligned receipt missing.'
    $normal = @(& (Join-Path $fixture 'scripts\Update-DotnetSdkPatch.ps1') -DotnetRoot $fakeDotnet)
    Assert-Case ((Test-Path -LiteralPath $markerPath) -and ([IO.File]::ReadAllText($markerPath) -ceq '10.0.16')) 'Aligned normal run must run platform updater to refresh locks.'
    Assert-Case (($normal -join ' ') -match 'SDK 10\.0\.405 is already the newest patch of its band') 'Aligned normal receipt missing.'
    Assert-Alignment '10.0.405' '10.0.16'
    $cases++
    Write-Output "DOTNET_SDK_PATCH_GREEN cases=$cases"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

