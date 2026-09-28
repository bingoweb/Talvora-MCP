namespace Talvora.Memory;

public static class TalvoraMemoryRuntime
{
    public static ITalvoraMemoryEmbeddingProvider Embeddings { get; } =
        TalvoraMemoryOnnxEmbeddingProvider.CreateDefault();

    public static TalvoraMemoryStore Store { get; } =
        new(embeddingProvider: Embeddings);
}

