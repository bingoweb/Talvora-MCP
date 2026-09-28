$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-RepoText {
    param([Parameter(Mandatory = $true)][string] $RelativePath)
    return [IO.File]::ReadAllText(
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
        throw "Memory semantic contract failed: $Contract"
    }
}

$packages = Read-RepoText 'Directory.Packages.props'
$project = Read-RepoText 'src\Talvora\Talvora.csproj'
$provider = Read-RepoText 'src\Talvora\Memory\TalvoraMemoryOnnxEmbeddingProvider.cs'
$store = Read-RepoText 'src\Talvora\Memory\TalvoraMemoryStore.Semantic.cs'
$quality = Read-RepoText 'src\Talvora\Memory\TalvoraMemoryStore.Quality.cs'
$installer = Read-RepoText 'scripts\Build-Windows-Installer.ps1'

Assert-Contains $packages 'Microsoft.ML.OnnxRuntime" Version="1.30.0"' 'ONNX Runtime version is centrally pinned'
Assert-Contains $packages 'Microsoft.ML.Tokenizers" Version="2.0.0"' 'Microsoft tokenizer version is centrally pinned'
Assert-Contains $project 'PackageReference Include="Microsoft.ML.OnnxRuntime"' 'Talvora references ONNX Runtime'
Assert-Contains $project 'PackageReference Include="Microsoft.ML.Tokenizers"' 'Talvora references Microsoft tokenizers'

Assert-Contains $provider 'paraphrase-multilingual-MiniLM-L12-v2' 'canonical multilingual model identity is fixed'
Assert-Contains $provider 'CanonicalDimensions = 384' 'canonical embedding dimensions are fixed'
Assert-Contains $provider 'q8-sha256-66fc00f5f29afcaf' 'canonical quantized model revision is fixed'
Assert-Contains $provider 'SentencePieceTokenizer.Create(stream, true, true, null)' 'Microsoft SentencePiece tokenizer is used'
Assert-Contains $provider 'MapXlmRobertaTokenId' 'XLM-R token id mapping is explicit'
Assert-Contains $provider 'GraphOptimizationLevel.ORT_ENABLE_ALL' 'ONNX graph optimization is enabled'
Assert-Contains $provider 'EmbedQueryAsync' 'query embedding contract exists'
Assert-Contains $provider 'EmbedPassageAsync' 'passage embedding contract exists'

Assert-Contains $quality 'CREATE TABLE IF NOT EXISTS memory_embeddings' 'embedding schema is additive'
Assert-Contains $quality 'model_revision TEXT NOT NULL' 'embedding revision metadata is persisted'
Assert-Contains $quality 'content_hash TEXT NOT NULL' 'embedding content hash is persisted'
Assert-Contains $quality 'item_updated_utc TEXT NOT NULL' 'embedding is bound to canonical item revision'
Assert-Contains $quality 'CHECK(length(vector) = dimensions * 4)' 'float32 vector byte length is constrained'

Assert-Contains $store 'if (!embeddingProvider.IsAvailable)' 'hybrid search keeps deterministic lexical fallback'
Assert-Contains $store 'return TrimSearchResult(lexical, limit);' 'provider failure returns lexical results'
Assert-Contains $store 'AND ($project IS NULL OR m.project = $project)' 'semantic retrieval enforces project boundary'
Assert-Contains $store 'AND ($scope IS NULL OR m.scope = $scope)' 'semantic retrieval enforces scope boundary'
Assert-Contains $store 'AND ($session IS NULL OR m.session = $session)' 'semantic retrieval enforces session boundary'
Assert-Contains $store 'AND ($category IS NULL OR m.category = $category)' 'semantic retrieval enforces category boundary'
Assert-Contains $store 'AND e.item_updated_utc = m.updated_utc' 'stale vectors are excluded from search'
Assert-Contains $store 'SemanticScanLimit = 5000' 'semantic scan has a finite bound'
Assert-Contains $store '(0.35d * lexicalScore)' 'hybrid lexical weight is explicit'
Assert-Contains $store '(0.45d * semanticScore)' 'hybrid semantic weight is explicit'
Assert-Contains $store 'batchSize is < 1 or > 500' 're-embedding batch is bounded'

Assert-Contains $installer 'Install-MemoryEmbeddingPayload' 'installer vendors the embedding assets'
Assert-Contains $installer '66FC00F5F29AFCAFF34092E1BDD20008CA3918265A82FB9695A551E510CC4EBC' 'model SHA-256 is pinned'
Assert-Contains $installer 'CFC8146ABE2A0488E9E2A0C56DE7952F7C11AB059ECA145A0A727AFCE0DB2865' 'tokenizer SHA-256 is pinned'
Assert-Contains $installer 'Service/models/memory/model.onnx' 'installer payload requires the model'
Assert-Contains $installer 'Service/models/memory/sentencepiece.bpe.model' 'installer payload requires the tokenizer'
Assert-Contains $installer 'Service/models/memory/provenance.json' 'installer payload requires model provenance'

Write-Output 'TALVORA MEMORY SEMANTIC SOURCE REGRESSION GREEN'
