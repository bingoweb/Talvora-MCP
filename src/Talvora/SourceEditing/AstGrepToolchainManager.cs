using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.SourceEditing;

internal sealed record AstGrepToolchain(
    string ExecutablePath,
    string Version,
    string PackageName,
    string Integrity,
    string? Warning);

internal static class AstGrepToolchainManager
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly TimeSpan MetadataTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(3);
    private const long MaxTarballBytes = 256L * 1024L * 1024L;
    private const int RetainedVersions = 3;

    private static AstGrepRegistryMetadata? cachedMetadata;
    private static DateTimeOffset cachedMetadataAtUtc;

    public static async Task<AstGrepToolchain> ResolveAsync(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Unavailable(
                "The ast-grep structural adapter currently requires a Windows Talvora runtime.");
        }

        var vendored =
            await TryResolveVendoredAsync(
                cancellationToken);
        if (vendored is not null)
        {
            return vendored;
        }

        var packageName = ResolvePackageName();
        var architectureKey = ResolveArchitectureKey();
        var root = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "Talvora",
            "Toolchains",
            "ast-grep",
            architectureKey);
        Directory.CreateDirectory(root);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            AstGrepRegistryMetadata? latest = null;
            Exception? metadataFailure = null;
            try
            {
                latest =
                    await GetLatestMetadataAsync(
                        packageName,
                        cancellationToken);
            }
            catch (Exception ex) when (
                ex is HttpRequestException or
                    IOException or
                    JsonException or
                    TaskCanceledException or
                    SourceEditDomainException)
            {
                metadataFailure = ex;
            }

            if (latest is not null)
            {
                var installed =
                    await TryResolveInstalledAsync(
                        root,
                        latest.Version,
                        packageName,
                        latest.Integrity,
                        cancellationToken);
                if (installed is not null)
                {
                    return installed;
                }

                return await InstallAsync(
                    root,
                    latest,
                    cancellationToken);
            }

            var fallback =
                await TryResolveNewestCachedAsync(
                    root,
                    packageName,
                    cancellationToken);
            if (fallback is not null)
            {
                return fallback with
                {
                    Warning =
                        "Latest ast-grep metadata could not be refreshed; using the newest previously verified private Talvora cache. " +
                        (metadataFailure?.Message ?? "Registry metadata was unavailable."),
                };
            }

            throw Unavailable(
                "No verified ast-grep cache is available and the latest official npm registry metadata could not be retrieved.",
                metadataFailure);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<AstGrepRegistryMetadata> GetLatestMetadataAsync(
        string packageName,
        CancellationToken cancellationToken)
    {
        if (cachedMetadata is not null &&
            string.Equals(
                cachedMetadata.PackageName,
                packageName,
                StringComparison.Ordinal) &&
            DateTimeOffset.UtcNow - cachedMetadataAtUtc < MetadataTtl)
        {
            return cachedMetadata;
        }

        var encoded =
            Uri.EscapeDataString(packageName);
        var uri =
            new Uri(
                $"https://registry.npmjs.org/{encoded}/latest");

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(MetadataTimeout);

        using var response =
            await Http.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        response.EnsureSuccessStatusCode();

        await using var body =
            await response.Content.ReadAsStreamAsync(
                timeout.Token);
        using var json =
            await JsonDocument.ParseAsync(
                body,
                cancellationToken: timeout.Token);
        var root = json.RootElement;
        var version =
            RequireString(root, "version");
        var license =
            root.TryGetProperty("license", out var licenseNode) &&
            licenseNode.ValueKind == JsonValueKind.String
                ? licenseNode.GetString()
                : null;

        if (!string.Equals(
                license,
                "MIT",
                StringComparison.OrdinalIgnoreCase))
        {
            throw Unavailable(
                $"Unexpected ast-grep package license '{license ?? "<missing>"}'.");
        }

        if (!root.TryGetProperty("dist", out var dist) ||
            dist.ValueKind != JsonValueKind.Object)
        {
            throw Unavailable(
                "ast-grep registry metadata is missing the dist object.");
        }

        var integrity =
            RequireString(dist, "integrity");
        var tarballText =
            RequireString(dist, "tarball");
        if (!integrity.StartsWith(
                "sha512-",
                StringComparison.Ordinal))
        {
            throw Unavailable(
                "ast-grep registry metadata does not provide SHA-512 SRI integrity.");
        }

        if (!Uri.TryCreate(
                tarballText,
                UriKind.Absolute,
                out var tarball) ||
            !string.Equals(
                tarball.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                tarball.Host,
                "registry.npmjs.org",
                StringComparison.OrdinalIgnoreCase))
        {
            throw Unavailable(
                "ast-grep tarball metadata does not point to the official HTTPS npm registry.");
        }

        var metadata =
            new AstGrepRegistryMetadata(
                packageName,
                version,
                integrity,
                tarball);
        cachedMetadata = metadata;
        cachedMetadataAtUtc =
            DateTimeOffset.UtcNow;
        return metadata;
    }

    private static async Task<AstGrepToolchain> InstallAsync(
        string root,
        AstGrepRegistryMetadata metadata,
        CancellationToken cancellationToken)
    {
        var finalDirectory =
            Path.Combine(
                root,
                metadata.Version);
        var finalExecutable =
            Path.Combine(
                finalDirectory,
                "ast-grep.exe");

        if (Directory.Exists(finalDirectory))
        {
            SafeDeleteDirectory(finalDirectory);
        }

        var staging =
            Path.Combine(
                root,
                ".staging-" +
                Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var archive =
            Path.Combine(
                staging,
                "package.tgz");
        var executable =
            Path.Combine(
                staging,
                "ast-grep.exe");

        try
        {
            await DownloadAndVerifyAsync(
                metadata,
                archive,
                cancellationToken);
            await ExtractExecutableAsync(
                archive,
                executable,
                cancellationToken);
            File.Delete(archive);

            await VerifyVersionAsync(
                executable,
                metadata.Version,
                cancellationToken);

            var metadataPath =
                Path.Combine(
                    staging,
                    "toolchain.json");
            await File.WriteAllTextAsync(
                metadataPath,
                JsonSerializer.Serialize(
                    new
                    {
                        packageName = metadata.PackageName,
                        version = metadata.Version,
                        integrity = metadata.Integrity,
                        executableSha256 = await ComputeFileSha256Async(executable, cancellationToken),
                        installedAtUtc = DateTimeOffset.UtcNow,
                    }),
                cancellationToken);

            Directory.Move(
                staging,
                finalDirectory);
            await VerifyVersionAsync(
                finalExecutable,
                metadata.Version,
                cancellationToken);
            PruneOldVersions(
                root,
                metadata.Version);

            return new AstGrepToolchain(
                finalExecutable,
                metadata.Version,
                metadata.PackageName,
                metadata.Integrity,
                null);
        }
        catch
        {
            SafeDeleteDirectory(staging);
            throw;
        }
    }

    private static async Task DownloadAndVerifyAsync(
        AstGrepRegistryMetadata metadata,
        string destination,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(DownloadTimeout);

        using var response =
            await Http.GetAsync(
                metadata.Tarball,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long length &&
            length > MaxTarballBytes)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.ResourceLimit,
                $"ast-grep package tarball is unexpectedly large ({length} bytes).");
        }

        var expected =
            Convert.FromBase64String(
                metadata.Integrity["sha512-".Length..]);
        using var hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA512);
        await using var input =
            await response.Content.ReadAsStreamAsync(
                timeout.Token);
        await using var output =
            new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);
        var buffer =
            new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read =
                await input.ReadAsync(
                    buffer,
                    timeout.Token);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaxTarballBytes)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.ResourceLimit,
                    "ast-grep package tarball exceeded the download size limit.");
            }

            hash.AppendData(
                buffer,
                0,
                read);
            await output.WriteAsync(
                buffer.AsMemory(0, read),
                timeout.Token);
        }

        await output.FlushAsync(
            timeout.Token);
        output.Flush(flushToDisk: true);
        var actual =
            hash.GetHashAndReset();
        if (!CryptographicOperations.FixedTimeEquals(
                expected,
                actual))
        {
            throw Unavailable(
                "ast-grep npm package SHA-512 integrity verification failed.");
        }
    }

    private static async Task ExtractExecutableAsync(
        string archive,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var file =
            new FileStream(
                archive,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);
        await using var gzip =
            new GZipStream(
                file,
                CompressionMode.Decompress,
                leaveOpen: false);
        await using var tar =
            new TarReader(
                gzip,
                leaveOpen: false);

        while (true)
        {
            var entry =
                await tar.GetNextEntryAsync(
                    copyData: false,
                    cancellationToken);
            if (entry is null)
            {
                break;
            }

            var normalized =
                entry.Name.Replace(
                    '\\',
                    '/');
            if (!string.Equals(
                    normalized,
                    "package/ast-grep.exe",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (entry.DataStream is null)
            {
                throw Unavailable(
                    "ast-grep package executable entry has no data stream.");
            }

            await using var output =
                new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan);
            await entry.DataStream.CopyToAsync(
                output,
                cancellationToken);
            await output.FlushAsync(
                cancellationToken);
            output.Flush(flushToDisk: true);
            return;
        }

        throw Unavailable(
            "ast-grep package did not contain package/ast-grep.exe.");
    }

    private static async Task<AstGrepToolchain?> TryResolveVendoredAsync(
        CancellationToken cancellationToken)
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "tools",
                "ast-grep");
        var executable =
            Path.Combine(
                directory,
                "ast-grep.exe");
        var provenance =
            Path.Combine(
                directory,
                "provenance.json");

        var executableExists =
            File.Exists(executable);
        var provenanceExists =
            File.Exists(provenance);
        if (!executableExists &&
            !provenanceExists)
        {
            return null;
        }

        if (!executableExists ||
            !provenanceExists)
        {
            throw Unavailable(
                "The installed ast-grep payload is incomplete; ast-grep.exe and provenance.json must both be present.");
        }

        try
        {
            await using var stream =
                File.OpenRead(provenance);
            using var json =
                await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
            var root =
                json.RootElement;
            var packageName =
                RequireString(
                    root,
                    "packageName");
            var version =
                RequireString(
                    root,
                    "version");
            var integrity =
                RequireString(
                    root,
                    "integrity");
            var expectedSha =
                RequireString(
                    root,
                    "executableSha256");
            var actualSha =
                await ComputeFileSha256Async(
                    executable,
                    cancellationToken);
            if (!string.Equals(
                    expectedSha,
                    actualSha,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw Unavailable(
                    "The installed ast-grep executable SHA-256 does not match installer provenance.");
            }

            await VerifyVersionAsync(
                executable,
                version,
                cancellationToken);
            return new AstGrepToolchain(
                executable,
                version,
                packageName,
                integrity,
                null);
        }
        catch (SourceEditDomainException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                JsonException)
        {
            throw Unavailable(
                "The installed ast-grep provenance could not be verified.",
                ex);
        }
    }

    private static async Task<string> ComputeFileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);
        using var hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
        var buffer =
            new byte[128 * 1024];
        while (true)
        {
            var read =
                await stream.ReadAsync(
                    buffer,
                    cancellationToken);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(
                buffer,
                0,
                read);
        }

        return Convert.ToHexString(
            hash.GetHashAndReset());
    }

    private static async Task<AstGrepToolchain?> TryResolveInstalledAsync(
        string root,
        string version,
        string packageName,
        string? integrity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executable = Path.Combine(root, version, "ast-grep.exe");
        if (!File.Exists(executable)) return null;

        try
        {
            var verifiedIntegrity = await VerifyCachedIntegrityAsync(
                executable, packageName, version, integrity, cancellationToken);
            await VerifyVersionAsync(executable, version, cancellationToken);
            return new AstGrepToolchain(executable, version, packageName, verifiedIntegrity, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            JsonException or FormatException or SourceEditDomainException)
        {
            SafeDeleteDirectory(Path.GetDirectoryName(executable)!);
            return null;
        }
    }

    private static async Task<AstGrepToolchain?> TryResolveNewestCachedAsync(
        string root,
        string packageName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(root)) return null;

        foreach (var item in Directory.EnumerateDirectories(root)
                     .Select(path => new
                     {
                         Name = Path.GetFileName(path),
                         Version = Version.TryParse(Path.GetFileName(path), out var parsed) ? parsed : null,
                     })
                     .Where(item => item.Version is not null)
                     .OrderByDescending(item => item.Version))
        {
            var cached = await TryResolveInstalledAsync(
                root, item.Name, packageName, integrity: null, cancellationToken);
            if (cached is not null) return cached;
        }

        return null;
    }

    private static async Task VerifyVersionAsync(
        string executable,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        var result =
            await ProcessRunner.RunAsync(
                executable,
                Path.GetDirectoryName(executable),
                ["--version"],
                timeoutSeconds: 30,
                cancellationToken: cancellationToken);
        var expected =
            "ast-grep " +
            expectedVersion;
        if (result.TimedOut ||
            result.ExitCode != 0 ||
            !string.Equals(
                result.StandardOutput.Trim(),
                expected,
                StringComparison.Ordinal))
        {
            throw Unavailable(
                $"ast-grep executable verification failed. Expected '{expected}', got exit={result.ExitCode}, stdout='{result.StandardOutput.Trim()}', stderr='{result.StandardError.Trim()}'.");
        }
    }

    private static async Task<string> VerifyCachedIntegrityAsync(
        string executable,
        string packageName,
        string version,
        string? expectedIntegrity,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetDirectoryName(executable)!, "toolchain.json");
        await using var stream = File.OpenRead(path);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var metadata = json.RootElement;
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            throw Unavailable("Cached ast-grep provenance must be a JSON object.");
        }
        var integrity = RequireString(metadata, "integrity");
        if (!string.Equals(RequireString(metadata, "packageName"), packageName, StringComparison.Ordinal) ||
            !string.Equals(RequireString(metadata, "version"), version, StringComparison.Ordinal) ||
            !integrity.StartsWith("sha512-", StringComparison.Ordinal) ||
            Convert.FromBase64String(integrity["sha512-".Length..]).Length != 64 ||
            (expectedIntegrity is not null &&
             !string.Equals(integrity, expectedIntegrity, StringComparison.Ordinal)))
        {
            throw Unavailable("Cached ast-grep package identity or integrity does not match its provenance.");
        }

        var expectedSha = Convert.FromHexString(RequireString(metadata, "executableSha256"));
        var actualSha = Convert.FromHexString(await ComputeFileSha256Async(executable, cancellationToken));
        if (expectedSha.Length != 32 || !CryptographicOperations.FixedTimeEquals(expectedSha, actualSha))
        {
            throw Unavailable("Cached ast-grep executable SHA-256 does not match its provenance.");
        }

        return integrity;
    }

    private static void PruneOldVersions(
        string root,
        string currentVersion)
    {
        try
        {
            var candidates =
                Directory
                    .EnumerateDirectories(root)
                    .Where(path =>
                        !Path.GetFileName(path)
                            .StartsWith(
                                ".staging-",
                                StringComparison.Ordinal))
                    .Select(path =>
                        new
                        {
                            Path = path,
                            Version =
                                Version.TryParse(
                                    Path.GetFileName(path),
                                    out var parsed)
                                    ? parsed
                                    : null,
                        })
                    .Where(item =>
                        item.Version is not null)
                    .OrderByDescending(item =>
                        item.Version)
                    .ToArray();

            foreach (var item in candidates
                         .Skip(RetainedVersions))
            {
                if (string.Equals(
                        Path.GetFileName(item.Path),
                        currentVersion,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                SafeDeleteDirectory(item.Path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ResolvePackageName() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 =>
                "@ast-grep/cli-win32-x64-msvc",
            Architecture.Arm64 =>
                "@ast-grep/cli-win32-arm64-msvc",
            Architecture.X86 =>
                "@ast-grep/cli-win32-ia32-msvc",
            var architecture =>
                throw Unavailable(
                    $"No official ast-grep Windows CLI package is known for process architecture '{architecture}'."),
        };

    private static string ResolveArchitectureKey() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            Architecture.X86 => "win-x86",
            var architecture =>
                architecture.ToString().ToLowerInvariant(),
        };

    private static string RequireString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var value) ||
            value.ValueKind !=
            JsonValueKind.String ||
            string.IsNullOrWhiteSpace(
                value.GetString()))
        {
            throw Unavailable(
                $"ast-grep registry metadata is missing '{propertyName}'.");
        }

        return value.GetString()!;
    }

    private static void SafeDeleteDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client =
            new HttpClient
            {
                Timeout =
                    Timeout.InfiniteTimeSpan,
            };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Talvora/3.0 ast-grep-toolchain");
        return client;
    }

    private static SourceEditDomainException Unavailable(
        string message,
        Exception? innerException = null) =>
        new(
            SourceEditCodes.StructuralToolchainUnavailable,
            $"{SourceEditCodes.StructuralToolchainUnavailable}: {message}",
            innerException: innerException);

    private sealed record AstGrepRegistryMetadata(
        string PackageName,
        string Version,
        string Integrity,
        Uri Tarball);
}
