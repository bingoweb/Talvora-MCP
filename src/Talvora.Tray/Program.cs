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
                var registry = ManagedMcpRegistryCoordinator
                    .LoadOrRecoverAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                ManagedMcpRecoveryState.AssertPolicyContract();
                ControlCenterEventStore.AssertPolicyContract();
                ControlCenterRawLogService.AssertBoundedReadContract();
                DpapiSecretStore.AssertRoundTripContract();
                ManagedMcpTunnelProvisioningService.AssertPolicyContract();
                ControlCenterSetupService.AssertPolicyContract();
                ManagedMcpOperationCoordinator.AssertContract();
                ManagedMcpSessionState.AssertContract();
                ManagedMcpRegistryStore.AssertRecoveryContractAsync(
                    CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                ManagedMcpOwnershipManifestStore.AssertHealthContractAsync(
                    CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                ManagedMcpRegistryCoordinator.AssertPrimaryRefreshContractAsync(
                    CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                TrayLog.Write(
                    $"Self-test succeeded. Alias={config.Alias}; ManagedMcpCount={registry.Mcps.Count}");
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
        if (args.Length >= 1 &&
            string.Equals(
                args[0],
                "--managed-mcp-probe",
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var managedMcpId =
                    args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
                        ? args[1]
                        : "talvora";

                var registry = ManagedMcpRegistryCoordinator
                    .LoadOrRecoverAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                var registration = registry.Mcps.FirstOrDefault(entry =>
                    string.Equals(
                        entry.Id,
                        managedMcpId,
                        StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException(
                        $"Managed MCP kaydı bulunamadı: {managedMcpId}");

                var probe = ManagedMcpProtocolProbeService
                    .WaitUntilReadyAsync(
                        registration,
                        runBrowserSmoke: true,
                        timeout: TimeSpan.FromSeconds(90),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                var smokeRequired =
                    registration.ProtocolProbe?.BrowserSmokeRequired == true;

                TrayLog.Write(
                    $"Managed MCP probe completed. MCP={registration.Id}; Ready={probe.Ready}; BrowserSmokePassed={probe.BrowserSmokePassed}; ToolCount={probe.ToolCount}; Detail={probe.Detail}");

                return probe.Ready &&
                       (!smokeRequired || probe.BrowserSmokePassed)
                    ? 0
                    : 1;
            }
            catch (Exception ex)
            {
                TrayLog.Write("Managed MCP probe command failed", ex);
                return 1;
            }
        }

        if (args.Any(arg =>
            string.Equals(arg, "--control-center-smoke", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                return ControlCenterSmoke.Run();
            }
            catch (Exception ex)
            {
                TrayLog.Write("Control Center smoke command failed", ex);
                return 1;
            }
        }

        if (args.Any(arg =>
            string.Equals(
                arg,
                "--tunnel-provisioning-check",
                StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var preflight = ManagedMcpTunnelProvisioningService
                    .RunReadOnlyPreflightAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                TrayLog.Write(
                    $"Tunnel provisioning preflight succeeded. Client={preflight.ClientVersion}; " +
                    $"OrganizationScopes={preflight.OrganizationScopeCount}; " +
                    $"WorkspaceScopes={preflight.WorkspaceScopeCount}; " +
                    $"AdminCredentialPresent={preflight.AdminCredentialPresent}");
                return 0;
            }
            catch (Exception ex)
            {
                TrayLog.Write("Tunnel provisioning preflight failed", ex);
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
            TrayLog.Write("Tray mutex acquired; entering WPF/WinForms hybrid message loop.");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var controlCenterApplication = new ControlCenterApplication();
            using var trayContext = new TrayApplicationContext(controlCenterApplication);

            var exitCode = controlCenterApplication.Run();
            TrayLog.Write("Tray/Control Center message loop exited.");
            return exitCode;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}