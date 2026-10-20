[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,
    [switch] $SkipRestore
)
# Moves every .NET platform package to one version and regenerates all lock files.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$path = Join-Path $repoRoot 'Directory.Packages.props'
$bytes = [IO.File]::ReadAllBytes($path)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$text = [IO.File]::ReadAllText($path)
$pattern = '(<PackageVersion Include="(?:Microsoft\.Extensions\.(?!AI\.)[^"]+|Microsoft\.Data\.Sqlite|System\.Composition[^"]*)" Version=")([^"]+)(")'
$count = [regex]::Matches($text, $pattern).Count
if ($count -lt 10) { throw "Expected the platform package family in Directory.Packages.props, found $count entries." }
$updated = [regex]::Replace($text, $pattern, { param($m) $m.Groups[1].Value + $Version + $m.Groups[3].Value })
[IO.File]::WriteAllText($path, $updated, [Text.UTF8Encoding]::new($hasBom))
Write-Host "Updated $count platform packages to $Version."
if ($SkipRestore) { exit 0 }
foreach ($project in @('Talvora.slnx', 'src/Talvora.Installer/Talvora.Installer.csproj')) {
    & dotnet restore (Join-Path $repoRoot $project) --force-evaluate --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore --force-evaluate failed for $project." }
}
& pwsh -NoProfile -File (Join-Path $repoRoot 'tests/PlatformPackageLockstepRegression.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Platform package lockstep check failed after the update.' }
