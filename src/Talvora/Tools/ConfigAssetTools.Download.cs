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
[McpServerTool(
        Name = "talvora_http_download",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraHttpDownloadResponse)),
     Description("Download an HTTP/HTTPS response body directly to a file. Development-workspace source/text destinations are routed to talvora_apply_patch by default; explicitAdmin=true deliberately preserves unrestricted administrative download-to-file capability. Supports arbitrary request headers, redirects, optional TLS certificate handling override, overwrite, and byte-range resume.")]
    public static async Task<TalvoraHttpDownloadResponse> HttpDownload(
        string url,
        string destinationPath,
        Dictionary<string, string>? headers = null,
        bool overwrite = false,
        bool resume = false,
        int timeoutSeconds = 300,
        bool allowAutoRedirect = true,
        bool ignoreTlsErrors = false,
        bool explicitAdmin = false,
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
        SourceMutationPolicy.EnsureGenericDestinationMutationAllowed(
            destination,
            "talvora_http_download",
            explicitAdmin);
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

        using var client = TalvoraHttp.CreateClient(
            ignoreTlsErrors,
            allowAutoRedirect);
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

        var stageDirectory =
            parent ??
            Directory.GetCurrentDirectory();
        var stage =
            Path.Combine(
                stageDirectory,
                "." +
                Path.GetFileName(destination) +
                ".talvora-download-" +
                Guid.NewGuid().ToString("N") +
                ".tmp");
        long bytesWritten;
        long finalLength;
        string hash;
        try
        {
            if (actualResume)
            {
                File.Copy(
                    destination,
                    stage,
                    overwrite: false);
            }

            await using (var output = new FileStream(
                stage,
                actualResume
                    ? FileMode.Append
                    : FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                options:
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan))
            await using (var input =
                         await response.Content.ReadAsStreamAsync(
                             timeout.Token))
            {
                var beforeWrite =
                    output.Position;
                await input.CopyToAsync(
                    output,
                    128 * 1024,
                    timeout.Token);
                bytesWritten =
                    output.Position -
                    beforeWrite;
                if (response.Content.Headers.ContentLength is long expectedLength &&
                    bytesWritten != expectedLength)
                {
                    throw new IOException(
                        $"HTTP response body length did not match Content-Length. Expected={expectedLength} Actual={bytesWritten}.");
                }

                await output.FlushAsync(
                    timeout.Token);
                output.Flush(
                    flushToDisk: true);
            }

            finalLength =
                new FileInfo(stage).Length;
            await using (var hashStream = new FileStream(
                stage,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                options:
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan))
            {
                hash = Convert.ToHexString(
                    await System.Security.Cryptography.SHA256.HashDataAsync(
                        hashStream,
                        cancellationToken));
            }

            if (File.Exists(destination))
            {
                File.Replace(
                    stage,
                    destination,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(
                    stage,
                    destination);
            }
        }
        finally
        {
            if (File.Exists(stage))
            {
                try
                {
                    File.Delete(stage);
                }
                catch (Exception ex) when (
                    ex is IOException or
                        UnauthorizedAccessException)
                {
                }
            }
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
}
