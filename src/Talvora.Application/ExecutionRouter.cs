using System.ComponentModel;
using Talvora.Abstractions;
using Talvora.Ipc.Client;
using Talvora.Modules.Processes;
using Talvora.Modules.Shell;

namespace Talvora.Application;

public sealed class ExecutionRouter(
    IShellService shellService,
    IProcessService processService,
    IOperationExecutor executor,
    IBrokerClient brokerClient) : IExecutionRouter
{
    public async ValueTask<TalvoraResult<ShellExecutionResult>> ExecuteShellAsync(
        ShellExecutionRequest request,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return privilege switch
        {
            ExecutionPrivilege.Auto or ExecutionPrivilege.Normal =>
                await executor.ExecuteAsync(
                    "shell.execute",
                    token => shellService.ExecuteAsync(request, token),
                    cancellationToken).ConfigureAwait(false),
            ExecutionPrivilege.Elevated =>
                await ExecuteElevatedShellAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidEnumArgumentException(nameof(privilege), (int)privilege, typeof(ExecutionPrivilege)),
        };
    }

    public async ValueTask<TalvoraResult<ProcessStartResult>> StartProcessAsync(
        StartProcessRequest request,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return privilege switch
        {
            ExecutionPrivilege.Auto or ExecutionPrivilege.Normal =>
                await executor.ExecuteAsync(
                    "process.start",
                    token => processService.StartAsync(request, token),
                    cancellationToken).ConfigureAwait(false),
            ExecutionPrivilege.Elevated =>
                await StartElevatedProcessAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidEnumArgumentException(nameof(privilege), (int)privilege, typeof(ExecutionPrivilege)),
        };
    }

    private async ValueTask<TalvoraResult<ShellExecutionResult>> ExecuteElevatedShellAsync(
        ShellExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var brokerResult = await brokerClient.ExecuteShellAsync(
            new BrokerShellExecutionRequest(
                request.Command,
                request.Shell switch
                {
                    ShellKind.PowerShell => BrokerShellKind.PowerShell,
                    ShellKind.Cmd => BrokerShellKind.Cmd,
                    _ => throw new InvalidEnumArgumentException(nameof(request.Shell), (int)request.Shell, typeof(ShellKind)),
                },
                LoadProfile: request.LoadProfile,
                WorkingDirectory: request.WorkingDirectory,
                Environment: request.Environment,
                Timeout: request.Timeout),
            cancellationToken).ConfigureAwait(false);

        if (!brokerResult.IsSuccess)
        {
            return TalvoraResult.Failure<ShellExecutionResult>(
                brokerResult.Error ?? MissingBrokerError("elevated.shell.execute", "shell operation"));
        }

        if (brokerResult.Value is not { } value)
        {
            return TalvoraResult.Failure<ShellExecutionResult>(
                MissingBrokerError("elevated.shell.execute", "shell operation"));
        }

        if (value.ExitCode is not { } exitCode)
        {
            return TalvoraResult.Failure<ShellExecutionResult>(new TalvoraError(
                "operation_failed",
                "Elevated Broker returned a completed shell operation without an exit code.",
                "elevated.shell.execute"));
        }

        return TalvoraResult.Success(new ShellExecutionResult(
            value.ProcessId,
            exitCode,
            value.StandardOutput,
            value.StandardError,
            value.Duration));
    }

    private async ValueTask<TalvoraResult<ProcessStartResult>> StartElevatedProcessAsync(
        StartProcessRequest request,
        CancellationToken cancellationToken)
    {
        var brokerResult = await brokerClient.ExecuteProcessAsync(
            new BrokerProcessExecutionRequest(
                request.FileName,
                Arguments: request.Arguments,
                WorkingDirectory: request.WorkingDirectory,
                Environment: request.Environment,
                CreateNoWindow: request.CreateNoWindow,
                Mode: BrokerProcessExecutionMode.StartOnly),
            cancellationToken).ConfigureAwait(false);

        if (!brokerResult.IsSuccess)
        {
            return TalvoraResult.Failure<ProcessStartResult>(
                brokerResult.Error ?? MissingBrokerError("elevated.process.execute", "process start"));
        }

        if (brokerResult.Value is not { } value)
        {
            return TalvoraResult.Failure<ProcessStartResult>(
                MissingBrokerError("elevated.process.execute", "process start"));
        }

        return TalvoraResult.Success(new ProcessStartResult(value.ProcessId));
    }

    private static TalvoraError MissingBrokerError(string operation, string description) =>
        new(
            "operation_failed",
            $"Elevated Broker returned a successful {description} without an execution result.",
            operation);
}
