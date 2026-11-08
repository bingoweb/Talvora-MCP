$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/Talvora.Shared/Talvora.Shared.csproj'

function Get-ProjectVersionProperties {
    param([string[]] $AdditionalArguments)

    $output = @(& dotnet msbuild $project '-getProperty:Version' '-getProperty:InformationalVersion' @AdditionalArguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw ('MSBuild property evaluation failed: ' + ($output -join [Environment]::NewLine))
    }
    return (($output -join [Environment]::NewLine) | ConvertFrom-Json).Properties
}

$release = Get-ProjectVersionProperties -AdditionalArguments @(
    '-p:TalvoraReleaseVersion=0.0.7.3',
    '-p:SourceRevisionId=abc1234'
)
if ($release.Version -cne '0.0.7.3' -or $release.InformationalVersion -cne '0.0.7.3+abc1234') {
    throw ('A release must use the 0.0.x.x version and append the source commit: ' + ($release | ConvertTo-Json -Compress))
}

$development = Get-ProjectVersionProperties -AdditionalArguments @('-p:SourceRevisionId=abc1234')
if ($development.Version -cne '0.0.0.0' -or $development.InformationalVersion -cne '0.0.0.0-dev+abc1234') {
    throw ('A development build must use the 0.0.0.0-dev informational version: ' + ($development | ConvertTo-Json -Compress))
}

$invalidOutput = @(& dotnet msbuild $project '-t:TalvoraCheckReleaseVersion' '-p:TalvoraReleaseVersion=1.2.3' 2>&1)
if ($LASTEXITCODE -eq 0 -or ($invalidOutput -join [Environment]::NewLine) -notmatch '0\.0\.x\.x') {
    throw ('The release target must reject versions outside 0.0.x.x: ' + ($invalidOutput -join [Environment]::NewLine))
}

Write-Output 'RELEASE_VERSION_GREEN'
