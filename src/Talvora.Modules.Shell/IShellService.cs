namespace Talvora.Modules.Shell;

public interface IShellService
{
    ValueTask<ShellExecutionResult> ExecuteAsync(
        ShellExecutionRequest request,
        CancellationToken cancellationToken = default);
}
