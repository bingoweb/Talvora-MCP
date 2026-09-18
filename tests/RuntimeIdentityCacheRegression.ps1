param(
    [string] $RepoRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'

$programPath = Join-Path $RepoRoot 'src\Talvora\Program.cs'
$systemToolsPath = Join-Path $RepoRoot 'src\Talvora\Tools\SystemTools.cs'
$identityPath = Join-Path $RepoRoot 'src\Talvora\RuntimeIdentity.cs'

$program = [IO.File]::ReadAllText((Resolve-Path $programPath))
$systemTools = [IO.File]::ReadAllText((Resolve-Path $systemToolsPath))
$identityExists = Test-Path -LiteralPath $identityPath -PathType Leaf
$identity = if ($identityExists) { [IO.File]::ReadAllText((Resolve-Path $identityPath)) } else { '' }

$programUsesCachedSid = $program -match 'TalvoraRuntimeIdentity\.Sid'
$systemToolUsesCachedSid = $systemTools -match 'TalvoraRuntimeIdentity\.Sid'
$hotPathsDoNotCallGetCurrent = (
    $program -notmatch 'WindowsIdentity\.GetCurrent\(' -and
    $systemTools -notmatch 'WindowsIdentity\.GetCurrent\('
)
$identityCachesSid = (
    $identityExists -and
    $identity -match 'static\s+readonly\s+Lazy<string\?>' -and
    $identity -match 'using\s+var\s+identity\s*=\s*WindowsIdentity\.GetCurrent\('
)

[pscustomobject]@{
    ProgramUsesCachedSid = $programUsesCachedSid
    SystemToolUsesCachedSid = $systemToolUsesCachedSid
    HotPathsDoNotCallGetCurrent = $hotPathsDoNotCallGetCurrent
    IdentityCachesSid = $identityCachesSid
} | Format-List

if (-not $programUsesCachedSid -or
    -not $systemToolUsesCachedSid -or
    -not $hotPathsDoNotCallGetCurrent -or
    -not $identityCachesSid) {
    throw 'Windows identity SID is not process-lifetime cached.'
}

Write-Output 'RUNTIME_IDENTITY_CACHE_GREEN'
