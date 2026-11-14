using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Talvora.Shared;

namespace Talvora.Tray;

/// <summary>RL5: read-only, once-a-day release discovery; no downloads or installation.</summary>
internal sealed class ReleaseCheckService : INotifyPropertyChanged, IDisposable
{
    internal sealed record AvailableRelease(
        string Version, string HtmlUrl, DateTimeOffset CheckedAtUtc);

    private const string LatestReleaseUrl =
        "https://api.github.com/repos/bingoweb/Talvora-MCP/releases/latest";
    private const string TrustedReleasePrefix =
        "https://github.com/bingoweb/Talvora-MCP/releases/";
    internal static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions CacheOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static readonly Lazy<ReleaseCheckService> Default = new(() =>
    {
        // Explicitly prevent GitHub redirect targets outside this approved endpoint.
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var info = typeof(ReleaseCheckService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora", "ControlCenter", "release-check.json");
        var service = new ReleaseCheckService(client, info ?? "", path, ownsClient: true);
        service.Start();
        return service;
    });

    // Module initialization runs when the tray process starts, not when the user opens Settings.
    [ModuleInitializer]
    internal static void BeginReleaseCheckingAtTrayStartup()
    {
        if (Assembly.GetEntryAssembly()?.GetName().Name != "Talvora.Tray")
            return;
        _ = Default.Value;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Default.IsValueCreated) Default.Value.Dispose();
        };
    }

    internal static ReleaseCheckService Instance => Default.Value;

    private readonly HttpClient _client;
    private readonly string _cachePath;
    private readonly Version? _installedVersion;
    private readonly bool _development;
    private readonly bool _ownsClient;
    private readonly CancellationTokenSource _stop = new();
    private int _started;
    private int _disposed;
    private AvailableRelease? _current;

    internal ReleaseCheckService(
        HttpClient client, string buildVersion, string cachePath, bool ownsClient = false)
    {
        _client = client;
        _cachePath = cachePath;
        _ownsClient = ownsClient;
        _development = buildVersion.StartsWith("0.0.0.0-dev", StringComparison.Ordinal);
        var identity = buildVersion.Split('+')[0];
        _installedVersion = Version.TryParse(identity, out var parsed) ? parsed : null;
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
            "Talvora", _installedVersion?.ToString() ?? "0.0.0.0"));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (_development || _installedVersion is null) return;
        try
        {
            if (!File.Exists(_cachePath)) return;
            var saved = JsonFileStore.ReadBounded<AvailableRelease>(
                _cachePath, 4096, CacheOptions);
            if (ValidNewer(saved.Version, saved.HtmlUrl))
                _current = saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or JsonException or ArgumentException)
        {
            // Corrupted caches are ignored; no tray error or toast.
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal AvailableRelease? Current => _current;

    private static bool TryReadTag(string tag, out Version? version)
    {
        version = null;
        if (!Regex.IsMatch(tag, @"^v0\.0\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant))
            return false;
        return Version.TryParse(tag.AsSpan(1), out version);
    }

    internal static bool IsApprovedReleaseUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 443 && uri.UserInfo.Length == 0 &&
        uri.AbsoluteUri.StartsWith(TrustedReleasePrefix, StringComparison.Ordinal);

    private bool ValidNewer(string? tagOrVersion, string? url)
    {
        if (_installedVersion is null || string.IsNullOrWhiteSpace(tagOrVersion) ||
            !IsApprovedReleaseUrl(url)) return false;
        var tag = tagOrVersion.StartsWith('v') ? tagOrVersion : "v" + tagOrVersion;
        return TryReadTag(tag, out var incoming) && incoming > _installedVersion;
    }

    private void SetCurrent(AvailableRelease? updated)
    {
        void Publish()
        {
            _current = updated;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        }
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted &&
            !dispatcher.CheckAccess())
            _ = dispatcher.BeginInvoke(Publish);
        else
            Publish();
    }

    internal async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        if (_development || _installedVersion is null) return;
        try
        {
            using var response = await _client.GetAsync(
                LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[262_145];
            var length = 0;
            while (length < buffer.Length)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (count == 0) break;
                length += count;
            }
            if (length > 262_144) return;
            using var json = JsonDocument.Parse(buffer.AsMemory(0, length));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tag_name", out var tag) ||
                !root.TryGetProperty("html_url", out var htmlUrl) ||
                tag.ValueKind != JsonValueKind.String ||
                htmlUrl.ValueKind != JsonValueKind.String) return;
            var latestTag = tag.GetString();
            var link = htmlUrl.GetString();
            AvailableRelease? updated = null;
            if (ValidNewer(latestTag, link))
                updated = new AvailableRelease(latestTag![1..], link!, DateTimeOffset.UtcNow);
            SetCurrent(updated);
            if (updated is not null)
                await JsonFileStore.WriteAsync(
                    _cachePath, updated, CacheOptions, createBackup: false, cancellationToken);
            else if (File.Exists(_cachePath))
                File.Delete(_cachePath);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or
                                   IOException or UnauthorizedAccessException or JsonException or
                                   InvalidDataException or ArgumentException)
        {
            // Errors are intentionally silent until the next scheduled 24-hour check.
        }
    }

    private void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        if (_development)
        {
            TrayLog.Write("Sürüm denetimi geliştirme sürümünde kapalı.");
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(InitialDelay, _stop.Token);
                while (!_stop.IsCancellationRequested)
                {
                    await CheckOnceAsync(_stop.Token);
                    await Task.Delay(RefreshInterval, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        });
    }

    internal void ForceNewVersionForSmoke(string version)
    {
        if (TryReadTag("v" + version, out _))
            SetCurrent(new AvailableRelease(version,
                TrustedReleasePrefix + "tag/v" + version, DateTimeOffset.UtcNow));
        else
            SetCurrent(null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _stop.Dispose();
        if (_ownsClient) _client.Dispose();
    }
}
