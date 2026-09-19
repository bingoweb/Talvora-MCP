using System.IO;
using System.Net.Http;
using ModelContextProtocol.Client;
using Talvora.Shared;

namespace Talvora.Tray;

internal enum ManagedMcpLifecycleOperation
{
    Start,
    Stop,
    Restart,
}

internal sealed record ManagedMcpLifecycleResult(
    ManagedMcpLifecycleOperation Operation,
    string Summary,
    string Detail);

internal static class ControlCenterLifecycleService
{
    private const string TalvoraId = "talvora";
    private const string GiteaId = "gitea";
    private const string TalvoraServiceName = "Talvora";
    private const string TalvoraMcpUrl = "http://127.0.0.1:7676/mcp";

    public static bool IsManuallyStopped(ManagedMcpRegistration registration) =>
        ManagedMcpSessionState.IsManuallyStopped(registration.Id);

    public static Task<ManagedMcpLifecycleResult> StartAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            registration,
            ManagedMcpLifecycleOperation.Start,
            cancellationToken);

    public static Task<ManagedMcpLifecycleResult> StopAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            registration,
            ManagedMcpLifecycleOperation.Stop,
            cancellationToken);

    public static Task<ManagedMcpLifecycleResult> RestartAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            registration,
            ManagedMcpLifecycleOperation.Restart,
            cancellationToken);

    private static async Task<ManagedMcpLifecycleResult> ExecuteAsync(
        ManagedMcpRegistration registration,
        ManagedMcpLifecycleOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (string.Equals(
                registration.Id,
                TalvoraId,
                StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteTalvoraAsync(operation, cancellationToken);
        }

        if (string.Equals(
                registration.Id,
                GiteaId,
                StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteGiteaAsync(operation, cancellationToken);
        }

        return await ExecuteGenericAsync(
            registration,
            operation,
            cancellationToken);
    }

    private static async Task<ManagedMcpLifecycleResult> ExecuteTalvoraAsync(
        ManagedMcpLifecycleOperation operation,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case ManagedMcpLifecycleOperation.Start:
                ManagedMcpSessionState.ClearManualStop(TalvoraId);
                await RunTalvoraServiceCommandAsync(
                    "start",
                    allowAlreadyInRequestedState: true,
                    cancellationToken);
                await WaitForTalvoraHealthAsync(
                    expectedHealthy: true,
                    TimeSpan.FromSeconds(45),
                    cancellationToken);
                await BusinessTunnelClient.ReconnectAsync(cancellationToken);
                return new ManagedMcpLifecycleResult(
                    operation,
                    "Talvora MCP başlatıldı",
                    "Yerel servis ve güvenli MCP tüneli hazır.");

            case ManagedMcpLifecycleOperation.Stop:
                ManagedMcpSessionState.MarkManuallyStopped(TalvoraId);
                try
                {
                    await BusinessTunnelClient.DisconnectAsync(cancellationToken);
                    await RunTalvoraServiceCommandAsync(
                        "stop",
                        allowAlreadyInRequestedState: true,
                        cancellationToken);
                    await WaitForTalvoraHealthAsync(
                        expectedHealthy: false,
                        TimeSpan.FromSeconds(30),
                        cancellationToken);

                    return new ManagedMcpLifecycleResult(
                        operation,
                        "Talvora MCP durduruldu",
                        "Yerel servis ve güvenli MCP tüneli bu Windows oturumu için durduruldu.");
                }
                catch
                {
                    ManagedMcpSessionState.ClearManualStop(TalvoraId);
                    throw;
                }

            case ManagedMcpLifecycleOperation.Restart:
                ManagedMcpSessionState.ClearManualStop(TalvoraId);
                await BusinessTunnelClient.DisconnectAsync(cancellationToken);
                await RunTalvoraServiceCommandAsync(
                    "stop",
                    allowAlreadyInRequestedState: true,
                    cancellationToken);
                await WaitForTalvoraHealthAsync(
                    expectedHealthy: false,
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
                await RunTalvoraServiceCommandAsync(
                    "start",
                    allowAlreadyInRequestedState: true,
                    cancellationToken);
                await WaitForTalvoraHealthAsync(
                    expectedHealthy: true,
                    TimeSpan.FromSeconds(45),
                    cancellationToken);
                await BusinessTunnelClient.ReconnectAsync(cancellationToken);

                return new ManagedMcpLifecycleResult(
                    operation,
                    "Talvora MCP yeniden başlatıldı",
                    "Yerel servis ve güvenli MCP tüneli yeniden hazırlandı.");

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static async Task<ManagedMcpLifecycleResult> ExecuteGiteaAsync(
        ManagedMcpLifecycleOperation operation,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case ManagedMcpLifecycleOperation.Start:
                ManagedMcpSessionState.ClearManualStop(GiteaId);
                var started = await GiteaTrayClient.StartAsync(cancellationToken);
                EnsureGiteaReady(started, "başlatma");
                return new ManagedMcpLifecycleResult(
                    operation,
                    "Gitea MCP başlatıldı",
                    started.Detail);

            case ManagedMcpLifecycleOperation.Stop:
                ManagedMcpSessionState.MarkManuallyStopped(GiteaId);
                try
                {
                    var stopped = await GiteaTrayClient.StopAsync(cancellationToken);
                    if (stopped.State != GiteaConnectionState.Offline)
                    {
                        throw new InvalidOperationException(
                            "Gitea MCP zinciri tamamen durmadı.");
                    }

                    return new ManagedMcpLifecycleResult(
                        operation,
                        "Gitea MCP durduruldu",
                        stopped.Detail);
                }
                catch
                {
                    ManagedMcpSessionState.ClearManualStop(GiteaId);
                    throw;
                }

            case ManagedMcpLifecycleOperation.Restart:
                ManagedMcpSessionState.ClearManualStop(GiteaId);
                var restarted = await GiteaTrayClient.RestartAsync(cancellationToken);
                EnsureGiteaReady(restarted, "yeniden başlatma");
                return new ManagedMcpLifecycleResult(
                    operation,
                    "Gitea MCP yeniden başlatıldı",
                    restarted.Detail);

            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static async Task<ManagedMcpLifecycleResult> ExecuteGenericAsync(
        ManagedMcpRegistration registration,
        ManagedMcpLifecycleOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation == ManagedMcpLifecycleOperation.Stop)
        {
            ManagedMcpSessionState.MarkManuallyStopped(registration.Id);
        }
        else
        {
            ManagedMcpSessionState.ClearManualStop(registration.Id);
        }

        try
        {
            var tunnelAssessment =
                ManagedMcpTunnelProvisioningService.Assess(registration);
            var tunnelConfigured =
                tunnelAssessment.Required &&
                tunnelAssessment.HasTunnelId &&
                tunnelAssessment.HasConfig &&
                tunnelAssessment.HasRuntimeCredential;

            if (tunnelConfigured &&
                operation is ManagedMcpLifecycleOperation.Stop
                    or ManagedMcpLifecycleOperation.Restart)
            {
                await ManagedMcpTunnelProvisioningService.DisconnectExistingAsync(
                    registration,
                    cancellationToken);
            }

            var script = BuildGenericLifecycleScript(registration, operation);
            await RunPrivilegedPowerShellAsync(script, cancellationToken);

            if (tunnelAssessment.Required &&
                operation is ManagedMcpLifecycleOperation.Start
                    or ManagedMcpLifecycleOperation.Restart)
            {
                if (!tunnelConfigured)
                {
                    throw new InvalidOperationException(
                        $"{registration.DisplayName} güvenli tünel kurulumu eksik.");
                }

                await ManagedMcpTunnelProvisioningService.ConnectExistingAsync(
                    registration,
                    cancellationToken);
            }

            return new ManagedMcpLifecycleResult(
                operation,
                operation switch
                {
                    ManagedMcpLifecycleOperation.Start =>
                        $"{registration.DisplayName} başlatıldı",
                    ManagedMcpLifecycleOperation.Stop =>
                        $"{registration.DisplayName} durduruldu",
                    _ => $"{registration.DisplayName} yeniden başlatıldı",
                },
                tunnelAssessment.Required
                    ? "Kayıtlı servis/görev zinciri ve güvenli tünel birlikte işlendi."
                    : "Kayıtlı servis ve zamanlanmış görev zinciri işlendi.");
        }
        catch
        {
            if (operation == ManagedMcpLifecycleOperation.Stop)
            {
                ManagedMcpSessionState.ClearManualStop(registration.Id);
            }

            throw;
        }
    }

    private static void EnsureGiteaReady(
        GiteaStatus status,
        string operationName)
    {
        if (status.State != GiteaConnectionState.Running)
        {
            throw new InvalidOperationException(
                $"Gitea MCP {operationName} sonrası hazır olmadı: {status.Detail}");
        }
    }

    private static async Task RunTalvoraServiceCommandAsync(
        string command,
        bool allowAlreadyInRequestedState,
        CancellationToken cancellationToken)
    {
        var systemDirectory =
            Environment.GetFolderPath(Environment.SpecialFolder.System);
        var sc = Path.Combine(systemDirectory, "sc.exe");

        var result = await ProcessRunner.RunAsync(
            sc,
            systemDirectory,
            new[] { command, TalvoraServiceName },
            timeoutSeconds: 45,
            cancellationToken: cancellationToken);

        if (result.ExitCode == 0)
        {
            return;
        }

        var combined = string.Join(
            " ",
            result.StandardOutput,
            result.StandardError);

        if (allowAlreadyInRequestedState &&
            (combined.Contains("1056", StringComparison.OrdinalIgnoreCase) ||
             combined.Contains("1062", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Talvora Windows servisi '{command}' işlemini tamamlayamadı. " +
            "Kurulu sürümün servis yönetim iznini doğrulayın.");
    }

    private static async Task WaitForTalvoraHealthAsync(
        bool expectedHealthy,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var healthy =
                await BusinessTunnelClient.IsLocalMcpHealthyAsync(
                    cancellationToken);

            if (healthy == expectedHealthy)
            {
                return;
            }

            await Task.Delay(400, cancellationToken);
        }

        throw new TimeoutException(
            expectedHealthy
                ? "Talvora servisi zamanında sağlıklı duruma gelmedi."
                : "Talvora servisi zamanında durmadı.");
    }

    private static async Task RunPrivilegedPowerShellAsync(
        string script,
        CancellationToken cancellationToken)
    {
        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(TalvoraMcpUrl),
                TransportMode = HttpTransportMode.StreamableHttp,
                ConnectionTimeout = TimeSpan.FromSeconds(10),
            });

        await using var client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);

        var result = await client.CallToolAsync(
            "talvora_run_powershell",
            new Dictionary<string, object?>
            {
                ["script"] = script,
                ["workingDirectory"] = @"C:\Windows\System32",
                ["timeoutSeconds"] = 120,
            },
            cancellationToken: cancellationToken);

        if (result.IsError is true)
        {
            throw new InvalidOperationException(
                "Talvora MCP yükseltilmiş yaşam döngüsü işlemini tamamlayamadı.");
        }

        if (result.StructuredContent is { } structured &&
            structured.TryGetProperty("exitCode", out var exitCode) &&
            exitCode.GetInt32() != 0)
        {
            throw new InvalidOperationException(
                "Yükseltilmiş yaşam döngüsü komutu başarısız oldu.");
        }
    }

    private static string BuildGenericLifecycleScript(
        ManagedMcpRegistration registration,
        ManagedMcpLifecycleOperation operation)
    {
        var services = registration.Components
            .Where(component =>
                string.Equals(
                    component.Kind,
                    "windows-service",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(component.Name))
            .Select(component => EscapePowerShellLiteral(component.Name!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var tasks = registration.Components
            .Where(component =>
                string.Equals(
                    component.Kind,
                    "scheduled-task",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(component.Name))
            .Select(component => EscapePowerShellLiteral(component.Name!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (services.Length == 0 && tasks.Length == 0)
        {
            throw new InvalidOperationException(
                $"{registration.DisplayName} için yönetilebilir servis veya zamanlanmış görev kaydı yok.");
        }

        static string PsArray(IEnumerable<string> values) =>
            "@(" + string.Join(", ", values.Select(value => $"'{value}'")) + ")";

        var op = operation.ToString().ToLowerInvariant();

        return $$"""
$ErrorActionPreference = 'Stop'
$operation = '{{op}}'
$services = {{PsArray(services)}}
$tasks = {{PsArray(tasks)}}

function Stop-Chain {
    foreach ($taskName in [array]$tasks) {
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($null -ne $task -and $task.State -eq 'Running') {
            Stop-ScheduledTask -TaskName $taskName -ErrorAction Stop
        }
    }

    foreach ($serviceName in [array]$services) {
        $service = Get-Service -Name $serviceName -ErrorAction Stop
        if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
            Stop-Service -Name $serviceName -Force -ErrorAction Stop
            $service.WaitForStatus(
                [ServiceProcess.ServiceControllerStatus]::Stopped,
                [TimeSpan]::FromSeconds(30))
        }
    }
}

function Start-Chain {
    foreach ($serviceName in [array]$services) {
        $service = Get-Service -Name $serviceName -ErrorAction Stop
        if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Running) {
            Start-Service -Name $serviceName -ErrorAction Stop
            $service.WaitForStatus(
                [ServiceProcess.ServiceControllerStatus]::Running,
                [TimeSpan]::FromSeconds(30))
        }
    }

    foreach ($taskName in [array]$tasks) {
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
        if ($task.State -ne 'Running') {
            Start-ScheduledTask -TaskName $taskName -ErrorAction Stop
        }
    }
}

switch ($operation) {
    'start' { Start-Chain }
    'stop' { Stop-Chain }
    'restart' {
        Stop-Chain
        Start-Sleep -Milliseconds 500
        Start-Chain
    }
    default { throw "Unsupported lifecycle operation: $operation" }
}
""";
    }

    private static string EscapePowerShellLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}