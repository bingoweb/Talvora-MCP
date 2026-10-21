$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# .NET platform packages (Microsoft.Extensions.* except AI, Microsoft.Data.Sqlite, System.Composition*)
# ship together with the runtime and must share one version; a mix produces NU1109 downgrade errors.
$repoRoot = Split-Path -Parent $PSScriptRoot
[xml] $props = [IO.File]::ReadAllText((Join-Path $repoRoot 'Directory.Packages.props'))
$family = @($props.Project.ItemGroup.PackageVersion | Where-Object {
    ($_.Include -like 'Microsoft.Extensions.*' -and $_.Include -ne 'Microsoft.Extensions.AI.Abstractions') -or
    $_.Include -eq 'Microsoft.Data.Sqlite' -or
    $_.Include -like 'System.Composition*'
})
if ($family.Count -lt 10) { throw "Platform package family not found (only $($family.Count) entries)." }
$versions = @($family | ForEach-Object { $_.Version } | Sort-Object -Unique)
if ($versions.Count -ne 1) {
    $detail = ($family | ForEach-Object { "$($_.Include)=$($_.Version)" }) -join ', '
    throw "Platform packages must share one version. Found: $detail. Fix: pwsh scripts/Update-DotnetPlatformPackages.ps1 -Version <x.y.z>"
}
Write-Host "PLATFORM_PACKAGE_LOCKSTEP_GREEN $($versions[0]) ($($family.Count) packages)"
