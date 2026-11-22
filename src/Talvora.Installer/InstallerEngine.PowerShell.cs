#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Talvora.Installer;

internal static partial class InstallerEngine
{
    private const string DownloadError =
        "PowerShell 7 indirilemedi. İnternet bağlantını kontrol edip kurulumu yeniden başlat.";
    private const string IntegrityError =
        "İndirilen PowerShell 7 paketi doğrulanamadı; güvenlik için kurulmadı.";
    private const string InstallHelp =
        "https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows";

    private sealed record PowerShellPin(string Version, string Url, string Sha256);

    internal static async Task EnsurePowerShell7Async(
        IProgress<InstallProgress> progress, CancellationToken cancellationToken,
        Func<bool>? availabilityProbe = null)
    {
        if ((availabilityProbe ?? IsPowerShell7Available)())
        {
            InstallerLog.Write("PowerShell 7 hazır.");
            return;
        }

        var pin = ReadPowerShellPin();
        var path = GetPowerShellPackagePath(pin.Version);
        try
        {
            await DownloadPowerShell7PackageAsync(pin, path, progress, cancellationToken);
            using (var verifiedStream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ValidatePowerShell7PackageHash(verifiedStream, pin.Sha256);
                progress.Report(new InstallProgress(5, "PowerShell 7 kuruluyor..."));
                ExtractPowerShell7Zip(verifiedStream,
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            }

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerShell", "7");
            bool pathEntryAdded;
            try
            {
                pathEntryAdded = AddMachinePathEntry(folder);
            }
            catch (Exception error)
            {
                InstallerLog.Write("PowerShell 7 machine PATH update failed", error);
                pathEntryAdded = false;
            }
            if (pathEntryAdded)
            {
                var marker = Path.Combine(folder, "talvora-installed.json");
                var temporary = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, "{\"version\":\"7.7.0\",\"pathEntryAdded\":true}");
                    File.Move(temporary, marker, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
            }
        }
        finally
        {
            DeletePowerShellPackage(path);
        }
    }

    internal static async Task<string> VerifyPowerShell7DownloadAsync(CancellationToken cancellationToken)
    {
        var pin = ReadPowerShellPin();
        var path = GetPowerShellPackagePath(pin.Version);
        try
        {
            await DownloadPowerShell7PackageAsync(pin, path, null, cancellationToken);
            using (var verifiedStream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ValidatePowerShell7PackageHash(verifiedStream, pin.Sha256);
            }
            return pin.Version;
        }
        finally
        {
            DeletePowerShellPackage(path);
        }
    }

    private static bool IsPowerShell7Available()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (File.Exists(Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe")))
            return true;

        var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine);
        foreach (var entry in (machinePath ?? string.Empty).Split(Path.PathSeparator))
        {
            var directory = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
            if (Path.IsPathFullyQualified(directory) &&
                File.Exists(Path.Combine(directory, "pwsh.exe")))
                return true;
        }
        return false;
    }

    private static PowerShellPin ReadPowerShellPin()
    {
        using var resource = typeof(InstallerEngine).Assembly
            .GetManifestResourceStream("Talvora.PowerShellPin.json")
            ?? throw new InvalidOperationException("PowerShell 7 sürüm doğrulama bilgisi bulunamadı.");
        using var document = JsonDocument.Parse(resource);
        var pin = document.RootElement;
        var version = pin.GetProperty("version").GetString() ?? string.Empty;
        var url = pin.GetProperty("url").GetString() ?? string.Empty;
        var sha256 = pin.GetProperty("sha256").GetString() ?? string.Empty;
        if (!Regex.IsMatch(version, @"^7\.\d+\.\d+$", RegexOptions.CultureInvariant) ||
            !string.Equals(url,
                $"https://github.com/PowerShell/PowerShell/releases/download/v{version}/PowerShell-{version}-win-x64.zip",
                StringComparison.Ordinal) ||
            !Regex.IsMatch(sha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("PowerShell 7 sürüm doğrulama bilgisi geçersiz.");
        return new PowerShellPin(version, url, sha256);
    }

    private static string GetPowerShellPackagePath(string version) =>
        Path.Combine(Path.GetTempPath(), $"Talvora-PowerShell-{version}-{Guid.NewGuid():N}.zip");

    private static async Task DownloadPowerShell7PackageAsync(
        PowerShellPin pin, string path, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await client.GetAsync(
                pin.Url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is not > 0)
                progress?.Report(new InstallProgress(3, "PowerShell 7 indiriliyor…"));
            await using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
            await using var destination = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            long downloaded = 0;
            var lastPercent = -1;
            int read;
            while ((read = await source.ReadAsync(buffer, deadline.Token)) != 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
                downloaded += read;
                if (contentLength is > 0)
                {
                    var percent = (int)(downloaded * 100 / contentLength.Value);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(new InstallProgress(3,
                            $"PowerShell 7 indiriliyor… %{percent.ToString(CultureInfo.InvariantCulture)}"));
                    }
                }
            }
            await destination.FlushAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or
            OperationCanceledException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(DownloadError, error);
        }
    }

    internal static void ValidatePowerShell7PackageHash(FileStream stream, string expectedHash)
    {
        stream.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(hash, expectedHash, StringComparison.Ordinal))
            throw new InvalidOperationException(IntegrityError);
    }

    internal static void ExtractPowerShell7Zip(FileStream lockedZip, string programFilesRoot)
    {
        var powerShellRoot = Path.Combine(programFilesRoot, "PowerShell");
        var finalFolder = Path.Combine(powerShellRoot, "7");
        if (Directory.Exists(finalFolder))
            throw PowerShellZipError(@"C:\Program Files\PowerShell\7 klasörü zaten var");

        Directory.CreateDirectory(powerShellRoot);
        var stagingFolder = Path.Combine(powerShellRoot, $"7.talvora-{Guid.NewGuid():N}");
        try
        {
            using (var archive = new ZipArchive(lockedZip, ZipArchiveMode.Read, leaveOpen: true))
                archive.ExtractToDirectory(stagingFolder);
            if (!File.Exists(Path.Combine(stagingFolder, "pwsh.exe")))
                throw PowerShellZipError("paket içinde pwsh.exe yok");
            File.WriteAllText(Path.Combine(stagingFolder, "talvora-installed.json"),
                "{\"version\":\"7.7.0\",\"pathEntryAdded\":false}");
            Directory.Move(stagingFolder, finalFolder);
        }
        catch (Exception error)
        {
            if (Directory.Exists(stagingFolder))
                Directory.Delete(stagingFolder, recursive: true);
            if (error is InvalidOperationException invalid &&
                invalid.Message.StartsWith("PowerShell 7 kurulamadı:", StringComparison.Ordinal))
                throw;
            throw PowerShellZipError($"paket açılamadı ({error.Message})", error);
        }
    }

    internal static string? AppendPathEntry(string rawPath, string entry)
    {
        var wanted = NormalizePathEntry(entry);
        foreach (var part in rawPath.Split(';'))
        {
            if (string.Equals(NormalizePathEntry(part), wanted,
                    StringComparison.OrdinalIgnoreCase))
                return null;
        }
        return string.IsNullOrEmpty(rawPath)
            ? entry
            : rawPath.TrimEnd(';') + ";" + entry;
    }

    internal static string? RemovePathEntry(string rawPath, string entry)
    {
        var wanted = NormalizePathEntry(entry);
        var parts = rawPath.Split(';');
        var retained = new System.Collections.Generic.List<string>();
        var removed = false;
        foreach (var part in parts)
        {
            if (string.Equals(NormalizePathEntry(part), wanted,
                    StringComparison.OrdinalIgnoreCase))
                removed = true;
            else
                retained.Add(part);
        }
        return removed ? string.Join(';', retained) : null;
    }

    private static string NormalizePathEntry(string entry) =>
        entry.Trim().Trim('"').Trim().TrimEnd('\\');

    internal static bool ShouldRemoveTalvoraPowerShell(
        string markerVersion, string? installedFileVersion) =>
        !string.IsNullOrWhiteSpace(markerVersion) &&
        installedFileVersion is not null &&
        installedFileVersion.StartsWith(markerVersion + ".", StringComparison.Ordinal);

    internal static string? FindTalvoraPowerShellToRemove(
        string programFilesRoot, out bool pathEntryAdded)
    {
        pathEntryAdded = false;
        var folder = Path.Combine(programFilesRoot, "PowerShell", "7");
        var marker = Path.Combine(folder, "talvora-installed.json");
        if (!File.Exists(marker))
            return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(marker));
            var version = document.RootElement.GetProperty("version").GetString() ?? "";
            var recordedPath = document.RootElement.GetProperty("pathEntryAdded").GetBoolean();
            var executable = Path.Combine(folder, "pwsh.exe");
            var installedVersion = File.Exists(executable)
                ? FileVersionInfo.GetVersionInfo(executable).FileVersion
                : null;
            if (!ShouldRemoveTalvoraPowerShell(version, installedVersion))
                return null;
            pathEntryAdded = recordedPath;
            return folder;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or JsonException or KeyNotFoundException)
        {
            InstallerLog.Write("PowerShell 7 installation marker could not be verified", error);
            return null;
        }
    }

    private static InvalidOperationException PowerShellZipError(
        string reason, Exception? inner = null) =>
        new($"PowerShell 7 kurulamadı: {reason}. PowerShell 7'yi {InstallHelp} adresindeki adımlarla kurup Talvora kurulumunu yeniden başlat.", inner);

    private static void DeletePowerShellPackage(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            InstallerLog.Write("PowerShell 7 temporary ZIP cleanup failed", error);
        }
    }
}
