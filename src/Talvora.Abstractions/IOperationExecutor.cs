namespace Talvora.Abstractions;

public interface IOperationExecutor
{
    ValueTask<TalvoraResult<T>> ExecuteAsync<T>(
        string operation,
        Func<CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken = default);
}
