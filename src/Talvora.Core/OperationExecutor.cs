using System.Diagnostics;
using Talvora.Abstractions;

namespace Talvora.Core;

public sealed class OperationExecutor(IErrorMapper errorMapper) : IOperationExecutor
{
    public static readonly ActivitySource ActivitySource = new("Talvora.Core");

    public async ValueTask<TalvoraResult<T>> ExecuteAsync<T>(
        string operation,
        Func<CancellationToken, ValueTask<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(action);

        using var activity = ActivitySource.StartActivity(operation, ActivityKind.Internal);
        activity?.SetTag("talvora.operation", operation);

        try
        {
            var value = await action(cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return TalvoraResult.Success(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Operation cancelled.");
            return TalvoraResult.Failure<T>(new TalvoraError(
                "operation_cancelled",
                "The operation was cancelled.",
                operation));
        }
        catch (Exception exception)
        {
            var error = errorMapper.Map(operation, exception);
            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            activity?.SetTag("talvora.error_code", error.Code);
            activity?.SetTag("talvora.native_code", error.NativeCode);
            return TalvoraResult.Failure<T>(error);
        }
    }
}
