using System.Diagnostics;

namespace Talvora.Modules.Shell;

public sealed class ShellService : IShellService
{
    public async ValueTask<ShellExecutionResult> ExecuteAsync(
        ShellExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var spec = ShellLaunchSpecFactory.Create(request);
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
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

        using var process = new Process { StartInfo = startInfo };
        var started = Stopwatch.GetTimestamp();

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {spec.FileName}.");
        }

        using var timeoutSource = request.Timeout is { } timeout
            ? new CancellationTokenSource(timeout)
            : null;
        using var linkedSource = timeoutSource is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var effectiveToken = linkedSource?.Token ?? cancellationToken;
        using var killRegistration = effectiveToken.Register(() => TryKill(process));

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(effectiveToken);
            var stderrTask = process.StandardError.ReadToEndAsync(effectiveToken);

            await process.WaitForExitAsync(effectiveToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return new ShellExecutionResult(
                process.Id,
                process.ExitCode,
                stdout,
                stderr,
                Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException) when (
            timeoutSource?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"Command exceeded the configured timeout of {request.Timeout}.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
