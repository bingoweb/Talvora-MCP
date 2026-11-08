using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Talvora.Shared;

namespace Talvora.Installer;

internal static partial class InstallerEngine
{
    private const string UninstallRegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Talvora";
    private const string InstalledSetupFileName = "Talvora-Kurulum.exe";
    private const string StartMenuFileName = "Talvora Kontrol Merkezi.lnk";

    internal static IReadOnlyList<string> BuildUninstallPlan(
        string installRoot, string programDataRoot, string startMenuRoot,
        string userProfile, string userSid, bool removeData)
    {
        var actions = new List<string>
        {
            $"Windows hizmeti: {ServiceName}",
            $@"Başlangıç kaydı: HKU\{userSid}\Software\Microsoft\Windows\CurrentVersion\Run\{RunValueName}",
            "Başlat menüsü: " + Path.Combine(startMenuRoot, StartMenuFileName),
            "Codex kaydı: " + Path.Combine(userProfile, ".codex", "config.toml") +
                " [mcp_servers.talvora_local]",
            "Kurulum dosyaları: " + installRoot,
            @"Uygulamalar kaydı: HKLM\" + UninstallRegistryPath,
        };
        if (removeData && Directory.Exists(programDataRoot))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(programDataRoot, "*",
                         SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(Path.GetFileName(entry), "Secrets",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                actions.Add("Kayıtlar ve ayarlar: " + entry);
            }
        }
        return actions;
    }

    internal static void WriteUninstallRegistration(
        RegistryKey registryRoot, string installRoot, string trayExecutable,
        string informationalVersion, int estimatedSizeKb)
    {
        using var key = registryRoot.CreateSubKey(UninstallRegistryPath, writable: true)
            ?? throw new InvalidOperationException("Talvora uygulama kaydı oluşturulamadı.");
        var setupExe = Path.Combine(installRoot, InstalledSetupFileName);
        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        var displayVersion = plus >= 0 ? informationalVersion[..plus] : informationalVersion;
        key.SetValue("DisplayName", "Talvora", RegistryValueKind.String);
        key.SetValue("DisplayVersion", displayVersion, RegistryValueKind.String);
        key.SetValue("Publisher", "Taylan Soylu", RegistryValueKind.String);
        key.SetValue("DisplayIcon", trayExecutable + ",0", RegistryValueKind.String);
        key.SetValue("InstallLocation", installRoot, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{setupExe}\" --uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{setupExe}\" --uninstall --silent",
            RegistryValueKind.String);
        key.SetValue("URLInfoAbout", "https://github.com/bingoweb/Talvora-MCP",
            RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", Math.Max(1, estimatedSizeKb), RegistryValueKind.DWord);
    }

    private static void RegisterTalvoraUninstall(string installRoot, string trayExecutable)
    {
        var runningSetup = Environment.ProcessPath
            ?? throw new InvalidOperationException("Talvora kurulum dosyası bulunamadı.");
        var installedSetup = Path.Combine(installRoot, InstalledSetupFileName);
        if (!string.Equals(Path.GetFullPath(runningSetup), Path.GetFullPath(installedSetup),
                StringComparison.OrdinalIgnoreCase))
            File.Copy(runningSetup, installedSetup, overwrite: true);

        var infoVersion = typeof(InstallerEngine).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0.0-dev";
        WriteUninstallRegistration(Registry.LocalMachine, installRoot, trayExecutable,
            infoVersion, EstimateInstalledSizeKb(installRoot));
    }

    private static int EstimateInstalledSizeKb(string root)
    {
        var directories = new Stack<string>();
        directories.Push(root);
        long total = 0;
        var count = 0;
        while (directories.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directories.Pop(), "*",
                         SearchOption.TopDirectoryOnly))
            {
                if (++count > 100_000)
                    return int.MaxValue;
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if ((attributes & FileAttributes.Directory) != 0)
                    directories.Push(entry);
                else
                    total = Math.Min((long)int.MaxValue * 1024,
                        total + new FileInfo(entry).Length);
            }
        }
        return (int)Math.Clamp((total + 1023) / 1024, 1, int.MaxValue);
    }

    internal static IReadOnlyList<string> GetCurrentUninstallPlan(bool removeData)
    {
        var installUser = ResolveInstallUserContext();
        var installRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Talvora");
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Talvora");
        return BuildUninstallPlan(installRoot, dataRoot,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            installUser.UserProfile, installUser.Sid, removeData);
    }

    internal static async Task UninstallAsync(
        IProgress<InstallProgress> progress, bool removeData,
        CancellationToken cancellationToken)
    {
        var installUser = ResolveInstallUserContext();
        var installRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Talvora");
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Talvora");

        try
        {
            progress.Report(new InstallProgress(10, "Talvora hizmeti durduruluyor..."));
            await StopTrayProcessesAsync(cancellationToken);
            await StopAndDeleteServiceAsync(ServiceName, cancellationToken);

            using (var userRoot = OpenInstallUserRoot(installUser, writable: true))
            using (var runKey = userRoot.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
            {
                runKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
            }

            var shortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                StartMenuFileName);
            if (File.Exists(shortcut))
                File.Delete(shortcut);

            await RemoveTalvoraCodexTableAsync(installUser, cancellationToken);

            progress.Report(new InstallProgress(60, "Dosyalar siliniyor..."));
            await RemoveTalvoraProgramFilesAsync(installRoot, cancellationToken);

            Registry.LocalMachine.DeleteSubKeyTree(UninstallRegistryPath,
                throwOnMissingSubKey: false);

            // When data is removed, writing a success log would recreate the deleted folder.
            if (!removeData)
                InstallerLog.Write("Uninstall succeeded.");

            if (removeData && Directory.Exists(dataRoot))
            {
                if ((File.GetAttributes(dataRoot) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Talvora veri klasörü bağlantı içeriyor; güvenli kaldırma durduruldu.");
                foreach (var entry in Directory.EnumerateFileSystemEntries(dataRoot, "*",
                             SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.Equals(Path.GetFileName(entry), "Secrets",
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                        await TryDeleteDirectoryAsync(entry, cancellationToken);
                    else
                        File.Delete(entry);
                }
            }

            progress.Report(new InstallProgress(100, "Talvora kaldırıldı."));
        }
        catch (Exception error)
        {
            InstallerLog.Write("Uninstall failed", error);
            throw new InvalidOperationException(
                "Talvora kaldırılamadı. Ayrıntılar kurulum günlüğünde kaydedildi.", error);
        }
    }

    private static async Task RemoveTalvoraCodexTableAsync(
        InstallUserContext installUser, CancellationToken cancellationToken)
    {
        var path = Path.Combine(installUser.UserProfile, ".codex", "config.toml");
        if (!File.Exists(path))
            return;

        var original = await TextFileStore.ReadBoundedAsync(path,
            MaximumInstallerUserStateFileBytes, cancellationToken);
        var updated = RemoveTalvoraCodexTable(original);
        if (string.Equals(original, updated, StringComparison.Ordinal))
            return;
        _ = await AtomicFile.WriteAllTextAsync(path, updated,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            createBackup: true, cancellationToken);
    }

    internal static string RemoveTalvoraCodexTable(string original)
    {
        var headerPattern = new Regex(
            @"^[ \t]*\[\[?[^\r\n\[\]]+\]\]?[ \t]*(?:\#[^\r\n]*)?\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        var header = headerPattern.Matches(original).Cast<Match>().FirstOrDefault(match =>
            string.Equals(match.Value.Trim().Split(']')[0].TrimStart(' ', '\t', '['),
                "mcp_servers.talvora_local", StringComparison.Ordinal));
        if (header is null)
            return original;

        var next = headerPattern.Match(original, header.Index + header.Length);
        var end = next.Success ? next.Index : original.Length;
        var section = original[header.Index..end];
        // Do not delete a similarly named, user-owned table without the URL this installer writes.
        if (!section.Contains($"url = \"{McpUrl}/dev\"", StringComparison.Ordinal))
            return original;
        return original.Remove(header.Index, end - header.Index);
    }

    private static async Task RemoveTalvoraProgramFilesAsync(
        string installRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(installRoot))
            return;
        if ((File.GetAttributes(installRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Talvora kurulum klasörü bağlantı içeriyor; güvenli kaldırma durduruldu.");
        var retainedSetup = Path.Combine(installRoot, InstalledSetupFileName);
        foreach (var entry in Directory.EnumerateFileSystemEntries(installRoot, "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(entry, retainedSetup, StringComparison.OrdinalIgnoreCase))
                continue;
            if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                await TryDeleteDirectoryAsync(entry, cancellationToken);
            else
                File.Delete(entry);
        }

        if (File.Exists(retainedSetup))
        {
            if (!MoveFileEx(retainedSetup, null, MoveFileFlags.DelayUntilReboot))
                throw new IOException("Talvora kurulum dosyası yeniden başlatmada silinemedi. " +
                    "Windows hatası: " + Marshal.GetLastWin32Error());
            if (!MoveFileEx(installRoot, null, MoveFileFlags.DelayUntilReboot))
                throw new IOException("Talvora klasörü yeniden başlatmada silinemedi. " +
                    "Windows hatası: " + Marshal.GetLastWin32Error());
        }
        else if (!Directory.EnumerateFileSystemEntries(installRoot).Any())
        {
            Directory.Delete(installRoot);
        }
    }
}
