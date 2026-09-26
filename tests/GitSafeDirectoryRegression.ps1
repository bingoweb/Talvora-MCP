param(
    [string] $SourcePath = (Join-Path $PSScriptRoot '..\src\Talvora\Tools\GitTools.cs')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$text = [IO.File]::ReadAllText([IO.Path]::GetFullPath($SourcePath))

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
    $text -notmatch 'safe\.directory=\*'

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

Write-Output 'GIT_SAFE_DIRECTORY_SOURCE_GREEN'
