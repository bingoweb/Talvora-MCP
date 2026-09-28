using System.IO;
using System.Text.Json;
using ModelContextProtocol.Client;
using Talvora.Shared;

namespace Talvora.Tray;

internal sealed record ControlCenterMemoryItem(
    string Id,
    string Scope,
    string? Project,
    string? Session,
    string Category,
    string Title,
    string Content,
    double Importance,
    double Confidence,
    string? Source,
    string? SourceReference,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? SupersededBy,
    string RetentionClass,
    string? ClaimKey);

internal sealed record ControlCenterMemorySearchHit(
    ControlCenterMemoryItem Item,
    double Rank,
    double SourceAuthority,
    double? SemanticScore,
    double? HybridScore);

internal sealed record ControlCenterMemorySearchResult(
    string Query,
    int Count,
    IReadOnlyList<ControlCenterMemorySearchHit> Items);

internal sealed record ControlCenterMemoryListResult(
    int Count,
    IReadOnlyList<ControlCenterMemoryItem> Items);

internal sealed record ControlCenterMemoryDiagnosticsResult(
    string DatabasePath,
    long DatabaseBytes,
    string Integrity,
    int TotalCount,
    int ActiveCount,
    int ExpiredCount,
    int SupersededCount,
    int DuplicateGroups);

internal sealed record ControlCenterMemoryEmbeddingStatusResult(
    bool Available,
    string ModelId,
    string ModelRevision,
    int Dimensions,
    string? UnavailableReason,
    int ActiveMemories,
    int CurrentEmbeddings,
    int MissingOrStaleEmbeddings);

internal sealed record ControlCenterMemoryReembedResult(
    string ModelId,
    string ModelRevision,
    int Dimensions,
    int Scanned,
    int Embedded,
    int Skipped,
    int Failed,
    int Remaining);

internal sealed record ControlCenterMemoryBackupResult(
    string DestinationPath,
    long Length,
    string Sha256,
    DateTimeOffset CreatedAtUtc);

internal sealed record ControlCenterMemoryRestoreStageResult(
    string SourcePath,
    string PendingPath,
    long Length,
    string Sha256,
    bool RequiresRestart);

internal sealed record ControlCenterMemoryRestoreStatusResult(
    bool Pending,
    string? PendingPath,
    long? Length,
    string? Sha256);

internal sealed record ControlCenterMemoryMutationResult(
    bool Success,
    string Id,
    ControlCenterMemoryItem? Item);

internal sealed record ControlCenterMemoryForgetResult(
    bool Found,
    bool Deleted,
    string Id);

internal sealed record ControlCenterMemorySupersedeResult(
    bool Success,
    ControlCenterMemoryItem? Stale,
    ControlCenterMemoryItem? Replacement);

internal static class ControlCenterMemoryService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan MaintenanceTimeout = TimeSpan.FromSeconds(45);

    public static Task<ControlCenterMemoryListResult> ListAsync(
        string? scope,
        string? project,
        string? category,
        DateTimeOffset? updatedAfterUtc,
        DateTimeOffset? updatedBeforeUtc,
        int limit,
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemoryListResult>(
            "talvora_memory_list",
            new Dictionary<string, object?>
            {
                ["scope"] = EmptyToNull(scope),
                ["project"] = EmptyToNull(project),
                ["category"] = EmptyToNull(category),
                ["updatedAfterUtc"] = updatedAfterUtc,
                ["updatedBeforeUtc"] = updatedBeforeUtc,
                ["limit"] = limit,
            },
            DefaultTimeout,
            cancellationToken);

    public static Task<ControlCenterMemorySearchResult> SearchAsync(
        string query,
        string? scope,
        string? project,
        string? category,
        int limit,
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemorySearchResult>(
            "talvora_memory_search",
            new Dictionary<string, object?>
            {
                ["query"] = query,
                ["scope"] = EmptyToNull(scope),
                ["project"] = EmptyToNull(project),
                ["category"] = EmptyToNull(category),
                ["limit"] = limit,
            },
            DefaultTimeout,
            cancellationToken);

    public static Task<ControlCenterMemoryMutationResult> GetAsync(
        string id,
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemoryMutationResult>(
            "talvora_memory_get",
            new Dictionary<string, object?>
            {
                ["id"] = id,
            },
            DefaultTimeout,
            cancellationToken);

    public static Task<ControlCenterMemoryDiagnosticsResult> DiagnosticsAsync(
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemoryDiagnosticsResult>(
            "talvora_memory_diagnostics",
            new Dictionary<string, object?>(),
            DefaultTimeout,
            cancellationToken);

    public static Task<ControlCenterMemoryEmbeddingStatusResult> EmbeddingStatusAsync(
        string? project,
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemoryEmbeddingStatusResult>(
            "talvora_memory_embedding_status",
            new Dictionary<string, object?>
            {
                ["project"] = EmptyToNull(project),
            },
            DefaultTimeout,
            cancellationToken);

    public static Task<ControlCenterMemoryReembedResult> ReembedAsync(
        string? project,
        int batchSize,
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemoryReembedResult>(
            "talvora_memory_reembed",
            new Dictionary<string, object?>
            {
                ["project"] = EmptyToNull(project),
                ["batchSize"] = batchSize,
                ["force"] = false,
            },
            MaintenanceTimeout,
            cancellationToken);

    public static Task<ControlCenterMemoryBackupResult> BackupAsync(
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemoryBackupResult>(
            "talvora_memory_backup",
            new Dictionary<string, object?>
            {
                ["destinationPath"] = destinationPath,
                ["overwrite"] = overwrite,
            },
            MaintenanceTimeout,
            cancellationToken);

    public static Task<ControlCenterMemoryRestoreStageResult> RestoreStageAsync(
        string sourcePath,
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemoryRestoreStageResult>(
            "talvora_memory_restore_stage",
            new Dictionary<string, object?>
            {
                ["sourcePath"] = sourcePath,
            },
            MaintenanceTimeout,
            cancellationToken);

    public static Task<ControlCenterMemoryRestoreStatusResult> RestoreStatusAsync(
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemoryRestoreStatusResult>(
            "talvora_memory_restore_status",
            new Dictionary<string, object?>(),
            DefaultTimeout,
            cancellationToken);

    public static Task<ControlCenterMemoryMutationResult> UpdateAsync(
        string id,
        string? title,
        string? content,
        string? category,
        double? importance,
        double? confidence,
        DateTimeOffset? expiresAtUtc,
        string? retentionClass,
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemoryMutationResult>(
            "talvora_memory_update",
            new Dictionary<string, object?>
            {
                ["id"] = id,
                ["title"] = EmptyToNull(title),
                ["content"] = EmptyToNull(content),
                ["category"] = EmptyToNull(category),
                ["importance"] = importance,
                ["confidence"] = confidence,
                ["expiresAtUtc"] = expiresAtUtc,
                ["retentionClass"] = EmptyToNull(retentionClass),
            },
            DefaultTimeout,
            cancellationToken);

    public static Task<ControlCenterMemoryForgetResult> ForgetAsync(
        string id,
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemoryForgetResult>(
            "talvora_memory_forget",
            new Dictionary<string, object?>
            {
                ["id"] = id,
            },
            DefaultTimeout,
            cancellationToken);

    public static Task<ControlCenterMemorySupersedeResult> SupersedeAsync(
        string staleId,
        string replacementId,
        CancellationToken cancellationToken) =>
        CallAsync<ControlCenterMemorySupersedeResult>(
            "talvora_memory_supersede",
            new Dictionary<string, object?>
            {
                ["staleId"] = staleId,
                ["replacementId"] = replacementId,
            },
            DefaultTimeout,
            cancellationToken);

    private static async Task<T> CallAsync<T>(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await using var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(TalvoraConstants.McpDevUrl),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    ConnectionTimeout = TimeSpan.FromSeconds(6),
                });

            await using var client = await McpClient.CreateAsync(
                transport,
                cancellationToken: timeoutCts.Token);

            var result = await client.CallToolAsync(
                toolName,
                arguments,
                cancellationToken: timeoutCts.Token);

            if (result.IsError is true)
            {
                throw new InvalidOperationException(
                    "Talvora hafıza işlemini tamamlayamadı.");
            }

            if (result.StructuredContent is not JsonElement structured)
            {
                throw new InvalidDataException(
                    "Talvora hafıza yanıtı beklenen structured içeriği taşımıyor.");
            }

            var value = structured.Deserialize<T>(JsonOptions);
            return value ?? throw new InvalidDataException(
                "Talvora hafıza yanıtı okunamadı.");
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Talvora hafıza işlemi zaman aşımına uğradı.");
        }
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
