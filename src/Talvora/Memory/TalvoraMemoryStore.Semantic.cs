using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Talvora.Memory;

public sealed partial class TalvoraMemoryStore
{
    private const int SemanticScanLimit = 5000;
    private const double MinimumSemanticSimilarity = 0.25d;

    public async Task<TalvoraMemorySearchResult> SearchAsync(
        string query,
        string? scope,
        string? project,
        string? session,
        string? category,
        int limit,
        CancellationToken cancellationToken)
    {
        var lexicalLimit = Math.Min(
            100,
            Math.Max(limit * 4, 40));
        var lexical = await SearchLexicalAsync(
            query,
            scope,
            project,
            session,
            category,
            lexicalLimit,
            cancellationToken);

        if (!embeddingProvider.IsAvailable)
        {
            return TrimSearchResult(lexical, limit);
        }

        float[]? queryVector;
        try
        {
            queryVector =
                await embeddingProvider.EmbedQueryAsync(
                    query.Trim(),
                    cancellationToken);
        }
        catch
        {
            return TrimSearchResult(lexical, limit);
        }
        if (queryVector is null ||
            queryVector.Length != embeddingProvider.Dimensions)
        {
            return TrimSearchResult(lexical, limit);
        }

        var semantic = await LoadSemanticCandidatesAsync(
            queryVector,
            scope,
            project,
            session,
            category,
            cancellationToken);
        if (semantic.Count == 0)
        {
            return TrimSearchResult(lexical, limit);
        }

        var lexicalPositions = lexical.Items
            .Select((hit, index) => (hit.Item.Id, Position: index + 1))
            .ToDictionary(
                pair => pair.Id,
                pair => pair.Position,
                StringComparer.Ordinal);
        var semanticPositions = semantic
            .Select((hit, index) => (hit.Item.Id, Position: index + 1))
            .ToDictionary(
                pair => pair.Id,
                pair => pair.Position,
                StringComparer.Ordinal);

        var candidates =
            new Dictionary<string, SemanticCandidate>(
                StringComparer.Ordinal);
        foreach (var hit in lexical.Items)
        {
            candidates[hit.Item.Id] =
                new SemanticCandidate(
                    hit.Item,
                    hit.SourceAuthority,
                    null);
        }
        foreach (var hit in semantic)
        {
            candidates[hit.Item.Id] = hit;
        }

        var now = DateTimeOffset.UtcNow;
        var ranked = candidates.Values
            .Select(candidate =>
            {
                var lexicalScore =
                    lexicalPositions.TryGetValue(
                        candidate.Item.Id,
                        out var lexicalPosition)
                        ? 1d / lexicalPosition
                        : 0d;
                var semanticScore =
                    candidate.SemanticScore is null
                        ? 0d
                        : Math.Clamp(
                            (candidate.SemanticScore.Value -
                             MinimumSemanticSimilarity) /
                            (1d - MinimumSemanticSimilarity),
                            0d,
                            1d);
                var ageDays = Math.Max(
                    0d,
                    (now - candidate.Item.UpdatedAtUtc).TotalDays);
                var recency =
                    1d / (1d + (ageDays / 180d));
                var hybrid =
                    (0.35d * lexicalScore) +
                    (0.45d * semanticScore) +
                    (0.07d * candidate.SourceAuthority) +
                    (0.05d * candidate.Item.Confidence) +
                    (0.04d * candidate.Item.Importance) +
                    (0.04d * recency);

                return new
                {
                    Candidate = candidate,
                    LexicalPosition =
                        lexicalPositions.GetValueOrDefault(
                            candidate.Item.Id,
                            int.MaxValue),
                    SemanticPosition =
                        semanticPositions.GetValueOrDefault(
                            candidate.Item.Id,
                            int.MaxValue),
                    Hybrid = hybrid,
                };
            })
            .OrderByDescending(item => item.Hybrid)
            .ThenBy(item => item.LexicalPosition)
            .ThenBy(item => item.SemanticPosition)
            .ThenByDescending(item => item.Candidate.SourceAuthority)
            .ThenByDescending(item => item.Candidate.Item.Confidence)
            .ThenByDescending(item => item.Candidate.Item.Importance)
            .ThenByDescending(item => item.Candidate.Item.UpdatedAtUtc)
            .Take(limit)
            .Select(item =>
                new TalvoraMemorySearchHit(
                    item.Candidate.Item,
                    item.Hybrid,
                    item.Candidate.SourceAuthority,
                    item.Candidate.SemanticScore,
                    item.Hybrid))
            .ToArray();

        return new TalvoraMemorySearchResult(
            query.Trim(),
            ranked.Length,
            ranked);
    }

    public async Task<TalvoraMemoryEmbeddingStatusResult>
        EmbeddingStatusAsync(
            string? project,
            CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var normalizedProject = NormalizeProjectIdentity(project);
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                COUNT(*),
                SUM(CASE
                    WHEN e.memory_id IS NOT NULL
                     AND e.model_id = $modelId
                     AND e.model_revision = $modelRevision
                     AND e.dimensions = $dimensions
                     AND e.item_updated_utc = m.updated_utc
                    THEN 1 ELSE 0 END)
            FROM memory_items AS m
            LEFT JOIN memory_embeddings AS e
                ON e.memory_id = m.id
            WHERE m.superseded_by IS NULL
              AND (m.expires_utc IS NULL OR m.expires_utc > $now)
              AND ($project IS NULL OR m.project = $project);
            """;
        Add(command, "$modelId", embeddingProvider.ModelId);
        Add(command, "$modelRevision", embeddingProvider.ModelRevision);
        Add(command, "$dimensions", embeddingProvider.Dimensions);
        Add(command, "$now", Format(DateTimeOffset.UtcNow));
        Add(command, "$project", normalizedProject);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var active = reader.GetInt32(0);
        var current = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        return new TalvoraMemoryEmbeddingStatusResult(
            embeddingProvider.IsAvailable,
            embeddingProvider.ModelId,
            embeddingProvider.ModelRevision,
            embeddingProvider.Dimensions,
            embeddingProvider.UnavailableReason,
            active,
            current,
            Math.Max(0, active - current));
    }

    public async Task<TalvoraMemoryReembedResult> ReembedAsync(
        string? project,
        int batchSize,
        bool force,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "Re-embedding batch size must be between 1 and 500.");
        }
        if (!embeddingProvider.IsAvailable)
        {
            throw new InvalidOperationException(
                embeddingProvider.UnavailableReason ??
                "The embedding provider is unavailable.");
        }

        await EnsureInitializedAsync(cancellationToken);
        var normalizedProject = NormalizeProjectIdentity(project);
        var items = new List<TalvoraMemoryItem>();
        await using (var connection =
            await OpenConnectionAsync(cancellationToken))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT m.id, m.scope, m.project, m.session,
                       m.category, m.title, m.content,
                       m.importance, m.confidence, m.source, m.source_ref,
                       m.created_utc, m.updated_utc, m.expires_utc,
                       m.superseded_by, m.retention_class, m.claim_key
                FROM memory_items AS m
                LEFT JOIN memory_embeddings AS e
                    ON e.memory_id = m.id
                WHERE m.superseded_by IS NULL
                  AND (m.expires_utc IS NULL OR m.expires_utc > $now)
                  AND ($project IS NULL OR m.project = $project)
                  AND (
                        $force = 1
                        OR e.memory_id IS NULL
                        OR e.model_id <> $modelId
                        OR e.model_revision <> $modelRevision
                        OR e.dimensions <> $dimensions
                        OR e.item_updated_utc <> m.updated_utc
                      )
                ORDER BY m.updated_utc DESC, m.id
                LIMIT $limit;
                """;
            Add(command, "$now", Format(DateTimeOffset.UtcNow));
            Add(command, "$project", normalizedProject);
            Add(command, "$force", force ? 1 : 0);
            Add(command, "$modelId", embeddingProvider.ModelId);
            Add(command, "$modelRevision", embeddingProvider.ModelRevision);
            Add(command, "$dimensions", embeddingProvider.Dimensions);
            Add(command, "$limit", batchSize);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(ReadItem(reader));
            }
        }

        var embedded = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var item in items)
        {
            try
            {
                var changed =
                    await TryRefreshEmbeddingAsync(
                        item,
                        cancellationToken,
                        force);
                if (changed) embedded++;
                else skipped++;
            }
            catch
            {
                failed++;
            }
        }

        var status =
            await EmbeddingStatusAsync(
                normalizedProject,
                cancellationToken);
        return new TalvoraMemoryReembedResult(
            embeddingProvider.ModelId,
            embeddingProvider.ModelRevision,
            embeddingProvider.Dimensions,
            items.Count,
            embedded,
            skipped,
            failed,
            status.MissingOrStaleEmbeddings);
    }

    private async Task<IReadOnlyList<SemanticCandidate>>
        LoadSemanticCandidatesAsync(
            float[] queryVector,
            string? scope,
            string? project,
            string? session,
            string? category,
            CancellationToken cancellationToken)
    {
        var hits = new List<SemanticCandidate>();
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT m.id, m.scope, m.project, m.session,
                   m.category, m.title, m.content,
                   m.importance, m.confidence, m.source, m.source_ref,
                   m.created_utc, m.updated_utc, m.expires_utc,
                   m.superseded_by, m.retention_class, m.claim_key,
                   e.vector
            FROM memory_items AS m
            JOIN memory_embeddings AS e
                ON e.memory_id = m.id
            WHERE e.model_id = $modelId
              AND e.model_revision = $modelRevision
              AND e.dimensions = $dimensions
              AND e.item_updated_utc = m.updated_utc
              AND ($scope IS NULL OR m.scope = $scope)
              AND ($project IS NULL OR m.project = $project)
              AND ($session IS NULL OR m.session = $session)
              AND ($category IS NULL OR m.category = $category)
              AND (m.expires_utc IS NULL OR m.expires_utc > $now)
              AND m.superseded_by IS NULL
            ORDER BY m.updated_utc DESC
            LIMIT $scanLimit;
            """;
        Add(command, "$modelId", embeddingProvider.ModelId);
        Add(command, "$modelRevision", embeddingProvider.ModelRevision);
        Add(command, "$dimensions", embeddingProvider.Dimensions);
        Add(command, "$scope", NormalizeOptional(scope));
        Add(command, "$project", NormalizeProjectIdentity(project));
        Add(command, "$session", NormalizeOptional(session));
        Add(command, "$category", NormalizeOptional(category));
        Add(command, "$now", Format(DateTimeOffset.UtcNow));
        Add(command, "$scanLimit", SemanticScanLimit);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var vector = DeserializeVector(
                (byte[])reader.GetValue(17),
                embeddingProvider.Dimensions);
            var similarity = Dot(queryVector, vector);
            if (similarity < MinimumSemanticSimilarity)
            {
                continue;
            }
            hits.Add(
                new SemanticCandidate(
                    ReadItem(reader),
                    GetSourceAuthority(
                        reader.IsDBNull(9)
                            ? null
                            : reader.GetString(9)),
                    similarity));
        }
        return hits
            .OrderByDescending(hit => hit.SemanticScore)
            .ThenByDescending(hit => hit.SourceAuthority)
            .ThenByDescending(hit => hit.Item.Confidence)
            .ThenByDescending(hit => hit.Item.Importance)
            .Take(100)
            .ToArray();
    }

    private async Task<bool> TryRefreshEmbeddingAsync(
        TalvoraMemoryItem item,
        CancellationToken cancellationToken,
        bool force = false)
    {
        if (!embeddingProvider.IsAvailable)
        {
            return false;
        }

        var passage = BuildEmbeddingText(item);
        var contentHash = HashEmbeddingText(passage);
        if (!force &&
            await HasCurrentEmbeddingAsync(
                item.Id,
                contentHash,
                item.UpdatedAtUtc,
                cancellationToken))
        {
            return false;
        }

        var vector =
            await embeddingProvider.EmbedPassageAsync(
                passage,
                cancellationToken);
        if (vector is null ||
            vector.Length != embeddingProvider.Dimensions)
        {
            return false;
        }

        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO memory_embeddings(
                memory_id, model_id, model_revision, dimensions,
                content_hash, item_updated_utc, vector, updated_utc)
            VALUES(
                $memoryId, $modelId, $modelRevision, $dimensions,
                $contentHash, $itemUpdatedUtc, $vector, $updatedUtc)
            ON CONFLICT(memory_id) DO UPDATE SET
                model_id = excluded.model_id,
                model_revision = excluded.model_revision,
                dimensions = excluded.dimensions,
                content_hash = excluded.content_hash,
                item_updated_utc = excluded.item_updated_utc,
                vector = excluded.vector,
                updated_utc = excluded.updated_utc;
            """;
        Add(command, "$memoryId", item.Id);
        Add(command, "$modelId", embeddingProvider.ModelId);
        Add(command, "$modelRevision", embeddingProvider.ModelRevision);
        Add(command, "$dimensions", embeddingProvider.Dimensions);
        Add(command, "$contentHash", contentHash);
        Add(command, "$itemUpdatedUtc", Format(item.UpdatedAtUtc));
        Add(command, "$vector", SerializeVector(vector));
        Add(command, "$updatedUtc", Format(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private async Task<bool> HasCurrentEmbeddingAsync(
        string memoryId,
        string contentHash,
        DateTimeOffset itemUpdatedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM memory_embeddings
            WHERE memory_id = $memoryId
              AND model_id = $modelId
              AND model_revision = $modelRevision
              AND dimensions = $dimensions
              AND content_hash = $contentHash
              AND item_updated_utc = $itemUpdatedUtc;
            """;
        Add(command, "$memoryId", memoryId);
        Add(command, "$modelId", embeddingProvider.ModelId);
        Add(command, "$modelRevision", embeddingProvider.ModelRevision);
        Add(command, "$dimensions", embeddingProvider.Dimensions);
        Add(command, "$contentHash", contentHash);
        Add(command, "$itemUpdatedUtc", Format(itemUpdatedAtUtc));
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static TalvoraMemorySearchResult TrimSearchResult(
        TalvoraMemorySearchResult source,
        int limit)
    {
        var items = source.Items.Take(limit).ToArray();
        return new TalvoraMemorySearchResult(
            source.Query,
            items.Length,
            items);
    }

    private static string BuildEmbeddingText(TalvoraMemoryItem item) =>
        $"{item.Category}\n{item.Title}\n{item.Content}";

    private static string HashEmbeddingText(string text) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();

    private static byte[] SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(
            vector,
            0,
            bytes,
            0,
            bytes.Length);
        return bytes;
    }

    private static float[] DeserializeVector(
        byte[] bytes,
        int dimensions)
    {
        if (bytes.Length != dimensions * sizeof(float))
        {
            throw new InvalidDataException(
                "Stored memory embedding has an invalid byte length.");
        }
        var vector = new float[dimensions];
        Buffer.BlockCopy(
            bytes,
            0,
            vector,
            0,
            bytes.Length);
        return vector;
    }

    private static double Dot(
        IReadOnlyList<float> left,
        IReadOnlyList<float> right)
    {
        double sum = 0;
        for (var index = 0; index < left.Count; index++)
        {
            sum += left[index] * right[index];
        }
        return sum;
    }

    private sealed record SemanticCandidate(
        TalvoraMemoryItem Item,
        double SourceAuthority,
        double? SemanticScore);
}
