using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public static partial class ConfigAssetTools
{
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
     Description("Extract any accessible ZIP archive. overwrite replaces existing files. By default entry paths are contained under destinationDirectory; allowOutsideDestination=true permits archive relative/absolute paths to resolve outside that directory, preserving Talvora's full filesystem capability.")]
    public static TalvoraArchiveExtractResponse ArchiveExtract(
        string archivePath,
        string destinationDirectory,
        bool overwrite = false,
        bool allowOutsideDestination = false,
        CancellationToken cancellationToken = default)
    {
        var archiveFullPath = Path.GetFullPath(archivePath);
        var destination = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destination);

        var destinationPrefix = destination.EndsWith(Path.DirectorySeparatorChar)
            ? destination
            : destination + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var entriesExtracted = 0;
        long bytesExtracted = 0;

        using var archive = ZipFile.OpenRead(archiveFullPath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = entry.FullName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            var target = Path.GetFullPath(Path.Combine(destination, relative));

            if (!allowOutsideDestination &&
                !string.Equals(target, destination, comparison) &&
                !target.StartsWith(destinationPrefix, comparison))
            {
                throw new IOException(
                    $"Archive entry resolves outside destinationDirectory: {entry.FullName}");
            }

            var isDirectory = IsDirectoryEntry(entry);
            if (isDirectory)
            {
                Directory.CreateDirectory(target);
                entriesExtracted++;
                continue;
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(target, overwrite);
            entriesExtracted++;
            bytesExtracted += entry.Length;
        }

        return new TalvoraArchiveExtractResponse(
            archiveFullPath,
            destination,
            entriesExtracted,
            bytesExtracted,
            overwrite,
            allowOutsideDestination);
    }

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
