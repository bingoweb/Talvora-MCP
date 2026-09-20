$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$deployPath = Join-Path $repoRoot 'scripts\Deploy-Windows-Installer.ps1'

$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $deployPath,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw "Deploy script parse failed: $($parseErrors[0].Message)"
}

$identityFunction = $ast.Find(
    {
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Assert-InstallerArtifactIdentity'
    },
    $true)
if ($null -eq $identityFunction) {
    throw 'Assert-InstallerArtifactIdentity function was not found.'
}

Invoke-Expression $identityFunction.Extent.Text

$root = Join-Path $env:TEMP (
    'TalvoraDeployArtifactIdentity-' +
    [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null

try {
    $installer = Join-Path $root 'Talvora-Setup.exe'
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(
        'canonical-installer-fixture')
    [IO.File]::WriteAllBytes($installer, $bytes)
    $size = [long](Get-Item -LiteralPath $installer).Length
    $sha256 = (
        Get-FileHash -LiteralPath $installer -Algorithm SHA256
    ).Hash.ToLowerInvariant()

    $accepted = Assert-InstallerArtifactIdentity -Path $installer -ExpectedFileName 'Talvora-Setup.exe' -ExpectedSizeBytes $size -ExpectedSha256 $sha256
    if (-not [string]::Equals(
            $accepted,
            $sha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Canonical installer fixture did not return the verified SHA-256.'
    }

    [IO.File]::AppendAllText(
        $installer,
        '-mutated',
        [Text.UTF8Encoding]::new($false))

    $rejected = $false
    try {
        Assert-InstallerArtifactIdentity -Path $installer -ExpectedFileName 'Talvora-Setup.exe' -ExpectedSizeBytes $size -ExpectedSha256 $sha256 | Out-Null
    }
    catch {
        $rejected = $true
    }

    if (-not $rejected) {
        throw 'Mutated installer fixture was not rejected by the deploy identity gate.'
    }

    Write-Output 'CANONICAL_DEPLOY_ARTIFACT_IDENTITY_GREEN'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
