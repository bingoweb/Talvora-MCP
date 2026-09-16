$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script = Join-Path $PSScriptRoot '..\..\scripts\setup-secure-mcp-tunnel.ps1'
if (-not (Test-Path -LiteralPath $script)) {
    throw 'Secure MCP Tunnel bootstrap script is missing.'
}

$content = Get-Content -Raw -LiteralPath $script

$tokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($script, [ref] $tokens, [ref] $parseErrors) | Out-Null
if ($parseErrors.Count -ne 0) {
    $messages = ($parseErrors | ForEach-Object { $_.Message }) -join '; '
    throw "Secure tunnel bootstrap has PowerShell parse errors: $messages"
}

$required = @(
    'https://api.github.com/repos/openai/tunnel-client/releases/latest',
    'CONTROL_PLANE_API_KEY',
    '--tunnel-id',
    '--mcp-server-url',
    '--control-plane-api-key-ref',
    '--health-listen-addr',
    'doctor',
    'run',
    'Get-FileHash'
)

foreach ($needle in $required) {
    if (-not $content.Contains($needle, [StringComparison]::Ordinal)) {
        throw "Secure tunnel bootstrap must contain '$needle'."
    }
}

if ($content -match 'v0\.0\.14') {
    throw 'Secure tunnel bootstrap must not hard-code the current tunnel-client release version.'
}

if ($content -match '(?i)winget(?:\.exe)?') {
    throw 'Secure tunnel bootstrap must not use WinGet.'
}

if ($content.Contains('Copy-Item -LiteralPath (Join-Path $extractRoot ''*'')', [StringComparison]::Ordinal)) {
    throw 'Secure tunnel bootstrap must not use a wildcard with Copy-Item -LiteralPath.'
}

Write-Host 'Secure MCP Tunnel bootstrap regression GREEN.' -ForegroundColor Green
