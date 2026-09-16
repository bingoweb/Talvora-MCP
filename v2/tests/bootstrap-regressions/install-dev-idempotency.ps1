$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installer = Join-Path $PSScriptRoot '..\..\scripts\install-dev-from-github.ps1'
$content = Get-Content -Raw -LiteralPath $installer
$healthProbe = "Invoke-RestMethod -Uri 'http://127.0.0.1:7676/healthz'"
$startGateway = "Start-Process -FilePath 'dotnet.exe'"
$healthIndex = $content.IndexOf($healthProbe, [StringComparison]::Ordinal)
$startIndex = $content.IndexOf($startGateway, [StringComparison]::Ordinal)
if ($healthIndex -lt 0) { throw 'Installer must probe the Talvora health endpoint.' }
if ($startIndex -lt 0) { throw 'Installer must contain the Gateway start operation.' }
if ($healthIndex -gt $startIndex) { throw 'Installer must check health before starting another Gateway process.' }

# Execute the installer's real dependency functions and top-level dependency calls.
# External package installs are replaced because tests must not alter the runner.
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($installer, [ref] $tokens, [ref] $parseErrors)
if ($parseErrors.Count -gt 0) { throw "Installer parse errors: $($parseErrors.Message -join '; ')" }

foreach ($definition in $ast.EndBlock.Statements) {
    if ($definition -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
        . ([scriptblock]::Create($definition.Extent.Text))
    }
}
$dependencyCalls = @($ast.EndBlock.Statements | Where-Object {
    $_ -is [System.Management.Automation.Language.PipelineAst] -and
    $_.PipelineElements[0] -is [System.Management.Automation.Language.CommandAst] -and
    $_.PipelineElements[0].GetCommandName() -match '^Ensure-(Chocolatey|ChocoPackage|DotNet10Sdk)$'
})
if ($dependencyCalls.Count -eq 0) { throw 'No dependency bootstrap calls were found.' }

function Refresh-Path { }
function Get-Command {
    param([string] $Name, $ErrorAction)
    if ($Name -eq 'dotnet.exe' -and -not $script:HasDotnet) { return $null }
    return [pscustomobject]@{ Name = $Name }
}
function dotnet.exe {
    if ($args[0] -ne '--list-sdks') { throw 'SDK discovery must use dotnet --list-sdks.' }
    $global:LASTEXITCODE = 0
    return $script:InstalledSdks
}
function choco.exe {
    if ($args[0] -ne 'install' -or $args[1] -ne 'dotnet-10.0-sdk') {
        throw "Unexpected package operation: $($args -join ' ')"
    }
    $script:InstallCalls++
    if ($script:FailInstall) { $global:LASTEXITCODE = 1; return }
    $script:HasDotnet = $true
    $script:InstalledSdks = @('10.0.401 [C:\Program Files\dotnet\sdk]')
    $global:LASTEXITCODE = 0
}

$cases = @(
    @{ Name = 'older SDK'; HasDotnet = $true; Sdks = @('9.0.100 [C:\Program Files\dotnet\sdk]'); Installs = 1 },
    @{ Name = '.NET 10 already installed'; HasDotnet = $true; Sdks = @('10.0.401 [C:\Program Files\dotnet\sdk]'); Installs = 0 },
    @{ Name = 'runtime only'; HasDotnet = $true; Sdks = @(); Installs = 1 },
    @{ Name = 'dotnet absent'; HasDotnet = $false; Sdks = @(); Installs = 1 }
)
foreach ($case in $cases) {
    $script:HasDotnet = $case.HasDotnet
    $script:InstalledSdks = $case.Sdks
    $script:InstallCalls = 0
    $script:FailInstall = $false
    foreach ($statement in $dependencyCalls) { . ([scriptblock]::Create($statement.Extent.Text)) }
    if ($script:InstallCalls -ne $case.Installs) {
        throw "SDK regression ($($case.Name)): expected $($case.Installs) .NET 10 installs, got $script:InstallCalls."
    }
    Write-Host "PASS dependency bootstrap: $($case.Name)."
}

$script:HasDotnet = $true
$script:InstalledSdks = @('9.0.100 [C:\Program Files\dotnet\sdk]')
$script:InstallCalls = 0
$script:FailInstall = $true
$failed = $false
try {
    foreach ($statement in $dependencyCalls) { . ([scriptblock]::Create($statement.Extent.Text)) }
}
catch { $failed = $true }
if (-not $failed) { throw 'A failed Chocolatey SDK install must stop bootstrap.' }
Write-Host 'PASS failed SDK installation stops bootstrap.'
Write-Host 'Installer regression GREEN: startup order and SDK bootstrap cases.' -ForegroundColor Green
