namespace Talvora.Memory;

public static class TalvoraMemoryRuntime
{
    public static ITalvoraMemoryEmbeddingProvider Embeddings { get; } =
        TalvoraMemoryOnnxEmbeddingProvider.CreateDefault();

    public static TalvoraMemoryStore Store { get; } =
        CreateStore();

    private static TalvoraMemoryStore CreateStore()
    {
        var databasePath =
            TalvoraMemoryStore.ResolveDefaultDatabasePath();
        TalvoraMemoryStore.ApplyPendingRestoreIfPresent(
            databasePath);
        return new TalvoraMemoryStore(
            databasePath,
            Embeddings);
    }
}

