using Microsoft.Data.Sqlite;

namespace Talvora.Memory;

public sealed partial class TalvoraMemoryStore
{
    private static readonly HashSet<string> ValidRetentionClasses =
        new(StringComparer.Ordinal)
        {
            "ephemeral",
            "session",
            "short",
            "standard",
            "durable",
            "permanent",
        };

    private static string ResolveRetentionClass(
        string scope,
        string category,
        string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var normalized = requested.Trim().ToLowerInvariant();
            if (!ValidRetentionClasses.Contains(normalized))
            {
                throw new ArgumentException(
                    $"Unsupported retention class '{requested}'.",
                    nameof(requested));
            }
            return normalized;
        }

        if (string.Equals(scope, "session", StringComparison.Ordinal))
        {
            return "session";
        }

        return category switch
        {
            "todo" => "short",
            "error" or "solution" or "fact" => "standard",
            _ => "durable",
        };
    }

    private static DateTimeOffset? ResolveExpiry(
        DateTimeOffset now,
        string retentionClass,
        DateTimeOffset? explicitExpiry) =>
        explicitExpiry ?? retentionClass switch
        {
            "ephemeral" => now.AddDays(1),
            "session" => now.AddDays(7),
            "short" => now.AddDays(30),
            "standard" => now.AddDays(180),
            "durable" or "permanent" => null,
            _ => throw new InvalidOperationException(
                $"Unhandled retention class '{retentionClass}'."),
        };

    private static string? NormalizeProjectIdentity(string? project)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return null;
        }

        var value = project.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            !string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
        {
            var path = uri.AbsolutePath
                .Replace('\\', '/')
                .TrimEnd('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^4];
            }

            return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{path.ToLowerInvariant()}";
        }

        var normalized = value.Replace('\\', '/').TrimEnd('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        normalized = string.Join(
            ' ',
            normalized.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
        return normalized.ToLowerInvariant();
    }

    private static string? NormalizeClaimKey(string? claimKey) =>
        string.IsNullOrWhiteSpace(claimKey)
            ? null
            : string.Join(
                    ' ',
                    claimKey.Split(
                        (char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries))
                .ToLowerInvariant();

    private async Task EnsureQualityColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(memory_items);";
            await using var reader =
                await pragma.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existing.Add(reader.GetString(1));
            }
        }

        if (!existing.Contains("retention_class"))
        {
            await using var addRetention = connection.CreateCommand();
            addRetention.CommandText =
                "ALTER TABLE memory_items ADD COLUMN retention_class TEXT NOT NULL DEFAULT 'durable';";
            await addRetention.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!existing.Contains("claim_key"))
        {
            await using var addClaim = connection.CreateCommand();
            addClaim.CommandText =
                "ALTER TABLE memory_items ADD COLUMN claim_key TEXT NULL;";
            await addClaim.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var indexes = connection.CreateCommand();
        indexes.CommandText =
            """
            CREATE INDEX IF NOT EXISTS ix_memory_items_claim
                ON memory_items(scope, project, session, claim_key)
                WHERE claim_key IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_memory_items_retention
                ON memory_items(retention_class, expires_utc);
            """;
        await indexes.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task NormalizeExistingProjectsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var changes = new List<(string Id, string Project)>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText =
                "SELECT id, project FROM memory_items WHERE project IS NOT NULL;";
            await using var reader =
                await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                var current = reader.GetString(1);
                var normalized = NormalizeProjectIdentity(current);
                if (normalized is not null &&
                    !string.Equals(
                        current,
                        normalized,
                        StringComparison.Ordinal))
                {
                    changes.Add((id, normalized));
                }
            }
        }

        if (changes.Count == 0)
        {
            return;
        }

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        foreach (var change in changes)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText =
                """
                UPDATE memory_items
                SET project = $project,
                    updated_utc = $updatedUtc
                WHERE id = $id;
                """;
            Add(update, "$project", change.Project);
            Add(update, "$updatedUtc", Format(DateTimeOffset.UtcNow));
            Add(update, "$id", change.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SuppressStaleClaimsAsync(
        TalvoraMemoryItem current,
        CancellationToken cancellationToken)
    {
        if (current.ClaimKey is null)
        {
            return;
        }

        var authority = GetSourceAuthority(current.Source);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE memory_items
            SET superseded_by = $currentId,
                updated_utc = $updatedUtc
            WHERE id <> $currentId
              AND superseded_by IS NULL
              AND scope = $scope
              AND COALESCE(project, '') = COALESCE($project, '')
              AND COALESCE(session, '') = COALESCE($session, '')
              AND claim_key = $claimKey
              AND (
                    CASE
                        WHEN lower(COALESCE(source, '')) IN
                            ('runtime', 'repository', 'repo', 'git', 'health', 'filesystem')
                            THEN 1.0
                        WHEN lower(COALESCE(source, '')) IN
                            ('user', 'explicit-user', 'user-instruction')
                            THEN 0.95
                        WHEN lower(COALESCE(source, '')) IN
                            ('handoff', 'project-doc', 'project-document')
                            THEN 0.90
                        WHEN lower(COALESCE(source, '')) IN
                            ('tool', 'verified', 'test', 'smoke')
                            THEN 0.75
                        WHEN lower(COALESCE(source, '')) IN
                            ('inferred', 'auto', 'model')
                            THEN 0.40
                        ELSE 0.50
                    END
                  ) < $authority;
            """;
        Add(command, "$currentId", current.Id);
        Add(command, "$updatedUtc", Format(DateTimeOffset.UtcNow));
        Add(command, "$scope", current.Scope);
        Add(command, "$project", current.Project);
        Add(command, "$session", current.Session);
        Add(command, "$claimKey", current.ClaimKey);
        Add(command, "$authority", authority);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
