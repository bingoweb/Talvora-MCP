param(
    [string] $WorkingRoot = (Join-Path $env:TEMP ('TalvoraDependencyProvenanceRegression-' + [Guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Get-PackageIdentities {
    param(
        [Parameter(Mandatory = $true)]
        [string] $JsonPath
    )

    $document = Get-Content -LiteralPath $JsonPath -Raw | ConvertFrom-Json
    return @(
        $document.libraries.PSObject.Properties |
            Where-Object { [string]$_.Value.type -eq 'package' } |
            ForEach-Object { [string]$_.Name } |
            Sort-Object
    )
}

$feedRoot = Join-Path $WorkingRoot 'feed'
$packageRoot = Join-Path $WorkingRoot 'package'
$consumerRoot = Join-Path $WorkingRoot 'consumer'
$publishRoot = Join-Path $WorkingRoot 'publish'
$packagesRoot = Join-Path $WorkingRoot 'packages'
$isolatedArtifactsRoot = Join-Path $WorkingRoot 'isolated-artifacts'
$immutableRoot = Join-Path $WorkingRoot 'immutable'

try {
    New-Item -ItemType Directory -Path $feedRoot,$packageRoot,$consumerRoot,$publishRoot,$packagesRoot,$isolatedArtifactsRoot,$immutableRoot -Force | Out-Null

    $packageProject = Join-Path $packageRoot 'Audit.Dep.csproj'
    [IO.File]::WriteAllText(
        $packageProject,
        @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>Audit.Dep</PackageId>
    <Authors>Talvora Regression</Authors>
    <Description>Dependency provenance regression fixture.</Description>
  </PropertyGroup>
</Project>
'@,
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $packageRoot 'Fixture.cs'),
        'namespace Audit.Dep; public static class Fixture { public static string Value => "ok"; }',
        [Text.UTF8Encoding]::new($false))

    Invoke-DotNet -Arguments @('restore',$packageProject,'--ignore-failed-sources')
    Invoke-DotNet -Arguments @('pack',$packageProject,'-c','Release','--no-restore','-o',$feedRoot,'-p:PackageVersion=1.0.0')

    $escapedFeed = [Security.SecurityElement]::Escape($feedRoot)
    $nugetConfig = Join-Path $consumerRoot 'NuGet.config'
    [IO.File]::WriteAllText(
        $nugetConfig,
        @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedFeed" />
  </packageSources>
</configuration>
"@,
        [Text.UTF8Encoding]::new($false))

    $consumerProject = Join-Path $consumerRoot 'Consumer.csproj'
    [IO.File]::WriteAllText(
        $consumerProject,
        @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Audit.Dep" Version="*" />
  </ItemGroup>
</Project>
'@,
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $consumerRoot 'Program.cs'),
        'System.Console.WriteLine(Audit.Dep.Fixture.Value);',
        [Text.UTF8Encoding]::new($false))

    Invoke-DotNet -Arguments @(
        'restore',$consumerProject,
        '--configfile',$nugetConfig,
        '--packages',$packagesRoot,
        '--artifacts-path',$isolatedArtifactsRoot
    )

    $isolatedAssets = Join-Path $isolatedArtifactsRoot 'obj\Consumer\project.assets.json'
    $immutableAssets = Join-Path $immutableRoot 'Consumer.project.assets.json'
    Copy-Item -LiteralPath $isolatedAssets -Destination $immutableAssets -Force
    $immutableHashBefore = (Get-FileHash -LiteralPath $immutableAssets -Algorithm SHA256).Hash

    # Simulate an unrelated restore racing the canonical build after its isolated restore.
    Invoke-DotNet -Arguments @('pack',$packageProject,'-c','Release','--no-restore','-o',$feedRoot,'-p:PackageVersion=2.0.0')
    Invoke-DotNet -Arguments @(
        'restore',$consumerProject,
        '--configfile',$nugetConfig,
        '--packages',$packagesRoot,
        '--force-evaluate'
    )

    $liveAssets = Join-Path $consumerRoot 'obj\project.assets.json'
    $liveAfter = @(Get-PackageIdentities -JsonPath $liveAssets)
    $isolatedAfter = @(Get-PackageIdentities -JsonPath $isolatedAssets)
    $immutableAfter = @(Get-PackageIdentities -JsonPath $immutableAssets)
    $immutableHashAfter = (Get-FileHash -LiteralPath $immutableAssets -Algorithm SHA256).Hash

    if ($liveAfter -notcontains 'Audit.Dep/2.0.0') {
        throw "Live restore graph did not advance to Audit.Dep/2.0.0: $($liveAfter -join ', ')"
    }
    if ($isolatedAfter -notcontains 'Audit.Dep/1.0.0' -or $isolatedAfter -contains 'Audit.Dep/2.0.0') {
        throw "Isolated restore graph drifted after unrelated restore: $($isolatedAfter -join ', ')"
    }
    if ($immutableAfter -notcontains 'Audit.Dep/1.0.0' -or $immutableAfter -contains 'Audit.Dep/2.0.0') {
        throw "Immutable dependency snapshot drifted: $($immutableAfter -join ', ')"
    }
    if (-not [string]::Equals($immutableHashBefore, $immutableHashAfter, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Immutable dependency snapshot hash changed after later restore.'
    }

    Invoke-DotNet -Arguments @(
        'publish',$consumerProject,
        '-c','Release',
        '--self-contained','false',
        '--artifacts-path',$isolatedArtifactsRoot,
        '--no-restore',
        '-o',$publishRoot
    )

    $publishedDeps = Join-Path $publishRoot 'Consumer.deps.json'
    $publishedAfter = @(Get-PackageIdentities -JsonPath $publishedDeps)
    if ($publishedAfter -notcontains 'Audit.Dep/1.0.0' -or $publishedAfter -contains 'Audit.Dep/2.0.0') {
        throw "Published dependency graph was not tied to the isolated restore: $($publishedAfter -join ', ')"
    }

    Write-Output 'INSTALLER_DEPENDENCY_PROVENANCE_GREEN'
}
finally {
    Remove-Item -LiteralPath $WorkingRoot -Recurse -Force -ErrorAction SilentlyContinue
}
