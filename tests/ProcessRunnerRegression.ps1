param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $PSScriptRoot 'Talvora.Smoke\Talvora.Smoke.csproj'
$SmokeDll = Join-Path $PSScriptRoot 'Talvora.Smoke\bin\Release\net10.0-windows\Talvora.Smoke.dll'

# CI builds the solution before invoking source-contract regressions. Earlier
# PowerShell checks load Talvora.Shared.dll via Add-Type and keep it locked
# in the current pwsh process; implicit dotnet run builds would fail to copy it.
if (-not (Test-Path -LiteralPath $SmokeDll -PathType Leaf)) {
    throw "Release smoke assembly missing. Build Talvora.slnx -c Release before running this regression: $SmokeDll"
}

& dotnet run --project $Project -c Release --no-build -- --shared-infrastructure-only
if ($LASTEXITCODE -ne 0) {
    throw "ProcessRunner regression failed with exit code $LASTEXITCODE."
}

Write-Host 'PROCESS_RUNNER_REGRESSION_GREEN'
