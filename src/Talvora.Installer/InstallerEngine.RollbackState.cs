using Microsoft.Win32;

namespace Talvora.Installer;

internal static partial class InstallerEngine
{
    private sealed record InstallerFileRollbackSnapshot(
        string Path,
        bool Existed,
        byte[]? Content,
        FileAttributes? Attributes);

    private sealed record InstallerTrayStartupRollbackSnapshot(
        bool Existed,
        string? Value,
        RegistryValueKind? ValueKind);

    private sealed record InstallerUserStateSnapshot(
        InstallerFileRollbackSnapshot CurrentState,
        InstallerFileRollbackSnapshot CodexConfiguration,
        InstallerTrayStartupRollbackSnapshot TrayStartup);

    private static async Task<InstallerUserStateSnapshot>
        CaptureInstallerUserStateAsync(
            InstallUserContext installUser,
            CancellationToken cancellationToken)
    {
        var currentStatePath = Path.Combine(
            installUser.LocalAppData,
            "Talvora",
            "current.json");
        var codexConfigurationPath = Path.Combine(
            installUser.UserProfile,
            ".codex",
            "config.toml");

        return new InstallerUserStateSnapshot(
            await CaptureInstallerFileRollbackSnapshotAsync(
                currentStatePath,
                cancellationToken),
            await CaptureInstallerFileRollbackSnapshotAsync(
                codexConfigurationPath,
                cancellationToken),
            CaptureTrayStartupRollbackSnapshot(installUser));
    }

    private static async Task<InstallerFileRollbackSnapshot>
        CaptureInstallerFileRollbackSnapshotAsync(
            string path,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new InstallerFileRollbackSnapshot(
                path,
                Existed: false,
                Content: null,
                Attributes: null);
        }

        var content = await File.ReadAllBytesAsync(
            path,
            cancellationToken);
        var attributes = File.GetAttributes(path);

        return new InstallerFileRollbackSnapshot(
            path,
            Existed: true,
            content,
            attributes);
    }

    private static InstallerTrayStartupRollbackSnapshot
        CaptureTrayStartupRollbackSnapshot(
            InstallUserContext installUser)
    {
        using var userRoot = OpenInstallUserRoot(
            installUser,
            writable: false);
        using var runKey = userRoot.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: false);

        if (runKey is null ||
            !runKey.GetValueNames().Contains(
                RunValueName,
                StringComparer.OrdinalIgnoreCase))
        {
            return new InstallerTrayStartupRollbackSnapshot(
                Existed: false,
                Value: null,
                ValueKind: null);
        }

        var value = runKey.GetValue(
            RunValueName,
            null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) as string
            ?? throw new InvalidOperationException(
                $"Tray startup value '{RunValueName}' is not a string.");
        var valueKind = runKey.GetValueKind(RunValueName);

        return new InstallerTrayStartupRollbackSnapshot(
            Existed: true,
            value,
            valueKind);
    }

    private static async Task RestoreInstallerUserStateAsync(
        InstallerUserStateSnapshot snapshot,
        InstallUserContext installUser,
        CancellationToken cancellationToken)
    {
        await RestoreInstallerFileRollbackSnapshotAsync(
            snapshot.CurrentState,
            cancellationToken);
        await RestoreInstallerFileRollbackSnapshotAsync(
            snapshot.CodexConfiguration,
            cancellationToken);
        RestoreTrayStartupRollbackSnapshot(
            snapshot.TrayStartup,
            installUser);
    }

    private static async Task RestoreInstallerFileRollbackSnapshotAsync(
        InstallerFileRollbackSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!snapshot.Existed)
        {
            DeleteRollbackCreatedFile(snapshot.Path);
            return;
        }

        var content = snapshot.Content
            ?? throw new InvalidOperationException(
                $"Rollback snapshot content is missing: {snapshot.Path}");
        var directory = Path.GetDirectoryName(snapshot.Path)
            ?? throw new InvalidOperationException(
                $"Rollback file directory could not be resolved: {snapshot.Path}");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            "." + Path.GetFileName(snapshot.Path) + "." +
            Guid.NewGuid().ToString("N") + ".rollback.tmp");

        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             FileOptions.WriteThrough))
            {
                await stream.WriteAsync(
                    content,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            MakeRollbackTargetWritable(snapshot.Path);
            File.Move(
                tempPath,
                snapshot.Path,
                overwrite: true);

            if (snapshot.Attributes is { } attributes)
            {
                File.SetAttributes(
                    snapshot.Path,
                    attributes);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException)
            {
                InstallerLog.Write(
                    $"Rollback temp cleanup deferred: {tempPath}",
                    ex);
            }
        }
    }

    private static void DeleteRollbackCreatedFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        MakeRollbackTargetWritable(path);
        File.Delete(path);
    }

    private static void MakeRollbackTargetWritable(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(
                path,
                attributes & ~FileAttributes.ReadOnly);
        }
    }

    private static void RestoreTrayStartupRollbackSnapshot(
        InstallerTrayStartupRollbackSnapshot snapshot,
        InstallUserContext installUser)
    {
        using var userRoot = OpenInstallUserRoot(
            installUser,
            writable: true);

        if (!snapshot.Existed)
        {
            using var existingRunKey = userRoot.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);
            existingRunKey?.DeleteValue(
                RunValueName,
                throwOnMissingValue: false);
            return;
        }

        using var runKey = userRoot.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true);
        runKey.SetValue(
            RunValueName,
            snapshot.Value
                ?? throw new InvalidOperationException(
                    "Rollback tray startup value is missing."),
            snapshot.ValueKind
                ?? throw new InvalidOperationException(
                    "Rollback tray startup value kind is missing."));
    }
}
