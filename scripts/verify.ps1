[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$dotnetPath = (& (Join-Path $PSScriptRoot 'Resolve-DotNet.ps1') | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
    throw '.NET executable bulunamadı.'
}

& (Join-Path $PSScriptRoot 'Test-Architecture.ps1')
& $dotnetPath restore Talvora.slnx
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnetPath build Talvora.slnx -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnetPath test --solution Talvora.slnx -c Debug --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot 'Test-McpSmoke.ps1') -DotNetPath $dotnetPath -Configuration Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot 'Test-BrokerSmoke.ps1') -DotNetPath $dotnetPath -Configuration Debug
exit $LASTEXITCODE
