$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# S3 put interactive runs under a parent folder the signed-in user cannot read. The user-side helper must
# therefore never touch that parent: PowerShell's Remove-Item does (it opens the parent), and broke every
# talvora_user_run on installed 8dfb855 (audit 2026-10-23 (10)). This test runs the REAL helper script from
# InteractiveUserProcessRunner.cs, as the current user, in a run folder whose parent denies that user.
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText((Join-Path $repoRoot 'src/Talvora.Shared/InteractiveUserProcessRunner.cs'))
$marker = 'private const string RunnerScript = """'
$start = $source.IndexOf($marker, [StringComparison]::Ordinal)
if ($start -lt 0) { throw 'RunnerScript was not found in InteractiveUserProcessRunner.cs.' }
$start = $source.IndexOf("`n", $start) + 1
$end = $source.IndexOf('""";', $start, [StringComparison]::Ordinal)
$script = $source.Substring($start, $end - $start)
if ($script -match '(?m)^\s*Remove-Item\b') {
    throw 'The interactive helper must delete its request with [IO.File]::Delete, not Remove-Item (parent folder is unreadable to the user).'
}

$helperAssembly = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/Talvora.Shared/bin') -Recurse -Filter 'Talvora.Shared.dll' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if ($null -eq $helperAssembly) { throw 'Build Talvora.Shared before this test (src/Talvora.Shared/bin/**/Talvora.Shared.dll).' }

$me = [Security.Principal.WindowsIdentity]::GetCurrent().User
# Two closed levels, like C:\ProgramData\Talvora\InteractiveRuns: Windows grants "read attributes" on a folder
# to anyone who may list the folder above it, so one closed level would not reproduce the failure.
$root = Join-Path ([IO.Path]::GetTempPath()) ('Talvora-RunnerParent-' + [Guid]::NewGuid().ToString('N'))
$parent = Join-Path $root 'runs'
$run = Join-Path $parent 'run'
New-Item -ItemType Directory -Path $run | Out-Null
$inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
try {
    # Run folder: the user may modify it (as the service grants per run).
    $runSecurity = [Security.AccessControl.DirectorySecurity]::new()
    $runSecurity.SetAccessRuleProtection($true, $false)
    $runSecurity.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($me, 'Modify', $inherit, 'None', 'Allow'))
    [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($run), $runSecurity)
    $scriptPath = Join-Path $run 'runner.ps1'
    $requestPath = Join-Path $run 'request.json'
    [IO.File]::WriteAllText($scriptPath, $script, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($requestPath, (@{
        HelperAssemblyPath = $helperAssembly.FullName
        Executable = Join-Path $env:SystemRoot 'System32\whoami.exe'
        WorkingDirectory = $repoRoot
        Arguments = @()
        Environment = @{}
        StandardOutputPath = Join-Path $run 'stdout.txt'
        StandardErrorPath = Join-Path $run 'stderr.txt'
        MaximumPersistedCharacters = 100000
        CloseStandardInput = $true
        DiscardStandardError = $false
        CodexProgressPath = $null
        ClaudeProgressPath = $null
        ClaudeWorkspaceRoot = $null
    } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

    # Parent and root: no entry for the user at all (owner keeps WRITE_DAC to restore them in finally).
    foreach ($closed in @($parent, $root)) {
        $closedSecurity = [Security.AccessControl.DirectorySecurity]::new()
        $closedSecurity.SetAccessRuleProtection($true, $false)
        $closedSecurity.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new('S-1-5-18'), 'FullControl', $inherit, 'None', 'Allow'))
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($closed), $closedSecurity)
    }

    & pwsh -NoProfile -NonInteractive -File $scriptPath -RequestPath $requestPath | Out-Null
    $result = [IO.File]::ReadAllText((Join-Path $run 'result.json')) | ConvertFrom-Json
    if ($result.ExitCode -ne 0 -or $null -ne $result.Error) {
        throw "Interactive helper failed under an unreadable parent: $($result.Error)"
    }
    if ([IO.File]::Exists($requestPath)) { throw 'Interactive helper left its request file behind.' }
    $output = [IO.File]::ReadAllText((Join-Path $run 'stdout.txt')).Trim()
    if ($output -ne ([Security.Principal.WindowsIdentity]::GetCurrent().Name.ToLowerInvariant())) {
        throw "Interactive helper ran as '$output', not as the current user."
    }
}
finally {
    # One security object per folder: an applied DirectorySecurity is not re-persisted on a second folder.
    foreach ($closed in @($root, $parent)) {
        $restore = [Security.AccessControl.DirectorySecurity]::new()
        $restore.SetAccessRuleProtection($true, $false)
        $restore.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($me, 'FullControl', $inherit, 'None', 'Allow'))
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.DirectoryInfo]::new($closed), $restore)
    }
    Remove-Item -LiteralPath $root -Recurse -Force
}

Write-Output 'INTERACTIVE_RUNNER_PARENT_ACCESS_GREEN'
