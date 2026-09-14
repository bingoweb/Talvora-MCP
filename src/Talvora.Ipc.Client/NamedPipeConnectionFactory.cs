using System.IO.Pipes;
using System.Net.Http;
using System.Security.Principal;

namespace Talvora.Ipc.Client;

internal sealed class NamedPipeConnectionFactory
{
    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;

    public NamedPipeConnectionFactory(string pipeName, TimeSpan connectTimeout)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? throw new ArgumentException("Pipe name cannot be empty.", nameof(pipeName))
            : pipeName;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(connectTimeout, TimeSpan.Zero);
        _connectTimeout = connectTimeout;
    }

    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stream = new NamedPipeClientStream(
            serverName: ".",
            pipeName: _pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            impersonationLevel: TokenImpersonationLevel.None);

        using var connectSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectSource.CancelAfter(_connectTimeout);

        try
        {
            await stream.ConnectAsync(connectSource.Token).ConfigureAwait(false);
            BrokerServerIdentityValidator.Validate(stream);
            return stream;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw new TimeoutException(
                $"Timed out connecting to Elevated Broker pipe '{_pipeName}' after {_connectTimeout.TotalMilliseconds:0} ms.",
                exception);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
