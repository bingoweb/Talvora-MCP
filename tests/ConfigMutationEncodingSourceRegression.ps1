$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$codecPath = Join-Path $repoRoot 'src\Talvora\SourceEditing\SourceTextCodec.cs'
$replacePath = Join-Path $repoRoot 'src\Talvora\Tools\DeveloperTools.FileMutation.cs'
$jsonPath = Join-Path $repoRoot 'src\Talvora\Tools\ConfigAssetTools.Json.cs'
$sharedPath = Join-Path $repoRoot 'src\Talvora\Tools\StructuredConfigTools.Shared.cs'
$yamlPath = Join-Path $repoRoot 'src\Talvora\Tools\StructuredConfigTools.Yaml.cs'
$tomlPath = Join-Path $repoRoot 'src\Talvora\Tools\StructuredConfigTools.Toml.cs'
$xmlPath = Join-Path $repoRoot 'src\Talvora\Tools\ConfigFormatTools.Xml.cs'

$codec = Get-Content -LiteralPath $codecPath -Raw
$replace = Get-Content -LiteralPath $replacePath -Raw
$json = Get-Content -LiteralPath $jsonPath -Raw
$shared = Get-Content -LiteralPath $sharedPath -Raw
$yaml = Get-Content -LiteralPath $yamlPath -Raw
$toml = Get-Content -LiteralPath $tomlPath -Raw
$xml = Get-Content -LiteralPath $xmlPath -Raw

if ($codec -notmatch 'ReadEncodingDescriptor\(' -or
    $codec -notmatch 'CreateWriterEncoding\(') {
    throw 'Canonical SourceTextCodec encoding preservation API is missing.'
}

foreach ($encodingName in @(
    'utf-8',
    'utf-8-bom',
    'utf-16le-bom',
    'utf-16be-bom',
    'utf-32le-bom',
    'utf-32be-bom'
)) {
    if ($codec -notmatch [regex]::Escape('"' + $encodingName + '"')) {
        throw "SourceTextCodec writer mapping is missing $encodingName."
    }
}

if ($replace -notmatch 'SourceTextCodec\.CreateWriterEncoding') {
    throw 'replace_text is not using the canonical writer-encoding API.'
}

if (($json | Select-String -Pattern 'SourceTextCodec\.ReadEncodingDescriptor' -AllMatches).Matches.Count -lt 2 -or
    ($json | Select-String -Pattern 'SourceTextCodec\.CreateWriterEncoding' -AllMatches).Matches.Count -lt 2) {
    throw 'JSON set/delete do not preserve the detected source encoding.'
}

if ($shared -notmatch 'SourceTextEncodingDescriptor originalEncoding' -or
    $shared -notmatch 'SourceTextCodec\.CreateWriterEncoding') {
    throw 'Shared YAML/TOML mutation writer does not preserve source encoding.'
}

foreach ($pair in @(
    @{ Name='YAML'; Text=$yaml },
    @{ Name='TOML'; Text=$toml }
)) {
    if (($pair.Text | Select-String -Pattern 'SourceTextCodec\.ReadEncodingDescriptor' -AllMatches).Matches.Count -lt 2) {
        throw "$($pair.Name) set/delete do not capture the original encoding."
    }
}

if (($xml | Select-String -Pattern 'SourceTextCodec\.ReadEncodingDescriptor' -AllMatches).Matches.Count -lt 2 -or
    $xml -notmatch 'SourceTextCodec\.CreateWriterEncoding') {
    throw 'XML set/delete do not preserve the detected source encoding.'
}

Write-Output 'CONFIG_MUTATION_ENCODING_SOURCE_GREEN'
