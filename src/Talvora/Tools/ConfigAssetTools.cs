using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraTextRangeResponse(
    string Path,
    int StartLine,
    int LinesRead,
    bool EndReached,
    string Text);

public sealed record TalvoraTextTailResponse(
    string Path,
    int TotalLines,
    int StartLine,
    int LinesRead,
    string Text);

public sealed record TalvoraAppendTextResponse(
    string Path,
    int CharactersAppended,
    long FileLength,
    bool NewLineAppended);

public sealed record TalvoraJsonGetResponse(
    string Path,
    string Pointer,
    bool Found,
    string Kind,
    string? ValueJson);

public sealed record TalvoraJsonMutationResponse(
    string Path,
    string Pointer,
    bool Changed,
    string? BackupPath);

public sealed record TalvoraArchiveEntry(
    string FullName,
    long Length,
    long CompressedLength,
    DateTimeOffset LastWriteTime,
    bool IsDirectory,
    int ExternalAttributes);

public sealed record TalvoraArchiveListResponse(
    string ArchivePath,
    int Count,
    IReadOnlyList<TalvoraArchiveEntry> Entries);

public sealed record TalvoraArchiveCreateResponse(
    string SourceDirectory,
    string ArchivePath,
    long Length,
    int EntryCount,
    string Compression,
    bool IncludedBaseDirectory);

public sealed record TalvoraArchiveExtractResponse(
    string ArchivePath,
    string DestinationDirectory,
    int EntriesExtracted,
    long BytesExtracted,
    bool Overwrite,
    bool AllowOutsideDestination);

public sealed record TalvoraHttpDownloadResponse(
    string Url,
    string FinalUrl,
    int StatusCode,
    string? ReasonPhrase,
    string DestinationPath,
    long ExistingBytes,
    long BytesWritten,
    long FileLength,
    bool Resumed,
    string Sha256);

[McpServerToolType]
public static class ConfigAssetTools
{
    [McpServerTool(
        Name = "talvora_read_text_range",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTextRangeResponse)),
     Description("Read a line range from any accessible text file without returning the entire file. startLine is 1-based; lineCount=0 reads from startLine to EOF. No path allow-list is applied.")]
    public static async Task<TalvoraTextRangeResponse> ReadTextRange(
        string path,
        int startLine = 1,
        int lineCount = 200,
        CancellationToken cancellationToken = default)
    {
        if (startLine < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine));
        }
        if (lineCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount));
        }

        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var reader = new StreamReader(
            stream,
            encoding: Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        var builder = new StringBuilder();
        var currentLine = 0;
        var linesRead = 0;
        var endReached = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                endReached = true;
                break;
            }

            currentLine++;
            if (currentLine < startLine)
            {
                continue;
            }

            if (lineCount > 0 && linesRead >= lineCount)
            {
                endReached = false;
                break;
            }

            if (linesRead > 0)
            {
                builder.AppendLine();
            }
            builder.Append(line);
            linesRead++;

            if (lineCount > 0 && linesRead >= lineCount)
            {
                var nextLine = await reader.ReadLineAsync(cancellationToken);
                endReached = nextLine is null;
                break;
            }
        }

        return new TalvoraTextRangeResponse(
            fullPath,
            startLine,
            linesRead,
            endReached,
            builder.ToString());
    }

    [McpServerTool(
        Name = "talvora_tail_text",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTextTailResponse)),
     Description("Return the last lines of any accessible text file. lineCount=0 returns the complete file. Useful for logs and build output. No path allow-list is applied.")]
    public static TalvoraTextTailResponse TailText(
        string path,
        int lineCount = 200,
        CancellationToken cancellationToken = default)
    {
        if (lineCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount));
        }

        var fullPath = Path.GetFullPath(path);

        if (lineCount == 0)
        {
            var all = new List<string>();
            foreach (var line in File.ReadLines(fullPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                all.Add(line);
            }

            return new TalvoraTextTailResponse(
                fullPath,
                all.Count,
                all.Count == 0 ? 0 : 1,
                all.Count,
                string.Join(Environment.NewLine, all));
        }

        var queue = new Queue<string>(lineCount);
        var totalLines = 0;

        foreach (var line in File.ReadLines(fullPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalLines++;

            if (queue.Count == lineCount)
            {
                queue.Dequeue();
            }
            queue.Enqueue(line);
        }

        var startLine = queue.Count == 0
            ? 0
            : totalLines - queue.Count + 1;

        return new TalvoraTextTailResponse(
            fullPath,
            totalLines,
            startLine,
            queue.Count,
            string.Join(Environment.NewLine, queue));
    }

    [McpServerTool(
        Name = "talvora_append_text",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraAppendTextResponse)),
     Description("Append UTF-8 text to any accessible file, creating parent directories and the file when needed. appendNewLine=true appends the platform newline after the supplied content. No path allow-list is applied.")]
    public static async Task<TalvoraAppendTextResponse> AppendText(
        string path,
        string content,
        bool appendNewLine = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 64 * 1024,
            leaveOpen: true);

        await writer.WriteAsync(content.AsMemory(), cancellationToken);
        if (appendNewLine)
        {
            await writer.WriteAsync(Environment.NewLine.AsMemory(), cancellationToken);
        }
        await writer.FlushAsync(cancellationToken);
        await stream.FlushAsync(cancellationToken);

        return new TalvoraAppendTextResponse(
            fullPath,
            content.Length,
            stream.Length,
            appendNewLine);
    }

    [McpServerTool(
        Name = "talvora_json_get",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJsonGetResponse)),
     Description("Read a JSON value from any accessible JSON file using RFC 6901 JSON Pointer syntax. An empty pointer returns the complete document.")]
    public static async Task<TalvoraJsonGetResponse> JsonGet(
        string path,
        string pointer = "",
        bool indented = true,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var root = await ParseJsonFileAsync(fullPath, cancellationToken);
        var tokens = ParsePointer(pointer);

        if (!TryResolve(root, tokens, out var node))
        {
            return new TalvoraJsonGetResponse(
                fullPath,
                pointer,
                false,
                "Missing",
                null);
        }

        return new TalvoraJsonGetResponse(
            fullPath,
            pointer,
            true,
            GetJsonKind(node),
            ToJson(node, indented));
    }

    [McpServerTool(
        Name = "talvora_json_set",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJsonMutationResponse)),
     Description("Set or create a JSON value in any accessible JSON file using RFC 6901 JSON Pointer syntax. valueJson must itself be valid JSON. createMissing can create intermediate object/array nodes. No path allow-list is applied.")]
    public static async Task<TalvoraJsonMutationResponse> JsonSet(
        string path,
        string pointer,
        string valueJson,
        bool createMissing = true,
        bool indented = true,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var originalText = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var root = JsonNode.Parse(originalText);
        var value = JsonNode.Parse(valueJson);
        var tokens = ParsePointer(pointer);

        root = SetPointer(root, tokens, value, createMissing);

        var updatedText = ToJson(root, indented);
        var changed = !string.Equals(
            NormalizeTrailingNewline(originalText),
            NormalizeTrailingNewline(updatedText),
            StringComparison.Ordinal);

        string? backupPath = null;
        if (changed)
        {
            if (createBackup)
            {
                backupPath = fullPath + ".bak";
                File.Copy(fullPath, backupPath, overwrite: true);
            }

            await File.WriteAllTextAsync(
                fullPath,
                updatedText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }

        return new TalvoraJsonMutationResponse(
            fullPath,
            pointer,
            changed,
            backupPath);
    }

    [McpServerTool(
        Name = "talvora_json_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraJsonMutationResponse)),
     Description("Delete a JSON value from any accessible JSON file using RFC 6901 JSON Pointer syntax. An empty pointer replaces the complete document with JSON null. Missing targets are handled idempotently.")]
    public static async Task<TalvoraJsonMutationResponse> JsonDelete(
        string path,
        string pointer,
        bool indented = true,
        bool createBackup = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var originalText = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var root = JsonNode.Parse(originalText);
        var tokens = ParsePointer(pointer);
        var changed = DeletePointer(ref root, tokens);

        string? backupPath = null;
        if (changed)
        {
            if (createBackup)
            {
                backupPath = fullPath + ".bak";
                File.Copy(fullPath, backupPath, overwrite: true);
            }

            await File.WriteAllTextAsync(
                fullPath,
                ToJson(root, indented),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }

        return new TalvoraJsonMutationResponse(
            fullPath,
            pointer,
            changed,
            backupPath);
    }

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
                try { File.Delete(temp); } catch { }
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

    [McpServerTool(
        Name = "talvora_http_download",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraHttpDownloadResponse)),
     Description("Download an HTTP/HTTPS response body directly to any accessible file without an MCP response-size limit. Supports arbitrary request headers, redirects, optional TLS certificate bypass, overwrite, and byte-range resume.")]
    public static async Task<TalvoraHttpDownloadResponse> HttpDownload(
        string url,
        string destinationPath,
        Dictionary<string, string>? headers = null,
        bool overwrite = false,
        bool resume = false,
        int timeoutSeconds = 300,
        bool allowAutoRedirect = true,
        bool ignoreTlsErrors = false,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("A valid absolute URI is required.", nameof(url));
        }
        if (timeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        var destination = Path.GetFullPath(destinationPath);
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var existingBytes = File.Exists(destination)
            ? new FileInfo(destination).Length
            : 0;

        if (existingBytes > 0 && !overwrite && !resume)
        {
            throw new IOException($"Download destination already exists: {destination}");
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
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        foreach (var pair in headers ?? new Dictionary<string, string>())
        {
            if (!request.Headers.TryAddWithoutValidation(pair.Key, pair.Value))
            {
                throw new InvalidOperationException(
                    $"Unable to apply HTTP request header: {pair.Key}");
            }
        }

        if (resume && existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP download failed with status {(int)response.StatusCode} {response.ReasonPhrase}.",
                null,
                response.StatusCode);
        }

        var actualResume =
            resume &&
            existingBytes > 0 &&
            response.StatusCode == HttpStatusCode.PartialContent;

        var mode = actualResume
            ? FileMode.Append
            : FileMode.Create;

        await using (var output = new FileStream(
            destination,
            mode,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 128 * 1024,
            useAsync: true))
        await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
        {
            await input.CopyToAsync(output, 128 * 1024, timeout.Token);
            await output.FlushAsync(timeout.Token);
        }

        var finalLength = new FileInfo(destination).Length;
        var bytesWritten = actualResume
            ? finalLength - existingBytes
            : finalLength;

        string hash;
        await using (var hashStream = new FileStream(
            destination,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 128 * 1024,
            useAsync: true))
        {
            hash = Convert.ToHexString(
                await System.Security.Cryptography.SHA256.HashDataAsync(
                    hashStream,
                    cancellationToken));
        }

        return new TalvoraHttpDownloadResponse(
            uri.ToString(),
            response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString(),
            (int)response.StatusCode,
            response.ReasonPhrase,
            destination,
            existingBytes,
            bytesWritten,
            finalLength,
            actualResume,
            hash);
    }

    private static async Task<JsonNode?> ParseJsonFileAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);

        return await JsonNode.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
    }

    internal static string[] ParsePointer(string pointer)
    {
        if (pointer is null)
        {
            throw new ArgumentNullException(nameof(pointer));
        }

        if (pointer.Length == 0)
        {
            return [];
        }

        if (!pointer.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "JSON Pointer must be empty or begin with '/'.",
                nameof(pointer));
        }

        return pointer[1..]
            .Split('/')
            .Select(token => token.Replace("~1", "/").Replace("~0", "~"))
            .ToArray();
    }

    internal static bool TryResolve(
        JsonNode? root,
        IReadOnlyList<string> tokens,
        out JsonNode? node)
    {
        node = root;
        if (tokens.Count == 0)
        {
            return true;
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            if (node is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(tokens[i], out node))
                {
                    return false;
                }
            }
            else if (node is JsonArray array)
            {
                if (!int.TryParse(tokens[i], out var index) ||
                    index < 0 ||
                    index >= array.Count)
                {
                    return false;
                }

                node = array[index];
            }
            else
            {
                return false;
            }

            if (node is null && i < tokens.Count - 1)
            {
                return false;
            }
        }

        return true;
    }

    internal static JsonNode? SetPointer(
        JsonNode? root,
        IReadOnlyList<string> tokens,
        JsonNode? value,
        bool createMissing)
    {
        if (tokens.Count == 0)
        {
            return value;
        }

        root ??= CreateContainerForToken(tokens[0]);

        JsonNode current = root;
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            var token = tokens[i];
            var nextToken = tokens[i + 1];

            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(token, out var child) || child is null)
                {
                    if (!createMissing)
                    {
                        throw new KeyNotFoundException(
                            $"JSON Pointer segment was not found: {token}");
                    }

                    child = CreateContainerForToken(nextToken);
                    obj[token] = child;
                }

                current = child;
            }
            else if (current is JsonArray array)
            {
                if (!int.TryParse(token, out var index) || index < 0)
                {
                    throw new InvalidOperationException(
                        $"JSON Pointer array segment is not a non-negative integer: {token}");
                }

                if (index >= array.Count)
                {
                    if (!createMissing)
                    {
                        throw new IndexOutOfRangeException(
                            $"JSON Pointer array index is outside the array: {index}");
                    }

                    while (array.Count <= index)
                    {
                        array.Add(null);
                    }
                }

                var child = array[index];
                if (child is null)
                {
                    if (!createMissing)
                    {
                        throw new KeyNotFoundException(
                            $"JSON Pointer segment resolved to null: {token}");
                    }

                    child = CreateContainerForToken(nextToken);
                    array[index] = child;
                }

                current = child;
            }
            else
            {
                throw new InvalidOperationException(
                    $"JSON Pointer cannot traverse through scalar value at segment: {token}");
            }
        }

        var finalToken = tokens[^1];

        if (current is JsonObject finalObject)
        {
            finalObject[finalToken] = value;
            return root;
        }

        if (current is JsonArray finalArray)
        {
            if (string.Equals(finalToken, "-", StringComparison.Ordinal))
            {
                finalArray.Add(value);
                return root;
            }

            if (!int.TryParse(finalToken, out var index) || index < 0)
            {
                throw new InvalidOperationException(
                    $"JSON Pointer array segment is not a non-negative integer or '-': {finalToken}");
            }

            if (index < finalArray.Count)
            {
                finalArray[index] = value;
                return root;
            }

            if (!createMissing)
            {
                throw new IndexOutOfRangeException(
                    $"JSON Pointer array index is outside the array: {index}");
            }

            while (finalArray.Count < index)
            {
                finalArray.Add(null);
            }

            finalArray.Add(value);
            return root;
        }

        throw new InvalidOperationException(
            "JSON Pointer parent is a scalar value.");
    }

    internal static bool DeletePointer(
        ref JsonNode? root,
        IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            var changed = root is not null;
            root = null;
            return changed;
        }

        if (root is null)
        {
            return false;
        }

        JsonNode current = root;
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            var token = tokens[i];

            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(token, out var child) || child is null)
                {
                    return false;
                }
                current = child;
            }
            else if (current is JsonArray array)
            {
                if (!int.TryParse(token, out var index) ||
                    index < 0 ||
                    index >= array.Count ||
                    array[index] is not { } child)
                {
                    return false;
                }
                current = child;
            }
            else
            {
                return false;
            }
        }

        var finalToken = tokens[^1];
        if (current is JsonObject finalObject)
        {
            return finalObject.Remove(finalToken);
        }

        if (current is JsonArray finalArray &&
            int.TryParse(finalToken, out var finalIndex) &&
            finalIndex >= 0 &&
            finalIndex < finalArray.Count)
        {
            finalArray.RemoveAt(finalIndex);
            return true;
        }

        return false;
    }

    private static JsonNode CreateContainerForToken(string token) =>
        string.Equals(token, "-", StringComparison.Ordinal) ||
        int.TryParse(token, out _)
            ? new JsonArray()
            : new JsonObject();

    internal static string GetJsonKind(JsonNode? node) =>
        node switch
        {
            null => "Null",
            JsonObject => "Object",
            JsonArray => "Array",
            JsonValue value => GetJsonValueKind(value),
            _ => "Unknown",
        };

    private static string GetJsonValueKind(JsonValue value)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.ValueKind.ToString();
    }

    internal static string ToJson(JsonNode? node, bool indented) =>
        node?.ToJsonString(new JsonSerializerOptions { WriteIndented = indented })
        ?? "null";

    private static string NormalizeTrailingNewline(string value) =>
        value.TrimEnd('\r', '\n');

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