using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraWindowsServiceInfo(
    string ServiceName,
    string DisplayName,
    string Status,
    string StartType,
    string ServiceType,
    bool CanStop,
    bool CanPauseAndContinue,
    bool CanShutdown);

public sealed record TalvoraServiceListResponse(
    IReadOnlyList<TalvoraWindowsServiceInfo> Services);

public sealed record TalvoraServiceGetResponse(
    bool Found,
    TalvoraWindowsServiceInfo? Service);

public sealed record TalvoraServiceActionResponse(
    bool Found,
    bool Changed,
    string ServiceName,
    string BeforeStatus,
    string AfterStatus);

[SupportedOSPlatform("windows")]
[McpServerToolType]
public static class ServiceTools
{
    private const string TalvoraServiceName = "Talvora";
    private const int SelfLifecycleDelayMilliseconds = 2_000;

    [McpServerTool(
        Name = "talvora_service_list",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraServiceListResponse)),
     Description("List local Windows services as structured data. Optional query filters service/display names. Set includeDevices=true to include driver services.")]
    public static TalvoraServiceListResponse ListServices(
        string? query = null,
        bool includeDevices = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var controllers = ServiceController.GetServices().AsEnumerable();
        if (includeDevices)
        {
            controllers = controllers.Concat(ServiceController.GetDevices());
        }

        var items = new List<TalvoraWindowsServiceInfo>();
        foreach (var service in controllers)
        {
            using (service)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(query) &&
                    !service.ServiceName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !service.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                items.Add(ToInfo(service));
            }
        }

        return new TalvoraServiceListResponse(
            items.OrderBy(item => item.ServiceName, StringComparer.Ordinal).ToArray());
    }

    [McpServerTool(
        Name = "talvora_service_get",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraServiceGetResponse)),
     Description("Get one local Windows service by service name or exact display name. Missing services return found=false.")]
    public static TalvoraServiceGetResponse GetService(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var service = FindService(serviceName);
        return service is null
            ? new TalvoraServiceGetResponse(false, null)
            : new TalvoraServiceGetResponse(true, ToInfo(service));
    }

    [McpServerTool(
        Name = "talvora_service_start",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraServiceActionResponse)),
     Description("Start or continue a local Windows service and wait for Running. Missing services return found=false. No service-name allow-list is applied.")]
    public static async Task<TalvoraServiceActionResponse> StartService(
        string serviceName,
        string[]? arguments = null,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        ValidateTimeout(timeoutSeconds);
        using var service = FindService(serviceName);
        if (service is null)
        {
            return MissingAction(serviceName);
        }

        service.Refresh();
        var before = service.Status;
        if (before == ServiceControllerStatus.Running)
        {
            return Action(false, service, before);
        }

        var deadline = Stopwatch.StartNew();

        if (before == ServiceControllerStatus.StopPending)
        {
            await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, Remaining(timeoutSeconds, deadline), cancellationToken);
        }
        else if (before == ServiceControllerStatus.Paused)
        {
            service.Continue();
            await WaitForStatusAsync(service, ServiceControllerStatus.Running, Remaining(timeoutSeconds, deadline), cancellationToken);
            return Action(true, service, before);
        }
        else if (before == ServiceControllerStatus.PausePending)
        {
            await WaitForStatusAsync(service, ServiceControllerStatus.Paused, Remaining(timeoutSeconds, deadline), cancellationToken);
            service.Continue();
            await WaitForStatusAsync(service, ServiceControllerStatus.Running, Remaining(timeoutSeconds, deadline), cancellationToken);
            return Action(true, service, before);
        }
        else if (before is ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending)
        {
            await WaitForStatusAsync(service, ServiceControllerStatus.Running, Remaining(timeoutSeconds, deadline), cancellationToken);
            return Action(true, service, before);
        }

        service.Refresh();
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            throw new InvalidOperationException($"Service '{service.ServiceName}' cannot be started from status {service.Status}.");
        }

        if (arguments is { Length: > 0 })
        {
            service.Start(arguments);
        }
        else
        {
            service.Start();
        }

        await WaitForStatusAsync(service, ServiceControllerStatus.Running, Remaining(timeoutSeconds, deadline), cancellationToken);
        return Action(true, service, before);
    }

    [McpServerTool(
        Name = "talvora_service_stop",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraServiceActionResponse)),
     Description("Stop a local Windows service and wait for Stopped. When targeting Talvora itself, the stop is handed to a detached LocalSystem helper and the tool returns afterStatus=StopScheduled before the MCP host exits. Missing services return found=false. No service-name allow-list is applied.")]
    public static async Task<TalvoraServiceActionResponse> StopService(
        string serviceName,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        ValidateTimeout(timeoutSeconds);
        using var service = FindService(serviceName);
        if (service is null)
        {
            return MissingAction(serviceName);
        }

        service.Refresh();
        var before = service.Status;
        if (before == ServiceControllerStatus.Stopped)
        {
            return Action(false, service, before);
        }

        if (IsTalvoraSelf(service))
        {
            ScheduleSelfLifecycle(
                restart: false,
                timeoutSeconds);
            return new TalvoraServiceActionResponse(
                true,
                true,
                service.ServiceName,
                before.ToString(),
                "StopScheduled");
        }

        var deadline = Stopwatch.StartNew();

        if (before == ServiceControllerStatus.StopPending)
        {
            await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, Remaining(timeoutSeconds, deadline), cancellationToken);
            return Action(true, service, before);
        }

        if (before is ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending or ServiceControllerStatus.PausePending)
        {
            await WaitForStableStatusAsync(service, Remaining(timeoutSeconds, deadline), cancellationToken);
            service.Refresh();
            if (service.Status == ServiceControllerStatus.Stopped)
            {
                return Action(true, service, before);
            }
        }

        service.Refresh();
        if (!service.CanStop)
        {
            throw new InvalidOperationException($"Service '{service.ServiceName}' cannot be stopped from status {service.Status}.");
        }

        service.Stop();
        await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, Remaining(timeoutSeconds, deadline), cancellationToken);
        return Action(true, service, before);
    }

    [McpServerTool(
        Name = "talvora_service_restart",
        Destructive = true,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraServiceActionResponse)),
     Description("Restart a local Windows service and wait for Running. When targeting Talvora itself, the restart is handed to a detached LocalSystem helper and the tool returns afterStatus=RestartScheduled before the MCP host exits. A stopped service is started. Missing services return found=false. No service-name allow-list is applied.")]
    public static async Task<TalvoraServiceActionResponse> RestartService(
        string serviceName,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        ValidateTimeout(timeoutSeconds);
        using var service = FindService(serviceName);
        if (service is null)
        {
            return MissingAction(serviceName);
        }

        service.Refresh();
        var before = service.Status;
        var timer = Stopwatch.StartNew();

        if (IsTalvoraSelf(service))
        {
            ScheduleSelfLifecycle(
                restart: true,
                timeoutSeconds);
            return new TalvoraServiceActionResponse(
                true,
                true,
                service.ServiceName,
                before.ToString(),
                "RestartScheduled");
        }

        if (before != ServiceControllerStatus.Stopped)
        {
            if (before == ServiceControllerStatus.StopPending)
            {
                await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, Remaining(timeoutSeconds, timer), cancellationToken);
            }
            else
            {
                if (before is ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending or ServiceControllerStatus.PausePending)
                {
                    await WaitForStableStatusAsync(service, Remaining(timeoutSeconds, timer), cancellationToken);
                    service.Refresh();
                }

                if (service.Status != ServiceControllerStatus.Stopped)
                {
                    if (!service.CanStop)
                    {
                        throw new InvalidOperationException($"Service '{service.ServiceName}' cannot be stopped from status {service.Status}.");
                    }

                    service.Stop();
                    await WaitForStatusAsync(service, ServiceControllerStatus.Stopped, Remaining(timeoutSeconds, timer), cancellationToken);
                }
            }
        }

        service.Start();
        await WaitForStatusAsync(service, ServiceControllerStatus.Running, Remaining(timeoutSeconds, timer), cancellationToken);
        return Action(true, service, before);
    }

    private static TalvoraWindowsServiceInfo ToInfo(ServiceController service)
    {
        service.Refresh();
        return new TalvoraWindowsServiceInfo(
            service.ServiceName,
            service.DisplayName,
            service.Status.ToString(),
            service.StartType.ToString(),
            service.ServiceType.ToString(),
            service.CanStop,
            service.CanPauseAndContinue,
            service.CanShutdown);
    }

    private static ServiceController? FindService(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ServiceController? matched = null;

        foreach (var service in ServiceController.GetServices())
        {
            if (matched is null &&
                (string.Equals(service.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(service.DisplayName, serviceName, StringComparison.OrdinalIgnoreCase)))
            {
                matched = service;
            }
            else
            {
                service.Dispose();
            }
        }

        return matched;
    }

    private static async Task WaitForStableStatusAsync(
        ServiceController service,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Refresh();

            if (service.Status is not (
                ServiceControllerStatus.StartPending or
                ServiceControllerStatus.StopPending or
                ServiceControllerStatus.ContinuePending or
                ServiceControllerStatus.PausePending))
            {
                return;
            }

            if (started.Elapsed >= timeout)
            {
                throw new System.TimeoutException($"Service '{service.ServiceName}' did not reach a stable status within {timeout.TotalSeconds:F1} seconds.");
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private static async Task WaitForStatusAsync(
        ServiceController service,
        ServiceControllerStatus desiredStatus,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Refresh();
            if (service.Status == desiredStatus)
            {
                return;
            }

            if (started.Elapsed >= timeout)
            {
                throw new System.TimeoutException(
                    $"Service '{service.ServiceName}' did not reach {desiredStatus} within {timeout.TotalSeconds:F1} seconds. Current status: {service.Status}.");
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private static TalvoraServiceActionResponse Action(
        bool changed,
        ServiceController service,
        ServiceControllerStatus before)
    {
        service.Refresh();
        return new TalvoraServiceActionResponse(
            true,
            changed,
            service.ServiceName,
            before.ToString(),
            service.Status.ToString());
    }

    private static TalvoraServiceActionResponse MissingAction(string serviceName) =>
        new(false, false, serviceName, "Missing", "Missing");

    private static bool IsTalvoraSelf(ServiceController service) =>
        string.Equals(
            service.ServiceName,
            TalvoraServiceName,
            StringComparison.OrdinalIgnoreCase);

    private static void ScheduleSelfLifecycle(
        bool restart,
        int timeoutSeconds)
    {
        var systemDirectory =
            Environment.GetFolderPath(Environment.SpecialFolder.System);
        var powershell =
            Path.Combine(
                systemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
        if (!File.Exists(powershell))
        {
            throw new FileNotFoundException(
                "Windows PowerShell could not be located for Talvora self-lifecycle handoff.",
                powershell);
        }

        var effectiveTimeout =
            Math.Max(
                5,
                timeoutSeconds);
        var script =
            "$ErrorActionPreference='Stop';" +
            $"Start-Sleep -Milliseconds {SelfLifecycleDelayMilliseconds};" +
            $"$s=Get-Service -Name '{TalvoraServiceName}' -ErrorAction Stop;" +
            "if($s.Status -ne 'Stopped'){" +
            $"Stop-Service -Name '{TalvoraServiceName}' -Force -ErrorAction Stop;" +
            $"$s.WaitForStatus('Stopped',[TimeSpan]::FromSeconds({effectiveTimeout}));" +
            "}" +
            (restart
                ? $"Start-Service -Name '{TalvoraServiceName}' -ErrorAction Stop;" +
                  $"(Get-Service -Name '{TalvoraServiceName}' -ErrorAction Stop).WaitForStatus('Running',[TimeSpan]::FromSeconds({effectiveTimeout}));"
                : string.Empty);

        var startInfo =
            new ProcessStartInfo
            {
                FileName = powershell,
                WorkingDirectory = systemDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        using var helper =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Talvora self-lifecycle helper could not be started.");
    }

    private static void ValidateTimeout(int timeoutSeconds)
    {
        if (timeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }
    }

    private static TimeSpan Remaining(int timeoutSeconds, Stopwatch stopwatch)
    {
        var remaining = TimeSpan.FromSeconds(timeoutSeconds) - stopwatch.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            throw new System.TimeoutException($"Service operation exceeded {timeoutSeconds} seconds.");
        }

        return remaining;
    }
}
