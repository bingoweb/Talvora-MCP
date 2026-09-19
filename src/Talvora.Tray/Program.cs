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

        using var mutex = new Mutex(initiallyOwned: true, @"Local\Talvora.Tray", out var createdNew);
        if (!createdNew)
        {
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApplicationContext());
        return 0;
    }
}
