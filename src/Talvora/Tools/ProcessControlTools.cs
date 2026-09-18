using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public sealed record TalvoraProcessInfo(
    int ProcessId,
    string ProcessName,
    int? SessionId,
    string? StartTimeUtc,
    long? WorkingSetBytes,
    string? ExecutablePath);

public sealed record TalvoraProcessListResponse(
    IReadOnlyList<TalvoraProcessInfo> Processes);

public sealed record TalvoraProcessGetResponse(
    bool Found,
    TalvoraProcessInfo? Process);

public sealed record TalvoraProcessKillResponse(
    bool Found,
    bool Exited,
    int ProcessId,
    string? ProcessName,
    bool EntireProcessTree);

[McpServerToolType]
public static class ProcessControlTools
{
    [McpServerTool(
        Name = "talvora_process_list",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraProcessListResponse)),
     Description("List local Windows processes as structured data. Optional query matches process name or PID. No process allow-list is applied.")]
    public static TalvoraProcessListResponse ListProcesses(
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = new List<TalvoraProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string processName;
                try
                {
                    processName = process.ProcessName;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (!Matches(process.Id, processName, query))
                {
                    continue;
                }

                items.Add(ToInfo(process, processName));
            }
        }

        return new TalvoraProcessListResponse(
            items
                .OrderBy(item => item.ProcessId)
                .ToArray());
    }

    [McpServerTool(
        Name = "talvora_process_get",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraProcessGetResponse)),
     Description("Get one local Windows process by PID. Returns found=false when the PID does not exist.")]
    public static TalvoraProcessGetResponse GetProcess(
        int processId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (processId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        using var process = TryGetProcess(processId);
        if (process is null)
        {
            return new TalvoraProcessGetResponse(false, null);
        }

        try
        {
            if (process.HasExited)
            {
                return new TalvoraProcessGetResponse(false, null);
            }

            return new TalvoraProcessGetResponse(true, ToInfo(process));
        }
        catch (InvalidOperationException)
        {
            return new TalvoraProcessGetResponse(false, null);
        }
    }

    [McpServerTool(
        Name = "talvora_process_kill",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraProcessKillResponse)),
     Description("Terminate a local Windows process by PID and optionally its entire process tree. Missing/exited PIDs return found=false. No process allow-list is applied.")]
    public static async Task<TalvoraProcessKillResponse> KillProcess(
        int processId,
        bool entireProcessTree = true,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        if (processId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        if (timeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        using var process = TryGetProcess(processId);
        if (process is null)
        {
            return new TalvoraProcessKillResponse(false, true, processId, null, entireProcessTree);
        }

        string? processName = null;
        try
        {
            processName = process.ProcessName;
            if (process.HasExited)
            {
                return new TalvoraProcessKillResponse(false, true, processId, processName, entireProcessTree);
            }

            process.Kill(entireProcessTree);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return new TalvoraProcessKillResponse(true, false, processId, processName, entireProcessTree);
            }

            return new TalvoraProcessKillResponse(true, true, processId, processName, entireProcessTree);
        }
        catch (InvalidOperationException)
        {
            return new TalvoraProcessKillResponse(false, true, processId, processName, entireProcessTree);
        }
    }

    private static Process? TryGetProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool Matches(int processId, string processName, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var normalized = query.Trim();
        return processName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
            || processId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                .Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static TalvoraProcessInfo ToInfo(Process process, string? knownName = null)
    {
        var processName = knownName ?? SafeRead(() => process.ProcessName) ?? string.Empty;
        var sessionId = SafeReadNullable(() => process.SessionId);
        var startTime = SafeRead(() => process.StartTime.ToUniversalTime().ToString("O"));
        var workingSet = SafeReadNullable(() => process.WorkingSet64);
        var executablePath = SafeRead(() => process.MainModule?.FileName);

        return new TalvoraProcessInfo(
            process.Id,
            processName,
            sessionId,
            startTime,
            workingSet,
            executablePath);
    }

    private static string? SafeRead(Func<string?> reader)
    {
        try
        {
            return reader();
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static T? SafeReadNullable<T>(Func<T> reader)
        where T : struct
    {
        try
        {
            return reader();
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
