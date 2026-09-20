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
public static async Task<HealthSnapshot> InstallAsync(
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new InstallProgress(2, "Kurulum paketi hazırlanıyor..."));
        InstallerLog.Write("Install started");

        var installUser = ResolveInstallUserContext();

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Setup-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempRoot);

        string? previousServiceExecutable = null;
        string? previousTrayExecutable = null;
        var serviceSwitchStarted = false;

        try
        {
            ExtractPayload(tempRoot);
            var sourceCommit = (await File.ReadAllTextAsync(
                Path.Combine(tempRoot, "source-commit.txt"),
                cancellationToken)).Trim();

            if (string.IsNullOrWhiteSpace(sourceCommit))
            {
                throw new InvalidOperationException("Kurulum paketinde source commit bilgisi yok.");
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var installRoot = Path.Combine(programFiles, "Talvora");
            var versionsRoot = Path.Combine(installRoot, "Versions");
            var versionId = sourceCommit + "-" + DateTime.UtcNow.ToString(
                "yyyyMMddHHmmssfff",
                System.Globalization.CultureInfo.InvariantCulture);
            var versionRoot = Path.Combine(versionsRoot, versionId);
            var serviceRoot = Path.Combine(versionRoot, "Service");
            var trayRoot = Path.Combine(versionRoot, "Tray");
            var serviceExecutable = Path.Combine(serviceRoot, "Talvora.exe");
            var trayExecutable = Path.Combine(trayRoot, "Talvora.Tray.exe");

            previousServiceExecutable = await GetExistingServiceExecutableAsync(cancellationToken);
            previousTrayExecutable = ReadTrayStartupExecutable(installUser);

            progress.Report(new InstallProgress(8, "Yeni Talvora sürümü hazırlanıyor..."));
            Directory.CreateDirectory(versionsRoot);
            Directory.CreateDirectory(serviceRoot);
            Directory.CreateDirectory(trayRoot);

            CopyTree(Path.Combine(tempRoot, "Service"), serviceRoot);
            CopyTree(Path.Combine(tempRoot, "Tray"), trayRoot);

            if (!File.Exists(serviceExecutable))
            {
                throw new FileNotFoundException(
                    "Talvora servis executable bulunamadı.",
                    serviceExecutable);
            }

            if (!File.Exists(trayExecutable))
            {
                throw new FileNotFoundException(
                    "Talvora Tray executable bulunamadı.",
                    trayExecutable);
            }

            var installedAtUtc = DateTime.UtcNow.ToString(
                "O",
                System.Globalization.CultureInfo.InvariantCulture);
            var dependencyProvenancePath = Path.Combine(
                tempRoot,
                "resolved-dependencies.json");
            using var dependencyProvenanceDocument =
                JsonDocument.Parse(
                    await File.ReadAllTextAsync(
                        dependencyProvenancePath,
                        cancellationToken));

            var runtimeMetadata = new
            {
                SourceCommit = sourceCommit,
                InstalledAtUtc = installedAtUtc,
                DependencyProvenance =
                    dependencyProvenanceDocument.RootElement.Clone(),
            };
            await File.WriteAllTextAsync(
                Path.Combine(serviceRoot, "talvora-runtime.json"),
                JsonSerializer.Serialize(runtimeMetadata, IndentedJsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            progress.Report(new InstallProgress(22, "Eski Talvora kalıntıları temizleniyor..."));
            await RemoveLegacyInstallationAsync(installUser, cancellationToken);

            progress.Report(new InstallProgress(34, "Çalışan Talvora kontrollü olarak değiştiriliyor..."));
            serviceSwitchStarted = true;
            await StopServiceForUpgradeAsync(ServiceName, cancellationToken);
            await StopTrayProcessesAsync(cancellationToken);

            progress.Report(new InstallProgress(50, "Windows servisi yeni sürüme bağlanıyor..."));
            await CreateServiceAsync(serviceExecutable, installUser, cancellationToken);

            progress.Report(new InstallProgress(63, "Talvora servisi başlatılıyor..."));
            await RunScAsync(
                allowNonZero: false,
                cancellationToken,
                "start",
                ServiceName);

            var health = await WaitForHealthAsync(sourceCommit, cancellationToken);

            progress.Report(new InstallProgress(76, "Sistem tepsisi uygulaması kaydediliyor..."));
            RegisterTrayStartup(trayExecutable, installUser);
            await WriteCurrentStateAsync(
                health,
                sourceCommit,
                installedAtUtc,
                installUser,
                cancellationToken);
            await UpsertCodexConfigurationAsync(
                installUser,
                cancellationToken);
            progress.Report(new InstallProgress(82, "Talvora tepsi uygulaması başlatılıyor..."));
            var trayStarted = await TryStartTrayAsync(
                trayExecutable,
                installUser,
                cancellationToken);
            if (!trayStarted)
            {
                InstallerLog.Write(
                    "Tray could not be started immediately. " +
                    "The startup registration is intact, so the core Talvora service remains installed.");
            }
            progress.Report(new InstallProgress(90, "Eski sürüm dosyaları temizleniyor..."));
            try
            {
                await CleanupObsoleteInstallationsAsync(
                    installRoot,
                    versionRoot,
                    cancellationToken);
            }
            catch (Exception cleanupError) when (
                cleanupError is IOException or
                UnauthorizedAccessException or
                OperationCanceledException)
            {
                InstallerLog.Write(
                    "Post-commit obsolete version cleanup deferred",
                    cleanupError);
            }

            progress.Report(new InstallProgress(100, "Talvora hazır."));
            InstallerLog.Write(
                $"Install succeeded. Commit={sourceCommit} PID={health.ProcessId}");
            return health;
        }
        catch
        {
            if (serviceSwitchStarted &&
                !string.IsNullOrWhiteSpace(previousServiceExecutable) &&
                File.Exists(previousServiceExecutable))
            {
                try
                {
                    InstallerLog.Write(
                        $"Install failed after service switch; rolling back to {previousServiceExecutable}");
                    var rollbackToken = CancellationToken.None;
                    await StopServiceForUpgradeAsync(ServiceName, rollbackToken);
                    await CreateServiceAsync(previousServiceExecutable, installUser, rollbackToken);
                    await RunScAsync(
                        allowNonZero: false,
                        rollbackToken,
                        "start",
                        ServiceName);

                    if (!string.IsNullOrWhiteSpace(previousTrayExecutable) &&
                        File.Exists(previousTrayExecutable))
                    {
                        RegisterTrayStartup(previousTrayExecutable, installUser);
                        await StartTrayAsync(previousTrayExecutable, installUser, rollbackToken);
                    }
                }
                catch (Exception rollbackError)
                {
                    InstallerLog.Write("Rollback failed", rollbackError);
                }
            }

            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                InstallerLog.Write("Temporary installer directory cleanup skipped", ex);
            }
        }
    }

    private static void ExtractPayload(string destination)
    {
        using var stream = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream("Talvora.Payload.zip")
            ?? throw new InvalidOperationException("Gömülü Talvora payload bulunamadı.");

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(destination, overwriteFiles: true);
    }
}
