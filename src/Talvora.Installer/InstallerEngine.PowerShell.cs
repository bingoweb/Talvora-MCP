#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        var path = GetPowerShellMsiPath(pin.Version);
        try
        {
            progress.Report(new InstallProgress(3, "PowerShell 7 indiriliyor..."));
            await DownloadPowerShell7MsiAsync(pin, path, cancellationToken);
            progress.Report(new InstallProgress(5, "PowerShell 7 kuruluyor..."));
            await InstallPowerShell7MsiAsync(path, cancellationToken);
        }
        finally
        {
            DeletePowerShellMsi(path);
        }
    }

    internal static async Task<string> VerifyPowerShell7DownloadAsync(CancellationToken cancellationToken)
    {
        var pin = ReadPowerShellPin();
        var path = GetPowerShellMsiPath(pin.Version);
        try
        {
            await DownloadPowerShell7MsiAsync(pin, path, cancellationToken);
            return pin.Version;
        }
        finally
        {
            DeletePowerShellMsi(path);
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
                $"https://github.com/PowerShell/PowerShell/releases/download/v{version}/PowerShell-{version}-win-x64.msi",
                StringComparison.Ordinal) ||
            !Regex.IsMatch(sha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("PowerShell 7 sürüm doğrulama bilgisi geçersiz.");
        return new PowerShellPin(version, url, sha256);
    }

    private static string GetPowerShellMsiPath(string version) =>
        Path.Combine(Path.GetTempPath(), $"Talvora-PowerShell-{version}.msi");

    private static async Task DownloadPowerShell7MsiAsync(
        PowerShellPin pin, string path, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await client.GetAsync(
                pin.Url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
            await using var destination = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await source.CopyToAsync(destination, deadline.Token);
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
        ValidatePowerShell7MsiHash(path, pin.Sha256);
    }

    internal static void ValidatePowerShell7MsiHash(string path, string expectedHash)
    {
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(hash, expectedHash, StringComparison.Ordinal))
            throw new InvalidOperationException(IntegrityError);
    }

    private static async Task InstallPowerShell7MsiAsync(string path, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("msiexec.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var part in new[] {
            "/i", path, "/quiet", "/norestart", "ADD_PATH=1",
            "REGISTER_MANIFEST=1", "USE_MU=1", "ENABLE_MU=1"
        })
            start.ArgumentList.Add(part);

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            throw PowerShellMsiError(-1, error);
        }
        if (process is null)
            throw PowerShellMsiError(-1);

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode is not (0 or 3010))
                throw PowerShellMsiError(process.ExitCode);
        }
    }

    private static InvalidOperationException PowerShellMsiError(int code, Exception? inner = null) =>
        new($"PowerShell 7 kurulamadı (msiexec çıkış kodu {code}). " +
            $"PowerShell 7'yi {InstallHelp} adresindeki adımlarla kurup " +
            "Talvora kurulumunu yeniden başlat.", inner);

    private static void DeletePowerShellMsi(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            InstallerLog.Write("PowerShell 7 temporary MSI cleanup failed", error);
        }
    }
}
