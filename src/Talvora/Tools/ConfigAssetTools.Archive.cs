using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Talvora.Shared;
using Talvora.SourceEditing;

namespace Talvora.Tools;

public static partial class ConfigAssetTools
{
    private const int ArchiveAbsoluteMaxEntries = 1_000_000;
    private const long ArchiveAbsoluteMaxEntryBytes = 1L << 40;
    private const long ArchiveAbsoluteMaxTotalBytes = 4L << 40;

[McpServerTool(
        Name = "talvora_archive_list",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraArchiveListResponse)),
     Description("List every entry in any accessible ZIP archive with sizes, timestamp, directory flag, and external attributes.")]
    public static TalvoraArchiveListResponse ArchiveList(string archivePath)
    {
        var fullPath = Path.GetFullPath(archivePath);
        using var archive = ZipFile.OpenRead(fullPath);

        var entries = archive.Entries
            .Select(ToArchiveEntry)
            .ToArray();

        return new TalvoraArchiveListResponse(
            fullPath,
            entries.Length,
            entries);
    }

    [McpServerTool(
        Name = "talvora_archive_create",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraArchiveCreateResponse)),
     Description("Create a ZIP archive from any accessible directory. Supports overwrite, optional base-directory inclusion, and no/fastest/optimal/smallest compression. The archive may be located inside the source tree because creation is staged through a temporary file.")]
    public static TalvoraArchiveCreateResponse ArchiveCreate(
        string sourceDirectory,
        string archivePath,
        bool overwrite = false,
        bool includeBaseDirectory = false,
        string compression = "optimal")
    {
        var source = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Archive source directory was not found: {source}");
        }

        var destination = Path.GetFullPath(archivePath);
        if (File.Exists(destination) && !overwrite)
        {
            throw new IOException($"Archive destination already exists: {destination}");
        }

        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var level = compression.Trim().ToLowerInvariant() switch
        {
            "none" or "nocompression" => CompressionLevel.NoCompression,
            "fast" or "fastest" => CompressionLevel.Fastest,
            "optimal" => CompressionLevel.Optimal,
            "smallest" or "smallestsize" => CompressionLevel.SmallestSize,
            _ => throw new ArgumentOutOfRangeException(
                nameof(compression),
                "compression must be none, fastest, optimal, or smallest."),
        };

        var temp = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Archive-" + Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            ZipFile.CreateFromDirectory(
                source,
                temp,
                level,
                includeBaseDirectory);

            File.Move(temp, destination, overwrite);

            using var archive = ZipFile.OpenRead(destination);
            return new TalvoraArchiveCreateResponse(
                source,
                destination,
                new FileInfo(destination).Length,
                archive.Entries.Count,
                compression.Trim().ToLowerInvariant(),
                includeBaseDirectory);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    [McpServerTool(
        Name = "talvora_archive_extract",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraArchiveExtractResponse)),
     Description("Extract any accessible ZIP archive through full preflight, isolated staging, and rollback-capable publication. Development-workspace source/text targets are routed to talvora_apply_patch by default; explicitAdmin=true deliberately preserves unrestricted administrative extraction. maxEntries/maxEntryBytes/maxTotalBytes=0 use Talvora's high emergency ceilings; positive values request tighter caller budgets. By default entry paths are contained under destinationDirectory and parent directory identities are pinned during commit; allowOutsideDestination=true deliberately permits paths outside that directory.")]
    public static TalvoraArchiveExtractResponse ArchiveExtract(
        string archivePath,
        string destinationDirectory,
        bool overwrite = false,
        bool allowOutsideDestination = false,
        bool explicitAdmin = false,
        int maxEntries = 0,
        long maxEntryBytes = 0,
        long maxTotalBytes = 0,
        CancellationToken cancellationToken = default)
    {
        var entryLimit =
            ResolveArchiveEntryCountLimit(
                maxEntries);
        var entryByteLimit =
            ResolveArchiveByteLimit(
                maxEntryBytes,
                ArchiveAbsoluteMaxEntryBytes,
                nameof(maxEntryBytes));
        var totalByteLimit =
            ResolveArchiveByteLimit(
                maxTotalBytes,
                ArchiveAbsoluteMaxTotalBytes,
                nameof(maxTotalBytes));

        var archiveFullPath =
            Path.GetFullPath(
                archivePath);
        var destination =
            Path.GetFullPath(
                destinationDirectory);
        if (File.Exists(destination))
        {
            throw new IOException(
                $"Archive destinationDirectory refers to a file: {destination}");
        }

        var destinationPrefix =
            destination.EndsWith(
                Path.DirectorySeparatorChar)
                ? destination
                : destination +
                  Path.DirectorySeparatorChar;
        var comparison =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        var pathComparer =
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        var stageRoot =
            Path.Combine(
                Path.GetTempPath(),
                "Talvora-Archive-Stage-" +
                Guid.NewGuid().ToString("N"));

        using var archive =
            ZipFile.OpenRead(
                archiveFullPath);
        if (archive.Entries.Count >
            entryLimit)
        {
            throw new InvalidDataException(
                $"Archive entry count {archive.Entries.Count} exceeds limit {entryLimit}.");
        }

        var plannedEntries =
            new List<ArchiveExtractionPlan>(
                archive.Entries.Count);
        var uniqueTargets =
            new HashSet<string>(
                pathComparer);
        long declaredBytes = 0;
        for (var index = 0;
             index < archive.Entries.Count;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry =
                archive.Entries[index];
            var relative =
                entry.FullName
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar);
            var target =
                Path.GetFullPath(
                    Path.Combine(
                        destination,
                        relative));
            if (!allowOutsideDestination &&
                !string.Equals(
                    target,
                    destination,
                    comparison) &&
                !target.StartsWith(
                    destinationPrefix,
                    comparison))
            {
                throw new IOException(
                    $"Archive entry resolves outside destinationDirectory: {entry.FullName}");
            }

            if (!uniqueTargets.Add(target))
            {
                throw new InvalidDataException(
                    $"Archive contains multiple entries that resolve to the same target: {entry.FullName}");
            }

            var isDirectory =
                IsDirectoryEntry(
                    entry);
            if (isDirectory)
            {
                if (File.Exists(target))
                {
                    throw new IOException(
                        $"Archive directory target conflicts with an existing file: {target}");
                }
            }
            else
            {
                if (entry.Length >
                    entryByteLimit)
                {
                    throw new InvalidDataException(
                        $"Archive entry '{entry.FullName}' declares {entry.Length} bytes, exceeding limit {entryByteLimit}.");
                }

                try
                {
                    declaredBytes =
                        checked(
                            declaredBytes +
                            entry.Length);
                }
                catch (OverflowException ex)
                {
                    throw new InvalidDataException(
                        "Archive declared decompressed size overflowed the supported accounting range.",
                        ex);
                }

                if (declaredBytes >
                    totalByteLimit)
                {
                    throw new InvalidDataException(
                        $"Archive declared decompressed size {declaredBytes} exceeds limit {totalByteLimit}.");
                }

                SourceMutationPolicy.EnsureGenericDestinationMutationAllowed(
                    target,
                    "talvora_archive_extract",
                    explicitAdmin);
                if (Directory.Exists(target))
                {
                    throw new IOException(
                        $"Archive file target conflicts with an existing directory: {target}");
                }

                if (File.Exists(target) &&
                    !overwrite)
                {
                    throw new IOException(
                        $"Archive destination already exists and overwrite=false: {target}");
                }
            }

            plannedEntries.Add(
                new ArchiveExtractionPlan(
                    entry,
                    target,
                    isDirectory,
                    File.Exists(target),
                    isDirectory
                        ? null
                        : Path.Combine(
                            stageRoot,
                            index.ToString(
                                "D8",
                                System.Globalization.CultureInfo.InvariantCulture) +
                            ".bin")));
        }

        Directory.CreateDirectory(
            stageRoot);
        SourceEditPathGuard? guard = null;
        var manualCreatedDirectories =
            new List<string>();
        var commitFiles =
            new List<ArchiveCommitFile>();
        var appliedFiles =
            new List<ArchiveCommitFile>();
        var success = false;
        try
        {
            foreach (var plan in plannedEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (plan.IsDirectory)
                {
                    continue;
                }

                plan.Entry.ExtractToFile(
                    plan.StagePath!,
                    overwrite: false);
                var actualLength =
                    new FileInfo(
                        plan.StagePath!).Length;
                if (actualLength !=
                    plan.Entry.Length)
                {
                    throw new InvalidDataException(
                        $"Archive entry '{plan.Entry.FullName}' extracted {actualLength} bytes but declared {plan.Entry.Length}.");
                }
            }

            if (!allowOutsideDestination)
            {
                var guardRoot =
                    FindExistingArchiveGuardRoot(
                        destination);
                var guardTargets =
                    plannedEntries.Select(
                        plan =>
                            plan.IsDirectory
                                ? Path.Combine(
                                    plan.TargetPath,
                                    ".talvora-archive-directory-anchor")
                                : plan.TargetPath);
                guard =
                    SourceEditPathGuard.AcquireFileSystemTargets(
                        guardRoot,
                        guardTargets);
            }
            else
            {
                foreach (var plan in plannedEntries)
                {
                    var requiredDirectory =
                        plan.IsDirectory
                            ? plan.TargetPath
                            : Path.GetDirectoryName(
                                  plan.TargetPath)!;
                    EnsureArchiveDirectoryTracked(
                        requiredDirectory,
                        manualCreatedDirectories);
                }
            }

            foreach (var plan in plannedEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (plan.IsDirectory)
                {
                    if (File.Exists(plan.TargetPath))
                    {
                        throw new IOException(
                            $"Archive directory target changed into a file before commit: {plan.TargetPath}");
                    }

                    continue;
                }

                if (Directory.Exists(plan.TargetPath))
                {
                    throw new IOException(
                        $"Archive file target changed into a directory before commit: {plan.TargetPath}");
                }

                var existsNow =
                    File.Exists(
                        plan.TargetPath);
                if (existsNow !=
                    plan.TargetExisted)
                {
                    throw new IOException(
                        $"Archive target existence changed after preflight: {plan.TargetPath}");
                }

                if (existsNow &&
                    !overwrite)
                {
                    throw new IOException(
                        $"Archive destination appeared with overwrite=false: {plan.TargetPath}");
                }

                if (!allowOutsideDestination &&
                    existsNow &&
                    (File.GetAttributes(
                         plan.TargetPath) &
                     FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"Archive target is a reparse-point file and cannot be replaced in contained mode: {plan.TargetPath}");
                }

                var parent =
                    Path.GetDirectoryName(
                        plan.TargetPath)!;
                var token =
                    Guid.NewGuid().ToString("N");
                var commitPath =
                    Path.Combine(
                        parent,
                        "." +
                        Path.GetFileName(
                            plan.TargetPath) +
                        ".talvora-archive-stage-" +
                        token +
                        ".tmp");
                var backupPath =
                    plan.TargetExisted
                        ? Path.Combine(
                            parent,
                            "." +
                            Path.GetFileName(
                                plan.TargetPath) +
                            ".talvora-archive-backup-" +
                            token +
                            ".tmp")
                        : null;
                File.Copy(
                    plan.StagePath!,
                    commitPath,
                    overwrite: false);
                FlushArchiveFileToDisk(
                    commitPath);
                commitFiles.Add(
                    new ArchiveCommitFile(
                        plan.TargetPath,
                        commitPath,
                        backupPath,
                        plan.TargetExisted));
            }

            try
            {
                foreach (var commit in commitFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    guard?.VerifyAnchors();
                    if (commit.TargetExisted)
                    {
                        File.Replace(
                            commit.CommitPath,
                            commit.TargetPath,
                            commit.BackupPath,
                            ignoreMetadataErrors: false);
                    }
                    else if (guard is not null)
                    {
                        guard.MoveFileByHandle(
                            commit.CommitPath,
                            commit.TargetPath,
                            replaceExisting: false);
                    }
                    else
                    {
                        File.Move(
                            commit.CommitPath,
                            commit.TargetPath);
                    }

                    appliedFiles.Add(
                        commit);
                }
            }
            catch (Exception commitError)
            {
                try
                {
                    RollbackArchiveCommit(
                        appliedFiles,
                        guard);
                }
                catch (Exception rollbackError)
                {
                    throw new IOException(
                        "Archive publication failed and rollback could not fully restore the previous destination state.",
                        new AggregateException(
                            commitError,
                            rollbackError));
                }

                throw;
            }

            foreach (var commit in commitFiles)
            {
                if (commit.BackupPath is not null &&
                    File.Exists(
                        commit.BackupPath))
                {
                    File.Delete(
                        commit.BackupPath);
                }
            }

            guard?.CommitCreatedDirectories();
            success = true;
            return new TalvoraArchiveExtractResponse(
                archiveFullPath,
                destination,
                plannedEntries.Count,
                declaredBytes,
                overwrite,
                allowOutsideDestination);
        }
        finally
        {
            foreach (var commit in commitFiles)
            {
                if (File.Exists(
                        commit.CommitPath))
                {
                    TryDeleteArchiveArtifact(
                        commit.CommitPath);
                }

                if (success &&
                    commit.BackupPath is not null &&
                    File.Exists(
                        commit.BackupPath))
                {
                    TryDeleteArchiveArtifact(
                        commit.BackupPath);
                }
            }

            guard?.Dispose();
            if (!success &&
                allowOutsideDestination)
            {
                CleanupArchiveDirectories(
                    manualCreatedDirectories);
            }

            if (Directory.Exists(
                    stageRoot))
            {
                try
                {
                    Directory.Delete(
                        stageRoot,
                        recursive: true);
                }
                catch (Exception ex) when (
                    ex is IOException or
                        UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static int ResolveArchiveEntryCountLimit(
        int requested)
    {
        if (requested < 0 ||
            requested >
            ArchiveAbsoluteMaxEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested),
                $"Archive entry limit must be 0..{ArchiveAbsoluteMaxEntries}.");
        }

        return requested == 0
            ? ArchiveAbsoluteMaxEntries
            : requested;
    }

    private static long ResolveArchiveByteLimit(
        long requested,
        long absoluteMaximum,
        string parameterName)
    {
        if (requested < 0 ||
            requested >
            absoluteMaximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Archive byte limit must be 0..{absoluteMaximum}.");
        }

        return requested == 0
            ? absoluteMaximum
            : requested;
    }

    private static string FindExistingArchiveGuardRoot(
        string destination)
    {
        var current =
            Directory.Exists(destination)
                ? destination
                : Path.GetDirectoryName(
                    destination);
        while (!string.IsNullOrWhiteSpace(
                   current))
        {
            if (Directory.Exists(current))
            {
                return Path.GetFullPath(
                    current);
            }

            current =
                Directory.GetParent(
                    current)?.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Archive destination has no existing parent directory: {destination}");
    }

    private static void EnsureArchiveDirectoryTracked(
        string directory,
        List<string> createdDirectories)
    {
        var missing =
            new Stack<string>();
        var current =
            Path.GetFullPath(
                directory);
        while (!Directory.Exists(
                   current))
        {
            if (File.Exists(current))
            {
                throw new IOException(
                    $"Archive destination directory path conflicts with a file: {current}");
            }

            missing.Push(
                current);
            current =
                Directory.GetParent(
                    current)?.FullName
                ?? throw new DirectoryNotFoundException(
                    $"Archive destination directory has no existing ancestor: {directory}");
        }

        while (missing.Count > 0)
        {
            var path =
                missing.Pop();
            Directory.CreateDirectory(
                path);
            createdDirectories.Add(
                path);
        }
    }

    private static void CleanupArchiveDirectories(
        IEnumerable<string> createdDirectories)
    {
        foreach (var directory in
                 createdDirectories
                     .Distinct(
                         StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(
                         path => path.Length))
        {
            try
            {
                if (Directory.Exists(directory) &&
                    !Directory.EnumerateFileSystemEntries(
                        directory).Any())
                {
                    Directory.Delete(
                        directory);
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                    UnauthorizedAccessException)
            {
            }
        }
    }

    private static void FlushArchiveFileToDisk(
        string path)
    {
        using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
        stream.Flush(
            flushToDisk: true);
    }

    private static void RollbackArchiveCommit(
        IReadOnlyList<ArchiveCommitFile> appliedFiles,
        SourceEditPathGuard? guard)
    {
        for (var index =
                 appliedFiles.Count - 1;
             index >= 0;
             index--)
        {
            var commit =
                appliedFiles[index];
            guard?.VerifyAnchors();
            if (commit.TargetExisted)
            {
                if (commit.BackupPath is null ||
                    !File.Exists(
                        commit.BackupPath))
                {
                    throw new IOException(
                        $"Archive rollback backup is missing: {commit.TargetPath}");
                }

                if (File.Exists(
                        commit.TargetPath))
                {
                    File.Replace(
                        commit.BackupPath,
                        commit.TargetPath,
                        destinationBackupFileName: null,
                        ignoreMetadataErrors: false);
                }
                else if (guard is not null)
                {
                    guard.MoveFileByHandle(
                        commit.BackupPath,
                        commit.TargetPath,
                        replaceExisting: false);
                }
                else
                {
                    File.Move(
                        commit.BackupPath,
                        commit.TargetPath);
                }
            }
            else if (File.Exists(
                         commit.TargetPath))
            {
                if (guard is not null)
                {
                    guard.DeleteFileByHandle(
                        commit.TargetPath);
                }
                else
                {
                    File.Delete(
                        commit.TargetPath);
                }
            }
        }
    }

    private static void TryDeleteArchiveArtifact(
        string path)
    {
        try
        {
            File.Delete(
                path);
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException)
        {
        }
    }

    private sealed record ArchiveExtractionPlan(
        ZipArchiveEntry Entry,
        string TargetPath,
        bool IsDirectory,
        bool TargetExisted,
        string? StagePath);

    private sealed record ArchiveCommitFile(
        string TargetPath,
        string CommitPath,
        string? BackupPath,
        bool TargetExisted);

    private static TalvoraArchiveEntry ToArchiveEntry(ZipArchiveEntry entry) =>
        new(
            entry.FullName,
            entry.Length,
            entry.CompressedLength,
            entry.LastWriteTime,
            IsDirectoryEntry(entry),
            entry.ExternalAttributes);

    private static bool IsDirectoryEntry(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
        entry.FullName.EndsWith("\\", StringComparison.Ordinal) ||
        string.IsNullOrEmpty(entry.Name);
}
