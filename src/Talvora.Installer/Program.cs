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

        if (!IsAdministrator())
        {
            MessageBox.Show(
                "Talvora kurulumu yönetici yetkisi gerektirir.",
                "Talvora Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return 3;
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

                InstallerEngine.InstallAsync(
                    new Progress<InstallProgress>(progress =>
                        InstallerLog.Write($"{progress.Percent}% {progress.Message}")),
                    CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex)
            {
                InstallerLog.Write("Silent install failed", ex);
                return 1;
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new InstallerForm();
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
