using System.ComponentModel;
using System.Security;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraSearchResult(string Id, string Title, string Text, string Url);
public sealed record TalvoraSearchResponse(IReadOnlyList<TalvoraSearchResult> Results);
public sealed record TalvoraFetchMetadata(string Path, long Length, DateTime LastWriteTimeUtc, string Extension);
public sealed record TalvoraFetchResponse(
    string Id,
    string Title,
    string Text,
    string Url,
    TalvoraFetchMetadata Metadata);

[McpServerToolType]
public static class KnowledgeTools
{
    private const int MaxResults = 20;
    private const long MaxSearchFileBytes = 4L * 1024 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "",
        ".txt", ".md", ".markdown", ".json", ".jsonl", ".yaml", ".yml", ".xml",
        ".csv", ".tsv", ".log", ".ini", ".toml", ".config", ".conf", ".properties",
        ".ps1", ".psm1", ".psd1", ".cmd", ".bat", ".cs", ".csproj", ".sln", ".slnx",
        ".props", ".targets", ".js", ".jsx", ".ts", ".tsx", ".py", ".sql", ".sh",
        ".html", ".htm", ".css", ".scss", ".java", ".kt", ".kts", ".go", ".rs",
        ".rb", ".php", ".env",
    };

    [McpServerTool(
        Name = "search",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraSearchResponse)),
     Description("Search local Windows text documents available to Talvora. Returns document IDs that can be passed to fetch. The default corpus is Public Documents, user profiles, and ProgramData; set TALVORA_KNOWLEDGE_ROOTS to override the roots.")]
    public static async Task<TalvoraSearchResponse> Search(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new TalvoraSearchResponse([]);
        }

        var term = query.Trim();
        var results = new List<TalvoraSearchResult>(MaxResults);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetSearchRoots())
        {
            foreach (var file in EnumerateFilesSafe(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(file);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    continue;
                }

                if (!seenFiles.Add(fullPath))
                {
                    continue;
                }

                if (results.Count >= MaxResults)
                {
                    return new TalvoraSearchResponse(results);
                }

                if (!TextExtensions.Contains(Path.GetExtension(fullPath)))
                {
                    continue;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(fullPath);
                    if (!info.Exists || info.Length > MaxSearchFileBytes)
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                {
                    continue;
                }

                string text;
                try
                {
                    text = await File.ReadAllTextAsync(fullPath, cancellationToken);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                {
                    continue;
                }

                if (text.IndexOf('\0') >= 0)
                {
                    continue;
                }

                var contentIndex = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                var nameMatch = info.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
                if (contentIndex < 0 && !nameMatch)
                {
                    continue;
                }

                results.Add(new TalvoraSearchResult(
                    fullPath,
                    info.Name,
                    CreateSnippet(text, contentIndex, term.Length),
                    ToFileUrl(fullPath)));
            }
        }

        return new TalvoraSearchResponse(results);
    }

    [McpServerTool(
        Name = "fetch",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraFetchResponse)),
     Description("Fetch the complete text of a local Windows document by the ID returned from search. The document ID is its absolute Windows path.")]
    public static async Task<TalvoraFetchResponse> Fetch(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Document ID is required.", nameof(id));
        }

        var fullPath = Path.GetFullPath(id);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Document was not found.", fullPath);
        }

        var text = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var info = new FileInfo(fullPath);

        return new TalvoraFetchResponse(
            fullPath,
            info.Name,
            text,
            ToFileUrl(fullPath),
            new TalvoraFetchMetadata(
                fullPath,
                info.Length,
                info.LastWriteTimeUtc,
                info.Extension));
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        var configured = Environment.GetEnvironmentVariable("TALVORA_KNOWLEDGE_ROOTS");
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(configured))
        {
            roots.AddRange(configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else
        {
            var publicDocuments = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
            if (!string.IsNullOrWhiteSpace(publicDocuments))
            {
                roots.Add(publicDocuments);
            }

            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrWhiteSpace(systemDrive))
            {
                roots.Add(Path.Combine(systemDrive, "Users"));
            }

            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                roots.Add(programData);
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (Directory.Exists(fullPath) && seen.Add(fullPath))
            {
                yield return fullPath;
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
            {
                files = [];
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
            {
                directories = [];
            }

            foreach (var directory in directories)
            {
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(directory);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                {
                    // Skip directories that disappear or become inaccessible while walking.
                }
            }
        }
    }

    private static string CreateSnippet(string text, int matchIndex, int queryLength)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var start = matchIndex >= 0 ? Math.Max(0, matchIndex - 120) : 0;
        var desiredLength = matchIndex >= 0 ? queryLength + 360 : 400;
        var length = Math.Min(desiredLength, text.Length - start);
        var snippet = text.Substring(start, length)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (start > 0)
        {
            snippet = "..." + snippet;
        }
        if (start + length < text.Length)
        {
            snippet += "...";
        }

        return snippet;
    }

    private static string ToFileUrl(string fullPath) => new Uri(fullPath).AbsoluteUri;
}
