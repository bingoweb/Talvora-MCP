using System.Diagnostics;

namespace Talvora.Modules.Processes;

public sealed class ProcessService : IProcessService
{
    public ValueTask<IReadOnlyList<ProcessSnapshot>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshots = Process.GetProcesses()
            .Select(CreateSnapshot)
            .OrderBy(snapshot => snapshot.Id)
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<ProcessSnapshot>>(snapshots);
    }

    public ValueTask<ProcessStartResult> StartAsync(
        StartProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = request.CreateNoWindow,
        };

        if (request.Arguments is not null)
        {
            foreach (var argument in request.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = Path.GetFullPath(request.WorkingDirectory);
        }

        if (request.Environment is not null)
        {
            foreach (var (name, value) in request.Environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {request.FileName}.");

        return ValueTask.FromResult(new ProcessStartResult(process.Id));
    }

    public async ValueTask StopAsync(
        int processId,
        bool entireProcessTree = true,
        CancellationToken cancellationToken = default)
    {
        using var process = Process.GetProcessById(processId);
        process.Kill(entireProcessTree);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ProcessSnapshot CreateSnapshot(Process process)
    {
        using (process)
        {
            string? executablePath = null;
            try
            {
                executablePath = process.MainModule?.FileName;
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            return new ProcessSnapshot(process.Id, process.ProcessName, executablePath);
        }
    }
}
