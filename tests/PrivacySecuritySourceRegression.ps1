$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-RepoText {
    param([Parameter(Mandatory = $true)][string] $RelativePath)
    return [IO.File]::ReadAllText(
        (Join-Path $repoRoot $RelativePath),
        [Text.Encoding]::UTF8)
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Expected,
        [Parameter(Mandatory = $true)][string] $Contract
    )

    if (-not $Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "Privacy/security contract failed: $Contract"
    }
}

$fileLog = Read-RepoText 'src\Talvora.Shared\FileLog.cs'
$businessTunnel = Read-RepoText 'src\Talvora.Tray\BusinessTunnelClient.cs'
$rawLog = Read-RepoText 'src\Talvora.Tray\ControlCenterRawLogService.cs'
$eventStore = Read-RepoText 'src\Talvora.Tray\ControlCenterEventStore.cs'
$interactive = Read-RepoText 'src\Talvora.Shared\InteractiveUserProcessRunner.cs'
$gitIgnore = Read-RepoText '.gitignore'
$securityPolicy = Read-RepoText 'SECURITY.md'

Assert-Contains $fileLog 'RedactSensitiveData(message)' 'persistent messages are redacted'
Assert-Contains $fileLog 'RedactSensitiveData(exception.Message)' 'persistent exception messages are redacted'
Assert-Contains $fileLog 'AuthorizationSchemeRegex' 'authorization schemes are recognized'
Assert-Contains $businessTunnel 'FileLog.RedactSensitiveData(value)' 'tunnel-client diagnostic output is redacted before display'
Assert-Contains $rawLog 'FileLog.RedactSensitiveData(line)' 'raw-log viewer redacts managed log lines'
Assert-Contains $eventStore 'FileLog.RedactSensitiveData(title)' 'structured event titles are redacted'
Assert-Contains $eventStore 'FileLog.RedactSensitiveData(detail)' 'structured event details are redacted'
Assert-Contains $interactive 'active.lock' 'interactive runs have an active lease marker'
Assert-Contains $interactive 'FileShare.None' 'stale cleanup proves the run lease is not active'
Assert-Contains $interactive 'CleanupStaleRunDirectories(runsRoot)' 'stale interactive runs are cleaned'
Assert-Contains $interactive 'Remove-Item -LiteralPath $RequestPath -Force' 'interactive request document is removed after deserialization'
Assert-Contains $gitIgnore '*.dpapi' 'DPAPI blobs are ignored'
Assert-Contains $gitIgnore '*.pfx' 'PFX bundles are ignored'
Assert-Contains $gitIgnore '*.key' 'private-key files are ignored'
Assert-Contains $gitIgnore '.env' 'local environment files are ignored'
Assert-Contains $securityPolicy 'must preserve its legitimate development and administration capabilities' 'hardening preserves Talvora capability'

Write-Output 'TALVORA PRIVACY SECURITY SOURCE REGRESSION GREEN'
