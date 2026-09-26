$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root 'src\Talvora\Tools\ServiceTools.cs'
$text = [IO.File]::ReadAllText($path)

$checks = @(
    'private const string TalvoraServiceName = "Talvora";',
    'ScheduleSelfLifecycle(',
    'restart: false',
    '"StopScheduled"',
    'restart: true',
    '"RestartScheduled"',
    'UseShellExecute = false',
    'CreateNoWindow = true',
    'Start-Sleep -Milliseconds',
    'Stop-Service -Name',
    'Start-Service -Name'
)

foreach ($check in $checks) {
    if (-not $text.Contains($check)) {
        throw "Talvora self-service lifecycle regression failed: missing '$check'."
    }
}

Write-Output 'SERVICE_SELF_LIFECYCLE_SOURCE_GREEN'
