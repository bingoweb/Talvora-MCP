$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# scripts/Reset-And-Install.ps1 stops running Talvora programs before it reinstalls. It used to stop any process
# whose command line merely contained "Talvora-MCP" — an editor, a shell or an agent working in the repository
# (audit 2026-10-24 (14)). Test-TalvoraProcess must match Talvora's own programs only.
$repoRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $repoRoot 'scripts/Reset-And-Install.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) { throw 'Reset-And-Install.ps1 does not parse.' }
$function = $ast.Find({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-TalvoraProcess'
}, $true)
if ($null -eq $function) { throw 'Reset-And-Install.ps1 must select processes with Test-TalvoraProcess.' }
. ([ScriptBlock]::Create($function.Extent.Text))

$source = [IO.File]::ReadAllText($scriptPath)
if ($source -match 'CommandLine\s+-match\s+''\(\?i\)Talvora\(\?:[^'']*-MCP') {
    throw 'Reset-And-Install.ps1 still stops processes by a command line that mentions the repository.'
}

$cases = @(
    @('C:\Program Files\Talvora\Versions\abc\Service\Talvora.exe', '"Talvora.exe"', $true),
    @('C:\Program Files\Talvora\Versions\abc\Tray\Talvora.Tray.exe', '', $true),
    @('C:\Program Files\dotnet\dotnet.exe', 'dotnet "C:\Users\tayla\Talvora-MCP\src\Talvora\bin\Release\Talvora.dll" --urls x', $true),
    @('C:\Users\tayla\AppData\Local\Programs\Microsoft VS Code\Code.exe', '"Code.exe" C:\Users\tayla\Talvora-MCP', $false),
    @('C:\Program Files\PowerShell\7\pwsh.exe', 'pwsh -NoProfile -File C:\Users\tayla\Talvora-MCP\scripts\Invoke-QualityGate.ps1', $false),
    @('C:\Users\tayla\AppData\Local\AnthropicClaude\claude.exe', 'claude --cwd C:\Users\tayla\Talvora-MCP', $false),
    @('C:\Users\tayla\Talvora-MCP\tests\Talvora.Smoke\bin\Release\Talvora.Smoke.exe', '', $false),
    @('', '', $false)
)
foreach ($case in $cases) {
    $actual = Test-TalvoraProcess -ExecutablePath $case[0] -CommandLine $case[1]
    if ($actual -ne $case[2]) {
        throw "Test-TalvoraProcess('$($case[0])') returned $actual, expected $($case[2])."
    }
}

Write-Output 'RESET_PROCESS_FILTER_GREEN'
