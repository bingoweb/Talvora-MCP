using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal static class ControlCenterVersionService
{
    private static readonly HttpClient Http = TalvoraHttp.CreateClient(
        timeout: TimeSpan.FromSeconds(3));
    private static readonly ConcurrentDictionary<string, DateTimeOffset> FailureLogTimes =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan FailureLogInterval =
        TimeSpan.FromMinutes(5);

    public static async Task<string> GetVersionAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                registration.Id,
                "talvora",
                StringComparison.OrdinalIgnoreCase))
        {
            return await GetTalvoraVersionAsync(cancellationToken);
        }

        if (string.Equals(
                registration.Id,
                "gitea",
                StringComparison.OrdinalIgnoreCase))
        {
            return await GetGiteaVersionAsync(cancellationToken);
        }

        return await GetPackageVersionAsync(
            registration,
            cancellationToken);
    }

    private static async Task<string> GetPackageVersionAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        var packagePath = registration.DiscoveryHints
            .FirstOrDefault(hint => string.Equals(
                hint.Kind,
                "package-file",
                StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (string.IsNullOrWhiteSpace(packagePath) ||
            !File.Exists(packagePath))
        {
            return "Bilinmiyor";
        }

        try
        {
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(packagePath, cancellationToken));

            if (document.RootElement.TryGetProperty(
                    "version",
                    out var version) &&
                version.ValueKind == JsonValueKind.String)
            {
                var value = version.GetString();
                return string.IsNullOrWhiteSpace(value)
                    ? "Bilinmiyor"
                    : value;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            UnauthorizedAccessException)
        {
            TrayLog.Write(
                $"Managed MCP package version could not be read. MCP={registration.Id}",
                ex);
        }

        return "Bilinmiyor";
    }

    private static async Task<string> GetTalvoraVersionAsync(
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "current.json");

        if (!File.Exists(path))
        {
            return "Bilinmiyor";
        }

        try
        {
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(path, cancellationToken));

            if (document.RootElement.TryGetProperty(
                    "Version",
                    out var version) &&
                version.ValueKind == JsonValueKind.String)
            {
                return version.GetString() ?? "Bilinmiyor";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            UnauthorizedAccessException)
        {
            TrayLog.Write("Talvora version metadata could not be read", ex);
        }

        return "Bilinmiyor";
    }

    private static async Task<string> GetGiteaVersionAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(
                "http://127.0.0.1:3000/api/v1/version",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return "Bilinmiyor";
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            if (document.RootElement.TryGetProperty(
                    "version",
                    out var version) &&
                version.ValueKind == JsonValueKind.String)
            {
                FailureLogTimes.TryRemove("gitea-version", out _);
                return version.GetString() ?? "Bilinmiyor";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
            JsonException or
            OperationCanceledException)
        {
            LogFailureRateLimited(
                "gitea-version",
                "Gitea version could not be read",
                ex);
        }

        return "Bilinmiyor";
    }

    private static void LogFailureRateLimited(
        string key,
        string message,
        Exception exception)
    {
        var now = DateTimeOffset.UtcNow;
        if (FailureLogTimes.TryGetValue(key, out var last) &&
            now - last < FailureLogInterval)
        {
            return;
        }

        FailureLogTimes[key] = now;
        TrayLog.Write(message, exception);
    }}
