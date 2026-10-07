$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$context7Path = Join-Path $root '.context7\verification.json'
$awwwardsPath = Join-Path $root '.talvora\awwwards-verification.json'

function Read-Evidence([string]$Path, [string]$Name) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Name delivery evidence is missing: $Path" }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Assert-Fresh([object]$Evidence, [string]$Name) {
    if ([int]$Evidence.schemaVersion -ne 1) { throw "$Name evidence schemaVersion must be 1." }
    $verified = [DateTimeOffset]::Parse([string]$Evidence.verifiedAtUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal)
    $now = [DateTimeOffset]::UtcNow
    if ($verified -gt $now.AddMinutes(15)) { throw "$Name evidence timestamp is unexpectedly in the future." }
    if ($verified -lt $now.AddDays(-14)) { throw "$Name evidence is older than 14 days." }
}

$context7 = Read-Evidence $context7Path 'Context7'
Assert-Fresh $context7 'Context7'
if (@($context7.libraries).Count -eq 0) { throw 'Context7 delivery evidence requires at least one library.' }
if (@($context7.vendorSources).Count -eq 0) { throw 'Context7 delivery evidence requires at least one official/vendor source.' }

$awwwards = Read-Evidence $awwwardsPath 'Awwwards'
Assert-Fresh $awwwards 'Awwwards'
if ([string]$awwwards.sourceUrl -notmatch '^https://(www\.)?awwwards\.com/') { throw 'Awwwards sourceUrl must use https://www.awwwards.com/.' }
if (@($awwwards.references | Where-Object { [string]$_ -match '^https://(www\.)?awwwards\.com/' }).Count -eq 0) { throw 'Awwwards delivery evidence requires at least one Awwwards reference.' }
if (@($awwwards.designDecisions | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -eq 0) { throw 'Awwwards delivery evidence requires at least one concrete design/applicability decision.' }

$gitTools = [IO.File]::ReadAllText((Join-Path $root 'src\Talvora\Tools\GitTools.cs'))
$dotnetTools = [IO.File]::ReadAllText((Join-Path $root 'src\Talvora\Tools\BuildRunnerTools.Dotnet.cs'))
$gateSource = [IO.File]::ReadAllText((Join-Path $root 'src\Talvora\SourceEditing\ProjectDeliveryGate.cs'))
if ($gitTools -notmatch 'IsPush\(arguments\)[\s\S]{0,400}ProjectDeliveryGate\.EnsureSatisfied') { throw 'Git push is no longer protected by the project delivery gate.' }
if ($dotnetTools -notmatch 'DotnetPublish[\s\S]{0,1800}ProjectDeliveryGate\.EnsureSatisfied') { throw 'dotnet publish is no longer protected by the project delivery gate.' }
if ($gateSource -notmatch 'TALVORA_DELIVERY_GATE_BLOCKED' -or $gateSource -notmatch '\.context7/verification\.json' -or $gateSource -notmatch '\.talvora/awwwards-verification\.json') { throw 'Project delivery gate no longer fails closed on mandatory Context7/Awwwards evidence.' }

Write-Host 'PROJECT_DELIVERY_GATE_GREEN'
