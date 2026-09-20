using System.IO;
using System.Text;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal static class ManagedMcpOwnershipManifestStore
{
    private static readonly UTF8Encoding Utf8NoBom =
        new(encoderShouldEmitUTF8Identifier: false);

    public static string GetDirectory(string registryPath) =>
        Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(registryPath))
                ?? throw new InvalidOperationException(
                    "Managed MCP registry directory çözümlenemedi."),
            "managed-mcps.d");

    public static bool NeedsSeed(
        string registryPath,
        IEnumerable<ManagedMcpRegistration> registrations)
    {
        var directory = GetDirectory(registryPath);
        if (!Directory.Exists(directory))
        {
            return true;
        }

        foreach (var registration in registrations)
        {
            var storageKey =
                ManagedMcpIdentityKey.Create(
                    registration.Id);
            var path =
                Path.Combine(
                    directory,
                    storageKey + ".json");
            if (!File.Exists(path))
            {
                return true;
            }

            try
            {
                var persisted =
                    JsonSerializer.Deserialize<ManagedMcpRegistration>(
                        File.ReadAllText(
                            path,
                            Utf8NoBom),
                        ManagedMcpRegistryStore.JsonOptions);
                if (persisted is null)
                {
                    return true;
                }

                ManagedMcpRegistryStore.ValidateRegistration(
                    persisted);
                var expectedJson =
                    JsonSerializer.Serialize(
                        registration,
                        ManagedMcpRegistryStore.JsonOptions);
                var actualJson =
                    JsonSerializer.Serialize(
                        persisted,
                        ManagedMcpRegistryStore.JsonOptions);
                if (!string.Equals(
                        expectedJson,
                        actualJson,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                    InvalidDataException or
                    JsonException or
                    UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    internal static async Task AssertHealthContractAsync(
        CancellationToken cancellationToken)
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "Talvora-ManagedMcpOwnership-" +
                Guid.NewGuid().ToString("N"));
        var registryPath =
            Path.Combine(
                root,
                "managed-mcps.json");
        var registration =
            new ManagedMcpRegistration
            {
                Id = "health/probe",
                DisplayName = "Health Probe",
                Description = "Ownership manifest health contract.",
                Endpoint = "http://127.0.0.1:65531/mcp",
                AutoStart = false,
            };

        try
        {
            await PersistAsync(
                registryPath,
                [registration],
                cancellationToken).ConfigureAwait(false);
            if (NeedsSeed(
                    registryPath,
                    [registration]))
            {
                throw new InvalidOperationException(
                    "Fresh ownership manifest was not accepted as healthy.");
            }

            var path =
                Path.Combine(
                    GetDirectory(registryPath),
                    ManagedMcpIdentityKey.Create(
                        registration.Id) +
                    ".json");
            await File.WriteAllTextAsync(
                path,
                "{broken",
                cancellationToken);
            if (!NeedsSeed(
                    registryPath,
                    [registration]))
            {
                throw new InvalidOperationException(
                    "Corrupt ownership manifest was treated as healthy.");
            }

            await PersistAsync(
                registryPath,
                [registration],
                cancellationToken).ConfigureAwait(false);
            if (NeedsSeed(
                    registryPath,
                    [registration]))
            {
                throw new InvalidOperationException(
                    "Ownership manifest reseed did not restore health.");
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(
                        root,
                        recursive: true);
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                    UnauthorizedAccessException)
            {
            }
        }
    }

    public static async Task<IReadOnlyList<ManagedMcpRegistration>>
        ReadAllAsync(
            string registryPath,
            CancellationToken cancellationToken)
    {
        var directory = GetDirectory(registryPath);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var registrations =
            new List<ManagedMcpRegistration>();

        foreach (var path in Directory
                     .EnumerateFiles(
                         directory,
                         "*.json",
                         SearchOption.TopDirectoryOnly)
                     .OrderBy(
                         item => item,
                         StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var registration =
                    await JsonFileStore
                        .ReadAsync<ManagedMcpRegistration>(
                            path,
                            ManagedMcpRegistryStore.JsonOptions,
                            cancellationToken)
                        .ConfigureAwait(false);

                ManagedMcpRegistryStore.ValidateRegistration(
                    registration);
                registrations.Add(registration);
            }
            catch (Exception ex) when (
                ex is IOException or
                InvalidDataException or
                JsonException or
                UnauthorizedAccessException)
            {
                TrayLog.Write(
                    $"Managed MCP ownership manifest skipped because it is invalid. Path={path}",
                    ex);
            }
        }

        return registrations;
    }

    public static async Task PersistAsync(
        string registryPath,
        IEnumerable<ManagedMcpRegistration> registrations,
        CancellationToken cancellationToken)
    {
        var directory = GetDirectory(registryPath);
        Directory.CreateDirectory(directory);

        var expected =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedMcpRegistryStore.ValidateRegistration(
                registration);

            var storageKey =
                ManagedMcpIdentityKey.Create(
                    registration.Id);
            var path = Path.Combine(
                directory,
                storageKey + ".json");

            expected.Add(Path.GetFullPath(path));

            await AtomicFile.WriteAllTextAsync(
                    path,
                    JsonSerializer.Serialize(
                        registration,
                        ManagedMcpRegistryStore.JsonOptions),
                    Utf8NoBom,
                    createBackup: File.Exists(path),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var stale in Directory.EnumerateFiles(
                     directory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (expected.Contains(Path.GetFullPath(stale)))
            {
                continue;
            }

            try
            {
                File.Delete(stale);
                File.Delete(stale + ".bak");
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException)
            {
                TrayLog.Write(
                    $"Stale managed MCP ownership manifest could not be deleted. Path={stale}",
                    ex);
            }
        }
    }
}
