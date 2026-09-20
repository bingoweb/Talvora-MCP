using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
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
        using var operationLease =
            ManagedMcpOperationCoordinator.TryAcquire(TalvoraId)
            ?? throw new ManagedMcpOperationInProgressException(TalvoraId);

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
        using var operationLease =
            ManagedMcpOperationCoordinator.TryAcquire(registration.Id)
            ?? throw new ManagedMcpOperationInProgressException(registration.Id);

        ManagedMcpProtocolProbeService.InvalidateCache(registration.Id);
        ManagedMcpTunnelProvisioningService.InvalidateRuntimeStatusCache(registration.Id);
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
            var hasLocalLifecycleComponents =
                registration.Components.Any(component =>
                    string.Equals(
                        component.Kind,
                        "windows-service",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        component.Kind,
                        "scheduled-task",
                        StringComparison.OrdinalIgnoreCase));

            if (tunnelConfigured &&
                operation is ManagedMcpLifecycleOperation.Stop
                    or ManagedMcpLifecycleOperation.Restart)
            {
                await ManagedMcpTunnelProvisioningService.DisconnectExistingAsync(
                    registration,
                    cancellationToken);
            }

            if (hasLocalLifecycleComponents &&
                operation == ManagedMcpLifecycleOperation.Restart)
            {
                await RunGenericLifecyclePowerShellAsync(
                    registration,
                    BuildGenericLifecycleScript(
                        registration,
                        ManagedMcpLifecycleOperation.Stop),
                    cancellationToken);
                await EnsureRegisteredRuntimeStoppedAsync(
                    registration,
                    cancellationToken);
                await RunGenericLifecyclePowerShellAsync(
                    registration,
                    BuildGenericLifecycleScript(
                        registration,
                        ManagedMcpLifecycleOperation.Start),
                    cancellationToken);
            }
            else if (hasLocalLifecycleComponents)
            {
                await RunGenericLifecyclePowerShellAsync(
                    registration,
                    BuildGenericLifecycleScript(registration, operation),
                    cancellationToken);

                if (operation == ManagedMcpLifecycleOperation.Stop)
                {
                    await EnsureRegisteredRuntimeStoppedAsync(
                        registration,
                        cancellationToken);
                }
            }

            if (registration.ProtocolProbe is not null &&
                operation is ManagedMcpLifecycleOperation.Start
                    or ManagedMcpLifecycleOperation.Restart)
            {
                var localProbe = await ManagedMcpProtocolProbeService.WaitUntilReadyAsync(
                    registration,
                    runBrowserSmoke: false,
                    timeout: TimeSpan.FromSeconds(60),
                    cancellationToken);

                if (!localProbe.Ready)
                {
                    throw new InvalidOperationException(
                        $"{registration.DisplayName} yerel MCP endpoint'i hazır olmadı: {localProbe.Detail}");
                }
            }

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

            if (registration.ProtocolProbe is not null &&
                operation is ManagedMcpLifecycleOperation.Start
                    or ManagedMcpLifecycleOperation.Restart)
            {
                var probe = await ManagedMcpProtocolProbeService.WaitUntilReadyAsync(
                    registration,
                    runBrowserSmoke: true,
                    timeout: TimeSpan.FromSeconds(90),
                    cancellationToken);

                if (!probe.Ready || !probe.BrowserSmokePassed)
                {
                    throw new InvalidOperationException(
                        $"{registration.DisplayName} gerçek browser smoke doğrulamasını geçemedi: {probe.Detail}");
                }
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

    private static async Task RunGenericLifecyclePowerShellAsync(
        ManagedMcpRegistration registration,
        string script,
        CancellationToken cancellationToken)
    {
        var requiresPrivilege = registration.Components.Any(component =>
            string.Equals(
                component.Kind,
                "windows-service",
                StringComparison.OrdinalIgnoreCase));

        if (requiresPrivilege)
        {
            await RunPrivilegedPowerShellAsync(
                script,
                cancellationToken);
            return;
        }

        var pwsh = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "PowerShell",
            "7",
            "pwsh.exe");

        var executable = File.Exists(pwsh)
            ? pwsh
            : Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "PowerShell bulunamadı.",
                executable);
        }

        var result = await ProcessRunner.RunAsync(
            executable,
            Environment.GetFolderPath(
                Environment.SpecialFolder.System),
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                script,
            ],
            timeoutSeconds: 120,
            cancellationToken: cancellationToken);

        if (result.TimedOut || result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;

            throw new InvalidOperationException(
                $"Current-user yaşam döngüsü komutu başarısız oldu. Exit={result.ExitCode}; {error.Trim()}");
        }
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

    private static async Task EnsureRegisteredRuntimeStoppedAsync(
        ManagedMcpRegistration registration,
        CancellationToken cancellationToken)
    {
        var statePath =
            registration.ProtocolProbe?.RuntimeGenerationStatePath;

        if (!string.IsNullOrWhiteSpace(statePath) &&
            File.Exists(statePath))
        {
            try
            {
                using var document = JsonDocument.Parse(
                    await File.ReadAllTextAsync(
                        statePath,
                        cancellationToken));

                if (document.RootElement.TryGetProperty(
                        "launcherPid",
                        out var launcherPidElement) &&
                    launcherPidElement.ValueKind ==
                        JsonValueKind.Number &&
                    launcherPidElement.TryGetInt32(
                        out var launcherPid) &&
                    launcherPid > 0)
                {
                    try
                    {
                        using var process =
                            Process.GetProcessById(launcherPid);

                        if (!process.HasExited)
                        {
                            var identityMatches = true;
                            if (document.RootElement.TryGetProperty(
                                    "startedAtUtc",
                                    out var startedAtElement) &&
                                startedAtElement.ValueKind ==
                                    JsonValueKind.String &&
                                DateTimeOffset.TryParse(
                                    startedAtElement.GetString(),
                                    out var expectedStartedAt))
                            {
                                var actualStartedAt =
                                    new DateTimeOffset(
                                        process.StartTime
                                            .ToUniversalTime(),
                                        TimeSpan.Zero);

                                identityMatches = Math.Abs(
                                    (actualStartedAt -
                                     expectedStartedAt.ToUniversalTime())
                                    .TotalMinutes) <= 2;
                            }

                            if (!identityMatches)
                            {
                                throw new InvalidOperationException(
                                    $"{registration.DisplayName} launcher PID kaydı güncel process ile eşleşmiyor.");
                            }

                            process.Kill(
                                entireProcessTree: true);
                            await process
                                .WaitForExitAsync(
                                    cancellationToken)
                                .WaitAsync(
                                    TimeSpan.FromSeconds(10),
                                    cancellationToken);
                        }
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (InvalidOperationException ex) when (
                        ex.Message.Contains(
                            "has exited",
                            StringComparison.OrdinalIgnoreCase))
                    {
                    }
                }
            }
            catch (Exception ex) when (
                ex is IOException or
                JsonException or
                UnauthorizedAccessException)
            {
                TrayLog.Write(
                    "Managed MCP runtime state could not be read during stop verification",
                    ex);
            }
        }

        var endpointUris = new List<Uri>();
        if (Uri.TryCreate(
                registration.Endpoint,
                UriKind.Absolute,
                out var publicEndpoint))
        {
            endpointUris.Add(publicEndpoint);
        }

        if (Uri.TryCreate(
                registration.ProtocolProbe?.BackendEndpoint,
                UriKind.Absolute,
                out var backendEndpoint))
        {
            endpointUris.Add(backendEndpoint);
        }

        if (endpointUris.Count == 0)
        {
            return;
        }

        var deadline =
            DateTimeOffset.UtcNow.AddSeconds(15);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var anyOpen = false;
            foreach (var endpoint in endpointUris
                .DistinctBy(
                    uri => $"{uri.Host}:{uri.Port}",
                    StringComparer.OrdinalIgnoreCase))
            {
                if (await IsTcpEndpointOpenAsync(
                        endpoint,
                        cancellationToken))
                {
                    anyOpen = true;
                    break;
                }
            }

            if (!anyOpen)
            {
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(250),
                cancellationToken);
        }

        throw new TimeoutException(
            $"{registration.DisplayName} durdurulduktan sonra MCP listener'ları kapanmadı.");
    }

    private static async Task<bool> IsTcpEndpointOpenAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();

        try
        {
            await client.ConnectAsync(
                    endpoint.Host,
                    endpoint.Port,
                    cancellationToken)
                .AsTask()
                .WaitAsync(
                    TimeSpan.FromMilliseconds(500),
                    cancellationToken);
            return client.Connected;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return false;
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

        var processMarkers = registration.DiscoveryHints
            .Where(hint =>
                string.Equals(
                    hint.Kind,
                    "process-match",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    hint.Kind,
                    "profile-path",
                    StringComparison.OrdinalIgnoreCase))
            .Select(hint => EscapePowerShellLiteral(hint.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (services.Length == 0 && tasks.Length == 0)
        {
            throw new InvalidOperationException(
                $"{registration.DisplayName} için yönetilebilir servis veya zamanlanmış görev kaydı yok.");
        }

        static string PsArray(IEnumerable<string> values) =>
            "@(" +
            string.Join(
                ", ",
                values.Select(value => $"'{value}'")) +
            ")";

        var op = operation
            .ToString()
            .ToLowerInvariant();

        var script = new System.Text.StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"$operation = '{op}'");
        script.AppendLine($"$services = {PsArray(services)}");
        script.AppendLine($"$tasks = {PsArray(tasks)}");
        script.AppendLine(
            $"$processMarkers = {PsArray(processMarkers)}");
        script.AppendLine();
        script.AppendLine("function Stop-OwnedProcesses {");
        script.AppendLine(
            "    if (@($processMarkers).Count -eq 0) { return }");
        script.AppendLine(
            "    $owned = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {");
        script.AppendLine(
            "        if ($_.ProcessId -eq $PID -or [string]::IsNullOrWhiteSpace([string]$_.CommandLine)) { return $false }");
        script.AppendLine(
            "        foreach ($marker in [array]$processMarkers) {");
        script.AppendLine(
            "            if (([string]$_.CommandLine).IndexOf([string]$marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }");
        script.AppendLine("        }");
        script.AppendLine("        return $false");
        script.AppendLine("    }");
        script.AppendLine("    foreach ($process in @($owned)) {");
        script.AppendLine(
            "        Stop-Process -Id ([int]$process.ProcessId) -Force -ErrorAction SilentlyContinue");
        script.AppendLine("    }");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("function Stop-Chain {");
        script.AppendLine("    foreach ($taskName in [array]$tasks) {");
        script.AppendLine(
            "        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue");
        script.AppendLine(
            "        if ($null -ne $task -and $task.State -eq 'Running') {");
        script.AppendLine(
            "            Stop-ScheduledTask -TaskName $taskName -ErrorAction Stop");
        script.AppendLine("        }");
        script.AppendLine("    }");
        script.AppendLine(
            "    foreach ($serviceName in [array]$services) {");
        script.AppendLine(
            "        $service = Get-Service -Name $serviceName -ErrorAction Stop");
        script.AppendLine(
            "        if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {");
        script.AppendLine(
            "            Stop-Service -Name $serviceName -Force -ErrorAction Stop");
        script.AppendLine(
            "            $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))");
        script.AppendLine("        }");
        script.AppendLine("    }");
        script.AppendLine("    Start-Sleep -Milliseconds 350");
        script.AppendLine("    Stop-OwnedProcesses");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("function Start-Chain {");
        script.AppendLine(
            "    foreach ($serviceName in [array]$services) {");
        script.AppendLine(
            "        $service = Get-Service -Name $serviceName -ErrorAction Stop");
        script.AppendLine(
            "        if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Running) {");
        script.AppendLine(
            "            Start-Service -Name $serviceName -ErrorAction Stop");
        script.AppendLine(
            "            $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))");
        script.AppendLine("        }");
        script.AppendLine("    }");
        script.AppendLine("    foreach ($taskName in [array]$tasks) {");
        script.AppendLine(
            "        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop");
        script.AppendLine(
            "        if ($task.State -ne 'Running') {");
        script.AppendLine(
            "            Start-ScheduledTask -TaskName $taskName -ErrorAction Stop");
        script.AppendLine("        }");
        script.AppendLine("    }");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("switch ($operation) {");
        script.AppendLine("    'start' { Start-Chain }");
        script.AppendLine("    'stop' { Stop-Chain }");
        script.AppendLine("    'restart' {");
        script.AppendLine("        Stop-Chain");
        script.AppendLine("        Start-Sleep -Milliseconds 500");
        script.AppendLine("        Start-Chain");
        script.AppendLine("    }");
        script.AppendLine(
            "    default { throw \"Unsupported lifecycle operation: $operation\" }");
        script.AppendLine("}");

        return script.ToString();
    }
    private static string EscapePowerShellLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}