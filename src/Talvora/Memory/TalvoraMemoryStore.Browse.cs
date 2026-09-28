namespace Talvora.Memory;

public sealed partial class TalvoraMemoryStore
{
    public async Task<TalvoraMemoryListResult> ListAsync(
        string? scope,
        string? project,
        string? category,
        DateTimeOffset? updatedAfterUtc,
        DateTimeOffset? updatedBeforeUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Memory list limit must be between 1 and 500.");
        }

        await EnsureInitializedAsync(cancellationToken);
        var items = new List<TalvoraMemoryItem>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, scope, project, session, category, title, content,
                   importance, confidence, source, source_ref,
                   created_utc, updated_utc, expires_utc, superseded_by,
                   retention_class, claim_key
            FROM memory_items
            WHERE superseded_by IS NULL
              AND (expires_utc IS NULL OR expires_utc > $now)
              AND ($scope IS NULL OR scope = $scope)
              AND ($project IS NULL OR project = $project)
              AND ($category IS NULL OR category = $category)
              AND ($after IS NULL OR updated_utc >= $after)
              AND ($before IS NULL OR updated_utc <= $before)
            ORDER BY updated_utc DESC, id
            LIMIT $limit;
            """;
        Add(command, "$now", Format(DateTimeOffset.UtcNow));
        Add(command, "$scope", NormalizeOptional(scope));
        Add(command, "$project", NormalizeProjectIdentity(project));
        Add(command, "$category", NormalizeOptional(category));
        Add(
            command,
            "$after",
            updatedAfterUtc is null ? null : Format(updatedAfterUtc.Value));
        Add(
            command,
            "$before",
            updatedBeforeUtc is null ? null : Format(updatedBeforeUtc.Value));
        Add(command, "$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadItem(reader));
        }

        return new TalvoraMemoryListResult(items.Count, items);
    }
}
