$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText(
    (Join-Path $root 'src\Talvora\Tools\CodexCliTools.cs'))
$manifest = [IO.File]::ReadAllText(
    (Join-Path $root 'src\Talvora.Shared\TalvoraToolManifest.cs'))
$surface = [IO.File]::ReadAllText(
    (Join-Path $root 'src\Talvora.Shared\TalvoraMcpToolSurfacePolicy.cs'))
$workflow = [IO.File]::ReadAllText(
    (Join-Path $root '.github\workflows\windows-ci.yml'))

$checks = [ordered]@{
    ToolsAppearInManifest = (
        $manifest.Contains('"talvora_codex_info"') -and
        $manifest.Contains('"talvora_codex_exec"')
    )
    ToolsRegisteredAsMcp = (
        $source.Contains('[McpServerToolType]') -and
        $source.Contains('Name = "talvora_codex_info"') -and
        $source.Contains('Name = "talvora_codex_exec"') -and
        $source.Contains('UseStructuredContent = true')
    )
    ExplicitDefaultModelAndEffort = (
        $source.Contains('DefaultModel = "gpt-6.1-sol"') -and
        $source.Contains('DefaultReasoningEffort = "medium"') -and
        $source.Contains('"--model", model') -and
        $source.Contains('model_reasoning_effort=')
    )
    DynamicDesktopResolution = (
        $source.Contains('"OpenAI", "Codex", "bin"') -and
        $source.Contains('"OpenAI.Codex_*"') -and
        $source.Contains('EnumerateCodexExecutables(') -and
        -not $source.Contains('2e5e00daee91c61d')
    )
    InteractiveUserOwnsCodex = (
        $source.Contains('WindowsSessionLauncher.GetDefaultInteractiveUser()') -and
        $source.Contains('InteractiveUserProcessRunner.RunAsync(') -and
        $source.Contains('["login", "status"]')
    )
    FailsClosedOnPrivilegeEscalation = (
        $source.Contains('"workspace-write"') -and
        $source.Contains('sandbox is not ("read-only" or ProtectedSandbox)') -and
        $source.Contains('"-a", "never"') -and
        $source.Contains('"--sandbox", sandbox') -and
        -not $source.Contains('dangerously-bypass-approvals-and-sandbox')
    )
    BoundedTasksAndRedactedResults = (
        $source.Contains('MaximumPromptCharacters') -and
        $source.Contains('MaximumOutputCharacters') -and
        $source.Contains('timeoutSeconds is < 1 or > 3600') -and
        $source.Contains('FileLog.RedactSensitiveData(result.StandardOutput)') -and
        -not $source.Contains('result.Arguments,')
    )
    FocusedSurfaceCountUpdated = (
        $surface.Contains('ExpectedFullToolCount = 236') -and
        $surface.Contains('ExpectedDevelopmentToolCount = 199') -and
        $surface.Contains('ExpectedAdministrationToolCount = 88') -and
        $surface.Contains('ExpectedSharedToolCount = 51')
    )
    DedicatedCiGateExists = (
        $workflow.Contains('./tests/CodexCliIntegrationSourceRegression.ps1')
    )
}

$failed = @($checks.GetEnumerator() |
    Where-Object { -not $_.Value } |
    ForEach-Object { $_.Key })
if ($failed.Count -gt 0) {
    throw 'Codex CLI source regression failed: ' + ($failed -join ', ')
}

$checks.GetEnumerator() | ForEach-Object { "PASS $($_.Key)" }
'TALVORA CODEX CLI INTEGRATION SOURCE REGRESSION GREEN'
