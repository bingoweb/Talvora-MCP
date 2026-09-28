$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-RepoText {
    param([Parameter(Mandatory = $true)][string] $RelativePath)
    [IO.File]::ReadAllText(
        (Join-Path $repoRoot $RelativePath),
        [Text.Encoding]::UTF8)
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Expected,
        [Parameter(Mandatory = $true)][string] $Contract
    )
    if (-not $Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "Memory handoff contract failed: $Contract"
    }
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory = $true)][string] $Text,
        [Parameter(Mandatory = $true)][string] $Expected,
        [Parameter(Mandatory = $true)][string] $Contract
    )
    if ($Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw "Memory handoff contract failed: $Contract"
    }
}

$tools = Read-RepoText 'src\Talvora\Tools\MemoryHandoffTools.cs'
$store = Read-RepoText 'src\Talvora\Memory\TalvoraMemoryStore.Handoff.cs'

Assert-Contains $tools 'Name = "talvora_memory_handoff_candidates"' 'candidate tool exists'
Assert-Contains $tools 'Name = "talvora_memory_handoff_review"' 'review tool exists'
Assert-Contains $tools 'This tool never writes HANDOFF.md' 'candidate contract explicitly forbids handoff writes'
Assert-Contains $tools 'Review a project''s HANDOFF.md without modifying it' 'review contract explicitly forbids handoff writes'
Assert-NotContains $tools 'File.WriteAllText' 'handoff tools never write repository files'
Assert-NotContains $tools 'File.WriteAllTextAsync' 'handoff tools never write repository files asynchronously'
Assert-Contains $tools 'ReadGitHead(projectRoot)' 'review checks live repository HEAD'
Assert-Contains $tools 'TryReadTalvoraRuntimeSourceCommitAsync' 'Talvora review checks live runtime sourceCommit'
Assert-Contains $tools 'ResolveHandoffPath' 'handoff path is constrained to the requested project'
Assert-Contains $store 'importance >= $minImportance' 'candidate selection has an importance threshold'
Assert-Contains $store 'confidence >= $minConfidence' 'candidate selection has a confidence threshold'
Assert-Contains $store 'superseded_by IS NULL' 'superseded memories are excluded'
Assert-Contains $store 'expires_utc IS NULL OR expires_utc > $now' 'expired memories are excluded'
Assert-Contains $store 'BuildHandoffMarkdown(candidates)' 'candidate output is patch-ready markdown'

Write-Output 'TALVORA MEMORY HANDOFF SOURCE REGRESSION GREEN'
