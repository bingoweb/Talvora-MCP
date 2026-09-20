using System.ComponentModel;
using ModelContextProtocol.Server;
using Talvora.SourceEditing;

namespace Talvora.Tools;

public sealed record TalvoraPathEntry(string Path, string Name, bool IsDirectory, long? Length, DateTime LastWriteTimeUtc);
public sealed record TalvoraDirectoryCreateResponse(string Path, bool Created);
public sealed record TalvoraCopyResponse(string Source, string Destination, string Kind, bool Changed, bool Overwrite, bool Recursive);
public sealed record TalvoraMoveResponse(string Source, string Destination, string Kind, bool Changed, bool Overwrite);

[McpServerToolType]
public static class FileTools
{
    internal const int AbsoluteLegacyListResults =
        DeveloperTools.AbsoluteFileSearchResults;
    internal const long AbsoluteLegacyListResponseCharacters =
        DeveloperTools.AbsoluteSearchResponseCharacters;

    [McpServerTool(Name = "talvora_read_text", ReadOnly = true, OpenWorld = true), Description("Compatibility whole-file text reader with a finite server response budget. Small files are returned as a string exactly as before. If the file exceeds the bounded whole-file window, use talvora_read_text_range and its continuation metadata instead.")]
    public static async Task<string> ReadText(
        string path,
        CancellationToken cancellationToken = default)
    {
        var response =
            await ConfigAssetTools.ReadTextRange(
                path,
                startLine: 1,
                lineCount: 0,
                startCharacter: 0,
                cancellationToken);
        if (response.ResponseLimited)
        {
            throw new InvalidOperationException(
                "talvora_read_text whole-file response exceeds the finite server budget. " +
                "Use talvora_read_text_range and continue with nextStartLine/nextStartCharacter.");
        }

        return response.Text;
    }

    [McpServerTool(Name = "talvora_write_text", Destructive = true, Idempotent = true, OpenWorld = true), Description("Compatibility whole-file UTF-8 writer for ordinary/non-workspace files. Do not use it for development-workspace source editing: talvora_apply_patch is the PRIMARY/default editor, while talvora_apply_edits is only for already-known exact ranges; workspace source/text writes are rejected with SOURCE_EDIT_POLICY_VIOLATION.")]
    public static async Task<object> WriteText(string path, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        SourceMutationPolicy.EnsureLegacyTextMutationAllowed(
            fullPath,
            "talvora_write_text");
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return new { path = fullPath, length = new FileInfo(fullPath).Length };
    }

    [McpServerTool(Name = "talvora_delete", Destructive = true, OpenWorld = true), Description("General filesystem delete. Development-workspace source/text file deletion must use talvora_apply_patch so revision/WAL/rollback guarantees are preserved; direct source-file deletion is rejected. Directory and non-workspace deletion remain supported.")]
    public static object Delete(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            SourceMutationPolicy.EnsureLegacySourceFileDeleteAllowed(
                fullPath,
                "talvora_delete");
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

    [McpServerTool(Name = "talvora_list", ReadOnly = true, OpenWorld = true), Description("Compatibility directory listing with finite server entry/response budgets. If a listing exceeds the compatibility window, use talvora_find_files with resultOffset/nextResultOffset pagination. No path allow-list is applied.")]
    public static IReadOnlyList<TalvoraPathEntry> List(
        string path,
        bool recursive = false,
        CancellationToken cancellationToken = default) =>
        ListBounded(
            path,
            recursive,
            AbsoluteLegacyListResults,
            AbsoluteLegacyListResponseCharacters,
            cancellationToken);

    internal static IReadOnlyList<TalvoraPathEntry> ListBounded(
        string path,
        bool recursive,
        int maxResults,
        long maxResponseCharacters,
        CancellationToken cancellationToken)
    {
        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }
        if (maxResponseCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResponseCharacters));
        }

        var fullPath = Path.GetFullPath(path);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var directory = new DirectoryInfo(fullPath);
        var entries =
            new List<TalvoraPathEntry>(
                Math.Min(
                    maxResults,
                    1024));
        long responseCharacters = 0;

        foreach (var info in
                 directory.EnumerateFileSystemInfos(
                     "*",
                     option))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDirectory =
                (info.Attributes & FileAttributes.Directory) != 0;
            var entry =
                new TalvoraPathEntry(
                    info.FullName,
                    info.Name,
                    isDirectory,
                    isDirectory
                        ? null
                        : ((FileInfo)info).Length,
                    info.LastWriteTimeUtc);
            var entryCharacters =
                (long)entry.Path.Length +
                entry.Name.Length +
                64;

            if (entries.Count >= maxResults ||
                responseCharacters + entryCharacters >
                    maxResponseCharacters)
            {
                throw new InvalidOperationException(
                    "talvora_list result exceeds the finite compatibility response budget. " +
                    "Use talvora_find_files with resultOffset/nextResultOffset pagination.");
            }

            entries.Add(entry);
            responseCharacters += entryCharacters;
        }

        return entries;
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
     Description("General filesystem copy. Development-workspace source/text destinations are routed to talvora_apply_patch by default so revision/WAL/rollback guarantees are preserved; explicitAdmin=true deliberately keeps the unrestricted administrative copy capability. Directory copies are recursively preflighted before mutation. overwrite=true replaces conflicting copied entries while merging non-conflicting directory entries.")]
    public static TalvoraCopyResponse Copy(
        string source,
        string destination,
        bool overwrite = false,
        bool recursive = true,
        bool explicitAdmin = false,
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
            SourceMutationPolicy.EnsureGenericDestinationMutationAllowed(
                destinationPath,
                "talvora_copy",
                explicitAdmin);
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
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative =
                Path.GetRelativePath(
                    sourcePath,
                    file);
            var target =
                Path.Combine(
                    destinationPath,
                    relative);
            SourceMutationPolicy.EnsureGenericDestinationMutationAllowed(
                target,
                "talvora_copy",
                explicitAdmin);
        }

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
     Description("General filesystem move. Development-workspace source/text file removal or destination ingress is routed to talvora_apply_patch by default so revision/WAL/rollback guarantees are preserved; explicitAdmin=true deliberately keeps the unrestricted administrative move capability. Directory, generated, binary, and ordinary non-workspace moves remain supported.")]
    public static TalvoraMoveResponse Move(
        string source,
        string destination,
        bool overwrite = false,
        bool explicitAdmin = false,
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

        if (!isDirectory)
        {
            SourceMutationPolicy.EnsureGenericFileMoveAllowed(
                sourcePath,
                destinationPath,
                "talvora_move",
                explicitAdmin);
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