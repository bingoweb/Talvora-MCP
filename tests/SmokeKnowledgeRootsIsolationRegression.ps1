param(
    [string] $SourcePath = (Join-Path $PSScriptRoot 'Talvora.Smoke\Program.cs')
)

$ErrorActionPreference = 'Stop'
$text = [IO.File]::ReadAllText((Resolve-Path $SourcePath))

$captures = $text -match 'knowledgeRootsWasPresent'
$setsSmokeRoot = $text -match 'TALVORA_KNOWLEDGE_ROOTS' -and $text -match '\["value"\]\s*=\s*root'
$restores = $text -match 'knowledgeRootsOriginalValue' -and $text -match 'talvora_env_delete'

[pscustomobject]@{
    CapturesOriginal = $captures
    SetsSmokeRoot = $setsSmokeRoot
    RestoresOriginal = $restores
} | Format-List

if (-not $captures -or -not $setsSmokeRoot -or -not $restores) {
    throw 'Smoke test does not isolate TALVORA_KNOWLEDGE_ROOTS.'
}

Write-Output 'SMOKE_KNOWLEDGE_ROOTS_ISOLATION_GREEN'
