using Talvora.Abstractions;
using Talvora.Modules.Processes;
using Talvora.Modules.Shell;

namespace Talvora.Application;

public interface IExecutionRouter
{
    ValueTask<TalvoraResult<ShellExecutionResult>> ExecuteShellAsync(
        ShellExecutionRequest request,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default);

    ValueTask<TalvoraResult<ProcessStartResult>> StartProcessAsync(
        StartProcessRequest request,
        ExecutionPrivilege privilege,
        CancellationToken cancellationToken = default);
}
