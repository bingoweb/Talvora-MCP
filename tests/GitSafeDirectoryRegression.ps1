param(
    [string] $SourcePath = (Join-Path $PSScriptRoot '..\src\Talvora\Tools\GitTools.cs'),
    [string] $BuildScript = (Join-Path $PSScriptRoot '..\scripts\Build-Windows-Installer.ps1')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$text = [IO.File]::ReadAllText([IO.Path]::GetFullPath($SourcePath))
$buildText = [IO.File]::ReadAllText([IO.Path]::GetFullPath($BuildScript))

$runGitUsesScopedSafeDirectory =
    $text -match 'RunGitAsync[\s\S]*?AddSafeDirectoryArgument\(\s*workingDirectory,\s*arguments\)'
$serviceGitUsesScopedSafeDirectory =
    $text -match 'RunGitServiceAsync[\s\S]*?AddSafeDirectoryArgument\(\s*workingDirectory,\s*arguments\)'
$walksForRepositoryRoot =
    $text -match 'FindRepositoryDirectory' -and
    $text -match 'Path\.Combine\(current\.FullName, "\.git"\)'
$supportsBareRepositories =
    $text -match 'IsBareRepositoryDirectory' -and
    $text -match 'Path\.Combine\(path, "HEAD"\)' -and
    $text -match 'Path\.Combine\(path, "objects"\)'
$avoidsGlobalWildcard =
    $text -notmatch 'safe\.directory=\*' -and
    $buildText -notmatch 'safe\.directory=\*'
$installerUsesScopedSafeDirectory =
    $buildText -match 'function Invoke-RepositoryGit' -and
    $buildText -match 'safe\.directory=\{0\}' -and
    $buildText -match 'Get-GitHeadObjectId[\s\S]*?Invoke-RepositoryGit' -and
    $buildText -match '\$SourceStatus = @\([\s\S]*?Invoke-RepositoryGit'
$installerAvoidsUnsafeDirectRepoGit =
    $buildText -notmatch '& git -C \$Root' -and
    $buildText -notmatch '& git -C \$RepoRoot'

if (-not $runGitUsesScopedSafeDirectory) {
    throw 'RunGitAsync does not inject repository-scoped safe.directory.'
}
if (-not $serviceGitUsesScopedSafeDirectory) {
    throw 'RunGitServiceAsync does not inject repository-scoped safe.directory.'
}
if (-not $walksForRepositoryRoot) {
    throw 'Git safe.directory logic does not resolve a repository root.'
}
if (-not $supportsBareRepositories) {
    throw 'Git safe.directory logic does not cover bare repository roots.'
}
if (-not $avoidsGlobalWildcard) {
    throw 'Git safe.directory regression introduced an unsafe global wildcard.'
}
if (-not $installerUsesScopedSafeDirectory) {
    throw 'Canonical installer build does not use repository-scoped safe.directory.'
}
if (-not $installerAvoidsUnsafeDirectRepoGit) {
    throw 'Canonical installer build still contains direct repository Git calls without scoped safe.directory.'
}

Write-Output 'GIT_SAFE_DIRECTORY_SOURCE_GREEN'
