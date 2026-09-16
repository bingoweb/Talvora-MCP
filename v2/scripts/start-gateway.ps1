$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$gateway = Join-Path $root 'src\Talvora.Gateway\Talvora.Gateway.csproj'

Push-Location $root
try {
    & dotnet.exe run --project $gateway -c Release --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "Talvora Gateway exited with code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
