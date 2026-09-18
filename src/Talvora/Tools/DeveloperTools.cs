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

namespace Talvora.Tools;

public sealed record TalvoraPathInfoResponse(
    string Path,
    bool Exists,
    string Kind,
    long? Length,
    string Attributes,
    DateTime? CreationTimeUtc,
    DateTime? LastWriteTimeUtc,
    DateTime? LastAccessTimeUtc,
    string? LinkTarget);

public sealed record TalvoraFileSearchEntry(
    string Path,
    string Name,
    string Kind,
    long? Length,
    DateTime LastWriteTimeUtc);

public sealed record TalvoraFileSearchResponse(
    string Root,
    int Count,
    bool Truncated,
    IReadOnlyList<TalvoraFileSearchEntry> Entries,
    IReadOnlyList<string> Errors);

public sealed record TalvoraTextSearchMatch(
    string Path,
    int LineNumber,
    int Column,
    string Match,
    string Line);

public sealed record TalvoraTextSearchResponse(
    string Root,
    int FilesScanned,
    int MatchCount,
    bool Truncated,
    IReadOnlyList<TalvoraTextSearchMatch> Matches,
    IReadOnlyList<string> Errors);

public sealed record TalvoraFileHashResponse(
    string Path,
    string Algorithm,
    string Hash,
    long Length);

public sealed record TalvoraReadBytesResponse(
    string Path,
    long Offset,
    int Count,
    long FileLength,
    string Base64);

public sealed record TalvoraWriteBytesResponse(
    string Path,
    int BytesWritten,
    long FileLength,
    bool Appended,
    long? Offset);

public sealed record TalvoraReplaceTextResponse(
    string Path,
    int Matches,
    int Replacements,
    bool Changed,
    string? BackupPath);

public sealed record TalvoraHttpResponse(
    string Method,
    string Url,
    int StatusCode,
    string? ReasonPhrase,
    string Version,
    IReadOnlyDictionary<string, string[]> Headers,
    string ResponseMode,
    string? Body,
    long BodyBytes,
    bool BodyTruncated,
    long ElapsedMilliseconds);

public sealed record TalvoraTcpConnectionEntry(
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,
    int ProcessId,
    string? ProcessName);

public sealed record TalvoraTcpConnectionResponse(
    int Count,
    IReadOnlyList<TalvoraTcpConnectionEntry> Connections);

public sealed record TalvoraWaitTcpResponse(
    string Host,
    int Port,
    bool Connected,
    int Attempts,
    long ElapsedMilliseconds,
    string? LastError);

public sealed record TalvoraProjectEntry(
    string Path,
    string Type,
    string Name);

public sealed record TalvoraProjectDiscoverResponse(
    string Root,
    int Count,
    bool Truncated,
    IReadOnlyList<TalvoraProjectEntry> Projects,
    IReadOnlyList<string> Errors);

public sealed record TalvoraCommandResolveResponse(
    string Command,
    bool Found,
    IReadOnlyList<string> Paths);

[McpServerToolType]
public static class DeveloperTools
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    [McpServerTool(
        Name = "talvora_path_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraPathInfoResponse)),
     Description("Return detailed metadata for any file or directory path accessible to Talvora. No path allow-list is applied.")]
    public static TalvoraPathInfoResponse PathInfo(
        [Description("File or directory path.")] string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (File.Exists(fullPath))
        {
            var info = new FileInfo(fullPath);
            return new TalvoraPathInfoResponse(
                fullPath,
                true,
                "file",
                info.Length,
                info.Attributes.ToString(),
                info.CreationTimeUtc,
                info.LastWriteTimeUtc,
                info.LastAccessTimeUtc,
                info.LinkTarget);
        }

        if (Directory.Exists(fullPath))
        {
            var info = new DirectoryInfo(fullPath);
            return new TalvoraPathInfoResponse(
                fullPath,
                true,
                "directory",
                null,
                info.Attributes.ToString(),
                info.CreationTimeUtc,
                info.LastWriteTimeUtc,
                info.LastAccessTimeUtc,
                info.LinkTarget);
        }

        return new TalvoraPathInfoResponse(
            fullPath,
            false,
            "missing",
            null,
            string.Empty,
            null,
            null,
            null,
            null);
    }

    [McpServerTool(
        Name = "talvora_file_hash",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraFileHashResponse)),
     Description("Compute a cryptographic or checksum-style hash for any accessible file. Supported algorithms: SHA256, SHA384, SHA512, SHA1, MD5.")]
    public static async Task<TalvoraFileHashResponse> FileHash(
        [Description("File path.")] string path,
        [Description("Hash algorithm: SHA256, SHA384, SHA512, SHA1, or MD5.")] string algorithm = "SHA256",
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Hash source file was not found.", fullPath);
        }

        using HashAlgorithm hasher = algorithm.Trim().ToUpperInvariant() switch
        {
            "SHA256" => SHA256.Create(),
            "SHA384" => SHA384.Create(),
            "SHA512" => SHA512.Create(),
            "SHA1" => SHA1.Create(),
            "MD5" => MD5.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), "Supported algorithms: SHA256, SHA384, SHA512, SHA1, MD5."),
        };

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 128 * 1024,
            useAsync: true);

        var hash = await hasher.ComputeHashAsync(stream, cancellationToken);
        return new TalvoraFileHashResponse(
            fullPath,
            algorithm.Trim().ToUpperInvariant(),
            Convert.ToHexString(hash),
            stream.Length);
    }

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

        var fullRoot = Path.GetFullPath(root);
        var errors = new List<string>();
        var entries = new List<TalvoraFileSearchEntry>();
        var effectivePatterns = NormalizePatterns(patterns);

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

            entries.Add(ToSearchEntry(info));
            if (maxResults > 0 && entries.Count >= maxResults)
            {
                truncated = true;
                break;
            }
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

        var fullRoot = Path.GetFullPath(root);
        var errors = new List<string>();
        var matches = new List<TalvoraTextSearchMatch>();
        var filesScanned = 0;
        var truncated = false;
        var includes = NormalizePatterns(includePatterns);

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
            if (maxFileBytes > 0 && file.Length > maxFileBytes)
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
                            matches.Add(new TalvoraTextSearchMatch(
                                file.FullName,
                                lineNumber,
                                match.Index + 1,
                                match.Value,
                                TrimLine(line, maxLineChars)));

                            if (maxMatches > 0 && matches.Count >= maxMatches)
                            {
                                truncated = true;
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

                            matches.Add(new TalvoraTextSearchMatch(
                                file.FullName,
                                lineNumber,
                                index + 1,
                                query,
                                TrimLine(line, maxLineChars)));

                            if (maxMatches > 0 && matches.Count >= maxMatches)
                            {
                                truncated = true;
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
                errors.Add($"{file.FullName}: {ex.GetType().Name}: {ex.Message}");
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

    [McpServerTool(
        Name = "talvora_read_bytes",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraReadBytesResponse)),
     Description("Read raw bytes from any accessible file and return them as base64. count=0 reads from offset to end of file.")]
    public static async Task<TalvoraReadBytesResponse> ReadBytes(
        string path,
        long offset = 0,
        int count = 0,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || count < 0)
        {
            throw new ArgumentOutOfRangeException("offset and count cannot be negative.");
        }

        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);

        if (offset > stream.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "offset is beyond end of file.");
        }

        stream.Position = offset;
        var requested = count == 0
            ? checked((int)Math.Min(int.MaxValue, stream.Length - offset))
            : checked((int)Math.Min(count, stream.Length - offset));

        var buffer = new byte[requested];
        var read = 0;
        while (read < requested)
        {
            var current = await stream.ReadAsync(buffer.AsMemory(read, requested - read), cancellationToken);
            if (current == 0)
            {
                break;
            }
            read += current;
        }

        if (read != buffer.Length)
        {
            Array.Resize(ref buffer, read);
        }

        return new TalvoraReadBytesResponse(
            fullPath,
            offset,
            read,
            stream.Length,
            Convert.ToBase64String(buffer));
    }

    [McpServerTool(
        Name = "talvora_write_bytes",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWriteBytesResponse)),
     Description("Write base64-decoded raw bytes to any accessible file. By default replaces the file. append=true appends; offset writes in place starting at the requested position. No path allow-list is applied.")]
    public static async Task<TalvoraWriteBytesResponse> WriteBytes(
        string path,
        string base64,
        bool append = false,
        long? offset = null,
        CancellationToken cancellationToken = default)
    {
        if (append && offset is not null)
        {
            throw new ArgumentException("append and offset cannot be used together.");
        }
        if (offset is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var bytes = Convert.FromBase64String(base64);
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        FileMode mode;
        FileAccess access = FileAccess.Write;
        if (append)
        {
            mode = FileMode.Append;
        }
        else if (offset is not null)
        {
            mode = FileMode.OpenOrCreate;
        }
        else
        {
            mode = FileMode.Create;
        }

        await using var stream = new FileStream(
            fullPath,
            mode,
            access,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        if (offset is long position)
        {
            stream.Position = position;
        }

        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        return new TalvoraWriteBytesResponse(
            fullPath,
            bytes.Length,
            stream.Length,
            append,
            offset);
    }

    [McpServerTool(
        Name = "talvora_replace_text",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraReplaceTextResponse)),
     Description("Patch any accessible text file by literal text or .NET regex replacement. Supports first-only or replace-all, expected match counts, case sensitivity, and optional .bak creation. No path allow-list is applied.")]
    public static async Task<TalvoraReplaceTextResponse> ReplaceText(
        string path,
        string search,
        string replacement,
        bool regex = false,
        bool caseSensitive = true,
        bool replaceAll = true,
        int expectedMatches = -1,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(search))
        {
            throw new ArgumentException("Search value cannot be empty.", nameof(search));
        }
        if (expectedMatches < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedMatches));
        }

        var fullPath = Path.GetFullPath(path);
        var original = await File.ReadAllTextAsync(fullPath, cancellationToken);
        string updated;
        int matches;

        if (regex)
        {
            var options = RegexOptions.CultureInvariant | RegexOptions.Multiline;
            if (!caseSensitive)
            {
                options |= RegexOptions.IgnoreCase;
            }

            var expression = new Regex(search, options, TimeSpan.FromSeconds(2));
            matches = expression.Count(original);
            updated = replaceAll
                ? expression.Replace(original, replacement)
                : expression.Replace(original, replacement, 1);
        }
        else
        {
            var comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            matches = CountOccurrences(original, search, comparison);

            if (replaceAll)
            {
                updated = original.Replace(search, replacement, comparison);
            }
            else
            {
                var index = original.IndexOf(search, comparison);
                updated = index < 0
                    ? original
                    : string.Concat(
                        original.AsSpan(0, index),
                        replacement,
                        original.AsSpan(index + search.Length));
            }
        }

        if (expectedMatches >= 0 && matches != expectedMatches)
        {
            throw new InvalidOperationException(
                $"Expected {expectedMatches} match(es), found {matches}. File was not modified.");
        }

        var replacements = replaceAll ? matches : Math.Min(matches, 1);
        var changed = !string.Equals(original, updated, StringComparison.Ordinal);
        string? backupPath = null;

        if (changed)
        {
            if (createBackup)
            {
                backupPath = fullPath + ".bak";
                File.Copy(fullPath, backupPath, overwrite: true);
            }

            await File.WriteAllTextAsync(fullPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        }

        return new TalvoraReplaceTextResponse(
            fullPath,
            matches,
            replacements,
            changed,
            backupPath);
    }

    [McpServerTool(
        Name = "talvora_http_request",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraHttpResponse)),
     Description("Send an arbitrary HTTP request to any URI reachable by the Talvora service. Supports any method, headers, text or base64 request bodies, redirect control, optional TLS certificate bypass, and text/base64/none response modes.")]
    public static async Task<TalvoraHttpResponse> HttpRequest(
        string method,
        string url,
        Dictionary<string, string>? headers = null,
        string? body = null,
        string? bodyBase64 = null,
        string? contentType = null,
        int timeoutSeconds = 60,
        bool allowAutoRedirect = true,
        bool ignoreTlsErrors = false,
        string responseMode = "text",
        long maxResponseBytes = 2 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new ArgumentException("HTTP method is required.", nameof(method));
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("A valid absolute URI is required.", nameof(url));
        }
        if (body is not null && bodyBase64 is not null)
        {
            throw new ArgumentException("Provide either body or bodyBase64, not both.");
        }
        if (timeoutSeconds < 0 || maxResponseBytes < 0)
        {
            throw new ArgumentOutOfRangeException("Timeout and response limits cannot be negative.");
        }

        responseMode = responseMode.Trim().ToLowerInvariant();
        if (responseMode is not ("text" or "base64" or "none"))
        {
            throw new ArgumentOutOfRangeException(nameof(responseMode), "responseMode must be text, base64, or none.");
        }

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
        };
        if (ignoreTlsErrors)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var request = new HttpRequestMessage(new HttpMethod(method.Trim().ToUpperInvariant()), uri);

        if (bodyBase64 is not null)
        {
            request.Content = new ByteArrayContent(Convert.FromBase64String(bodyBase64));
        }
        else if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
        }

        if (request.Content is not null && !string.IsNullOrWhiteSpace(contentType))
        {
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        foreach (var pair in headers ?? new Dictionary<string, string>())
        {
            if (request.Headers.TryAddWithoutValidation(pair.Key, pair.Value))
            {
                continue;
            }

            if (request.Content is null)
            {
                request.Content = new ByteArrayContent([]);
            }

            if (!request.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value))
            {
                throw new InvalidOperationException($"Unable to apply HTTP header: {pair.Key}");
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        stopwatch.Stop();

        var responseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            responseHeaders[header.Key] = header.Value.ToArray();
        }
        foreach (var header in response.Content.Headers)
        {
            responseHeaders[header.Key] = header.Value.ToArray();
        }

        string? responseBody = null;
        long bodyBytes = 0;
        var truncated = false;

        if (responseMode != "none")
        {
            var read = await ReadResponseBytesAsync(response.Content, maxResponseBytes, timeout.Token);
            bodyBytes = read.Bytes.LongLength;
            truncated = read.Truncated;

            if (responseMode == "base64")
            {
                responseBody = Convert.ToBase64String(read.Bytes);
            }
            else
            {
                var encoding = ResolveEncoding(response.Content.Headers.ContentType?.CharSet);
                responseBody = encoding.GetString(read.Bytes);
            }
        }

        return new TalvoraHttpResponse(
            request.Method.Method,
            response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString(),
            (int)response.StatusCode,
            response.ReasonPhrase,
            response.Version.ToString(),
            responseHeaders,
            responseMode,
            responseBody,
            bodyBytes,
            truncated,
            stopwatch.ElapsedMilliseconds);
    }

    [McpServerTool(
        Name = "talvora_tcp_connections",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTcpConnectionResponse)),
     Description("Return structured Windows TCP connections with local/remote endpoints, state, owning PID, and process name. Optional filters can target a port, PID, state, or text query.")]
    public static async Task<TalvoraTcpConnectionResponse> TcpConnections(
        int? localPort = null,
        int? processId = null,
        string? state = null,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var all = await ReadNetstatTcpAsync(cancellationToken);
        var filtered = FilterConnections(all, localPort, processId, state, query).ToArray();
        return new TalvoraTcpConnectionResponse(filtered.Length, filtered);
    }

    [McpServerTool(
        Name = "talvora_tcp_listeners",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTcpConnectionResponse)),
     Description("Return structured Windows TCP listeners with local endpoint, owning PID, and process name. Optional filters can target a local port, PID, or text query.")]
    public static async Task<TalvoraTcpConnectionResponse> TcpListeners(
        int? localPort = null,
        int? processId = null,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var all = await ReadNetstatTcpAsync(cancellationToken);
        var filtered = FilterConnections(all, localPort, processId, "LISTENING", query).ToArray();
        return new TalvoraTcpConnectionResponse(filtered.Length, filtered);
    }

    [McpServerTool(
        Name = "talvora_wait_tcp",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWaitTcpResponse)),
     Description("Wait until a TCP host:port becomes reachable. timeoutSeconds=0 waits without an overall timeout; attemptTimeoutMilliseconds=0 allows each connect attempt to use the overall cancellation only.")]
    public static async Task<TalvoraWaitTcpResponse> WaitTcp(
        string host,
        int port,
        int timeoutSeconds = 30,
        int pollIntervalMilliseconds = 250,
        int attemptTimeoutMilliseconds = 1500,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host is required.", nameof(host));
        }
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
        if (timeoutSeconds < 0 || pollIntervalMilliseconds < 0 || attemptTimeoutMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException("Timeout values cannot be negative.");
        }

        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            overall.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;
        string? lastError = null;

        while (!overall.IsCancellationRequested)
        {
            attempts++;
            try
            {
                using var client = new TcpClient();
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                if (attemptTimeoutMilliseconds > 0)
                {
                    attempt.CancelAfter(TimeSpan.FromMilliseconds(attemptTimeoutMilliseconds));
                }

                await client.ConnectAsync(host, port, attempt.Token);
                stopwatch.Stop();
                return new TalvoraWaitTcpResponse(
                    host,
                    port,
                    true,
                    attempts,
                    stopwatch.ElapsedMilliseconds,
                    null);
            }
            catch (Exception ex) when (
                ex is SocketException or OperationCanceledException or IOException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                lastError = ex.Message;
            }

            if (overall.IsCancellationRequested)
            {
                break;
            }

            if (pollIntervalMilliseconds > 0)
            {
                try
                {
                    await Task.Delay(pollIntervalMilliseconds, overall.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        stopwatch.Stop();
        return new TalvoraWaitTcpResponse(
            host,
            port,
            false,
            attempts,
            stopwatch.ElapsedMilliseconds,
            lastError);
    }

    [McpServerTool(
        Name = "talvora_project_discover",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraProjectDiscoverResponse)),
     Description("Discover application-development projects under any accessible root. Recognizes .NET, Node, Python, Rust, Go, Maven, Gradle, CMake, Docker, and Git markers. maxResults=0 means unlimited.")]
    public static TalvoraProjectDiscoverResponse ProjectDiscover(
        string root,
        bool recursive = true,
        bool followReparsePoints = false,
        int maxResults = 500,
        CancellationToken cancellationToken = default)
    {
        if (maxResults < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }

        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Project discovery root was not found: {fullRoot}");
        }

        var projects = new List<TalvoraProjectEntry>();
        var errors = new List<string>();
        var truncated = false;

        foreach (var info in EnumerateTree(fullRoot, recursive, followReparsePoints, errors, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var type = GetProjectType(info);
            if (type is null)
            {
                continue;
            }

            projects.Add(new TalvoraProjectEntry(
                info.FullName,
                type,
                info.Name));

            if (maxResults > 0 && projects.Count >= maxResults)
            {
                truncated = true;
                break;
            }
        }

        return new TalvoraProjectDiscoverResponse(
            fullRoot,
            projects.Count,
            truncated,
            projects,
            errors);
    }

    [McpServerTool(
        Name = "talvora_resolve_command",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCommandResolveResponse)),
     Description("Resolve an executable or command name using the Talvora service PATH and Windows PATHEXT. Returns every matching executable path when includeAll=true.")]
    public static TalvoraCommandResolveResponse ResolveCommand(
        string command,
        bool includeAll = true)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command is required.", nameof(command));
        }

        var candidates = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        var extensions = GetExecutableExtensions();

        void AddIfFile(string candidate)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full) && seen.Add(full))
            {
                candidates.Add(full);
            }
        }

        var hasDirectory = command.Contains(Path.DirectorySeparatorChar) ||
                           command.Contains(Path.AltDirectorySeparatorChar) ||
                           Path.IsPathRooted(command);

        if (hasDirectory)
        {
            AddCommandCandidate(command, extensions, AddIfFile);
        }
        else
        {
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    AddCommandCandidate(Path.Combine(directory, command), extensions, AddIfFile);
                }
                catch
                {
                }

                if (!includeAll && candidates.Count > 0)
                {
                    break;
                }
            }
        }

        return new TalvoraCommandResolveResponse(
            command,
            candidates.Count > 0,
            includeAll ? candidates : candidates.Take(1).ToArray());
    }

    private static async Task<(byte[] Bytes, bool Truncated)> ReadResponseBytesAsync(
        HttpContent content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        var truncated = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = maxBytes == 0
                ? buffer.Length
                : (int)Math.Min(buffer.Length, Math.Max(0, maxBytes + 1 - memory.Length));

            if (remaining == 0)
            {
                truncated = true;
                break;
            }

            var read = await stream.ReadAsync(buffer.AsMemory(0, remaining), cancellationToken);
            if (read == 0)
            {
                break;
            }

            memory.Write(buffer, 0, read);

            if (maxBytes > 0 && memory.Length > maxBytes)
            {
                truncated = true;
                memory.SetLength(maxBytes);
                break;
            }
        }

        return (memory.ToArray(), truncated);
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"'));
        }
        catch
        {
            return Encoding.UTF8;
        }
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
            catch
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
                errors.Add($"{directory.FullName}: {ex.GetType().Name}: {ex.Message}");
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
                        errors.Add($"{next.FullName}: {ex.GetType().Name}: {ex.Message}");
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

    private static string? GetProjectType(FileSystemInfo info)
    {
        var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
        if (isDirectory)
        {
            return string.Equals(info.Name, ".git", StringComparison.OrdinalIgnoreCase)
                ? "git-repository"
                : null;
        }

        var name = info.Name;
        var extension = Path.GetExtension(name);

        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return "dotnet-solution";
        }
        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase))
        {
            return "dotnet-project";
        }
        if (extension.Equals(".vcxproj", StringComparison.OrdinalIgnoreCase))
        {
            return "cpp-msbuild-project";
        }

        return name.ToLowerInvariant() switch
        {
            "package.json" => "node",
            "pyproject.toml" => "python",
            "requirements.txt" => "python",
            "pipfile" => "python",
            "cargo.toml" => "rust",
            "go.mod" => "go",
            "pom.xml" => "maven",
            "build.gradle" => "gradle",
            "build.gradle.kts" => "gradle",
            "cmakelists.txt" => "cmake",
            "dockerfile" => "docker",
            "docker-compose.yml" => "docker-compose",
            "docker-compose.yaml" => "docker-compose",
            "compose.yml" => "docker-compose",
            "compose.yaml" => "docker-compose",
            _ => null,
        };
    }

    private static IEnumerable<TalvoraTcpConnectionEntry> FilterConnections(
        IEnumerable<TalvoraTcpConnectionEntry> source,
        int? localPort,
        int? processId,
        string? state,
        string? query)
    {
        foreach (var item in source)
        {
            if (localPort is int requestedPort && item.LocalPort != requestedPort)
            {
                continue;
            }
            if (processId is int requestedPid && item.ProcessId != requestedPid)
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(state) &&
                !string.Equals(item.State, state, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(query))
            {
                var haystack = $"{item.LocalAddress}:{item.LocalPort} {item.RemoteAddress}:{item.RemotePort} {item.State} {item.ProcessId} {item.ProcessName}";
                if (!haystack.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            yield return item;
        }
    }

    private static async Task<IReadOnlyList<TalvoraTcpConnectionEntry>> ReadNetstatTcpAsync(
        CancellationToken cancellationToken)
    {
        var netstat = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "netstat.exe");

        var startInfo = new ProcessStartInfo
        {
            FileName = netstat,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-ano");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("tcp");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start netstat.exe.");
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var error = await stderr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"netstat.exe failed with exit code {process.ExitCode}: {error}");
        }

        var result = new List<TalvoraTcpConnectionEntry>();
        foreach (var rawLine in (await stdout).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = Regex.Split(line, @"\s+");
            if (parts.Length < 5)
            {
                continue;
            }

            if (!TryParseEndpoint(parts[1], out var localAddress, out var localPort) ||
                !TryParseEndpoint(parts[2], out var remoteAddress, out var remotePort) ||
                !int.TryParse(parts[^1], out var pid))
            {
                continue;
            }

            var connectionState = parts.Length >= 5 ? parts[3] : string.Empty;
            string? processName = null;
            try
            {
                using var owner = Process.GetProcessById(pid);
                processName = owner.ProcessName;
            }
            catch
            {
            }

            result.Add(new TalvoraTcpConnectionEntry(
                "TCP",
                localAddress,
                localPort,
                remoteAddress,
                remotePort,
                connectionState,
                pid,
                processName));
        }

        return result;
    }

    private static bool TryParseEndpoint(
        string value,
        out string address,
        out int port)
    {
        address = string.Empty;
        port = 0;

        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var bracket = value.LastIndexOf(']');
            if (bracket < 0 || bracket + 2 > value.Length)
            {
                return false;
            }

            address = value[1..bracket];
            var portText = value[(bracket + 2)..];
            return portText == "*" || int.TryParse(portText, out port);
        }

        var separator = value.LastIndexOf(':');
        if (separator < 0)
        {
            return false;
        }

        address = value[..separator];
        var text = value[(separator + 1)..];
        return text == "*" || int.TryParse(text, out port);
    }

    private static string[] GetExecutableExtensions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [string.Empty];
        }

        var raw = Environment.GetEnvironmentVariable("PATHEXT");
        var extensions = string.IsNullOrWhiteSpace(raw)
            ? new[] { ".COM", ".EXE", ".BAT", ".CMD" }
            : raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return extensions
            .Select(extension => extension.StartsWith('.') ? extension : "." + extension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddCommandCandidate(
        string basePath,
        string[] extensions,
        Action<string> add)
    {
        if (Path.HasExtension(basePath))
        {
            add(basePath);
            return;
        }

        add(basePath);
        foreach (var extension in extensions)
        {
            if (extension.Length > 0)
            {
                add(basePath + extension);
            }
        }
    }
}
