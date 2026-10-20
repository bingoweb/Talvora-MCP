using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Talvora.Shared;
using ModelContextProtocol;
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
    private static readonly TimeSpan SearchBudget = TimeSpan.FromSeconds(8);

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
     Description("Search local Windows text documents available to Talvora. Returns document IDs that can be passed to fetch. By default searches the active interactive Windows user's Documents, Desktop, Downloads, OneDrive, Public Documents and Talvora Knowledge. Set TALVORA_KNOWLEDGE_ROOTS to replace the default corpus.")]
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
        var stopwatch = Stopwatch.StartNew();

        var roots = GetSearchRoots().ToArray();
        foreach (var root in roots)
        {
            foreach (var file in EnumerateFilesSafe(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stopwatch.Elapsed >= SearchBudget)
                {
                    return new TalvoraSearchResponse(results);
                }
                var fullPath = file;

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
                    if (IsCloudOnly(info.Attributes) ||
                        IsCloudReparseTag(fullPath, info.Attributes) ||
                        info.Length > MaxSearchFileBytes)
                    {
                        continue;
                    }

                    fullPath = ResolveFinalPath(fullPath);
                    if (!IsWithinSearchRoot(fullPath, roots))
                    {
                        continue;
                    }

                    info = new FileInfo(fullPath);
                    if (IsCloudOnly(info.Attributes) ||
                        IsCloudReparseTag(fullPath, info.Attributes) ||
                        info.Length > MaxSearchFileBytes)
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException or Win32Exception or ArgumentException)
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
     Description("Fetch the complete text of a local Windows document by the ID returned from search. The document ID must resolve under an allowed knowledge root. For unrestricted local reads use talvora_read_text_range. Files larger than 4 MiB or decoded text above the 4 MiB response budget are rejected before MCP serialization; use talvora_read_text_range for larger documents.")]
    public static async Task<TalvoraFetchResponse> Fetch(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Document ID is required.", nameof(id));
        }

        if (!Path.IsPathFullyQualified(id))
        {
            throw new ArgumentException("Document ID must be an absolute path.", nameof(id));
        }

        var fullPath = Path.GetFullPath(id);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Document was not found.", fullPath);
        }

        var initialAttributes = File.GetAttributes(fullPath);
        if (IsCloudOnly(initialAttributes) ||
            IsCloudReparseTag(fullPath, initialAttributes))
        {
            throw new InvalidOperationException(
                "Bulutta yalnızca çevrimiçi bulunan dosya otomatik indirilmeyecek.");
        }

        fullPath = ResolveFinalPath(fullPath);
        if (!IsWithinSearchRoot(fullPath, GetSearchRoots().ToArray()))
        {
            throw new McpException(
                "Belge izin verilen bilgi klasörlerinin dışında. Genel erişim için talvora_read_text_range aracını kullanın.");
        }

        var info = new FileInfo(fullPath);
        if (IsCloudOnly(info.Attributes) ||
            IsCloudReparseTag(fullPath, info.Attributes))
        {
            throw new InvalidOperationException(
                "Bulutta yalnızca çevrimiçi bulunan dosya otomatik indirilmeyecek.");
        }
        if (info.Length > MaxSearchFileBytes)
        {
            throw new InvalidOperationException(
                "Knowledge document exceeds the 4 MiB fetch budget. Use talvora_read_text_range for larger documents.");
        }

        var text = await File.ReadAllTextAsync(fullPath, cancellationToken);
        if (text.Length >
            ConfigAssetTools.AbsoluteTextResponseCharacters)
        {
            throw new InvalidOperationException(
                "Decoded knowledge document exceeds the 4 MiB fetch response budget. Use talvora_read_text_range for larger documents.");
        }

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
            roots.AddRange(configured.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        else
        {
            AddDefaultSearchRoots(roots);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            string? finalPath = null;
            try
            {
                var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
                if (!Directory.Exists(fullPath) ||
                    IsCloudOnly(File.GetAttributes(fullPath)))
                {
                    continue;
                }

                finalPath = ResolveFinalPath(fullPath);
            }
            catch (Exception ex) when (ex is
                ArgumentException or NotSupportedException or PathTooLongException or
                Win32Exception or UnauthorizedAccessException or IOException or SecurityException)
            {
                // A non-readable knowledge root is not an authorization grant.
            }

            if (finalPath is not null && seen.Add(finalPath))
            {
                yield return finalPath;
            }
        }
    }

    internal sealed record KnowledgeUserProfile(
        string ProfilePath,
        IReadOnlyList<string> OneDriveRoots);

    // Test seam only. Production code never sets it.
    internal static Func<KnowledgeUserProfile?>? UserProfileResolverOverride { get; set; }

    internal static KnowledgeUserProfile? ResolveKnowledgeUserProfile()
    {
        if (UserProfileResolverOverride is { } overrideResolver)
        {
            return overrideResolver();
        }

        if (IsRunningAsLocalSystem())
        {
            try
            {
                var user = WindowsSessionLauncher.GetActiveInteractiveUser();
                return CreateKnowledgeUserProfile(
                    user.UserProfile,
                    name => user.Environment.TryGetValue(name, out var value) ? value : null);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // LocalSystem never falls back to its own or another user's profile.
                return null;
            }
        }

        return CreateKnowledgeUserProfile(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable);
    }

    private static KnowledgeUserProfile? CreateKnowledgeUserProfile(
        string? profile,
        Func<string, string?> readVariable)
    {
        if (string.IsNullOrWhiteSpace(profile) || !Path.IsPathFullyQualified(profile))
        {
            return null;
        }

        var oneDrive = new List<string>();
        foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var value = readVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                oneDrive.Add(value);
            }
        }

        return new KnowledgeUserProfile(profile, oneDrive);
    }

    private static bool IsRunningAsLocalSystem()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true;
    }

    private static void AddDefaultSearchRoots(List<string> roots)
    {
        var publicDocuments = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonDocuments);
        if (!string.IsNullOrWhiteSpace(publicDocuments))
        {
            roots.Add(publicDocuments);
        }

        var userProfile = ResolveKnowledgeUserProfile();
        if (userProfile is not null)
        {
            var profile = userProfile.ProfilePath;
            roots.Add(Path.Combine(profile, "Documents"));
            roots.Add(Path.Combine(profile, "Desktop"));
            roots.Add(Path.Combine(profile, "Downloads"));
            roots.AddRange(userProfile.OneDriveRoots);

            try
            {
                roots.AddRange(Directory.GetDirectories(
                    profile, "OneDrive*", SearchOption.TopDirectoryOnly));
            }
            catch (Exception ex) when (ex is
                UnauthorizedAccessException or IOException or SecurityException)
            {
                // Missing/inaccessible OneDrive directories are optional.
            }
        }

        var programData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
        {
            roots.Add(Path.Combine(programData, "Talvora", "Knowledge"));
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
                    var attributes = File.GetAttributes(directory);
                    if ((attributes & FileAttributes.ReparsePoint) == 0 && !IsCloudOnly(attributes))
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

    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    private static bool IsCloudOnly(FileAttributes a) =>
        (a & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;

    private static bool IsWithinSearchRoot(
        string finalPath,
        IReadOnlyList<string> configuredRoots) =>
        configuredRoots.Any(root =>
            string.Equals(finalPath, root, StringComparison.OrdinalIgnoreCase) ||
            finalPath.StartsWith(
                Path.EndsInDirectorySeparator(root)
                    ? root
                    : root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));

    private static bool IsCloudReparseTag(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        var findHandle = FindFirstFileW(path, out var found);
        if (findHandle == new IntPtr(-1))
        {
            return true; // Unknown reparse metadata is not safe to hydrate.
        }

        try
        {
            return IsCloudReparseTag(found.ReparseTag);
        }
        finally
        {
            FindClose(findHandle);
        }
    }

    private static bool IsCloudReparseTag(uint tag) =>
        (tag & 0xFFFF0FFFu) == 0x9000001Au;

    // Resolve junctions and symbolic links, including links in a parent directory.
    // Metadata-only access does not read the document's data stream.
    private static string ResolveFinalPath(string path)
    {
        var normalized = Path.GetFullPath(path);
        using var handle = CreateFileW(
            normalized,
            desiredAccess: 0x00000080u, // FILE_READ_ATTRIBUTES
            shareMode: 0x00000007u, // READ | WRITE | DELETE
            securityAttributes: IntPtr.Zero,
            creationDisposition: 3u, // OPEN_EXISTING
            flagsAndAttributes: 0x02000000u, // FILE_FLAG_BACKUP_SEMANTICS
            templateFile: IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandleW(
            handle, buffer, (uint)buffer.Capacity, 0);
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));
            length = GetFinalPathNameByHandleW(
                handle, buffer, (uint)buffer.Capacity, 0);
        }

        if (length == 0 || length >= buffer.Capacity)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "The Windows filesystem could not resolve the final document path.");
        }

        var finalPath = buffer.ToString();
        if (finalPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            finalPath = @"\\" + finalPath[8..];
        }
        else if (finalPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            finalPath = finalPath[4..];
        }
        return Path.GetFullPath(finalPath);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindData
    {
        public FileAttributes Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint ReparseTag;
        public uint Reserved1;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? FileName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string? AlternateFileName;
    }

    [DllImport("kernel32.dll", EntryPoint = "FindFirstFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(
        string path, out Win32FindData findData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr findHandle);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle handle,
        StringBuilder output,
        uint outputLength,
        uint flags);

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