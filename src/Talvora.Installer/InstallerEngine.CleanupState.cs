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

        foreach (var directory in Directory.EnumerateDirectories(versionsRoot))
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

        foreach (var file in Directory.EnumerateFiles(
                     path,
                     "*",
                     SearchOption.AllDirectories)
                 .OrderByDescending(item => item.Length))
        {
            _ = MoveFileEx(
                file,
                null,
                MoveFileFlags.DelayUntilReboot);
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     path,
                     "*",
                     SearchOption.AllDirectories)
                 .OrderByDescending(item => item.Length))
        {
            _ = MoveFileEx(
                directory,
                null,
                MoveFileFlags.DelayUntilReboot);
        }

        if (!MoveFileEx(path, null, MoveFileFlags.DelayUntilReboot))
        {
            InstallerLog.Write(
                $"Unable to schedule legacy directory deletion. Path={path} Win32={Marshal.GetLastWin32Error()}");
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
                Directory.Delete(path, recursive: true);
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

        Directory.Delete(path, recursive: true);
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
            ? File.ReadAllText(path)
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
