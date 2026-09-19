using ModelContextProtocol.Client;
using static SmokeSupport;

internal static partial class SmokeScenarios
{
    internal static async Task RunSqliteAsync(IReadOnlyDictionary<string, McpClientTool> byName, string smokeId)
    {
        var sqliteInfoResult = await EnsureSuccess(byName["talvora_sqlite_info"], new());
        if (sqliteInfoResult.StructuredContent is not { } sqliteInfoJson ||
            string.IsNullOrWhiteSpace(sqliteInfoJson.GetProperty("providerVersion").GetString()) ||
            string.IsNullOrWhiteSpace(sqliteInfoJson.GetProperty("sqliteVersion").GetString()) ||
            string.IsNullOrWhiteSpace(sqliteInfoJson.GetProperty("sqliteSourceId").GetString()))
        {
            throw new InvalidOperationException("sqlite info did not return provider/native version metadata.");
        }
        
        var sqliteSmokeDirectory = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Sqlite-Smoke-" + smokeId);
        var sqliteDatabasePath = Path.Combine(sqliteSmokeDirectory, "app.db");
        var sqliteBackupPath = Path.Combine(sqliteSmokeDirectory, "app.backup.db");
        Directory.CreateDirectory(sqliteSmokeDirectory);
        
        try
        {
            var createTableResult = await EnsureSuccess(byName["talvora_sqlite_execute"], new()
            {
                ["databasePath"] = sqliteDatabasePath,
                ["sql"] = "CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, score INTEGER NOT NULL, metadata TEXT NULL);",
                ["transactional"] = true,
                ["createIfMissing"] = true,
            });
            if (createTableResult.StructuredContent is not { } createTableJson ||
                !createTableJson.GetProperty("transactional").GetBoolean())
            {
                throw new InvalidOperationException("sqlite execute did not create the smoke table transactionally.");
            }
        
            var firstInsertResult = await EnsureSuccess(byName["talvora_sqlite_execute"], new()
            {
                ["databasePath"] = sqliteDatabasePath,
                ["sql"] = "INSERT INTO items(name, score, metadata) VALUES ($name, $score, $metadata);",
                ["parameters"] = new Dictionary<string, object?>
                {
                    ["name"] = "alpha",
                    ["score"] = 7,
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["source"] = "smoke",
                        ["active"] = true,
                    },
                },
            });
            if (firstInsertResult.StructuredContent is not { } firstInsertJson ||
                firstInsertJson.GetProperty("changes").GetInt64() != 1 ||
                firstInsertJson.GetProperty("lastInsertRowId").GetInt64() <= 0)
            {
                throw new InvalidOperationException("sqlite first parameterized insert did not report one change.");
            }
        
            var secondInsertResult = await EnsureSuccess(byName["talvora_sqlite_execute"], new()
            {
                ["databasePath"] = sqliteDatabasePath,
                ["sql"] = "INSERT INTO items(name, score, metadata) VALUES ($name, $score, NULL);",
                ["parameters"] = new Dictionary<string, object?>
                {
                    ["$name"] = "beta",
                    ["$score"] = 12,
                },
            });
            if (secondInsertResult.StructuredContent is not { } secondInsertJson ||
                secondInsertJson.GetProperty("changes").GetInt64() != 1)
            {
                throw new InvalidOperationException("sqlite second parameterized insert did not report one change.");
            }
        
            var queryResult = await EnsureSuccess(byName["talvora_sqlite_query"], new()
            {
                ["databasePath"] = sqliteDatabasePath,
                ["sql"] = "SELECT id, name, score, metadata FROM items WHERE score >= $min ORDER BY score;",
                ["parameters"] = new Dictionary<string, object?>
                {
                    ["min"] = 10,
                },
                ["readOnly"] = true,
                ["maxRows"] = 10,
            });
            if (queryResult.StructuredContent is not { } queryJson ||
                queryJson.GetProperty("rowCount").GetInt32() != 1 ||
                queryJson.GetProperty("truncated").GetBoolean())
            {
                throw new InvalidOperationException("sqlite parameterized query returned an unexpected row count.");
            }
        
            var queryRows = queryJson.GetProperty("rows");
            var betaRow = queryRows[0].GetProperty("values");
            if (!string.Equals(
                    betaRow.GetProperty("name").GetProperty("text").GetString(),
                    "beta",
                    StringComparison.Ordinal) ||
                betaRow.GetProperty("score").GetProperty("integer").GetInt64() != 12)
            {
                throw new InvalidOperationException("sqlite query did not preserve typed row values.");
            }
        
            var truncatedQueryResult = await EnsureSuccess(byName["talvora_sqlite_query"], new()
            {
                ["databasePath"] = sqliteDatabasePath,
                ["sql"] = "SELECT id, name FROM items ORDER BY id;",
                ["readOnly"] = true,
                ["maxRows"] = 1,
            });
            if (truncatedQueryResult.StructuredContent is not { } truncatedQueryJson ||
                truncatedQueryJson.GetProperty("rowCount").GetInt32() != 1 ||
                !truncatedQueryJson.GetProperty("truncated").GetBoolean())
            {
                throw new InvalidOperationException("sqlite maxRows truncation contract failed.");
            }
        
            var schemaResult = await EnsureSuccess(byName["talvora_sqlite_schema"], new()
            {
                ["databasePath"] = sqliteDatabasePath,
                ["objectType"] = "table",
                ["nameLike"] = "item%",
                ["includeInternal"] = false,
            });
            if (schemaResult.StructuredContent is not { } schemaJson ||
                !schemaJson.GetProperty("objects").EnumerateArray().Any(item =>
                    string.Equals(
                        item.GetProperty("name").GetString(),
                        "items",
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("sqlite schema did not return the smoke table.");
            }
        
            var backupResult = await EnsureSuccess(byName["talvora_sqlite_backup"], new()
            {
                ["sourcePath"] = sqliteDatabasePath,
                ["destinationPath"] = sqliteBackupPath,
                ["overwrite"] = false,
            });
            if (backupResult.StructuredContent is not { } backupJson ||
                backupJson.GetProperty("length").GetInt64() <= 0 ||
                backupJson.GetProperty("sha256").GetString()?.Length != 64 ||
                !File.Exists(sqliteBackupPath))
            {
                throw new InvalidOperationException("sqlite online backup did not produce a valid backup file.");
            }
        
            var backupQueryResult = await EnsureSuccess(byName["talvora_sqlite_query"], new()
            {
                ["databasePath"] = sqliteBackupPath,
                ["sql"] = "SELECT COUNT(*) AS item_count FROM items;",
                ["readOnly"] = true,
            });
            if (backupQueryResult.StructuredContent is not { } backupQueryJson ||
                backupQueryJson.GetProperty("rowCount").GetInt32() != 1 ||
                backupQueryJson.GetProperty("rows")[0]
                    .GetProperty("values")
                    .GetProperty("item_count")
                    .GetProperty("integer")
                    .GetInt64() != 2)
            {
                throw new InvalidOperationException("sqlite backup verification query returned an unexpected count.");
            }
        }
        finally
        {
            try
            {
                if (File.Exists(sqliteBackupPath))
                {
                    File.Delete(sqliteBackupPath);
                }
        
                if (File.Exists(sqliteDatabasePath))
                {
                    File.Delete(sqliteDatabasePath);
                }
        
                if (Directory.Exists(sqliteSmokeDirectory))
                {
                    Directory.Delete(sqliteSmokeDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
