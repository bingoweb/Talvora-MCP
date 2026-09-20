using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public static partial class DeveloperTools
{
[McpServerTool(
        Name = "talvora_find_files",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraFileSearchResponse)),
     Description("Find files and optionally directories recursively using wildcard patterns. Works on any accessible path. maxResults=0 means unlimited. followReparsePoints=true permits traversal through junctions/symlinks.")]
    public static TalvoraFileSearchResponse FindFiles(
        [Description("Root file or directory to search.")] string root,
        [Description("Wildcard patterns matched against both name and relative path, for example *.cs or *Tests*.")] string[]? patterns = null,
        [Description("Optional wildcard patterns to exclude.")] string[]? excludePatterns = null,
        bool recursive = true,
        bool includeDirectories = false,
        bool followReparsePoints = false,
        [Description("Maximum returned entries; 0 means unlimited.")] int maxResults = 1000,
        CancellationToken cancellationToken = default)
    {
        if (maxResults < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }

        var effectiveMaxResults =
            maxResults == 0
                ? AbsoluteFileSearchResults
                : Math.Min(
                    maxResults,
                    AbsoluteFileSearchResults);

        var fullRoot = Path.GetFullPath(root);
        var errors = new List<string>();
        var entries = new List<TalvoraFileSearchEntry>();
        var effectivePatterns = NormalizePatterns(patterns);
        long responseCharacters = 0;

        if (File.Exists(fullRoot))
        {
            var file = new FileInfo(fullRoot);
            if (MatchesAny(file.FullName, file.Name, file.DirectoryName ?? fullRoot, effectivePatterns) &&
                !MatchesAny(file.FullName, file.Name, file.DirectoryName ?? fullRoot, excludePatterns, defaultWhenEmpty: false))
            {
                entries.Add(ToSearchEntry(file));
            }

            return new TalvoraFileSearchResponse(fullRoot, entries.Count, false, entries, errors);
        }

        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Search root was not found: {fullRoot}");
        }

        var truncated = false;
        foreach (var info in EnumerateTree(fullRoot, recursive, followReparsePoints, errors, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
            if (isDirectory && !includeDirectories)
            {
                continue;
            }

            var relative = Path.GetRelativePath(fullRoot, info.FullName);
            if (!MatchesAny(relative, info.Name, fullRoot, effectivePatterns))
            {
                continue;
            }

            if (MatchesAny(relative, info.Name, fullRoot, excludePatterns, defaultWhenEmpty: false))
            {
                continue;
            }

            var entry = ToSearchEntry(info);
            var entryCharacters =
                (long)entry.Path.Length +
                entry.Name.Length +
                entry.Kind.Length +
                64;
            if (entries.Count >=
                    effectiveMaxResults ||
                responseCharacters +
                    entryCharacters >
                AbsoluteSearchResponseCharacters)
            {
                truncated = true;
                break;
            }

            entries.Add(entry);
            responseCharacters +=
                entryCharacters;
        }

        return new TalvoraFileSearchResponse(fullRoot, entries.Count, truncated, entries, errors);
    }

    [McpServerTool(
        Name = "talvora_search_text",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTextSearchResponse)),
     Description("Search text across any accessible file tree using literal text or .NET regular expressions. Supports include/exclude wildcards. maxMatches=0 and maxFileBytes=0 mean unlimited.")]
    public static async Task<TalvoraTextSearchResponse> SearchText(
        [Description("Root file or directory to search.")] string root,
        [Description("Literal text or .NET regex pattern.")] string query,
        bool regex = false,
        bool caseSensitive = false,
        string[]? includePatterns = null,
        string[]? excludePatterns = null,
        bool recursive = true,
        bool followReparsePoints = false,
        [Description("Maximum matches returned; 0 means unlimited.")] int maxMatches = 500,
        [Description("Skip files larger than this many bytes; 0 means unlimited.")] long maxFileBytes = 10 * 1024 * 1024,
        [Description("Maximum characters returned for a matching source line; 0 means unlimited.")] int maxLineChars = 4000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(query))
        {
            throw new ArgumentException("Search query cannot be empty.", nameof(query));
        }
        if (maxMatches < 0 || maxFileBytes < 0 || maxLineChars < 0)
        {
            throw new ArgumentOutOfRangeException("Search limits cannot be negative.");
        }

        var effectiveMaxMatches =
            maxMatches == 0
                ? AbsoluteTextSearchMatches
                : Math.Min(
                    maxMatches,
                    AbsoluteTextSearchMatches);
        var effectiveMaxFileBytes =
            maxFileBytes == 0
                ? AbsoluteSearchFileBytes
                : Math.Min(
                    maxFileBytes,
                    AbsoluteSearchFileBytes);
        var effectiveMaxLineChars =
            maxLineChars == 0
                ? AbsoluteSearchLineCharacters
                : Math.Min(
                    maxLineChars,
                    AbsoluteSearchLineCharacters);

        var fullRoot = Path.GetFullPath(root);
        var errors = new List<string>();
        var matches = new List<TalvoraTextSearchMatch>();
        var filesScanned = 0;
        var truncated = false;
        var includes = NormalizePatterns(includePatterns);
        long responseCharacters = 0;

        bool TryAddMatch(
            string path,
            int lineNumber,
            int column,
            string matchText,
            string sourceLine)
        {
            if (matches.Count >= effectiveMaxMatches)
            {
                truncated = true;
                return false;
            }

            var renderedLine =
                TrimLine(
                    sourceLine,
                    effectiveMaxLineChars);
            var renderedMatch =
                TrimLine(
                    matchText,
                    effectiveMaxLineChars);
            var entryCharacters =
                (long)path.Length +
                renderedLine.Length +
                renderedMatch.Length +
                64;

            if (responseCharacters +
                    entryCharacters >
                AbsoluteSearchResponseCharacters)
            {
                truncated = true;
                return false;
            }

            matches.Add(
                new TalvoraTextSearchMatch(
                    path,
                    lineNumber,
                    column,
                    renderedMatch,
                    renderedLine));
            responseCharacters +=
                entryCharacters;
            return true;
        }

        Regex? expression = null;
        if (regex)
        {
            var options = RegexOptions.CultureInvariant;
            if (!caseSensitive)
            {
                options |= RegexOptions.IgnoreCase;
            }
            expression = new Regex(query, options, TimeSpan.FromSeconds(2));
        }

        IEnumerable<FileSystemInfo> candidates;
        if (File.Exists(fullRoot))
        {
            candidates = new FileSystemInfo[] { new FileInfo(fullRoot) };
        }
        else if (Directory.Exists(fullRoot))
        {
            candidates = EnumerateTree(fullRoot, recursive, followReparsePoints, errors, cancellationToken);
        }
        else
        {
            throw new FileNotFoundException("Text search root was not found.", fullRoot);
        }

        foreach (var info in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((info.Attributes & FileAttributes.Directory) != 0)
            {
                continue;
            }

            var file = (FileInfo)info;
            var relative = Directory.Exists(fullRoot)
                ? Path.GetRelativePath(fullRoot, file.FullName)
                : file.Name;

            if (!MatchesAny(relative, file.Name, fullRoot, includes))
            {
                continue;
            }
            if (MatchesAny(relative, file.Name, fullRoot, excludePatterns, defaultWhenEmpty: false))
            {
                continue;
            }
            if (file.Length > effectiveMaxFileBytes)
            {
                continue;
            }

            filesScanned++;

            try
            {
                using var stream = new FileStream(
                    file.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64 * 1024,
                    useAsync: true);
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

                var lineNumber = 0;
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    lineNumber++;
                    cancellationToken.ThrowIfCancellationRequested();

                    if (expression is not null)
                    {
                        foreach (Match match in expression.Matches(line))
                        {
                            if (!TryAddMatch(
                                    file.FullName,
                                    lineNumber,
                                    match.Index + 1,
                                    match.Value,
                                    line))
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        var comparison = caseSensitive
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase;
                        var searchFrom = 0;
                        while (searchFrom <= line.Length - query.Length)
                        {
                            var index = line.IndexOf(query, searchFrom, comparison);
                            if (index < 0)
                            {
                                break;
                            }

                            if (!TryAddMatch(
                                    file.FullName,
                                    lineNumber,
                                    index + 1,
                                    query,
                                    line))
                            {
                                break;
                            }

                            searchFrom = index + Math.Max(1, query.Length);
                        }
                    }

                    if (truncated)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                AddSearchError(
                    errors,
                    $"{file.FullName}: {ex.GetType().Name}: {ex.Message}");
            }

            if (truncated)
            {
                break;
            }
        }

        return new TalvoraTextSearchResponse(
            fullRoot,
            filesScanned,
            matches.Count,
            truncated,
            matches,
            errors);
    }

    private static int CountOccurrences(
        string source,
        string value,
        StringComparison comparison)
    {
        var count = 0;
        var index = 0;

        while (index <= source.Length - value.Length)
        {
            var found = source.IndexOf(value, index, comparison);
            if (found < 0)
            {
                break;
            }

            count++;
            index = found + Math.Max(1, value.Length);
        }

        return count;
    }

    private static string TrimLine(string line, int maxLineChars)
    {
        if (maxLineChars == 0 || line.Length <= maxLineChars)
        {
            return line;
        }

        return line[..maxLineChars];
    }

    private static void AddSearchError(
        List<string> errors,
        string message)
    {
        if (errors.Count < AbsoluteSearchErrors)
        {
            errors.Add(message);
            return;
        }

        if (errors.Count == AbsoluteSearchErrors)
        {
            errors.Add(
                "Additional search errors omitted.");
        }
    }

    private static string[] NormalizePatterns(string[]? patterns) =>
        patterns is { Length: > 0 }
            ? patterns.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            : ["*"];

    private static bool MatchesAny(
        string relativeOrFullPath,
        string name,
        string root,
        string[]? patterns,
        bool defaultWhenEmpty = true)
    {
        if (patterns is null || patterns.Length == 0)
        {
            return defaultWhenEmpty;
        }

        var relative = relativeOrFullPath;
        if (Path.IsPathRooted(relativeOrFullPath))
        {
            try
            {
                relative = Path.GetRelativePath(root, relativeOrFullPath);
            }
            catch (Exception ex) when (
                ex is ArgumentException or
                NotSupportedException or
                PathTooLongException)
            {
            }
        }

        relative = relative.Replace('\\', '/');

        foreach (var raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var pattern = raw.Replace('\\', '/');
            if (FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: OperatingSystem.IsWindows()) ||
                FileSystemName.MatchesSimpleExpression(pattern, relative, ignoreCase: OperatingSystem.IsWindows()))
            {
                return true;
            }
        }

        return false;
    }

    private static TalvoraFileSearchEntry ToSearchEntry(FileSystemInfo info)
    {
        var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
        return new TalvoraFileSearchEntry(
            info.FullName,
            info.Name,
            isDirectory ? "directory" : "file",
            isDirectory ? null : ((FileInfo)info).Length,
            info.LastWriteTimeUtc);
    }

    private static IEnumerable<FileSystemInfo> EnumerateTree(
        string root,
        bool recursive,
        bool followReparsePoints,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<DirectoryInfo>();
        var visited = new HashSet<string>(PathComparer);
        queue.Enqueue(new DirectoryInfo(root));
        visited.Add(Path.GetFullPath(root));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = queue.Dequeue();

            FileSystemInfo[] children;
            try
            {
                children = directory.EnumerateFileSystemInfos().ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AddSearchError(
                    errors,
                    $"{directory.FullName}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return child;

                if (!recursive || (child.Attributes & FileAttributes.Directory) == 0)
                {
                    continue;
                }

                var isReparse = (child.Attributes & FileAttributes.ReparsePoint) != 0;
                if (isReparse && !followReparsePoints)
                {
                    continue;
                }

                var next = (DirectoryInfo)child;
                var key = next.FullName;

                if (isReparse)
                {
                    try
                    {
                        key = next.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next.FullName;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        AddSearchError(
                            errors,
                            $"{next.FullName}: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }
                }

                key = Path.GetFullPath(key);
                if (visited.Add(key))
                {
                    queue.Enqueue(next);
                }
            }
        }
    }
}
