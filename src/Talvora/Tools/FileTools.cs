using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraPathEntry(string Path, string Name, bool IsDirectory, long? Length, DateTime LastWriteTimeUtc);
public sealed record TalvoraDirectoryCreateResponse(string Path, bool Created);
public sealed record TalvoraCopyResponse(string Source, string Destination, string Kind, bool Changed, bool Overwrite, bool Recursive);
public sealed record TalvoraMoveResponse(string Source, string Destination, string Kind, bool Changed, bool Overwrite);

[McpServerToolType]
public static class FileTools
{
    [McpServerTool(Name = "talvora_read_text", ReadOnly = true, OpenWorld = true), Description("Read a UTF-8 text file from any path accessible to the Talvora service.")]
    public static Task<string> ReadText(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(Path.GetFullPath(path), cancellationToken);

    [McpServerTool(Name = "talvora_write_text", Destructive = true, Idempotent = true, OpenWorld = true), Description("Write UTF-8 text to any path accessible to the Talvora service, creating parent directories when needed.")]
    public static async Task<object> WriteText(string path, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return new { path = fullPath, length = new FileInfo(fullPath).Length };
    }

    [McpServerTool(Name = "talvora_delete", Destructive = true, OpenWorld = true), Description("Delete a file or directory tree from any path accessible to the Talvora service.")]
    public static object Delete(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return new { path = fullPath, deleted = true, kind = "file" };
        }
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
            return new { path = fullPath, deleted = true, kind = "directory" };
        }
        return new { path = fullPath, deleted = false, kind = "missing" };
    }

    [McpServerTool(Name = "talvora_list", ReadOnly = true, OpenWorld = true), Description("List files and directories at a path accessible to the Talvora service.")]
    public static IReadOnlyList<TalvoraPathEntry> List(string path, bool recursive = false)
    {
        var fullPath = Path.GetFullPath(path);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var directory = new DirectoryInfo(fullPath);

        return directory.EnumerateFileSystemInfos("*", option)
            .Select(info =>
            {
                var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
                return new TalvoraPathEntry(
                    info.FullName,
                    info.Name,
                    isDirectory,
                    isDirectory ? null : ((FileInfo)info).Length,
                    info.LastWriteTimeUtc);
            })
            .ToArray();
    }

    [McpServerTool(
        Name = "talvora_create_directory",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraDirectoryCreateResponse)),
     Description("Create a complete directory path, including missing parents, at any path accessible to the Talvora service. Existing directories are handled idempotently. No path allow-list is applied.")]
    public static TalvoraDirectoryCreateResponse CreateDirectory(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = NormalizePath(path);
        var existed = Directory.Exists(fullPath);
        Directory.CreateDirectory(fullPath);
        return new TalvoraDirectoryCreateResponse(fullPath, !existed);
    }

    [McpServerTool(
        Name = "talvora_copy",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCopyResponse)),
     Description("Copy a file or directory to any destination accessible to the Talvora service. Directory copies are recursive by default. overwrite=true replaces conflicting copied entries while merging non-conflicting directory entries. Source reparse points are rejected before mutation to prevent accidental traversal loops. No path allow-list is applied.")]
    public static TalvoraCopyResponse Copy(
        string source,
        string destination,
        bool overwrite = false,
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = NormalizePath(source);
        var destinationPath = NormalizePath(destination);

        if (!TryGetAttributes(sourcePath, out var sourceAttributes))
        {
            throw new FileNotFoundException("Copy source was not found.", sourcePath);
        }

        var isDirectory = (sourceAttributes & FileAttributes.Directory) != 0;
        if (PathsEqual(sourcePath, destinationPath))
        {
            throw new IOException("Source and destination must be different paths.");
        }

        if (!isDirectory)
        {
            CopyFile(sourcePath, destinationPath, overwrite);
            return new TalvoraCopyResponse(sourcePath, destinationPath, "file", true, overwrite, recursive);
        }

        if (!recursive)
        {
            throw new InvalidOperationException("Directory copy requires recursive=true; partial non-recursive directory copies are not supported.");
        }

        if (IsDescendantPath(destinationPath, sourcePath))
        {
            throw new IOException("A directory cannot be copied into itself or one of its descendants.");
        }

        var manifest = BuildDirectoryManifest(sourcePath, cancellationToken);

        if (TryGetAttributes(destinationPath, out var destinationAttributes))
        {
            if (!overwrite)
            {
                throw new IOException($"Copy destination already exists: {destinationPath}");
            }

            if ((destinationAttributes & FileAttributes.Directory) == 0)
            {
                DeleteExistingEntry(destinationPath, destinationAttributes);
            }
        }

        Directory.CreateDirectory(destinationPath);

        foreach (var directory in manifest.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathsEqual(directory, sourcePath))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourcePath, directory);
            var target = Path.Combine(destinationPath, relative);
            if (TryGetAttributes(target, out var targetAttributes) &&
                (targetAttributes & FileAttributes.Directory) == 0)
            {
                if (!overwrite)
                {
                    throw new IOException($"Copy destination entry already exists: {target}");
                }
                DeleteExistingEntry(target, targetAttributes);
            }

            Directory.CreateDirectory(target);
        }

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourcePath, file);
            var target = Path.Combine(destinationPath, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (TryGetAttributes(target, out var targetAttributes) &&
                (targetAttributes & FileAttributes.Directory) != 0)
            {
                if (!overwrite)
                {
                    throw new IOException($"Copy destination entry already exists: {target}");
                }
                DeleteExistingEntry(target, targetAttributes);
            }

            File.Copy(file, target, overwrite);
        }

        return new TalvoraCopyResponse(sourcePath, destinationPath, "directory", true, overwrite, true);
    }

    [McpServerTool(
        Name = "talvora_move",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraMoveResponse)),
     Description("Move a file or directory to any destination accessible to the Talvora service. overwrite=false rejects an existing destination; overwrite=true removes/replaces the destination before moving. No path allow-list is applied.")]
    public static TalvoraMoveResponse Move(
        string source,
        string destination,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = NormalizePath(source);
        var destinationPath = NormalizePath(destination);

        if (!TryGetAttributes(sourcePath, out var sourceAttributes))
        {
            throw new FileNotFoundException("Move source was not found.", sourcePath);
        }

        var isDirectory = (sourceAttributes & FileAttributes.Directory) != 0;
        if (PathsEqual(sourcePath, destinationPath))
        {
            throw new IOException("Source and destination must be different paths.");
        }

        if (isDirectory && IsDescendantPath(destinationPath, sourcePath))
        {
            throw new IOException("A directory cannot be moved into itself or one of its descendants.");
        }

        if (TryGetAttributes(destinationPath, out var destinationAttributes))
        {
            if (!overwrite)
            {
                throw new IOException($"Move destination already exists: {destinationPath}");
            }

            DeleteExistingEntry(destinationPath, destinationAttributes);
        }

        var parent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (isDirectory)
        {
            Directory.Move(sourcePath, destinationPath);
        }
        else
        {
            File.Move(sourcePath, destinationPath);
        }

        return new TalvoraMoveResponse(
            sourcePath,
            destinationPath,
            isDirectory ? "directory" : "file",
            true,
            overwrite);
    }

    private static void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        if (TryGetAttributes(destinationPath, out var destinationAttributes) &&
            (destinationAttributes & FileAttributes.Directory) != 0)
        {
            if (!overwrite)
            {
                throw new IOException($"Copy destination already exists: {destinationPath}");
            }

            DeleteExistingEntry(destinationPath, destinationAttributes);
        }

        var parent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.Copy(sourcePath, destinationPath, overwrite);
    }

    private static DirectoryManifest BuildDirectoryManifest(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var directories = new List<string>();
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(sourcePath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Directory copy does not traverse reparse points: {current}");
            }

            directories.Add(current);

            foreach (var entry in Directory.EnumerateFileSystemEntries(current)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryAttributes = File.GetAttributes(entry);
                if ((entryAttributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException($"Directory copy does not traverse reparse points: {entry}");
                }

                if ((entryAttributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
                {
                    files.Add(entry);
                }
            }
        }

        directories.Sort(StringComparer.Ordinal);
        files.Sort(StringComparer.Ordinal);
        return new DirectoryManifest(directories, files);
    }

    private static void DeleteExistingEntry(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.Directory) == 0)
        {
            File.Delete(path);
            return;
        }

        Directory.Delete(
            path,
            recursive: (attributes & FileAttributes.ReparsePoint) == 0);
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsDescendantPath(string candidate, string parent)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(candidate);
        var normalizedParent = Path.TrimEndingDirectorySeparator(parent);
        if (string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(
            normalizedParent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DirectoryManifest(
        IReadOnlyList<string> Directories,
        IReadOnlyList<string> Files);
}