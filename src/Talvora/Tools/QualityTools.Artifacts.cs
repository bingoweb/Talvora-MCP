using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class QualityTools
{
    private static readonly string[] DefaultArtifactPatterns =
    [
        "*.exe",
        "*.dll",
        "*.msi",
        "*.msix",
        "*.appx",
        "*.nupkg",
        "*.snupkg",
        "*.zip",
        "*.jar",
        "*.war",
        "*.apk",
        "*.aab",
        "*.whl",
        "*.tgz",
        "*.tar.gz",
        "*.wasm",
        "*.pdb",
    ];

    [McpServerTool(
        Name = "talvora_artifact_inventory",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraArtifactInventoryResponse)),
     Description("Inventory build/release artifacts under any accessible directory with hashes, sizes, timestamps, and PE file/product versions where available. maxResults=0 requests the finite server maximum page; use resultOffset/nextResultOffset to continue while the tree is unchanged. Custom wildcard patterns may replace the default artifact set.")]
    public static async Task<TalvoraArtifactInventoryResponse>
        ArtifactInventory(
            string root,
            string[]? patterns = null,
            bool recursive = true,
            int maxResults = 500,
            long resultOffset = 0,
            string hashAlgorithm = "SHA256",
            bool includeVersionInfo = true,
            bool followReparsePoints = false,
            CancellationToken cancellationToken = default)
    {
        if (maxResults < 0 || resultOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                "maxResults and resultOffset cannot be negative.");
        }

        var effectiveMaxResults =
            maxResults == 0
                ? AbsoluteArtifactResults
                : Math.Min(
                    maxResults,
                    AbsoluteArtifactResults);

        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException(
                $"Artifact root was not found: {fullRoot}");
        }

        var effectivePatterns =
            patterns is { Length: > 0 }
                ? patterns
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray()
                : DefaultArtifactPatterns;

        var artifacts = new List<TalvoraArtifactEntry>();
        var errors = new List<string>();
        var queue = new Queue<DirectoryInfo>();
        var visited = new HashSet<string>(
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

        queue.Enqueue(new DirectoryInfo(fullRoot));
        visited.Add(fullRoot);
        var truncated = false;
        long matchingIndex = 0;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = queue.Dequeue();

            FileInfo[] files;
            DirectoryInfo[] directories;
            try
            {
                files = directory
                    .EnumerateFiles()
                    .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                directories = recursive
                    ? directory
                        .EnumerateDirectories()
                        .OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    : [];
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                AddArtifactError(
                    errors,
                    $"{directory.FullName}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = Path.GetRelativePath(
                    fullRoot,
                    file.FullName);

                if (!MatchesArtifact(
                        file.Name,
                        relative,
                        effectivePatterns))
                {
                    continue;
                }

                if (matchingIndex < resultOffset)
                {
                    matchingIndex++;
                    continue;
                }

                if (artifacts.Count >= effectiveMaxResults)
                {
                    truncated = true;
                    break;
                }

                try
                {
                    var hash = await DeveloperTools.FileHash(
                        file.FullName,
                        hashAlgorithm,
                        cancellationToken);

                    string? fileVersion = null;
                    string? productVersion = null;

                    if (includeVersionInfo &&
                        (file.Extension.Equals(
                             ".exe",
                             StringComparison.OrdinalIgnoreCase) ||
                         file.Extension.Equals(
                             ".dll",
                             StringComparison.OrdinalIgnoreCase)))
                    {
                        var version = FileVersionInfo.GetVersionInfo(
                            file.FullName);
                        fileVersion =
                            NullIfWhiteSpace(version.FileVersion);
                        productVersion =
                            NullIfWhiteSpace(version.ProductVersion);
                    }

                    artifacts.Add(new TalvoraArtifactEntry(
                        file.FullName,
                        relative,
                        file.Extension,
                        file.Length,
                        file.LastWriteTimeUtc,
                        hash.Algorithm,
                        hash.Hash,
                        fileVersion,
                        productVersion));
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException)
                {
                    AddArtifactError(
                        errors,
                        $"{file.FullName}: {ex.GetType().Name}: {ex.Message}");
                }

                matchingIndex++;
            }

            if (truncated)
            {
                break;
            }

            foreach (var child in directories)
            {
                var isReparse =
                    (child.Attributes & FileAttributes.ReparsePoint) != 0;
                if (isReparse && !followReparsePoints)
                {
                    continue;
                }

                var key = child.FullName;
                if (isReparse)
                {
                    try
                    {
                        key =
                            child.ResolveLinkTarget(returnFinalTarget: true)
                                ?.FullName
                            ?? child.FullName;
                    }
                    catch (Exception ex) when (
                        ex is IOException or UnauthorizedAccessException)
                    {
                        AddArtifactError(
                            errors,
                            $"{child.FullName}: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }
                }

                key = Path.GetFullPath(key);
                if (visited.Add(key))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return new TalvoraArtifactInventoryResponse(
            fullRoot,
            artifacts.Count,
            truncated,
            artifacts,
            errors,
            resultOffset,
            truncated
                ? matchingIndex
                : null);
    }

    private static bool MatchesArtifact(
        string name,
        string relative,
        IReadOnlyList<string> patterns)
    {
        var normalized = relative.Replace('\\', '/');

        foreach (var rawPattern in patterns)
        {
            var pattern = rawPattern.Replace('\\', '/');
            if (FileSystemName.MatchesSimpleExpression(
                    pattern,
                    name,
                    ignoreCase: OperatingSystem.IsWindows()) ||
                FileSystemName.MatchesSimpleExpression(
                    pattern,
                    normalized,
                    ignoreCase: OperatingSystem.IsWindows()))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddArtifactError(
        List<string> errors,
        string message)
    {
        if (errors.Count < AbsoluteArtifactErrors)
        {
            errors.Add(message);
            return;
        }

        if (errors.Count == AbsoluteArtifactErrors)
        {
            errors.Add(
                "Additional artifact errors omitted.");
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value;
}
