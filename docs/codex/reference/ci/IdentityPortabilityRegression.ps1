$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Tests must pass as the signed-in user AND as LocalSystem: GitHub CI runs as a normal administrator,
# while ChatGPT's Talvora gate runs as LocalSystem. Any test source calling a LocalSystem-only session
# API must guard it with an explicit LocalSystem check (WellKnownSidType.LocalSystemSid) and a SKIP path.
$repoRoot = Split-Path -Parent $PSScriptRoot
$systemOnly = 'GetDefaultInteractiveUser\(|GetActiveInteractiveUser\(|GetActiveUserForSession\(|QueryUserToken\('
$violations = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests') -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Where-Object {
            $text = [IO.File]::ReadAllText($_.FullName)
            $text -match $systemOnly -and $text -notmatch 'LocalSystemSid'
        } |
        ForEach-Object { $_.FullName.Substring($repoRoot.Length + 1) }
)
if ($violations.Count -gt 0) {
    throw ("LocalSystem-only session APIs used without an explicit LocalSystem guard " +
        "(they fail on GitHub CI and for the signed-in user): " + ($violations -join ', '))
}
Write-Host 'IDENTITY_PORTABILITY_GREEN'
