using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Talvora.Memory;

public sealed class TalvoraMemoryOnnxEmbeddingProvider :
    ITalvoraMemoryEmbeddingProvider
{
    public const string CanonicalModelId =
        "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2";
    public const string CanonicalModelRevision =
        "q8-sha256-66fc00f5f29afcaf";
    public const int CanonicalDimensions = 384;
    private const int MaxTokens = 512;

    private readonly InferenceSession? session;
    private readonly SentencePieceTokenizer? tokenizer;
    private readonly SemaphoreSlim inferenceGate = new(1, 1);

    private TalvoraMemoryOnnxEmbeddingProvider(
        InferenceSession? session,
        SentencePieceTokenizer? tokenizer,
        string? unavailableReason)
    {
        this.session = session;
        this.tokenizer = tokenizer;
        UnavailableReason = unavailableReason;
    }

    public bool IsAvailable => session is not null && tokenizer is not null;
    public string ModelId => CanonicalModelId;
    public string ModelRevision => CanonicalModelRevision;
    public int Dimensions => CanonicalDimensions;
    public string? UnavailableReason { get; }

    public static TalvoraMemoryOnnxEmbeddingProvider CreateDefault()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "models",
            "memory");
        return Create(
            Path.Combine(root, "model.onnx"),
            Path.Combine(root, "sentencepiece.bpe.model"));
    }

    public static TalvoraMemoryOnnxEmbeddingProvider Create(
        string modelPath,
        string tokenizerPath)
    {
        try
        {
            if (!File.Exists(modelPath))
            {
                return new(
                    null,
                    null,
                    $"Embedding model is missing: {modelPath}");
            }
            if (!File.Exists(tokenizerPath))
            {
                return new(
                    null,
                    null,
                    $"Embedding tokenizer is missing: {tokenizerPath}");
            }

            var options = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                GraphOptimizationLevel =
                    GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            };
            var session = new InferenceSession(modelPath, options);
            using var stream = File.OpenRead(tokenizerPath);
            var tokenizer =
                SentencePieceTokenizer.Create(stream, true, true, null);
            return new(session, tokenizer, null);
        }
        catch (Exception ex)
        {
            return new(
                null,
                null,
                $"Embedding provider initialization failed: {ex.GetType().Name}");
        }
    }

    public ValueTask<float[]?> EmbedQueryAsync(
        string text,
        CancellationToken cancellationToken) =>
        EmbedAsync(text, cancellationToken);

    public ValueTask<float[]?> EmbedPassageAsync(
        string text,
        CancellationToken cancellationToken) =>
        EmbedAsync(text, cancellationToken);

    private async ValueTask<float[]?> EmbedAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable ||
            string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        await inferenceGate.WaitAsync(cancellationToken);
        try
        {
            var rawIds = tokenizer!.EncodeToIds(text);
            var tokenIds = rawIds
                .Take(MaxTokens)
                .Select(MapXlmRobertaTokenId)
                .ToArray();
            if (tokenIds.Length == 0)
            {
                return null;
            }

            var attention =
                Enumerable.Repeat(1L, tokenIds.Length).ToArray();
            var tokenTypes = new long[tokenIds.Length];
            var idsTensor = new DenseTensor<long>(
                tokenIds,
                [1, tokenIds.Length]);
            var attentionTensor = new DenseTensor<long>(
                attention,
                [1, tokenIds.Length]);
            var typeTensor = new DenseTensor<long>(
                tokenTypes,
                [1, tokenIds.Length]);

            using var output = session!.Run(
            [
                NamedOnnxValue.CreateFromTensor(
                    "input_ids",
                    idsTensor),
                NamedOnnxValue.CreateFromTensor(
                    "attention_mask",
                    attentionTensor),
                NamedOnnxValue.CreateFromTensor(
                    "token_type_ids",
                    typeTensor),
            ]);
            var hidden = output
                .First(value =>
                    string.Equals(
                        value.Name,
                        "last_hidden_state",
                        StringComparison.Ordinal))
                .AsTensor<float>();

            var vector = new float[CanonicalDimensions];
            for (var token = 0; token < tokenIds.Length; token++)
            {
                for (var dimension = 0;
                     dimension < CanonicalDimensions;
                     dimension++)
                {
                    vector[dimension] +=
                        hidden[0, token, dimension];
                }
            }

            var inverseLength = 1f / tokenIds.Length;
            double normSquared = 0;
            for (var dimension = 0;
                 dimension < CanonicalDimensions;
                 dimension++)
            {
                vector[dimension] *= inverseLength;
                normSquared +=
                    vector[dimension] * vector[dimension];
            }
            if (normSquared <= double.Epsilon)
            {
                return null;
            }

            var inverseNorm =
                1f / (float)Math.Sqrt(normSquared);
            for (var dimension = 0;
                 dimension < CanonicalDimensions;
                 dimension++)
            {
                vector[dimension] *= inverseNorm;
            }
            return vector;
        }
        finally
        {
            inferenceGate.Release();
        }
    }

    private static long MapXlmRobertaTokenId(int id) =>
        id switch
        {
            0 => 3,
            1 => 0,
            2 => 2,
            _ => id + 1L,
        };

    public void Dispose()
    {
        session?.Dispose();
        inferenceGate.Dispose();
    }
}
