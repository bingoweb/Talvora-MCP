$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$library = Join-Path $root 'src/Talvora.Shared/bin/Release/net10.0-windows/Talvora.Shared.dll'
if (-not (Test-Path -LiteralPath $library)) { throw 'Talvora.Shared is not built.' }
$assembly = [Reflection.Assembly]::LoadFrom($library)
$type = $assembly.GetType('Talvora.Shared.InteractiveUserProcessRunner', $true)
$field = $type.GetField('RunnerScript', [Reflection.BindingFlags]'NonPublic, Static')
if ($null -eq $field) { throw 'Interactive runner script was not found.' }
$runnerScript = [string]$field.GetRawConstantValue()

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('Talvora-R2-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$helper = $null
try {
    $marker = 'TALVORA_R2_PRIVATE_PROMPT_MARKER'
    $seed = 'ÇĞİÖŞÜ çğıöşü İstanbul '
    $prompt = ($marker + ($seed * 6000)).Substring(0, 120000)
    if ($prompt.Length -ne 120000) { throw 'The fixture has an invalid length.' }
    $utf8 = [Text.UTF8Encoding]::new($false)
    $expectedHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($utf8.GetBytes($prompt)))

    $stdinPath = Join-Path $tempRoot 'stdin.txt'
    $requestPath = Join-Path $tempRoot 'request.json'
    $scriptPath = Join-Path $tempRoot 'runner.ps1'
    $fakeChild = Join-Path $tempRoot 'fake-child.ps1'
    $signalPath = Join-Path $tempRoot 'child-read.flag'
    $stdoutPath = Join-Path $tempRoot 'stdout.txt'
    $stderrPath = Join-Path $tempRoot 'stderr.txt'
    $resultPath = Join-Path $tempRoot 'result.json'
    [IO.File]::WriteAllText($stdinPath, $prompt, $utf8)
    [IO.File]::WriteAllText($scriptPath, $runnerScript, $utf8)
    $childScript = @'
param([string]$SignalFile)
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
$content = [Console]::In.ReadToEnd()
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes($content)))
$commandLine = [string](Get-CimInstance Win32_Process -Filter "ProcessId=$PID").CommandLine
if ([string]::IsNullOrWhiteSpace($commandLine)) { throw 'Win32_Process child command line was not available.' }
[Console]::WriteLine('HASH=' + $hash)
[Console]::WriteLine('VISIBLE=' + $commandLine.Contains('TALVORA_R2_PRIVATE_PROMPT_MARKER'))
[IO.File]::WriteAllText($SignalFile, 'read')
Start-Sleep -Milliseconds 1500
'@
    [IO.File]::WriteAllText($fakeChild, $childScript, $utf8)
    $request = @{
        Executable = (Get-Command pwsh -ErrorAction Stop).Source
        WorkingDirectory = $tempRoot
        Arguments = @('-NoProfile', '-NonInteractive', '-File', $fakeChild, '-SignalFile', $signalPath)
        Environment = @{}
        StandardOutputPath = $stdoutPath
        StandardErrorPath = $stderrPath
        ResultPath = $resultPath
        HelperAssemblyPath = $library
        MaximumPersistedCharacters = 4096
        DiscardStandardError = $false
        CodexProgressPath = $null
        ClaudeProgressPath = $null
        CloseStandardInput = $true
        StandardInputPath = $stdinPath
        ClaudeWorkspaceRoot = $null
    }
    [IO.File]::WriteAllText($requestPath,
        ($request | ConvertTo-Json -Depth 8 -Compress), $utf8)
    if ([IO.File]::ReadAllText($requestPath).Contains($marker)) {
        throw 'The prompt was stored in the request JSON.'
    }

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command pwsh -ErrorAction Stop).Source
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($arg in @('-NoProfile', '-NonInteractive', '-File', $scriptPath,
        '-RequestPath', $requestPath)) {
        [void]$start.ArgumentList.Add($arg)
    }
    $helper = [Diagnostics.Process]::Start($start)
    if ($null -eq $helper) { throw 'Interactive helper could not be started.' }
    $commandLine = [string](Get-CimInstance Win32_Process -Filter "ProcessId=$($helper.Id)").CommandLine
    if ([string]::IsNullOrWhiteSpace($commandLine)) {
        throw 'Win32_Process helper command line was not available.'
    }
    if ($commandLine.Contains($marker)) {
        throw 'Win32_Process.CommandLine exposed the prompt.'
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (-not [IO.File]::Exists($signalPath) -and
        [DateTime]::UtcNow -lt $deadline -and -not $helper.HasExited) {
        Start-Sleep -Milliseconds 50
    }
    if (-not [IO.File]::Exists($signalPath)) {
        throw 'Fake child did not read its stdin.'
    }
    if ([IO.File]::Exists($stdinPath) -or $helper.HasExited) {
        throw 'stdin.txt was not removed promptly while the child was still alive.'
    }
    if (-not $helper.WaitForExit(20000)) {
        throw 'Fake child execution timed out.'
    }
    $result = [IO.File]::ReadAllText($resultPath) | ConvertFrom-Json
    $stdout = [IO.File]::ReadAllText($stdoutPath)
    if ($result.ExitCode -ne 0 -or
        -not $stdout.Contains('HASH=' + $expectedHash) -or
        -not $stdout.Contains('VISIBLE=False') -or
        [IO.File]::Exists($stdinPath)) {
        throw 'Turkish UTF-8 prompt hash, process command line or prompt cleanup failed.'
    }
    'R2_STDIN_REGRESSION_GREEN'
}
finally {
    if ($null -ne $helper) {
        try {
            if (-not $helper.HasExited) { $helper.Kill($true) }
            $helper.Dispose()
        } catch { }
    }
    if (Test-Path -LiteralPath $tempRoot) {
        [IO.Directory]::Delete($tempRoot, $true)
    }
}
