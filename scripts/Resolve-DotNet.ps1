[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$candidates = @(
    (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
    $(if ($env:ProgramW6432) { Join-Path $env:ProgramW6432 'dotnet\dotnet.exe' }),
    $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe' })
) | Where-Object { $_ }

foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        (Resolve-Path -LiteralPath $candidate).Path
        exit 0
    }
}

$command = Get-Command dotnet.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
    $command.Source
    exit 0
}

Write-Error '.NET executable bulunamadı.'
exit 9009
