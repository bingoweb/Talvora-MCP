param(
    [string] $SourcePath = (Join-Path $PSScriptRoot 'Talvora.Smoke\SmokeScenarios.Workspace.cs')
)

$ErrorActionPreference = 'Stop'
$text = [IO.File]::ReadAllText((Resolve-Path $SourcePath))

$captures = $text -match 'knowledgeRootsWasPresent'
$setsSmokeRoot = $text -match 'TALVORA_KNOWLEDGE_ROOTS' -and $text -match '\["value"\]\s*=\s*root'
$restores = $text -match 'knowledgeRootsOriginalValue' -and $text -match 'talvora_env_delete'
$s6Scenario = [IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'Talvora.SourceEdit.Regression\SourceEditRegressionRunner.KnowledgeS6.cs'))
$s6Entry = [IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'Talvora.SourceEdit.Regression\Program.cs'))
$verifiesS6 = $s6Scenario.Contains('RunKnowledgeS6LocalAsync') -and
    $s6Scenario.Contains('ExpectOutsideAsync') -and
    $s6Scenario.Contains('FileAttributes.Offline') -and
    $s6Scenario.Contains('ResolveKnowledgeUserProfile()') -and
    $s6Scenario.Contains('File.CreateSymbolicLink(') -and
    $s6Entry.Contains('--knowledge-s6-local')

[pscustomobject]@{
    CapturesOriginal = $captures
    SetsSmokeRoot = $setsSmokeRoot
    RestoresOriginal = $restores
    CoversS6Policy = $verifiesS6
} | Format-List

if (-not $captures -or -not $setsSmokeRoot -or -not $restores -or -not $verifiesS6) {
    throw 'Smoke test does not isolate TALVORA_KNOWLEDGE_ROOTS.'
}

Write-Output 'SMOKE_KNOWLEDGE_ROOTS_ISOLATION_GREEN'
