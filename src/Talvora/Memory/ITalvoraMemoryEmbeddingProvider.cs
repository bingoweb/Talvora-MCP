namespace Talvora.Memory;

public interface ITalvoraMemoryEmbeddingProvider : IDisposable
{
    bool IsAvailable { get; }
    string ModelId { get; }
    string ModelRevision { get; }
    int Dimensions { get; }
    string? UnavailableReason { get; }

    ValueTask<float[]?> EmbedQueryAsync(
        string text,
        CancellationToken cancellationToken);

    ValueTask<float[]?> EmbedPassageAsync(
        string text,
        CancellationToken cancellationToken);
}

internal sealed class TalvoraNullMemoryEmbeddingProvider :
    ITalvoraMemoryEmbeddingProvider
{
    public static TalvoraNullMemoryEmbeddingProvider Instance { get; } = new();

    public bool IsAvailable => false;
    public string ModelId => "none";
    public string ModelRevision => "none";
    public int Dimensions => 0;
    public string? UnavailableReason => "No embedding provider is configured.";

    public ValueTask<float[]?> EmbedQueryAsync(
        string text,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<float[]?>(null);

    public ValueTask<float[]?> EmbedPassageAsync(
        string text,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<float[]?>(null);

    public void Dispose()
    {
    }
}
