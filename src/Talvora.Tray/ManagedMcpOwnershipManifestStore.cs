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
            var safeId = new string(
                registration.Id
                    .Select(ch =>
                        char.IsLetterOrDigit(ch) ||
                        ch is '-' or '_'
                            ? ch
                            : '_')
                    .ToArray());
            if (!File.Exists(Path.Combine(directory, safeId + ".json")))
            {
                return true;
            }
        }

        return false;
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

        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedMcpRegistryStore.ValidateRegistration(
                registration);

            var safeId = new string(
                registration.Id
                    .Select(ch =>
                        char.IsLetterOrDigit(ch) ||
                        ch is '-' or '_'
                            ? ch
                            : '_')
                    .ToArray());
            var path = Path.Combine(
                directory,
                safeId + ".json");

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
    }
}
