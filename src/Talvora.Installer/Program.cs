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

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
