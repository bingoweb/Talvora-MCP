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

function Assert-NotContains {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Unexpected,
        [Parameter(Mandatory = $true)][string] $Contract
    )

    if ($Text.Contains($Unexpected, [StringComparison]::Ordinal)) {
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
$learningObserver = Read-RepoText 'src\Talvora\TalvoraAutomaticLearningObserver.cs'
$learningStore = Read-RepoText 'src\Talvora\Memory\TalvoraMemoryStore.Learning.cs'
$memoryQuality = Read-RepoText 'src\Talvora\Memory\TalvoraMemoryStore.Quality.cs'
$dpapiSecretStore = Read-RepoText 'src\Talvora.Tray\DpapiSecretStore.cs'

Assert-Contains $fileLog 'RedactSensitiveData(message)' 'persistent messages are redacted'
Assert-Contains $fileLog 'RedactSensitiveData(exception.Message)' 'persistent exception messages are redacted'
Assert-Contains $fileLog 'FormatExceptionDetails(exception.StackTrace)' 'persistent exception details are redacted and bounded before logging'
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
if ($dpapiSecretStore -notmatch 'MaximumCredentialFileBytes\s*=\s*64\s*\*\s*1024') {
    throw 'Privacy/security contract failed: DPAPI credential files have a hard byte ceiling'
}
Assert-Contains $dpapiSecretStore 'stream.Length > MaximumCredentialFileBytes' 'DPAPI credential length is validated on the opened handle'
Assert-Contains $dpapiSecretStore 'FileShare.Read' 'DPAPI bounded read prevents concurrent write/delete replacement while validating'
Assert-Contains $dpapiSecretStore 'MaximumCredentialFileBytes + 1' 'DPAPI self-test covers oversized persisted credentials'
Assert-NotContains $dpapiSecretStore 'File.ReadAllText(fullPath)' 'DPAPI credential restore never allocates an unbounded string'
Assert-Contains $gitIgnore '*.pfx' 'PFX bundles are ignored'
Assert-Contains $gitIgnore '*.key' 'private-key files are ignored'
Assert-Contains $gitIgnore '.env' 'local environment files are ignored'
Assert-Contains $securityPolicy 'must preserve its legitimate development and administration capabilities' 'hardening preserves Talvora capability'
Assert-Contains $learningObserver '"workspaceRoot"' 'automatic learning only extracts allowlisted workspace identity fields'
Assert-Contains $learningObserver '"repositoryPath"' 'automatic learning recognizes repository identity without storing command payloads'
Assert-Contains $learningObserver '"workingDirectory"' 'automatic learning recognizes build working directory'
Assert-Contains $learningObserver '"project"' 'automatic learning recognizes explicit project identity'
Assert-Contains $learningObserver 'TalvoraMemoryStore.IsAutomaticLearningTool(toolName)' 'automatic learning is allowlist-gated before observation'
Assert-Contains $learningStore 'toolName.StartsWith(' 'memory tool recursion is explicitly rejected'
Assert-Contains $learningStore 'LooksLikeSecret(title)' 'explicit decision learning rejects secret-like titles'
Assert-Contains $learningStore 'LooksLikeSecret(content)' 'explicit decision learning rejects secret-like content'
Assert-Contains $learningStore 'LooksLikeSecret(claimKey)' 'explicit decision learning rejects secret-like claim keys'
Assert-NotContains $memoryQuality 'raw_arguments' 'automatic-learning schema never persists raw arguments'
Assert-NotContains $memoryQuality 'raw_result' 'automatic-learning schema never persists raw tool results'
Assert-NotContains $memoryQuality 'request_body' 'automatic-learning schema never persists request bodies'

Write-Output 'TALVORA PRIVACY SECURITY SOURCE REGRESSION GREEN'
