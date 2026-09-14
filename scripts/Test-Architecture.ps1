[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Get-ProjectReferences {
    param([Parameter(Mandatory)][string]$RelativeProjectPath)

    $path = Join-Path $root $RelativeProjectPath
    [xml]$xml = Get-Content -LiteralPath $path -Raw
    @($xml.SelectNodes('/Project/ItemGroup/ProjectReference') | ForEach-Object { $_.GetAttribute('Include') })
}

function Assert-ReferencesExactly {
    param(
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Expected
    )

    $actual = @(Get-ProjectReferences -RelativeProjectPath $Project | Sort-Object)
    $expectedSorted = @($Expected | Sort-Object)

    if (($actual -join '|') -ne ($expectedSorted -join '|')) {
        throw "Architecture boundary violation in $Project. Expected: [$($expectedSorted -join ', ')], actual: [$($actual -join ', ')]"
    }
}

Assert-ReferencesExactly 'src/Talvora.Abstractions/Talvora.Abstractions.csproj' @()
Assert-ReferencesExactly 'src/Talvora.Core/Talvora.Core.csproj' @('../Talvora.Abstractions/Talvora.Abstractions.csproj')
Assert-ReferencesExactly 'src/Talvora.Platform.Windows/Talvora.Platform.Windows.csproj' @(
    '../Talvora.Abstractions/Talvora.Abstractions.csproj',
    '../Talvora.Modules.Registry/Talvora.Modules.Registry.csproj'
)
Assert-ReferencesExactly 'src/Talvora.Modules.FileSystem/Talvora.Modules.FileSystem.csproj' @()
Assert-ReferencesExactly 'src/Talvora.Modules.Shell/Talvora.Modules.Shell.csproj' @()
Assert-ReferencesExactly 'src/Talvora.Modules.Processes/Talvora.Modules.Processes.csproj' @()
Assert-ReferencesExactly 'src/Talvora.Ipc.Contracts/Talvora.Ipc.Contracts.csproj' @()
Assert-ReferencesExactly 'src/Talvora.Ipc.Client/Talvora.Ipc.Client.csproj' @('../Talvora.Ipc.Contracts/Talvora.Ipc.Contracts.csproj')
Assert-ReferencesExactly 'src/Talvora.ElevatedBroker/Talvora.ElevatedBroker.csproj' @(
    '../Talvora.Ipc.Contracts/Talvora.Ipc.Contracts.csproj',
    '../Talvora.Modules.Registry/Talvora.Modules.Registry.csproj',
    '../Talvora.Platform.Windows/Talvora.Platform.Windows.csproj'
)

Write-Host 'Talvora architecture dependency boundaries: GREEN' -ForegroundColor Green
