using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraSqliteInfoResponse(
    string ProviderVersion,
    [property: JsonPropertyName("sqliteVersion")] string SQLiteVersion,
    [property: JsonPropertyName("sqliteSourceId")] string SQLiteSourceId);

public sealed record TalvoraSqliteValue(
    string StorageClass,
    string? Text,
    long? Integer,
    double? Real,
    string? Base64);

public sealed record TalvoraSqliteRow(
    IReadOnlyDictionary<string, TalvoraSqliteValue> Values);

public sealed record TalvoraSqliteQueryResponse(
    string Database,
    string Sql,
    bool ReadOnly,
    IReadOnlyList<string> Columns,
    int RowCount,
    bool Truncated,
    IReadOnlyList<TalvoraSqliteRow> Rows);

public sealed record TalvoraSqliteExecuteResponse(
    string Database,
    string Sql,
    bool Transactional,
    int RowsAffected,
    long Changes,
    long LastInsertRowId);

public sealed record TalvoraSqliteSchemaObject(
    string Type,
    string Name,
    string TableName,
    string? Sql);

public sealed record TalvoraSqliteSchemaResponse(
    string Database,
    int Count,
    IReadOnlyList<TalvoraSqliteSchemaObject> Objects);

public sealed record TalvoraSqliteBackupResponse(
    string SourcePath,
    string DestinationPath,
    long Length,
    string Sha256);

[McpServerToolType]
public static class SqliteTools
{
    [McpServerTool(
        Name = "talvora_sqlite_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraSqliteInfoResponse)),
     Description("Report the bundled Microsoft.Data.Sqlite provider and native SQLite versions used by Talvora.")]
    public static async Task<TalvoraSqliteInfoResponse> Info(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(
            ":memory:",
            SqliteOpenMode.Memory,
            30);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version(), sqlite_source_id();";

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "SQLite version probe returned no row.");
        }

        var providerAssembly = typeof(SqliteConnection).Assembly;
        var providerVersion =
            providerAssembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? providerAssembly.GetName().Version?.ToString()
            ?? "unknown";

        return new TalvoraSqliteInfoResponse(
            providerVersion,
            reader.GetString(0),
            reader.GetString(1));
    }

    [McpServerTool(
        Name = "talvora_sqlite_query",
        Destructive = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraSqliteQueryResponse)),
     Description("Execute arbitrary SQLite SQL that returns rows against any accessible database. Named parameters are bound as values; readOnly=true opens the database in SQLite read-only mode. Set maxRows=0 for unlimited returned rows.")]
    public static async Task<TalvoraSqliteQueryResponse> Query(
        string databasePath,
        string sql,
        Dictionary<string, JsonElement>? parameters = null,
        bool readOnly = true,
        int maxRows = 1000,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        if (maxRows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRows));
        }

        ValidateTimeout(timeoutSeconds);

        var database = NormalizeDatabaseSource(databasePath);
        var mode = database == ":memory:"
            ? SqliteOpenMode.Memory
            : readOnly
                ? SqliteOpenMode.ReadOnly
                : SqliteOpenMode.ReadWriteCreate;

        await using var connection =
            CreateConnection(database, mode, timeoutSeconds);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = timeoutSeconds;
        BindParameters(command, parameters);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var columns = BuildColumnNames(reader);
        var rows = new List<TalvoraSqliteRow>();
        var truncated = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (maxRows > 0 && rows.Count >= maxRows)
            {
                truncated = true;
                break;
            }

            var values =
                new Dictionary<string, TalvoraSqliteValue>(
                    columns.Count,
                    StringComparer.Ordinal);

            for (var index = 0; index < columns.Count; index++)
            {
                values[columns[index]] =
                    ConvertDatabaseValue(reader.GetValue(index));
            }

            rows.Add(new TalvoraSqliteRow(values));
        }

        return new TalvoraSqliteQueryResponse(
            database,
            sql,
            readOnly,
            columns,
            rows.Count,
            truncated,
            rows);
    }

    [McpServerTool(
        Name = "talvora_sqlite_execute",
        Destructive = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraSqliteExecuteResponse)),
     Description("Execute arbitrary SQLite write/DDL SQL against any accessible database with named parameter binding. transactional=true wraps the command in one ADO.NET transaction; createIfMissing controls whether a missing database file may be created.")]
    public static async Task<TalvoraSqliteExecuteResponse> Execute(
        string databasePath,
        string sql,
        Dictionary<string, JsonElement>? parameters = null,
        bool transactional = true,
        bool createIfMissing = true,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        ValidateTimeout(timeoutSeconds);

        var database = NormalizeDatabaseSource(databasePath);
        var mode = database == ":memory:"
            ? SqliteOpenMode.Memory
            : createIfMissing
                ? SqliteOpenMode.ReadWriteCreate
                : SqliteOpenMode.ReadWrite;

        await using var connection =
            CreateConnection(database, mode, timeoutSeconds);
        await connection.OpenAsync(cancellationToken);

        await using SqliteTransaction? transaction = transactional
            ? connection.BeginTransaction()
            : null;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = timeoutSeconds;
        command.Transaction = transaction;
        BindParameters(command, parameters);

        var rowsAffected =
            await command.ExecuteNonQueryAsync(cancellationToken);

        await using var metadata = connection.CreateCommand();
        metadata.CommandText =
            "SELECT changes(), last_insert_rowid();";
        metadata.CommandTimeout = timeoutSeconds;
        metadata.Transaction = transaction;

        await using var reader =
            await metadata.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "SQLite mutation metadata returned no row.");
        }

        var changes = reader.GetInt64(0);
        var lastInsertRowId = reader.GetInt64(1);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new TalvoraSqliteExecuteResponse(
            database,
            sql,
            transactional,
            rowsAffected,
            changes,
            lastInsertRowId);
    }

    [McpServerTool(
        Name = "talvora_sqlite_schema",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraSqliteSchemaResponse)),
     Description("Inspect SQLite schema objects from sqlite_schema for any accessible database. Optional objectType and nameLike filters are parameterized; includeInternal controls sqlite_* objects.")]
    public static async Task<TalvoraSqliteSchemaResponse> Schema(
        string databasePath,
        string? objectType = null,
        string? nameLike = null,
        bool includeInternal = false,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        ValidateTimeout(timeoutSeconds);

        var database = NormalizeDatabaseSource(databasePath);
        await using var connection = CreateConnection(
            database,
            database == ":memory:"
                ? SqliteOpenMode.Memory
                : SqliteOpenMode.ReadOnly,
            timeoutSeconds);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = timeoutSeconds;
        command.CommandText = """
            SELECT type, name, tbl_name, sql
            FROM sqlite_schema
            WHERE ($includeInternal = 1 OR name NOT LIKE 'sqlite_%')
              AND ($objectType IS NULL OR type = $objectType)
              AND ($nameLike IS NULL OR name LIKE $nameLike)
            ORDER BY type, name;
            """;
        command.Parameters.AddWithValue(
            "$includeInternal",
            includeInternal ? 1 : 0);
        command.Parameters.AddWithValue(
            "$objectType",
            objectType is null ? DBNull.Value : objectType);
        command.Parameters.AddWithValue(
            "$nameLike",
            nameLike is null ? DBNull.Value : nameLike);

        var objects = new List<TalvoraSqliteSchemaObject>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            objects.Add(
                new TalvoraSqliteSchemaObject(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3)
                        ? null
                        : reader.GetString(3)));
        }

        return new TalvoraSqliteSchemaResponse(
            database,
            objects.Count,
            objects);
    }

    [McpServerTool(
        Name = "talvora_sqlite_backup",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraSqliteBackupResponse)),
     Description("Create an online SQLite backup from any accessible database file to any accessible destination path using Microsoft.Data.Sqlite BackupDatabase. overwrite=true stages the new backup beside the destination and replaces the existing file only after the backup succeeds.")]
    public static async Task<TalvoraSqliteBackupResponse> Backup(
        string sourcePath,
        string destinationPath,
        bool overwrite = false,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        ValidateTimeout(timeoutSeconds);

        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);

        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                "SQLite source database was not found.",
                source);
        }

        if (string.Equals(
                source,
                destination,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new IOException(
                "SQLite backup source and destination must differ.");
        }

        if (File.Exists(destination) && !overwrite)
        {
            throw new IOException(
                $"SQLite backup destination already exists: {destination}");
        }

        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var temporaryDestination = Path.Combine(
            parent!,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using var sourceConnection = CreateConnection(
                source,
                SqliteOpenMode.ReadOnly,
                timeoutSeconds);
            await sourceConnection.OpenAsync(cancellationToken);

            await using var destinationConnection = CreateConnection(
                temporaryDestination,
                SqliteOpenMode.ReadWriteCreate,
                timeoutSeconds);
            await destinationConnection.OpenAsync(cancellationToken);

            sourceConnection.BackupDatabase(destinationConnection);

            await destinationConnection.CloseAsync();
            await sourceConnection.CloseAsync();

            var length = new FileInfo(temporaryDestination).Length;
            byte[] hash;
            await using (var stream = File.OpenRead(temporaryDestination))
            {
                using var sha256 = SHA256.Create();
                hash =
                    await sha256.ComputeHashAsync(
                        stream,
                        cancellationToken);
            }

            if (File.Exists(destination))
            {
                if (!overwrite)
                {
                    throw new IOException(
                        $"SQLite backup destination already exists: {destination}");
                }

                File.Replace(
                    temporaryDestination,
                    destination,
                    destinationBackupFileName: null);
            }
            else
            {
                File.Move(
                    temporaryDestination,
                    destination);
            }

            return new TalvoraSqliteBackupResponse(
                source,
                destination,
                length,
                Convert.ToHexString(hash));
        }
        finally
        {
            _ = TryDeleteFile(temporaryDestination);
        }
    }

    private static bool TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static SqliteConnection CreateConnection(
        string database,
        SqliteOpenMode mode,
        int timeoutSeconds)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = mode,
            Pooling = false,
            DefaultTimeout = timeoutSeconds,
        };

        return new SqliteConnection(builder.ToString());
    }

    private static string NormalizeDatabaseSource(
        string database)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new ArgumentException(
                "Database path/source is required.",
                nameof(database));
        }

        if (string.Equals(
                database,
                ":memory:",
                StringComparison.Ordinal) ||
            database.StartsWith(
                "file:",
                StringComparison.OrdinalIgnoreCase) ||
            database.StartsWith(
                "|DataDirectory|",
                StringComparison.OrdinalIgnoreCase))
        {
            return database;
        }

        return Path.GetFullPath(database);
    }

    private static void ValidateTimeout(
        int timeoutSeconds)
    {
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeoutSeconds));
        }
    }

    private static void BindParameters(
        SqliteCommand command,
        Dictionary<string, JsonElement>? parameters)
    {
        if (parameters is null)
        {
            return;
        }

        foreach (var pair in parameters)
        {
            command.Parameters.AddWithValue(
                NormalizeParameterName(pair.Key),
                ConvertParameterValue(pair.Value));
        }
    }

    private static string NormalizeParameterName(
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "SQLite parameter names cannot be empty.",
                nameof(name));
        }

        return name[0] is '$' or '@' or ':'
            ? name
            : "$" + name;
    }

    private static object ConvertParameterValue(
        JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined =>
                DBNull.Value,
            JsonValueKind.String =>
                value.GetString() ?? string.Empty,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out var integer) =>
                integer,
            JsonValueKind.Number when value.TryGetDouble(out var real) =>
                real,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Array or JsonValueKind.Object =>
                value.GetRawText(),
            _ => value.GetRawText(),
        };
    }

    private static IReadOnlyList<string> BuildColumnNames(
        SqliteDataReader reader)
    {
        var result = new List<string>(reader.FieldCount);
        var used = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < reader.FieldCount; index++)
        {
            var baseName = reader.GetName(index);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = $"column{index + 1}";
            }

            var name = baseName;
            var suffix = 2;
            while (!used.Add(name))
            {
                name = $"{baseName}#{suffix++}";
            }

            result.Add(name);
        }

        return result;
    }

    private static TalvoraSqliteValue ConvertDatabaseValue(
        object value)
    {
        if (value is DBNull)
        {
            return new TalvoraSqliteValue(
                "null",
                null,
                null,
                null,
                null);
        }

        return value switch
        {
            byte[] bytes =>
                new TalvoraSqliteValue(
                    "blob",
                    null,
                    null,
                    null,
                    Convert.ToBase64String(bytes)),
            sbyte or byte or short or ushort or int or uint or long =>
                new TalvoraSqliteValue(
                    "integer",
                    null,
                    Convert.ToInt64(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    null),
            ulong unsigned when unsigned <= long.MaxValue =>
                new TalvoraSqliteValue(
                    "integer",
                    null,
                    (long)unsigned,
                    null,
                    null),
            ulong unsigned =>
                new TalvoraSqliteValue(
                    "text",
                    unsigned.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    null,
                    null),
            float or double or decimal =>
                new TalvoraSqliteValue(
                    "real",
                    null,
                    null,
                    Convert.ToDouble(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture),
                    null),
            _ =>
                new TalvoraSqliteValue(
                    "text",
                    Convert.ToString(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    null,
                    null),
        };
    }
}
