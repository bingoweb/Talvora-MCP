using System.Diagnostics;
using Talvora.Shared;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Tray;

internal enum TalvoraConnectionState
{
    Ready,
    LocalOnly,
    Offline,
}

internal sealed record TalvoraStatus(
    TalvoraConnectionState State,
    string Summary,
    string Detail);

internal sealed record BusinessConfig(
    string Alias,
    string TunnelId,
    string McpUrl,
    string TunnelClient,
    string TunnelClientVersion,
    string StateRoot,
    string UpdatedAtUtc);

internal sealed class TunnelClientStatus
{
    public bool ProcessRunning { get; init; }
    public bool Healthy { get; init; }
    public bool Ready { get; init; }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--reconnect", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                BusinessTunnelClient.ReconnectAsync(CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex)
            {
                TrayLog.Write("Reconnect command failed", ex);
                return 1;
            }
        }

        if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var config = BusinessTunnelClient.LoadConfig();
                _ = BusinessTunnelClient.ReadRuntimeCredential();
                TrayLog.Write($"Self-test succeeded. Alias={config.Alias}");
                return 0;
            }
            catch (Exception ex)
            {
                TrayLog.Write("Self-test failed", ex);
                return 1;
            }
        }

        if (args.Any(arg => string.Equals(arg, "--gitea-status", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var status = GiteaTrayClient
                    .GetStatusAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                TrayLog.Write($"Gitea status command: {status.Summary}; {status.Detail}");
                return status.State == GiteaConnectionState.Running ? 0 : 1;
            }
            catch (Exception ex)
            {
                TrayLog.Write("Gitea status command failed", ex);
                return 1;
            }
        }

        if (args.Any(arg => string.Equals(arg, "--gitea-restart", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var status = GiteaTrayClient
                    .RestartAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                TrayLog.Write($"Gitea restart command: {status.Summary}; {status.Detail}");
                return status.State == GiteaConnectionState.Running ? 0 : 1;
            }
            catch (Exception ex)
            {
                TrayLog.Write("Gitea restart command failed", ex);
                return 1;
            }
        }

        var replaceExisting = args.Any(arg =>
            string.Equals(arg, "--replace", StringComparison.OrdinalIgnoreCase));

        TrayLog.Write(
            $"Tray startup requested. PID={Environment.ProcessId}; " +
            $"Session={Process.GetCurrentProcess().SessionId}; ReplaceExisting={replaceExisting}");

        using var mutex = new Mutex(
            initiallyOwned: true,
            @"Local\Talvora.Tray",
            out var createdNew);

        var ownsMutex = createdNew;
        if (!ownsMutex && replaceExisting)
        {
            try
            {
                ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }
        }

        if (!ownsMutex)
        {
            TrayLog.Write("Tray startup skipped because another tray instance owns the mutex.");
            return 0;
        }

        try
        {
            TrayLog.Write("Tray mutex acquired; entering message loop.");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var applicationContext = new TrayApplicationContext();
            Application.Run(applicationContext);
            TrayLog.Write("Tray message loop exited.");
            return 0;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
