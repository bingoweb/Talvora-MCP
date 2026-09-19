param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $PSScriptRoot 'Talvora.Smoke\Talvora.Smoke.csproj'

& dotnet run --project $Project -c Release -- --shared-infrastructure-only
if ($LASTEXITCODE -ne 0) {
    throw "ProcessRunner regression failed with exit code $LASTEXITCODE."
}

Write-Host 'PROCESS_RUNNER_REGRESSION_GREEN'
