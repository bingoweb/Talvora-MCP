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

internal sealed record InstallProgress(int Percent, string Message);

internal sealed record HealthSnapshot(
    string? Product,
    string? Version,
    string? SourceCommit,
    string? InstalledAtUtc,
    string? Mcp,
    int ProcessId,
    string? User,
    string? Sid,
    bool IsWindowsService);

internal sealed record InstallUserContext(
    bool UseInteractiveSession,
    int? SessionId,
    string Sid,
    string UserProfile,
    string LocalAppData,
    string RoamingAppData);

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 2;
        }

        if (args.Any(arg => string.Equals(arg, "--verify-powershell-download", StringComparison.OrdinalIgnoreCase)))
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                var version = InstallerEngine.VerifyPowerShell7DownloadAsync(
                    CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"PowerShell 7 {version} paketi doğrulandı.");
                return 0;
            }
            catch (Exception ex)
            {
                InstallerLog.Write("PowerShell 7 download verification failed", ex);
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        if (!IsAdministrator())
        {
            MessageBox.Show(
                "Talvora kurulumu yönetici yetkisi gerektirir.",
                "Talvora Kurulumu",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return 3;
        }

        var isUninstall = args.Any(arg =>
            string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase));
        var removeData = args.Any(arg =>
            string.Equals(arg, "--remove-data", StringComparison.OrdinalIgnoreCase));
        if (isUninstall && args.Any(arg =>
                string.Equals(arg, "--what-if", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var item in InstallerEngine.GetCurrentUninstallPlan(removeData))
                Console.WriteLine(item);
            return 0;
        }

        if (args.Any(arg => string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var deferMilliseconds =
                    ReadBoundedIntegerArgument(
                        args,
                        "--defer-ms",
                        maximum: 30_000);
                if (deferMilliseconds > 0)
                {
                    Thread.Sleep(deferMilliseconds);
                }

                var progress = new Progress<InstallProgress>(step =>
                {
                    if (!(isUninstall && removeData))
                        InstallerLog.Write($"{step.Percent}% {step.Message}");
                });
                if (isUninstall)
                    InstallerEngine.UninstallAsync(progress, removeData,
                        CancellationToken.None).GetAwaiter().GetResult();
                else
                    InstallerEngine.InstallAsync(progress,
                        CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex)
            {
                InstallerLog.Write(isUninstall ? "Silent uninstall failed" : "Silent install failed", ex);
                return 1;
            }
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = isUninstall
            ? new InstallerForm(SetupMode.Uninstall, async (progress, option, cancellationToken) =>
            {
                await InstallerEngine.UninstallAsync(progress, option, cancellationToken);
                return null;
            })
            : new InstallerForm();
        Application.Run(form);
        return 0;
    }

    private static int ReadBoundedIntegerArgument(
        IReadOnlyList<string> args,
        string name,
        int maximum)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(
                    args[index + 1],
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value) ||
                value < 0 ||
                value > maximum)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    $"Expected an integer between 0 and {maximum}.");
            }

            return value;
        }

        return 0;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
