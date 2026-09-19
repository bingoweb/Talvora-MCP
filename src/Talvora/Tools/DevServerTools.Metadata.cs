using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public static partial class DevServerTools
{
private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string GetMetadataRoot() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "Talvora",
            "DevServers");

    private static string GetMetadataPath(
        string jobId)
    {
        if (!Guid.TryParseExact(
                jobId,
                "N",
                out _))
        {
            throw new ArgumentException(
                "Invalid Talvora job ID.",
                nameof(jobId));
        }

        return Path.Combine(
            GetMetadataRoot(),
            jobId + ".json");
    }

    private static Task WriteMetadataAsync(
        TalvoraDevServerMetadata metadata,
        CancellationToken cancellationToken) =>
        JsonFileStore.WriteAsync(
            GetMetadataPath(metadata.JobId),
            metadata,
            JsonOptions,
            cancellationToken: cancellationToken);

    private static async Task<TalvoraDevServerMetadata>
        ReadMetadataAsync(
            string jobId,
            CancellationToken cancellationToken)
    {
        var path =
            GetMetadataPath(jobId);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Talvora dev-server definition was not found.",
                path);
        }

        return await ReadMetadataFileAsync(
            path,
            cancellationToken);
    }

    private static Task<TalvoraDevServerMetadata>
        ReadMetadataFileAsync(
            string path,
            CancellationToken cancellationToken) =>
        JsonFileStore.ReadAsync<TalvoraDevServerMetadata>(
            path,
            JsonOptions,
            cancellationToken);

    private static void TryDeleteMetadata(
        string jobId)
    {
        try
        {
            var path = GetMetadataPath(jobId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
