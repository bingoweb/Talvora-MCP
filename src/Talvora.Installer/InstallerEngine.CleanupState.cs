using Microsoft.Win32;
using Talvora.Shared;
using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Installer;

internal static partial class InstallerEngine
{
    private const int MaximumScheduledCleanupEntries =
        100_000;
    private const int MaximumImmediateCleanupEntries =
        100_000;
    private const int MaximumInstalledVersionDirectories =
        10_000;

private static async Task CleanupObsoleteInstallationsAsync(
        string installRoot,
        string activeVersionRoot,
        CancellationToken cancellationToken)
    {
        var legacyProgramDataService = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora",
            "Service");
        await DeleteDirectoryBestEffortAsync(
            legacyProgramDataService,
            cancellationToken);

        await DeleteDirectoryBestEffortAsync(
            Path.Combine(installRoot, "Service"),
            cancellationToken);
        await DeleteDirectoryBestEffortAsync(
            Path.Combine(installRoot, "Tray"),
            cancellationToken);
        await DeleteDirectoryBestEffortAsync(
            Path.Combine(installRoot, "Cloudflare"),
            cancellationToken);

        var versionsRoot = Path.Combine(installRoot, "Versions");
        if (!Directory.Exists(versionsRoot))
        {
            return;
        }

        var versionDirectories = Directory
            .EnumerateDirectories(
                versionsRoot,
                "*",
                SearchOption.TopDirectoryOnly)
            .Take(MaximumInstalledVersionDirectories + 1)
            .ToArray();
        if (versionDirectories.Length >
            MaximumInstalledVersionDirectories)
        {
            InstallerLog.Write(
                $"Installed-version cleanup reached its bounded scan limit. Root={versionsRoot}; Limit={MaximumInstalledVersionDirectories}");
        }

        foreach (var directory in versionDirectories
                     .Take(MaximumInstalledVersionDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(
                    Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(activeVersionRoot).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                await DeleteDirectoryBestEffortAsync(
                    directory,
                    cancellationToken);
            }
        }
    }

    private static async Task DeleteDirectoryBestEffortAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await TryDeleteDirectoryAsync(path, cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            InstallerLog.Write(
                $"Old installation cleanup deferred until reboot: {path}",
                ex);
            ScheduleDirectoryDeletionOnReboot(path);
        }
    }

    private static void ScheduleDirectoryDeletionOnReboot(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var root = Path.GetFullPath(path);
        var pending = new Stack<(string Path, bool Expanded)>();
        pending.Push((root, false));
        var discoveredEntries = 0;

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current.Path);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException)
            {
                InstallerLog.Write(
                    $"Unable to inspect deferred cleanup entry. Path={current.Path}",
                    ex);
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                TryScheduleDeletionOnReboot(current.Path);
                continue;
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                TryScheduleDeletionOnReboot(current.Path);
                continue;
            }

            if (current.Expanded)
            {
                TryScheduleDeletionOnReboot(current.Path);
                continue;
            }

            pending.Push((current.Path, true));
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(
                             current.Path,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    if (++discoveredEntries >
                        MaximumScheduledCleanupEntries)
                    {
                        InstallerLog.Write(
                            $"Deferred cleanup traversal reached its safety limit. Root={root}; Limit={MaximumScheduledCleanupEntries}");
                        return;
                    }

                    pending.Push((
                        Path.GetFullPath(entry),
                        false));
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException)
            {
                InstallerLog.Write(
                    $"Unable to enumerate deferred cleanup directory. Path={current.Path}",
                    ex);
            }
        }
    }

    private static void TryScheduleDeletionOnReboot(
        string path)
    {
        if (!MoveFileEx(
                path,
                null,
                MoveFileFlags.DelayUntilReboot))
        {
            InstallerLog.Write(
                $"Unable to schedule legacy path deletion. Path={path} Win32={Marshal.GetLastWin32Error()}");
        }
    }

    private static void CopyTree(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(
                Path.GetDirectoryName(target)
                ?? throw new InvalidOperationException("Hedef dosya klasörü çözümlenemedi."));
            File.Copy(file, target, overwrite: true);
        }
    }

    private static async Task TryDeleteDirectoryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                DeleteDirectoryTreeCancellationAware(
                    path,
                    cancellationToken);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                await Task.Delay(300, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                await Task.Delay(300, cancellationToken);
            }
        }

        DeleteDirectoryTreeCancellationAware(
            path,
            cancellationToken);
    }

    private static void DeleteDirectoryTreeCancellationAware(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = new DirectoryInfo(
            Path.GetFullPath(path));
        if (!root.Exists)
        {
            return;
        }

        if ((root.Attributes &
             FileAttributes.ReparsePoint) != 0)
        {
            root.Delete();
            return;
        }

        var pending =
            new Stack<(DirectoryInfo Directory, bool Expanded)>();
        pending.Push((root, false));
        var discoveredEntries = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            if (current.Expanded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current.Directory.Delete();
                continue;
            }

            pending.Push((current.Directory, true));
            foreach (var entry in
                     current.Directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++discoveredEntries >
                    MaximumImmediateCleanupEntries)
                {
                    throw new IOException(
                        $"Immediate cleanup traversal exceeded its safety limit. Root={root.FullName}; Limit={MaximumImmediateCleanupEntries}");
                }

                if ((entry.Attributes &
                     FileAttributes.ReparsePoint) != 0)
                {
                    if (entry is DirectoryInfo reparseDirectory)
                    {
                        reparseDirectory.Delete();
                    }
                    else
                    {
                        entry.Attributes = FileAttributes.Normal;
                        entry.Delete();
                    }
                    continue;
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    pending.Push((childDirectory, false));
                }
                else
                {
                    entry.Attributes = FileAttributes.Normal;
                    entry.Delete();
                }
            }
        }
    }

    private static async Task WriteCurrentStateAsync(
        HealthSnapshot health,
        string sourceCommit,
        string installedAtUtc,
        InstallUserContext installUser,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(installUser.LocalAppData, "Talvora");
        Directory.CreateDirectory(root);

        var clientConfig = Path.Combine(
            installUser.UserProfile,
            ".codex",
            "config.toml");

        var state = new
        {
            Product = "Talvora",
            Version = health.Version ?? "3.0.0-dev",
            SourceCommit = sourceCommit,
            Service = ServiceName,
            ServiceSid = health.Sid ?? "S-1-5-18",
            health.ProcessId,
            McpUrl,
            ClientConfig = clientConfig,
            ToolCount = ToolNames.Count,
            ToolNames,
            InstalledAtUtc = installedAtUtc,
        };

        _ = await AtomicFile.WriteAllTextAsync(
            Path.Combine(root, "current.json"),
            JsonSerializer.Serialize(state, IndentedJsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            createBackup: false,
            cancellationToken);
    }

    private static async Task UpsertCodexConfigurationAsync(
        InstallUserContext installUser,
        CancellationToken cancellationToken)
    {
        var codexRoot = Path.Combine(installUser.UserProfile, ".codex");
        Directory.CreateDirectory(codexRoot);

        var path = Path.Combine(codexRoot, "config.toml");
        var original = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken)
            : string.Empty;

        const string header = "[mcp_servers.talvora_local]";
        var lines = original.Replace("\r\n", "\n").Split('\n').ToList();
        var output = new List<string>();
        var skipping = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.Equals(trimmed, header, StringComparison.Ordinal))
            {
                skipping = true;
                continue;
            }

            if (skipping &&
                trimmed.StartsWith("[", StringComparison.Ordinal) &&
                trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                skipping = false;
            }

            if (!skipping)
            {
                output.Add(line);
            }
        }

        while (output.Count > 0 && string.IsNullOrWhiteSpace(output[^1]))
        {
            output.RemoveAt(output.Count - 1);
        }

        if (output.Count > 0)
        {
            output.Add(string.Empty);
        }

        output.Add(header);
        output.Add($"url = \"{McpUrl}\"");
        output.Add(string.Empty);

        _ = await AtomicFile.WriteAllTextAsync(
            path,
            string.Join(Environment.NewLine, output),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            createBackup: true,
            cancellationToken);
    }

    private static string Collapse(params string[] values)
    {
        var value = string.Join(
            " ",
            values
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim()));

        return value.Length <= 500 ? value : value[..500];
    }

    [Flags]
    private enum MoveFileFlags : uint
    {
        DelayUntilReboot = 0x00000004,
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(
        string existingFileName,
        string? newFileName,
        MoveFileFlags flags);
}
