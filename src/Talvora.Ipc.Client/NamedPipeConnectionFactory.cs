using System.IO.Pipes;
using System.Net.Http;
using System.Security.Principal;

namespace Talvora.Ipc.Client;

internal sealed class NamedPipeConnectionFactory(string pipeName)
{
    private readonly string _pipeName = string.IsNullOrWhiteSpace(pipeName)
        ? throw new ArgumentException("Pipe name cannot be empty.", nameof(pipeName))
        : pipeName;

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

        try
        {
            await stream.ConnectAsync(cancellationToken).ConfigureAwait(false);
            BrokerServerIdentityValidator.Validate(stream);
            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
