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
}
