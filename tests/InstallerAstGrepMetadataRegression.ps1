param([string] $BuildScript = (Join-Path $PSScriptRoot '..\scripts\Build-Windows-Installer.ps1'))
$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Resolve-Path $BuildScript), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw 'Installer build script does not parse.' }
$definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Install-AstGrepPayload' }, $true)
if ($null -eq $definition) { throw 'Install-AstGrepPayload was not found.' }
Invoke-Expression $definition.Extent.Text

$script:Calls = [Collections.Generic.List[object]]::new()
$script:Mode = 'valid'
$script:ExpectedPackage = '@ast-grep/cli-win32-x64-msvc'
$script:ExpectedIntegrity = 'sha512-' + [Convert]::ToBase64String([byte[]]::new(64))
function Get-Command {
    param([string] $Name, [object] $ErrorAction)
    if ($Name -in @('npm.cmd', 'npm')) { return [pscustomobject]@{ Source = 'Invoke-FakeNpm' } }
    throw "Unexpected command discovery: $Name"
}
function Invoke-FakeNpm {
    $script:Calls.Add(@($args))
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'install') { throw 'AUDIT_INSTALL_BOUNDARY' }
    if ($args[0] -ne 'view') { throw 'Unexpected npm operation.' }
    # Simulate a registry tag changing between separate field requests.
    switch ($args[2]) {
        'version' { return '"1.2.3"' }
        'license' { return '"MIT"' }
        'dist.integrity' { return ('"' + $script:ExpectedIntegrity + '"') }
    }
    $value = @{
        name = $script:ExpectedPackage
        version = '1.2.3'
        license = 'MIT'
        dist = @{ integrity = $script:ExpectedIntegrity }
    }
    if ($script:Mode -eq 'missing-integrity') { $value.dist.Remove('integrity') }
    if ($script:Mode -eq 'wrong-package') { $value.name = '@other/package' }
    if ($script:Mode -eq 'valid-array') {
        return (,([pscustomobject]$value) | ConvertTo-Json -Depth 4 -Compress)
    }
    if ($script:Mode -eq 'multiple-packages') {
        return (@(
            [pscustomobject]$value,
            [pscustomobject]$value
        ) | ConvertTo-Json -Depth 4 -Compress)
    }
    return ($value | ConvertTo-Json -Depth 4 -Compress)
}
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('TalvoraAstMetadata-' + [Guid]::NewGuid().ToString('N'))
try {
    foreach ($mode in @('valid', 'valid-array', 'missing-integrity', 'wrong-package', 'multiple-packages')) {
        $script:Mode = $mode
        $script:Calls.Clear()
        $caught = $null
        try {
            Install-AstGrepPayload -Rid win-x64 -DestinationRoot (Join-Path $testRoot 'payload') -StagingRoot (Join-Path $testRoot 'stage') | Out-Null
        } catch { $caught = $_.Exception.Message }
        $views = @($script:Calls | Where-Object { $_[0] -eq 'view' })
        $installs = @($script:Calls | Where-Object { $_[0] -eq 'install' })
        if ($mode -in @('valid', 'valid-array')) {
            if ($views.Count -ne 1) { throw "Metadata was read $($views.Count) times; expected a single snapshot." }
            if ($installs.Count -ne 1 -or $caught -ne 'AUDIT_INSTALL_BOUNDARY') { throw "Valid snapshot did not reach install: $caught" }
            if ($installs[0][-1] -ne ($script:ExpectedPackage + '@1.2.3')) { throw 'Install was not bound to the resolved version.' }
        } elseif ($installs.Count -ne 0 -or [string]::IsNullOrWhiteSpace($caught)) {
            throw "Invalid metadata '$mode' was accepted."
        }
        Write-Output "PASS ast-grep-metadata-$mode"
    }
} finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
