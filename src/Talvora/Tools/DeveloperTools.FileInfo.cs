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
}
