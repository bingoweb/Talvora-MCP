$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Assert-Contains([string]$Path, [string]$Pattern, [string]$Message) {
    $text = [IO.File]::ReadAllText((Join-Path $root $Path))
    if ($text -notmatch $Pattern) { throw $Message }
}

Assert-Contains 'global.json' '"version"\s*:\s*"10\.0\.4\d{2}"' 'global.json must pin the current .NET 10.0.4xx feature band.'
Assert-Contains 'global.json' '"rollForward"\s*:\s*"latestFeature"' 'global.json must permit current servicing SDKs in the pinned feature band.'
Assert-Contains 'Directory.Packages.props' '<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>' 'Central Package Management must remain enabled.'
Assert-Contains 'Directory.Build.props' '<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>' 'NuGet lock files must remain enabled.'
Assert-Contains 'Directory.Build.props' '<NuGetAuditMode>all</NuGetAuditMode>' 'NuGet audit must cover transitive dependencies.'
Assert-Contains 'AGENTS.md' 'Context7 is mandatory' 'Context7 must remain an explicit development quality gate.'
Assert-Contains 'docs/MODERNIZATION-POLICY.md' 'Gitea issue' 'Modernization phases must remain tracked in Gitea.'
Assert-Contains 'AGENTS.md' 'Mandatory Windows desktop progress reporting' 'Desktop progress reporting must remain a durable engineering rule.'
Assert-Contains 'src/Talvora/Program.cs' 'AddCallToolFilter' 'Every MCP tool call must remain wrapped by the desktop progress contract.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'HeartbeatInterval\s*=\s*TimeSpan\.FromMinutes\(2\)' 'Long-running tool calls must retain a bounded two-minute desktop heartbeat.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'WTSGetActiveConsoleSessionId' 'Desktop progress must target the active console session.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'WTSSendMessageW' 'Desktop progress must use the supported Windows session messaging path.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'StringMarshalling\s*=\s*StringMarshalling\.Utf16' 'Windows session messaging must keep explicit UTF-16 generated interop.'
Assert-Contains 'src/Talvora/TalvoraDesktopProgressNotifier.cs' 'IsTruthy\(Environment\.GetEnvironmentVariable\("CI"\)\)' 'Desktop progress must stay noninteractive in CI.'
Assert-Contains '.github/workflows/windows-ci.yml' '--locked-mode' 'CI restore must use locked mode.'
Assert-Contains '.github/workflows/windows-ci.yml' 'ModernizationPolicyRegression\.ps1' 'CI must enforce the modernization policy regression.'
Assert-Contains '.github/workflows/windows-ci.yml' 'Validate-Context7Gate\.ps1' 'CI must enforce fresh Context7 evidence.'
Assert-Contains '.github/workflows/windows-ci.yml' 'actions/checkout@[0-9a-f]{40}\s+#\s+v\d+\.\d+\.\d+' 'actions/checkout must be pinned to a full release SHA.'
Assert-Contains '.github/workflows/windows-ci.yml' 'actions/setup-dotnet@[0-9a-f]{40}\s+#\s+v\d+\.\d+\.\d+' 'actions/setup-dotnet must be pinned to a full release SHA.'
Assert-Contains '.github/dependabot.yml' 'package-ecosystem:\s*"dotnet-sdk"' 'Dependabot must keep global.json current.'
Assert-Contains '.context7/verification.json' '"schemaVersion"\s*:\s*1' 'Context7 evidence schema must remain versioned.'

$projectVersions = Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Filter '*.csproj' -Recurse |
    Select-String -Pattern 'PackageReference.+\sVersion="'
if ($projectVersions) {
    $projectVersions | ForEach-Object { Write-Host $_.Line }
    throw 'PackageReference versions must be centralized in Directory.Packages.props.'
}

[xml]$centralPackages = Get-Content -LiteralPath (Join-Path $root 'Directory.Packages.props') -Raw
foreach ($packageVersion in @($centralPackages.Project.ItemGroup.PackageVersion)) {
    $version = [string]$packageVersion.Version
    if ($version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
        throw "Central package version must be an exact stable version: $($packageVersion.Include)=$version"
    }
}
$projects = Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Filter '*.csproj' -Recurse
foreach ($project in $projects) {
    $lock = Join-Path $project.DirectoryName 'packages.lock.json'
    if (-not (Test-Path -LiteralPath $lock -PathType Leaf)) {
        throw "Missing NuGet lock file for $($project.FullName)"
    }
}

Write-Host 'MODERNIZATION_POLICY_GREEN'
