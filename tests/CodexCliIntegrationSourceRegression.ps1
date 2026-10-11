$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText(
    (Join-Path $root 'src\Talvora\Tools\CodexCliTools.cs'))
$policy = [IO.File]::ReadAllText(
    (Join-Path $root 'src\Talvora.Shared\CodexCliInvocationPolicy.cs'))
$runner = [IO.File]::ReadAllText(
    (Join-Path $root 'src\Talvora.Shared\InteractiveUserProcessRunner.cs'))
$pump = [IO.File]::ReadAllText(
    (Join-Path $root 'src\Talvora.Shared\BoundedTextCapture.cs'))
$installer = [IO.File]::ReadAllText(
    (Join-Path $root 'src\Talvora.Installer\InstallerEngine.CleanupState.cs'))
$toml = [IO.File]::ReadAllText(
    (Join-Path $root 'src\Talvora.Shared\CodexMcpConfiguration.cs'))
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
        $policy.Contains('"--model", model') -and
        $policy.Contains('model_reasoning_effort=')
    )
    DynamicDesktopResolution = (
        $source.Contains('"OpenAI", "Codex", "bin"') -and
        $source.Contains('"OpenAI.Codex_*"') -and
        $source.Contains('EnumerateCodexExecutables(') -and
        -not $source.Contains('2e5e00daee91c61d')
    )
    InteractiveUserOwnsCodex = (
        $source.Contains('WindowsSessionLauncher.GetActiveInteractiveUser()') -and
        $source.Contains('InteractiveUserProcessRunner.RunAsync(') -and
        $source.Contains('interactiveUser: context') -and
        $runner.Contains('expectedUserSid: interactiveUser?.Sid') -and
        $source.Contains('["login", "status"]')
    )
    FailsClosedOnPrivilegeEscalation = (
        $source.Contains('"workspace-write"') -and
        $source.Contains('sandbox is not ("read-only" or ProtectedSandbox)') -and
        $policy.Contains('"-a", "never"') -and
        $policy.Contains('"--sandbox", sandbox') -and
        $policy.Contains('"--", prompt') -and
        -not $source.Contains('dangerously-bypass-approvals-and-sandbox')
    )
    BoundedTasksAndRedactedResults = (
        $source.Contains('MaximumPromptCharacters') -and
        $source.Contains('MaximumOutputCharacters') -and
        $source.Contains('timeoutSeconds is < 1 or > 3600') -and
        $source.Contains('CodexCliInvocationPolicy.SanitizeFinalAnswer(') -and
        $source.Contains('CodexCliInvocationPolicy.SafeErrorSummary(') -and
        $source.Contains('discardStandardError: true') -and
        $policy.Contains('[TASK_PROMPT_REDACTED]') -and
        $runner.Contains('StandardOutputEncoding = [Text.UTF8Encoding]') -and
        $runner.Contains('StandardErrorEncoding = [Text.UTF8Encoding]') -and
        $runner.Contains('TextFileStore.ReadBoundedAsync(') -and
        $pump.Contains('var headLimit = maximumCharacters / 2') -and
        $pump.Contains('writer.FlushAsync(cancellationToken)') -and
        $pump.Contains('totalCharacters > maximumCharacters') -and
        -not $source.Contains('result.Arguments,')
    )
    TomlUpdatePreservesUnrelatedTables = (
        $installer.Contains('CodexMcpConfiguration.UpsertTalvoraLocal(') -and
        $toml.Contains('ReadTableParts(line)') -and
        $toml.Contains('UpdateMultilineState(line, ref multilineDelimiter)') -and
        $toml.Contains('tableParts[1], "talvora_local"')
    )
    FocusedSurfaceCountUpdated = (
        $surface.Contains('ExpectedFullToolCount = 236') -and
        $surface.Contains('ExpectedDevelopmentToolCount = 199') -and
        $surface.Contains('ExpectedAdministrationToolCount = 88') -and
        $surface.Contains('ExpectedSharedToolCount = 51')
    )
    DedicatedCiGateExists = (
        $workflow.Contains('./tests/CodexCliIntegrationSourceRegression.ps1') -and
        $workflow.Contains('--codex-integration-policy-only')
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
