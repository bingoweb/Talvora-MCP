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
private static async Task RemoveLegacyInstallationAsync(
        InstallUserContext installUser,
        CancellationToken cancellationToken)
    {
        await StopAndDeleteServiceAsync("Cloudflared", cancellationToken);

        await TryDeleteDirectoryAsync(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "cloudflared"),
            cancellationToken);

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        await TryDeleteDirectoryAsync(
            Path.Combine(programFiles, "Talvora", "Cloudflare"),
            cancellationToken);

        try
        {
            using var userRoot = OpenInstallUserRoot(installUser, writable: true);
            using var legacyKey = userRoot.OpenSubKey(
                @"SOFTWARE\Talvora",
                writable: true);
            if (legacyKey is not null &&
                legacyKey.ValueCount == 0 &&
                legacyKey.SubKeyCount == 0)
            {
                userRoot.DeleteSubKeyTree(
                    @"SOFTWARE\Talvora",
                    throwOnMissingSubKey: false);
            }
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            InstallerLog.Write("Legacy user registry cleanup skipped", ex);
        }
    }

    private static async Task StopAndDeleteServiceAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        var query = await RunScAsync(
            allowNonZero: true,
            cancellationToken,
            "queryex",
            serviceName);

        if (query.ExitCode != 0)
        {
            return;
        }

        var processId = ParseServiceProcessId(query.StandardOutput);

        _ = await RunScAsync(
            allowNonZero: true,
            cancellationToken,
            "stop",
            serviceName);

        var stopDeadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < stopDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await RunScAsync(
                allowNonZero: true,
                cancellationToken,
                "queryex",
                serviceName);

            if (status.ExitCode != 0)
            {
                return;
            }

            if (status.StandardOutput.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                _ = await RunScAsync(
                    allowNonZero: true,
                    cancellationToken,
                    "delete",
                    serviceName);
                await WaitForServiceDeletionAsync(serviceName, cancellationToken);
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        InstallerLog.Write(
            $"Service did not accept STOP; deleting registration before terminating PID={processId}.");

        _ = await RunScAsync(
            allowNonZero: true,
            cancellationToken,
            "delete",
            serviceName);

        if (processId > 0)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        await WaitForServiceDeletionAsync(serviceName, cancellationToken);
    }

    private static async Task WaitForServiceDeletionAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        var deleteDeadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deleteDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await RunScAsync(
                allowNonZero: true,
                cancellationToken,
                "query",
                serviceName);

            if (status.ExitCode != 0)
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException($"Windows service silinemedi: {serviceName}");
    }

    private static int ParseServiceProcessId(string output)
    {
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("PID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = trimmed.IndexOf(':');
            if (separator >= 0 &&
                int.TryParse(
                    trimmed[(separator + 1)..].Trim(),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var processId))
            {
                return processId;
            }
        }

        return 0;
    }

    private static async Task CreateServiceAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        var quotedExecutable = $"\"{executable}\"";

        await RunScAsync(
            allowNonZero: false,
            cancellationToken,
            "create",
            ServiceName,
            "binPath=",
            quotedExecutable,
            "start=",
            "auto",
            "obj=",
            "LocalSystem",
            "DisplayName=",
            "Talvora");

        await RunScAsync(
            allowNonZero: false,
            cancellationToken,
            "description",
            ServiceName,
            "Talvora Local MCP - LocalSystem automatic resilient service");

        await RunScAsync(
            allowNonZero: false,
            cancellationToken,
            "failure",
            ServiceName,
            "reset=",
            "86400",
            "actions=",
            "restart/1000/restart/3000/restart/10000/restart/30000/restart/60000");

        _ = await RunScAsync(
            allowNonZero: true,
            cancellationToken,
            "failureflag",
            ServiceName,
            "1");

        _ = await RunScAsync(
            allowNonZero: true,
            cancellationToken,
            "sidtype",
            ServiceName,
            "unrestricted");
    }

    private static async Task<ProcessExecutionResult> RunScAsync(
        bool allowNonZero,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var executable = Path.Combine(systemDirectory, "sc.exe");

        var result = await ProcessRunner.RunAsync(
            executable,
            Environment.CurrentDirectory,
            arguments,
            timeoutSeconds: 60,
            cancellationToken: cancellationToken);

        if (!allowNonZero && (result.ExitCode != 0 || result.TimedOut))
        {
            throw new InvalidOperationException(
                $"Windows service işlemi başarısız ({string.Join(' ', arguments)}): " +
                $"{Collapse(result.StandardError, result.StandardOutput)}");
        }

        return result;
    }

    private static async Task<HealthSnapshot> WaitForHealthAsync(
        string sourceCommit,
        CancellationToken cancellationToken)
    {
        using var client = TalvoraHttp.CreateClient(
            timeout: TimeSpan.FromSeconds(3));

        var deadline = DateTime.UtcNow.AddSeconds(45);
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = await client.GetStringAsync(HealthUrl, cancellationToken);
                var health = JsonSerializer.Deserialize<HealthSnapshot>(
                    json,
                    CaseInsensitiveJsonOptions);

                if (health is not null &&
                    health.IsWindowsService &&
                    string.Equals(health.Sid, "S-1-5-18", StringComparison.Ordinal) &&
                    string.Equals(health.SourceCommit, sourceCommit, StringComparison.OrdinalIgnoreCase))
                {
                    return health;
                }

                lastError = new InvalidOperationException(
                    "Talvora health yanıtı beklenen servis/commit bilgisiyle eşleşmedi.");
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException(
            "Talvora servisi 45 saniye içinde sağlıklı hale gelmedi.",
            lastError);
    }
}
