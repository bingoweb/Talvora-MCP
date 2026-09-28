using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Talvora.Memory;

public sealed class TalvoraMemoryStore
{
    private const int BusyTimeoutMilliseconds = 5000;
    private static readonly HashSet<string> ValidScopes =
        new(StringComparer.Ordinal) { "global", "user", "project", "session" };
    private static readonly HashSet<string> ValidCategories =
        new(StringComparer.Ordinal)
        {
            "decision", "preference", "lesson", "error", "solution",
            "architecture", "workflow", "todo", "fact",
        };

    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private volatile bool initialized;

    public TalvoraMemoryStore(string? databasePath = null)
    {
        DatabasePath = string.IsNullOrWhiteSpace(databasePath)
            ? GetDefaultDatabasePath()
            : Path.GetFullPath(databasePath);
    }

    public string DatabasePath { get; }

    public async Task<TalvoraMemoryItem> RememberAsync(
        string scope,
        string category,
        string title,
        string content,
        string? project,
        string? session,
        double importance,
        double confidence,
        string? source,
        string? sourceReference,
        DateTimeOffset? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        ValidateScope(scope);
        ValidateCategory(category);
        ValidateText(title, nameof(title), 512);
        ValidateText(content, nameof(content), 256 * 1024);
        ValidateScore(importance, nameof(importance));
        ValidateScore(confidence, nameof(confidence));
        await EnsureInitializedAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO memory_items(
                id, scope, project, session, category, title, content,
                importance, confidence, source, source_ref,
                created_utc, updated_utc, expires_utc)
            VALUES(
                $id, $scope, $project, $session, $category, $title, $content,
                $importance, $confidence, $source, $sourceRef,
                $createdUtc, $updatedUtc, $expiresUtc);
            """;
        Add(command, "$id", id);
        Add(command, "$scope", scope);
        Add(command, "$project", NormalizeOptional(project));
        Add(command, "$session", NormalizeOptional(session));
        Add(command, "$category", category);
        Add(command, "$title", title.Trim());
        Add(command, "$content", content.Trim());
        Add(command, "$importance", importance);
        Add(command, "$confidence", confidence);
        Add(command, "$source", NormalizeOptional(source));
        Add(command, "$sourceRef", NormalizeOptional(sourceReference));
        Add(command, "$createdUtc", Format(now));
        Add(command, "$updatedUtc", Format(now));
        Add(command, "$expiresUtc", expiresAtUtc is null ? null : Format(expiresAtUtc.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return (await GetAsync(id, cancellationToken))!;
    }

    public async Task<TalvoraMemoryItem?> GetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, scope, project, session, category, title, content,
                   importance, confidence, source, source_ref,
                   created_utc, updated_utc, expires_utc, superseded_by
            FROM memory_items
            WHERE id = $id;
            """;
        Add(command, "$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadItem(reader) : null;
    }

    public async Task<TalvoraMemorySearchResult> SearchAsync(
        string query,
        string? scope,
        string? project,
        string? session,
        string? category,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Memory search query cannot be empty.", nameof(query));
        }
        if (scope is not null) ValidateScope(scope);
        if (category is not null) ValidateCategory(category);
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Memory search limit must be between 1 and 100.");
        }

        await EnsureInitializedAsync(cancellationToken);
        var hits = new List<TalvoraMemorySearchHit>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT m.id, m.scope, m.project, m.session, m.category, m.title, m.content,
                   m.importance, m.confidence, m.source, m.source_ref,
                   m.created_utc, m.updated_utc, m.expires_utc, m.superseded_by,
                   bm25(memory_items_fts, 2.0, 1.0, 0.4, 0.4, 0.2) AS lexical_rank,
                   CASE
                       WHEN lower(COALESCE(m.source, '')) IN
                           ('runtime', 'repository', 'repo', 'git', 'health', 'filesystem')
                           THEN 1.0
                       WHEN lower(COALESCE(m.source, '')) IN
                           ('user', 'explicit-user', 'user-instruction')
                           THEN 0.95
                       WHEN lower(COALESCE(m.source, '')) IN
                           ('handoff', 'project-doc', 'project-document')
                           THEN 0.90
                       WHEN lower(COALESCE(m.source, '')) IN
                           ('tool', 'verified', 'test', 'smoke')
                           THEN 0.75
                       WHEN lower(COALESCE(m.source, '')) IN
                           ('inferred', 'auto', 'model')
                           THEN 0.40
                       ELSE 0.50
                   END AS source_authority
            FROM memory_items_fts
            JOIN memory_items AS m ON m.row_id = memory_items_fts.rowid
            WHERE memory_items_fts MATCH $query
              AND ($scope IS NULL OR m.scope = $scope)
              AND ($project IS NULL OR m.project = $project)
              AND ($session IS NULL OR m.session = $session)
              AND ($category IS NULL OR m.category = $category)
              AND (m.expires_utc IS NULL OR m.expires_utc > $now)
              AND m.superseded_by IS NULL
            ORDER BY lexical_rank ASC,
                     source_authority DESC,
                     m.confidence DESC,
                     m.importance DESC,
                     m.updated_utc DESC
            LIMIT $limit;
            """;
        Add(command, "$query", BuildFtsQuery(query));
        Add(command, "$scope", NormalizeOptional(scope));
        Add(command, "$project", NormalizeOptional(project));
        Add(command, "$session", NormalizeOptional(session));
        Add(command, "$category", NormalizeOptional(category));
        Add(command, "$now", Format(DateTimeOffset.UtcNow));
        Add(command, "$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(
                new TalvoraMemorySearchHit(
                    ReadItem(reader),
                    reader.GetDouble(15),
                    reader.GetDouble(16)));
        }
        return new TalvoraMemorySearchResult(query.Trim(), hits.Count, hits);
    }

    public async Task<TalvoraMemoryItem?> UpdateAsync(
        string id,
        string? category,
        string? title,
        string? content,
        double? importance,
        double? confidence,
        string? source,
        string? sourceReference,
        DateTimeOffset? expiresAtUtc,
        string? supersededBy,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        if (category is not null) ValidateCategory(category);
        if (title is not null) ValidateText(title, nameof(title), 512);
        if (content is not null) ValidateText(content, nameof(content), 256 * 1024);
        if (importance is not null) ValidateScore(importance.Value, nameof(importance));
        if (confidence is not null) ValidateScore(confidence.Value, nameof(confidence));
        if (supersededBy is not null)
        {
            ValidateId(supersededBy);
            if (string.Equals(id, supersededBy, StringComparison.Ordinal))
            {
                throw new ArgumentException("A memory cannot supersede itself.", nameof(supersededBy));
            }
        }

        await EnsureInitializedAsync(cancellationToken);
        if (await GetAsync(id, cancellationToken) is null) return null;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (supersededBy is not null)
        {
            await using var verify = connection.CreateCommand();
            verify.Transaction = (SqliteTransaction)transaction;
            verify.CommandText = "SELECT COUNT(*) FROM memory_items WHERE id = $id;";
            Add(verify, "$id", supersededBy);
            var count = Convert.ToInt64(
                await verify.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            if (count == 0)
            {
                throw new ArgumentException(
                    "The superseding memory was not found.",
                    nameof(supersededBy));
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            UPDATE memory_items
            SET category = COALESCE($category, category),
                title = COALESCE($title, title),
                content = COALESCE($content, content),
                importance = COALESCE($importance, importance),
                confidence = COALESCE($confidence, confidence),
                source = COALESCE($source, source),
                source_ref = COALESCE($sourceRef, source_ref),
                expires_utc = COALESCE($expiresUtc, expires_utc),
                superseded_by = COALESCE($supersededBy, superseded_by),
                updated_utc = $updatedUtc
            WHERE id = $id;
            """;
        Add(command, "$category", NormalizeOptional(category));
        Add(command, "$title", title?.Trim());
        Add(command, "$content", content?.Trim());
        Add(command, "$importance", importance);
        Add(command, "$confidence", confidence);
        Add(command, "$source", NormalizeOptional(source));
        Add(command, "$sourceRef", NormalizeOptional(sourceReference));
        Add(command, "$expiresUtc", expiresAtUtc is null ? null : Format(expiresAtUtc.Value));
        Add(command, "$supersededBy", NormalizeOptional(supersededBy));
        Add(command, "$updatedUtc", Format(DateTimeOffset.UtcNow));
        Add(command, "$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> ForgetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM memory_items WHERE id = $id;";
        Add(command, "$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<TalvoraMemorySupersedeResult> SupersedeAsync(
        string staleId,
        string replacementId,
        CancellationToken cancellationToken)
    {
        ValidateId(staleId);
        ValidateId(replacementId);
        if (string.Equals(staleId, replacementId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A memory cannot supersede itself.",
                nameof(replacementId));
        }

        await EnsureInitializedAsync(cancellationToken);
        var stale = await GetAsync(staleId, cancellationToken);
        var replacement = await GetAsync(replacementId, cancellationToken);
        if (stale is null || replacement is null)
        {
            return new TalvoraMemorySupersedeResult(false, stale, replacement);
        }

        if (!SameBoundary(stale, replacement))
        {
            throw new InvalidOperationException(
                "Supersession requires the same memory scope/project/session boundary.");
        }

        if (replacement.SupersededBy is not null)
        {
            throw new InvalidOperationException(
                "The replacement memory is already superseded.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE memory_items
            SET superseded_by = $replacementId,
                updated_utc = $updatedUtc
            WHERE id = $staleId;
            """;
        Add(command, "$replacementId", replacementId);
        Add(command, "$updatedUtc", Format(DateTimeOffset.UtcNow));
        Add(command, "$staleId", staleId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new TalvoraMemorySupersedeResult(
            true,
            await GetAsync(staleId, cancellationToken),
            replacement);
    }

    public async Task<TalvoraMemoryConsolidateResult> ConsolidateAsync(
        string? scope,
        string? project,
        string? session,
        string? category,
        int maxScan,
        CancellationToken cancellationToken)
    {
        if (scope is not null) ValidateScope(scope);
        if (category is not null) ValidateCategory(category);
        if (maxScan is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxScan),
                "Memory consolidation maxScan must be between 1 and 10000.");
        }

        await EnsureInitializedAsync(cancellationToken);
        var items = new List<TalvoraMemoryItem>();
        await using (var connection = await OpenConnectionAsync(cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT id, scope, project, session, category, title, content,
                       importance, confidence, source, source_ref,
                       created_utc, updated_utc, expires_utc, superseded_by
                FROM memory_items
                WHERE superseded_by IS NULL
                  AND (expires_utc IS NULL OR expires_utc > $now)
                  AND ($scope IS NULL OR scope = $scope)
                  AND ($project IS NULL OR project = $project)
                  AND ($session IS NULL OR session = $session)
                  AND ($category IS NULL OR category = $category)
                ORDER BY updated_utc DESC
                LIMIT $limit;
                """;
            Add(command, "$now", Format(DateTimeOffset.UtcNow));
            Add(command, "$scope", NormalizeOptional(scope));
            Add(command, "$project", NormalizeOptional(project));
            Add(command, "$session", NormalizeOptional(session));
            Add(command, "$category", NormalizeOptional(category));
            Add(command, "$limit", maxScan);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(ReadItem(reader));
            }
        }

        var duplicateSets = items
            .GroupBy(BuildDuplicateKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();
        var groups = new List<TalvoraMemoryConsolidationGroup>();

        await using var writeConnection =
            await OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await writeConnection.BeginTransactionAsync(cancellationToken);
        foreach (var duplicateSet in duplicateSets)
        {
            var ordered = duplicateSet
                .OrderByDescending(item => GetSourceAuthority(item.Source))
                .ThenByDescending(item => item.Confidence)
                .ThenByDescending(item => item.Importance)
                .ThenByDescending(item => item.UpdatedAtUtc)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
            var winner = ordered[0];
            var losers = ordered.Skip(1).ToArray();

            foreach (var loser in losers)
            {
                await using var update = writeConnection.CreateCommand();
                update.Transaction = (SqliteTransaction)transaction;
                update.CommandText =
                    """
                    UPDATE memory_items
                    SET superseded_by = $winnerId,
                        updated_utc = $updatedUtc
                    WHERE id = $loserId
                      AND superseded_by IS NULL;
                    """;
                Add(update, "$winnerId", winner.Id);
                Add(update, "$updatedUtc", Format(DateTimeOffset.UtcNow));
                Add(update, "$loserId", loser.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            groups.Add(
                new TalvoraMemoryConsolidationGroup(
                    winner.Id,
                    losers.Select(item => item.Id).ToArray()));
        }
        await transaction.CommitAsync(cancellationToken);

        return new TalvoraMemoryConsolidateResult(
            items.Count,
            groups.Count,
            groups.Sum(group => group.SupersededIds.Count),
            groups);
    }

    public async Task<TalvoraMemoryDiagnosticsResult> DiagnosticsAsync(
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        string integrity;
        var all = new List<TalvoraMemoryItem>();
        await using (var connection = await OpenConnectionAsync(cancellationToken))
        {
            await using (var quickCheck = connection.CreateCommand())
            {
                quickCheck.CommandText = "PRAGMA quick_check;";
                integrity =
                    Convert.ToString(
                        await quickCheck.ExecuteScalarAsync(cancellationToken),
                        CultureInfo.InvariantCulture)
                    ?? "unknown";
            }

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, scope, project, session, category, title, content,
                       importance, confidence, source, source_ref,
                       created_utc, updated_utc, expires_utc, superseded_by
                FROM memory_items;
                """;
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                all.Add(ReadItem(reader));
            }
        }

        var now = DateTimeOffset.UtcNow;
        var active = all
            .Where(item =>
                item.SupersededBy is null &&
                (item.ExpiresAtUtc is null || item.ExpiresAtUtc > now))
            .ToArray();
        var duplicateGroups = active
            .GroupBy(BuildDuplicateKey, StringComparer.Ordinal)
            .Count(group => group.Count() > 1);
        var file = new FileInfo(DatabasePath);

        return new TalvoraMemoryDiagnosticsResult(
            DatabasePath,
            file.Exists ? file.Length : 0,
            integrity,
            all.Count,
            active.Length,
            all.Count(item =>
                item.ExpiresAtUtc is not null &&
                item.ExpiresAtUtc <= now),
            all.Count(item => item.SupersededBy is not null),
            duplicateGroups);
    }

    public async Task<TalvoraMemoryContextResult> ContextAsync(
        string query,
        string? scope,
        string? project,
        string? session,
        string? category,
        int maxItems,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        if (maxItems is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxItems),
                "Memory context maxItems must be between 1 and 50.");
        }
        if (maxCharacters is < 256 or > 64 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharacters),
                "Memory context maxCharacters must be between 256 and 65536.");
        }

        var search = await SearchAsync(
            query, scope, project, session, category, maxItems, cancellationToken);
        var included = new List<TalvoraMemorySearchHit>();
        var builder = new StringBuilder();
        foreach (var hit in search.Items)
        {
            var item = hit.Item;
            var section =
                $"[{item.Scope}/{item.Category}] {item.Title}\n{item.Content}\n" +
                $"memory_id={item.Id}; confidence={item.Confidence:0.##}; " +
                $"importance={item.Importance:0.##}\n\n";
            if (builder.Length + section.Length > maxCharacters) break;
            builder.Append(section);
            included.Add(hit);
        }
        return new TalvoraMemoryContextResult(
            search.Query,
            included.Count,
            maxCharacters,
            builder.ToString().TrimEnd(),
            included);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized) return;
        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;
                PRAGMA wal_autocheckpoint = 1000;

                CREATE TABLE IF NOT EXISTS memory_items(
                    row_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    id TEXT NOT NULL UNIQUE,
                    scope TEXT NOT NULL,
                    project TEXT NULL,
                    session TEXT NULL,
                    category TEXT NOT NULL,
                    title TEXT NOT NULL,
                    content TEXT NOT NULL,
                    importance REAL NOT NULL DEFAULT 0.5,
                    confidence REAL NOT NULL DEFAULT 1.0,
                    source TEXT NULL,
                    source_ref TEXT NULL,
                    created_utc TEXT NOT NULL,
                    updated_utc TEXT NOT NULL,
                    expires_utc TEXT NULL,
                    superseded_by TEXT NULL,
                    FOREIGN KEY(superseded_by) REFERENCES memory_items(id)
                        ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS ix_memory_items_scope_project
                    ON memory_items(scope, project);
                CREATE INDEX IF NOT EXISTS ix_memory_items_category
                    ON memory_items(category);
                CREATE INDEX IF NOT EXISTS ix_memory_items_updated
                    ON memory_items(updated_utc DESC);
                CREATE INDEX IF NOT EXISTS ix_memory_items_expires
                    ON memory_items(expires_utc);

                CREATE VIRTUAL TABLE IF NOT EXISTS memory_items_fts USING fts5(
                    title,
                    content,
                    category,
                    project,
                    source,
                    content='memory_items',
                    content_rowid='row_id',
                    tokenize='unicode61 remove_diacritics 2',
                    prefix='2 3 4'
                );
                CREATE TRIGGER IF NOT EXISTS memory_items_ai
                AFTER INSERT ON memory_items BEGIN
                    INSERT INTO memory_items_fts(
                        rowid, title, content, category, project, source)
                    VALUES (
                        new.row_id, new.title, new.content,
                        new.category, new.project, new.source);
                END;
                CREATE TRIGGER IF NOT EXISTS memory_items_ad
                AFTER DELETE ON memory_items BEGIN
                    INSERT INTO memory_items_fts(
                        memory_items_fts, rowid, title, content,
                        category, project, source)
                    VALUES (
                        'delete', old.row_id, old.title, old.content,
                        old.category, old.project, old.source);
                END;
                CREATE TRIGGER IF NOT EXISTS memory_items_au
                AFTER UPDATE ON memory_items BEGIN
                    INSERT INTO memory_items_fts(
                        memory_items_fts, rowid, title, content,
                        category, project, source)
                    VALUES (
                        'delete', old.row_id, old.title, old.content,
                        old.category, old.project, old.source);
                    INSERT INTO memory_items_fts(
                        rowid, title, content, category, project, source)
                    VALUES (
                        new.row_id, new.title, new.content,
                        new.category, new.project, new.source);
                END;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            await using var rebuild = connection.CreateCommand();
            rebuild.CommandText =
                "INSERT INTO memory_items_fts(memory_items_fts) VALUES('rebuild');";
            await rebuild.ExecuteNonQueryAsync(cancellationToken);
            initialized = true;
        }
        finally
        {
            initializationGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = BusyTimeoutMilliseconds / 1000,
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds}; " +
            "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static TalvoraMemoryItem ReadItem(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetDouble(7),
            reader.GetDouble(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            Parse(reader.GetString(11)),
            Parse(reader.GetString(12)),
            reader.IsDBNull(13) ? null : Parse(reader.GetString(13)),
            reader.IsDBNull(14) ? null : reader.GetString(14));

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    public static double GetSourceAuthority(string? source) =>
        source?.Trim().ToLowerInvariant() switch
        {
            "runtime" or "repository" or "repo" or "git" or
                "health" or "filesystem" => 1.0,
            "user" or "explicit-user" or "user-instruction" => 0.95,
            "handoff" or "project-doc" or "project-document" => 0.90,
            "tool" or "verified" or "test" or "smoke" => 0.75,
            "inferred" or "auto" or "model" => 0.40,
            _ => 0.50,
        };

    private static bool SameBoundary(
        TalvoraMemoryItem left,
        TalvoraMemoryItem right) =>
        string.Equals(left.Scope, right.Scope, StringComparison.Ordinal) &&
        string.Equals(left.Project, right.Project, StringComparison.Ordinal) &&
        string.Equals(left.Session, right.Session, StringComparison.Ordinal);

    private static string BuildDuplicateKey(TalvoraMemoryItem item) =>
        string.Join(
            "\u001F",
            item.Scope,
            item.Project ?? string.Empty,
            item.Session ?? string.Empty,
            item.Category,
            NormalizeDuplicateText(item.Title),
            NormalizeDuplicateText(item.Content));

    private static string NormalizeDuplicateText(string value) =>
        string.Join(
                ' ',
                value.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries))
            .ToUpperInvariant();

    private static string BuildFtsQuery(string query)
    {
        var tokens = query
            .Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(token => token.Replace("\"", "\"\"", StringComparison.Ordinal))
            .Where(token => token.Length > 0)
            .Take(32)
            .Select(token => $"\"{token}\"*")
            .ToArray();
        if (tokens.Length == 0)
        {
            throw new ArgumentException(
                "Memory search query contains no searchable terms.",
                nameof(query));
        }
        return string.Join(" AND ", tokens);
    }

    private static void ValidateScope(string value)
    {
        if (!ValidScopes.Contains(value))
        {
            throw new ArgumentException(
                $"Unsupported memory scope '{value}'.",
                nameof(value));
        }
    }

    private static void ValidateCategory(string value)
    {
        if (!ValidCategories.Contains(value))
        {
            throw new ArgumentException(
                $"Unsupported memory category '{value}'.",
                nameof(value));
        }
    }

    private static void ValidateText(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw new ArgumentException(
                $"{name} must contain 1-{maxLength} characters.",
                name);
        }
    }

    private static void ValidateScore(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                name,
                "Memory scores must be between 0 and 1.");
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
        {
            throw new ArgumentException("Memory id is invalid.", nameof(id));
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static string GetDefaultDatabasePath()
    {
        var overridePath =
            Environment.GetEnvironmentVariable("TALVORA_MEMORY_DB");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "Talvora",
            "memory",
            "talvora-memory.db");
    }
}

