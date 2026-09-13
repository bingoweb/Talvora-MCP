using System.ComponentModel;
using System.Diagnostics;
using Talvora.Ipc.Contracts;
using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.ElevatedBroker;

public sealed class ElevatedOperationExecutor(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ElevatedOperationResponse> ExecuteAsync(
        ElevatedOperationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ProtocolVersion != BrokerProtocol.CurrentVersion)
        {
            return Failure(
                request,
                "protocol_mismatch",
                $"Unsupported broker protocol version {request.ProtocolVersion}. Expected {BrokerProtocol.CurrentVersion}.",
                "elevated.execute");
        }

        if (string.IsNullOrWhiteSpace(request.OperationId))
        {
            return Failure(request, "invalid_input", "OperationId is required.", "elevated.execute");
        }

        if (request.OperationCase != ElevatedOperationRequest.OperationOneofCase.Shell)
        {
            return Failure(
                request,
                "unsupported_operation",
                $"Elevated operation '{request.OperationCase}' is not supported yet.",
                "elevated.execute");
        }

        try
        {
            return await ExecuteShellAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                request,
                "operation_cancelled",
                "The elevated operation was cancelled.",
                "elevated.shell.execute");
        }
        catch (TimeoutException exception)
        {
            return Failure(
                request,
                "timeout",
                exception.Message,
                "elevated.shell.execute",
                retryable: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(
                request,
                "access_denied",
                exception.Message,
                "elevated.shell.execute",
                nativeCode: exception.HResult);
        }
        catch (FileNotFoundException exception)
        {
            return Failure(
                request,
                "not_found",
                exception.Message,
                "elevated.shell.execute",
                nativeCode: exception.HResult);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Failure(
                request,
                "not_found",
                exception.Message,
                "elevated.shell.execute",
                nativeCode: exception.HResult);
        }
        catch (ArgumentException exception)
        {
            return Failure(
                request,
                "invalid_input",
                exception.Message,
                "elevated.shell.execute",
                nativeCode: exception.HResult);
        }
        catch (Win32Exception exception)
        {
            return Failure(
                request,
                "native_error",
                exception.Message,
                "elevated.shell.execute",
                nativeCode: exception.NativeErrorCode);
        }
        catch (IOException exception)
        {
            return Failure(
                request,
                "io_error",
                exception.Message,
                "elevated.shell.execute",
                nativeCode: exception.HResult,
                retryable: true);
        }
        catch (Exception exception)
        {
            return Failure(
                request,
                "operation_failed",
                exception.Message,
                "elevated.shell.execute",
                nativeCode: exception.HResult);
        }
    }

    private async Task<ElevatedOperationResponse> ExecuteShellAsync(
        ElevatedOperationRequest request,
        CancellationToken cancellationToken)
    {
        var shell = request.Shell;
        ArgumentException.ThrowIfNullOrWhiteSpace(shell.Command);

        var startInfo = CreateShellStartInfo(shell);
        using var process = new Process { StartInfo = startInfo };
        var started = _timeProvider.GetTimestamp();

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");
        }

        using var timeoutSource = request.TimeoutMilliseconds > 0
            ? new CancellationTokenSource(TimeSpan.FromMilliseconds(request.TimeoutMilliseconds))
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

            return new ElevatedOperationResponse
            {
                OperationId = request.OperationId,
                ProtocolVersion = BrokerProtocol.CurrentVersion,
                Success = true,
                Execution = new ElevatedExecutionResult
                {
                    ProcessId = process.Id,
                    ExitCode = process.ExitCode,
                    Stdout = stdout,
                    Stderr = stderr,
                    DurationMilliseconds = (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds,
                },
            };
        }
        catch (OperationCanceledException) when (
            timeoutSource?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"Elevated command exceeded the configured timeout of {request.TimeoutMilliseconds} ms.");
        }
    }

    private static ProcessStartInfo CreateShellStartInfo(ShellExecutionOperation shell)
    {
        var startInfo = shell.Shell switch
        {
            ElevatedShellKind.Powershell => new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            ElevatedShellKind.Cmd => new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(shell), shell.Shell, "Unsupported elevated shell."),
        };

        if (shell.Shell == ElevatedShellKind.Powershell)
        {
            startInfo.ArgumentList.Add("-NoLogo");
            if (!shell.LoadProfile)
            {
                startInfo.ArgumentList.Add("-NoProfile");
            }

            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(shell.Command);
        }
        else
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(shell.Command);
        }

        if (!string.IsNullOrWhiteSpace(shell.WorkingDirectory))
        {
            startInfo.WorkingDirectory = Path.GetFullPath(shell.WorkingDirectory);
        }

        ApplyEnvironment(startInfo, shell.Environment);
        return startInfo;
    }

    private static void ApplyEnvironment(
        ProcessStartInfo startInfo,
        IEnumerable<EnvironmentVariable> environment)
    {
        foreach (var variable in environment)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(variable.Name);
            if (variable.HasValue)
            {
                startInfo.Environment[variable.Name] = variable.Value;
            }
            else
            {
                startInfo.Environment.Remove(variable.Name);
            }
        }
    }

    private static ElevatedOperationResponse Failure(
        ElevatedOperationRequest request,
        string code,
        string message,
        string operation,
        int? nativeCode = null,
        bool retryable = false)
    {
        var error = new TalvoraErrorContract
        {
            Code = code,
            Message = message,
            Operation = operation,
            Retryable = retryable,
        };

        if (nativeCode is { } value)
        {
            error.NativeCode = value;
        }

        return new ElevatedOperationResponse
        {
            OperationId = request.OperationId,
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            Success = false,
            Error = error,
        };
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
        catch (Win32Exception)
        {
        }
    }
}
