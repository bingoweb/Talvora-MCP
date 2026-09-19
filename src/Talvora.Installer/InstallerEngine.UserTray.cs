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
private static InstallUserContext ResolveInstallUserContext()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value
            ?? throw new InvalidOperationException("Installer user SID could not be resolved.");
        var isLocalSystem = identity.User?.IsWellKnown(
            WellKnownSidType.LocalSystemSid) is true;

        if (isLocalSystem)
        {
            var interactive = WindowsSessionLauncher.GetDefaultInteractiveUser();
            var userProfile = interactive.UserProfile
                ?? throw new InvalidOperationException(
                    "Interactive user profile could not be resolved.");
            var localAppData = interactive.LocalAppData
                ?? Path.Combine(userProfile, "AppData", "Local");

            return new InstallUserContext(
                true,
                interactive.SessionId,
                interactive.Sid,
                userProfile,
                localAppData);
        }

        var currentProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var currentLocalAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        return new InstallUserContext(
            false,
            null,
            sid,
            currentProfile,
            currentLocalAppData);
    }

    private static RegistryKey OpenInstallUserRoot(
        InstallUserContext installUser,
        bool writable)
    {
        if (!installUser.UseInteractiveSession)
        {
            return RegistryKey.OpenBaseKey(
                RegistryHive.CurrentUser,
                RegistryView.Default);
        }

        return Registry.Users.OpenSubKey(installUser.Sid, writable)
            ?? throw new InvalidOperationException(
                $"Interactive user registry hive is not loaded: {installUser.Sid}");
    }

    private static void RegisterTrayStartup(
        string trayExecutable,
        InstallUserContext installUser)
    {
        using var userRoot = OpenInstallUserRoot(installUser, writable: true);
        using var runKey = userRoot.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true);

        runKey.SetValue(
            RunValueName,
            $"\"{trayExecutable}\"",
            RegistryValueKind.String);
    }

    private static string? ReadTrayStartupExecutable(
        InstallUserContext installUser)
    {
        using var userRoot = OpenInstallUserRoot(installUser, writable: false);
        using var runKey = userRoot.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: false);

        var value = runKey?.GetValue(RunValueName) as string;
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Trim('"');
    }

    private static Task<string?> GetExistingServiceExecutableAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\" + ServiceName,
            writable: false);

        var imagePath = key?.GetValue("ImagePath") as string;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return Task.FromResult<string?>(null);
        }

        var expanded = Environment.ExpandEnvironmentVariables(imagePath).Trim();
        var executable = expanded.StartsWith('"')
            ? expanded[1..].Split('"', 2)[0]
            : expanded.Split(' ', 2)[0];

        return Task.FromResult<string?>(executable);
    }

    private static async Task<bool> TryStartTrayAsync(
        string trayExecutable,
        InstallUserContext installUser,
        CancellationToken cancellationToken,
        int attempts = 3)
    {
        if (attempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts));
        }

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await StartTrayAsync(
                    trayExecutable,
                    installUser,
                    cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is System.ComponentModel.Win32Exception or
                TimeoutException or
                IOException or
                InvalidOperationException)
            {
                InstallerLog.Write(
                    $"Tray launch attempt {attempt}/{attempts} failed. Path={trayExecutable}",
                    ex);

                if (attempt >= attempts)
                {
                    return false;
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        return false;
    }

    private static async Task StartTrayAsync(
        string trayExecutable,
        InstallUserContext installUser,
        CancellationToken cancellationToken)
    {
        await StopTrayProcessesAsync(cancellationToken);

        int launchedProcessId;
        if (installUser.UseInteractiveSession)
        {
            var launched = WindowsSessionLauncher.StartProcess(
                trayExecutable,
                arguments: ["--replace"],
                sessionId: installUser.SessionId,
                workingDirectory: Path.GetDirectoryName(trayExecutable),
                visible: true,
                newConsole: false);
            launchedProcessId = launched.ProcessId;
            InstallerLog.Write(
                $"Tray launch requested in session {launched.SessionId}. PID={launched.ProcessId}");
        }
        else
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = trayExecutable,
                UseShellExecute = true,
            };
            startInfo.ArgumentList.Add("--replace");

            using var launched = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                $"Tray process could not be started: {trayExecutable}");
            launchedProcessId = launched.Id;
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        DateTime? stableSinceUtc = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var process = Process.GetProcessById(launchedProcessId);
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Tray process exited before becoming stable. PID={launchedProcessId}");
                }

                var path = process.MainModule?.FileName;
                if (string.Equals(
                        path,
                        trayExecutable,
                        StringComparison.OrdinalIgnoreCase))
                {
                    stableSinceUtc ??= DateTime.UtcNow;
                    if (DateTime.UtcNow - stableSinceUtc >= TimeSpan.FromSeconds(2))
                    {
                        InstallerLog.Write(
                            $"Tray started and remained stable. PID={process.Id} Path={path}");
                        return;
                    }
                }
                else
                {
                    stableSinceUtc = null;
                }
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Tray process exited before becoming stable. PID={launchedProcessId}",
                    ex);
            }
            catch (Exception ex) when (
                ex is NotSupportedException or
                System.ComponentModel.Win32Exception)
            {
                stableSinceUtc = null;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException(
            $"Talvora Tray 10 saniye içinde kararlı şekilde başlayamadı. PID={launchedProcessId}");
    }

    private static async Task StopTrayProcessesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var shutdownEvent = EventWaitHandle.OpenExisting(
                @"Global\Talvora.Tray.Shutdown");
            shutdownEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }

        var gracefulDeadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < gracefulDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Process.GetProcessesByName("Talvora.Tray").Length == 0)
            {
                return;
            }

            await Task.Delay(200, cancellationToken);
        }

        foreach (var process in Process.GetProcessesByName("Talvora.Tray"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                InstallerLog.Write($"Tray force-stop failed. PID={process.Id}", ex);
            }
            finally
            {
                process.Dispose();
            }
        }

        var forcedDeadline = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < forcedDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Process.GetProcessesByName("Talvora.Tray").Length == 0)
            {
                return;
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new IOException("Talvora Tray kapatılamadı; güncelleme güvenle devam edemiyor.");
    }
}
