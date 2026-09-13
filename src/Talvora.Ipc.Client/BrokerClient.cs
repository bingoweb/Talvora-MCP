using Grpc.Core;
using Grpc.Net.Client;
using Talvora.Ipc.Contracts;
using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.Ipc.Client;

public sealed class BrokerClient : IBrokerClient, IDisposable
{
    private readonly SocketsHttpHandler _handler;
    private readonly GrpcChannel _channel;
    private readonly BrokerControl.BrokerControlClient _client;
    private bool _disposed;

    public BrokerClient(string pipeName)
    {
        var connectionFactory = new NamedPipeConnectionFactory(pipeName);
        _handler = new SocketsHttpHandler
        {
            ConnectCallback = connectionFactory.ConnectAsync,
        };

        _channel = GrpcChannel.ForAddress(
            "http://localhost",
            new GrpcChannelOptions
            {
                HttpHandler = _handler,
                DisposeHttpClient = false,
            });
        _client = new BrokerControl.BrokerControlClient(_channel);
    }

    public async Task<BrokerHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var response = await _client.HealthAsync(
            new BrokerHealthRequest { ProtocolVersion = BrokerProtocol.CurrentVersion },
            cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);

        return new BrokerHealthSnapshot(
            response.ProtocolVersion,
            response.Ready,
            response.ServiceVersion,
            response.IsElevated,
            response.ProcessId);
    }

    public async Task<BrokerProbeResult> ProbeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            var health = await GetHealthAsync(timeoutSource.Token).ConfigureAwait(false);
            return BrokerProbeResult.Success(health);
        }
        catch (RpcException exception)
        {
            var error = string.IsNullOrWhiteSpace(exception.Status.Detail)
                ? exception.Message
                : exception.Status.Detail;
            return BrokerProbeResult.Failure(error);
        }
        catch (IOException exception)
        {
            return BrokerProbeResult.Failure(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return BrokerProbeResult.Failure(exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BrokerProbeResult.Failure("Elevated Broker health probe timed out.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _channel.Dispose();
        _handler.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
