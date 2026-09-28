using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Talvora.Memory;

public sealed partial class TalvoraMemoryStore
{
    private sealed record PendingRestoreManifest(
        string Sha256,
        long Length,
        DateTimeOffset StagedAtUtc);

    public async Task<TalvoraMemoryBackupResult> BackupAsync(
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var destination = Path.GetFullPath(
            string.IsNullOrWhiteSpace(destinationPath)
                ? throw new ArgumentException(
                    "Memory backup destination cannot be empty.",
                    nameof(destinationPath))
                : destinationPath);
        if (SamePath(DatabasePath, destination))
        {
            throw new IOException(
                "Memory backup destination must differ from the live database.");
        }
        if (File.Exists(destination) && !overwrite)
        {
            throw new IOException(
                $"Memory backup destination already exists: {destination}");
        }

        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new IOException(
                "Memory backup destination requires a parent directory.");
        }
        Directory.CreateDirectory(parent);

        var temporary = Path.Combine(
            parent,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using var source =
                await OpenConnectionAsync(cancellationToken);
            await using var target =
                await OpenExternalConnectionAsync(
                    temporary,
                    SqliteOpenMode.ReadWriteCreate,
                    cancellationToken);
            source.BackupDatabase(target);
            await target.CloseAsync();
            await source.CloseAsync();

            await ValidateMemoryDatabaseAsync(
                temporary,
                cancellationToken);
            var (length, sha256) =
                await GetFileIdentityAsync(
                    temporary,
                    cancellationToken);
            CommitStagedFile(
                temporary,
                destination,
                overwrite);
            return new TalvoraMemoryBackupResult(
                destination,
                length,
                sha256,
                DateTimeOffset.UtcNow);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public async Task<TalvoraMemoryRestoreStageResult> StageRestoreAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var sourceFull = Path.GetFullPath(
            string.IsNullOrWhiteSpace(sourcePath)
                ? throw new ArgumentException(
                    "Memory restore source cannot be empty.",
                    nameof(sourcePath))
                : sourcePath);
        if (!File.Exists(sourceFull))
        {
            throw new FileNotFoundException(
                "Memory restore source was not found.",
                sourceFull);
        }
        if (SamePath(sourceFull, DatabasePath))
        {
            throw new IOException(
                "The live memory database cannot be staged as its own restore source.");
        }

        await ValidateMemoryDatabaseAsync(
            sourceFull,
            cancellationToken);

        var pendingPath = GetPendingRestorePath(DatabasePath);
        var manifestPath = GetPendingRestoreManifestPath(DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        var temporary = pendingPath + $".{Guid.NewGuid():N}.tmp";
        var manifestTemporary =
            manifestPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using var source =
                await OpenExternalConnectionAsync(
                    sourceFull,
                    SqliteOpenMode.ReadOnly,
                    cancellationToken);
            await using var target =
                await OpenExternalConnectionAsync(
                    temporary,
                    SqliteOpenMode.ReadWriteCreate,
                    cancellationToken);
            source.BackupDatabase(target);
            await target.CloseAsync();
            await source.CloseAsync();

            await ValidateMemoryDatabaseAsync(
                temporary,
                cancellationToken);
            var (length, sha256) =
                await GetFileIdentityAsync(
                    temporary,
                    cancellationToken);
            CommitStagedFile(
                temporary,
                pendingPath,
                overwrite: true);

            var manifest = new PendingRestoreManifest(
                sha256,
                length,
                DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(
                manifestTemporary,
                JsonSerializer.Serialize(manifest),
                cancellationToken);
            CommitStagedFile(
                manifestTemporary,
                manifestPath,
                overwrite: true);

            return new TalvoraMemoryRestoreStageResult(
                sourceFull,
                pendingPath,
                length,
                sha256,
                RequiresRestart: true);
        }
        finally
        {
            File.Delete(temporary);
            File.Delete(manifestTemporary);
        }
    }

    public async Task<TalvoraMemoryRestoreStatusResult> RestoreStatusAsync(
        CancellationToken cancellationToken)
    {
        var pendingPath = GetPendingRestorePath(DatabasePath);
        var manifestPath = GetPendingRestoreManifestPath(DatabasePath);
        if (!File.Exists(pendingPath) ||
            !File.Exists(manifestPath))
        {
            return new TalvoraMemoryRestoreStatusResult(
                false,
                null,
                null,
                null);
        }

        var manifest = JsonSerializer.Deserialize<PendingRestoreManifest>(
            await File.ReadAllTextAsync(
                manifestPath,
                cancellationToken));
        return manifest is null
            ? new TalvoraMemoryRestoreStatusResult(
                true,
                pendingPath,
                null,
                null)
            : new TalvoraMemoryRestoreStatusResult(
                true,
                pendingPath,
                manifest.Length,
                manifest.Sha256);
    }

    internal static string ResolveDefaultDatabasePath()
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

    internal static void ApplyPendingRestoreIfPresent(
        string databasePath)
    {
        var pendingPath = GetPendingRestorePath(databasePath);
        var manifestPath = GetPendingRestoreManifestPath(databasePath);
        if (!File.Exists(pendingPath) ||
            !File.Exists(manifestPath))
        {
            return;
        }

        try
        {
            var manifest =
                JsonSerializer.Deserialize<PendingRestoreManifest>(
                    File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException(
                    "Pending memory restore manifest is invalid.");
            var info = new FileInfo(pendingPath);
            if (info.Length != manifest.Length)
            {
                throw new InvalidDataException(
                    "Pending memory restore length does not match its manifest.");
            }

            using (var stream = File.OpenRead(pendingPath))
            using (var hasher = SHA256.Create())
            {
                var actualHash = Convert.ToHexString(
                    hasher.ComputeHash(stream));
                if (!string.Equals(
                        actualHash,
                        manifest.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Pending memory restore hash does not match its manifest.");
                }
            }

            ValidateMemoryDatabase(pendingPath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(databasePath)!);

            if (File.Exists(databasePath))
            {
                var backupPath =
                    databasePath +
                    $".pre-restore-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.bak";
                CreateConsistentPreRestoreBackup(
                    databasePath,
                    backupPath);
            }

            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
            if (File.Exists(databasePath))
            {
                File.Replace(
                    pendingPath,
                    databasePath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(
                    pendingPath,
                    databasePath);
            }
            File.Delete(manifestPath);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException or
            SqliteException)
        {
            QuarantinePendingRestore(
                pendingPath,
                manifestPath,
                ex.GetType().Name);
        }
    }

    private static async Task ValidateMemoryDatabaseAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await OpenExternalConnectionAsync(
                path,
                SqliteOpenMode.ReadOnly,
                cancellationToken);
        await ValidateMemoryDatabaseCoreAsync(
            connection,
            cancellationToken);
    }

    private static void ValidateMemoryDatabase(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = BusyTimeoutMilliseconds / 1000,
        };
        using var connection =
            new SqliteConnection(builder.ToString());
        connection.Open();

        using var quickCheck = connection.CreateCommand();
        quickCheck.CommandText = "PRAGMA quick_check;";
        var integrity = Convert.ToString(quickCheck.ExecuteScalar());
        if (!string.Equals(
                integrity,
                "ok",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Memory restore database failed SQLite quick_check.");
        }

        using var schema = connection.CreateCommand();
        schema.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_schema
            WHERE type = 'table'
              AND name = 'memory_items';
            """;
        if (Convert.ToInt64(schema.ExecuteScalar()) != 1)
        {
            throw new InvalidDataException(
                "Memory restore database does not contain memory_items.");
        }
    }

    private static void CreateConsistentPreRestoreBackup(
        string sourcePath,
        string destinationPath)
    {
        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = BusyTimeoutMilliseconds / 1000,
        };
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = BusyTimeoutMilliseconds / 1000,
        };

        using var source =
            new SqliteConnection(sourceBuilder.ToString());
        using var destination =
            new SqliteConnection(destinationBuilder.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
        destination.Close();
        source.Close();
        ValidateMemoryDatabase(destinationPath);
    }

    private static async Task ValidateMemoryDatabaseCoreAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var quickCheck = connection.CreateCommand();
        quickCheck.CommandText = "PRAGMA quick_check;";
        var integrity = Convert.ToString(
            await quickCheck.ExecuteScalarAsync(cancellationToken));
        if (!string.Equals(
                integrity,
                "ok",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Memory backup failed SQLite quick_check.");
        }

        await using var schema = connection.CreateCommand();
        schema.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_schema
            WHERE type = 'table'
              AND name = 'memory_items';
            """;
        if (Convert.ToInt64(
                await schema.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new InvalidDataException(
                "SQLite file is not a Talvora memory database.");
        }
    }

    private static async Task<SqliteConnection> OpenExternalConnectionAsync(
        string path,
        SqliteOpenMode mode,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = BusyTimeoutMilliseconds / 1000,
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<(long Length, string Sha256)>
        GetFileIdentityAsync(
            string path,
            CancellationToken cancellationToken)
    {
        var length = new FileInfo(path).Length;
        await using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(
            stream,
            cancellationToken);
        return (
            length,
            Convert.ToHexString(hash));
    }

    private static void CommitStagedFile(
        string temporary,
        string destination,
        bool overwrite)
    {
        if (File.Exists(destination))
        {
            if (!overwrite)
            {
                throw new IOException(
                    $"Destination already exists: {destination}");
            }
            File.Replace(
                temporary,
                destination,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(
                temporary,
                destination);
        }
    }

    private static string GetPendingRestorePath(string databasePath) =>
        databasePath + ".restore-pending.db";

    private static string GetPendingRestoreManifestPath(string databasePath) =>
        databasePath + ".restore-pending.json";

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static void QuarantinePendingRestore(
        string pendingPath,
        string manifestPath,
        string reason)
    {
        var suffix =
            $".rejected-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{reason}";
        try
        {
            if (File.Exists(pendingPath))
            {
                File.Move(
                    pendingPath,
                    pendingPath + suffix,
                    overwrite: true);
            }
            if (File.Exists(manifestPath))
            {
                File.Move(
                    manifestPath,
                    manifestPath + suffix,
                    overwrite: true);
            }
        }
        catch
        {
            // Restore failure must not prevent startup with the live database.
        }
    }
}
