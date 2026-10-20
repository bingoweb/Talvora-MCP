param([string] $BuildScript = (Join-Path $PSScriptRoot '..\scripts\Build-Windows-Installer.ps1'))
$ErrorActionPreference = 'Stop'
$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path $BuildScript).Path, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw 'Installer build script does not parse.' }

foreach ($name in @('Get-PinnedAstGrepMetadata','Assert-PinnedAstGrepTarball',
                    'Assert-PinnedAstGrepExecutable','Install-AstGrepPayload')) {
    $definition = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq $name
    }, $true)
    if ($null -eq $definition) { throw "Required pinned function missing: $name" }
    Invoke-Expression $definition.Extent.Text
}
foreach ($rid in @('win-x64','win-arm64','win-x86')) {
    $pin = Get-PinnedAstGrepMetadata -Rid $rid
    if ($pin.version -ne '0.45.3' -or
        -not $pin.tarball.EndsWith("-0.45.3.tgz", [StringComparison]::Ordinal) -or
        $pin.executableSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
        -not $pin.integrity.StartsWith('sha512-', [StringComparison]::Ordinal)) {
        throw "Invalid pinned metadata for $rid."
    }
    Write-Output "PASS ast-grep-pin-$rid"
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('TalvoraAstPin-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp) | Out-Null
try {
    $archive = Join-Path $temp 'archive.tgz'
    $binary = Join-Path $temp 'ast-grep.exe'
    $bytes = [Text.Encoding]::UTF8.GetBytes('fixture for pin integrity')
    [IO.File]::WriteAllBytes($archive, $bytes)
    [IO.File]::WriteAllBytes($binary, $bytes)
    $sri = 'sha512-' + [Convert]::ToBase64String(
        [Security.Cryptography.SHA512]::HashData($bytes))
    $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    Assert-PinnedAstGrepTarball -Path $archive -Integrity $sri
    Assert-PinnedAstGrepExecutable -Path $binary -Sha256 $sha
    Write-Output 'PASS ast-grep-pinned-untampered'

    [IO.File]::AppendAllText($archive, 'tampered')
    [IO.File]::AppendAllText($binary, 'tampered')
    $archiveRejected = $false
    try { Assert-PinnedAstGrepTarball -Path $archive -Integrity $sri }
    catch { $archiveRejected = $_.Exception.Message.Contains('SHA-512') }
    $binaryRejected = $false
    try { Assert-PinnedAstGrepExecutable -Path $binary -Sha256 $sha }
    catch { $binaryRejected = $_.Exception.Message.Contains('SHA-256') }
    if (-not $archiveRejected -or -not $binaryRejected) {
        throw 'A tampered ast-grep artifact was accepted.'
    }
    Write-Output 'PASS ast-grep-tampered-hashes-rejected'

    $script:DownloadedUri = $null
    function Invoke-WebRequest {
        param([string] $Uri, [string] $OutFile, [int] $MaximumRedirection,
              [int] $TimeoutSec, [object] $ErrorAction)
        $script:DownloadedUri = $Uri
        if ($MaximumRedirection -ne 0) { throw 'Redirects are not allowed.' }
        [IO.File]::WriteAllBytes($OutFile, [Text.Encoding]::UTF8.GetBytes('tampered tarball'))
    }
    $destination = Join-Path $temp 'payload'
    $staging = Join-Path $temp 'staging'
    $failedClosed = $false
    try {
        Install-AstGrepPayload -Rid 'win-x64' -DestinationRoot $destination -StagingRoot $staging | Out-Null
    }
    catch { $failedClosed = $_.Exception.Message.Contains('SHA-512 verification failed') }
    $pin = Get-PinnedAstGrepMetadata -Rid 'win-x64'
    if (-not $failedClosed -or $script:DownloadedUri -cne $pin.tarball -or
        (Test-Path (Join-Path $destination 'ast-grep.exe'))) {
        throw 'Tampered tarball crossed the pinned installer execution boundary.'
    }
    Write-Output 'PASS ast-grep-tampered-tarball-fails-closed'
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
