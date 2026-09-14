using Grpc.Core;
using Grpc.Net.Client;
using Talvora.Abstractions;
using Talvora.Ipc.Contracts;
using Talvora.Ipc.Contracts.Grpc;

namespace Talvora.Ipc.Client;

public sealed class BrokerClient : IBrokerClient, IDisposable
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly SocketsHttpHandler _handler;
    private readonly GrpcChannel _channel;
    private readonly BrokerControl.BrokerControlClient _client;
    private bool _disposed;

    public BrokerClient(string pipeName)
        : this(pipeName, DefaultConnectTimeout)
    {
    }

    public BrokerClient(string pipeName, TimeSpan connectTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(connectTimeout, TimeSpan.Zero);

        var connectionFactory = new NamedPipeConnectionFactory(pipeName, connectTimeout);
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

    public Task<TalvoraResult<BrokerExecutionResult>> ExecuteShellAsync(
        BrokerShellExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Command);

        var operation = new ShellExecutionOperation
        {
            Command = request.Command,
            Shell = request.Shell switch
            {
                BrokerShellKind.PowerShell => ElevatedShellKind.Powershell,
                BrokerShellKind.Cmd => ElevatedShellKind.Cmd,
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Shell, "Unsupported shell."),
            },
            LoadProfile = request.LoadProfile,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
        };
        AddEnvironment(operation.Environment, request.Environment);

        var envelope = new ElevatedOperationRequest
        {
            OperationId = ResolveOperationId(request.OperationId),
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            TimeoutMilliseconds = GetTimeoutMilliseconds(request.Timeout),
            Shell = operation,
        };

        return ExecuteAsync(envelope, cancellationToken);
    }

    public Task<TalvoraResult<BrokerExecutionResult>> ExecuteProcessAsync(
        BrokerProcessExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);

        var operation = new ProcessExecutionOperation
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            CreateNoWindow = request.CreateNoWindow,
            Mode = request.Mode switch
            {
                BrokerProcessExecutionMode.WaitForExit => ProcessExecutionMode.WaitForExit,
                BrokerProcessExecutionMode.StartOnly => ProcessExecutionMode.StartOnly,
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unsupported process execution mode."),
            },
        };
        if (request.Arguments is not null)
        {
            operation.Arguments.Add(request.Arguments);
        }
        AddEnvironment(operation.Environment, request.Environment);

        var envelope = new ElevatedOperationRequest
        {
            OperationId = ResolveOperationId(request.OperationId),
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            TimeoutMilliseconds = GetTimeoutMilliseconds(request.Timeout),
            Process = operation,
        };

        return ExecuteAsync(envelope, cancellationToken);
    }

    private async Task<TalvoraResult<BrokerExecutionResult>> ExecuteAsync(
        ElevatedOperationRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ElevatedOperationResponse response;
        try
        {
            response = await _client.ExecuteAsync(
                request,
                cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);
        }
        catch (RpcException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "Elevated Broker execution was cancelled by the caller.",
                exception,
                cancellationToken);
        }
        catch (RpcException exception)
        {
            return TalvoraResult.Failure<BrokerExecutionResult>(MapTransportError(exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return TalvoraResult.Failure<BrokerExecutionResult>(new TalvoraError(
                "access_denied",
                exception.Message,
                "elevated.execute",
                exception.HResult));
        }
        catch (IOException exception)
        {
            return TalvoraResult.Failure<BrokerExecutionResult>(BrokerUnavailable(exception.Message, exception.HResult));
        }
        catch (TimeoutException exception)
        {
            return TalvoraResult.Failure<BrokerExecutionResult>(BrokerUnavailable(exception.Message));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TalvoraResult.Failure<BrokerExecutionResult>(BrokerUnavailable(
                "Timed out while connecting to the Elevated Broker."));
        }

        if (response.ProtocolVersion != BrokerProtocol.CurrentVersion)
        {
            return TalvoraResult.Failure<BrokerExecutionResult>(new TalvoraError(
                "protocol_mismatch",
                $"Broker returned protocol version {response.ProtocolVersion}; expected {BrokerProtocol.CurrentVersion}.",
                "elevated.execute"));
        }

        if (response.Success && response.Execution is not null)
        {
            return TalvoraResult.Success(new BrokerExecutionResult(
                response.OperationId,
                response.Execution.ProcessId,
                response.Execution.HasExitCode ? response.Execution.ExitCode : null,
                response.Execution.Stdout,
                response.Execution.Stderr,
                TimeSpan.FromMilliseconds(response.Execution.DurationMilliseconds)));
        }

        return TalvoraResult.Failure<BrokerExecutionResult>(MapError(response));
    }

    private static TalvoraError MapTransportError(RpcException exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Status.Detail)
            ? exception.Message
            : exception.Status.Detail;
        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grpc_status"] = exception.StatusCode.ToString(),
        };

        var connectionStartupFailure =
            (exception.StatusCode is StatusCode.Internal or StatusCode.Unknown) &&
            message.StartsWith("Error starting gRPC call", StringComparison.Ordinal);
        if (connectionStartupFailure)
        {
            return new TalvoraError(
                "broker_unavailable",
                message,
                "elevated.execute",
                Retryable: true,
                Details: details);
        }

        return exception.StatusCode switch
        {
            StatusCode.PermissionDenied or StatusCode.Unauthenticated => new TalvoraError(
                "access_denied",
                message,
                "elevated.execute",
                Retryable: false,
                Details: details),
            StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Cancelled => new TalvoraError(
                "broker_unavailable",
                message,
                "elevated.execute",
                Retryable: true,
                Details: details),
            _ => new TalvoraError(
                "broker_transport_error",
                message,
                "elevated.execute",
                Retryable: true,
                Details: details),
        };
    }

    private static TalvoraError BrokerUnavailable(string message, int? nativeCode = null) =>
        new(
            "broker_unavailable",
            message,
            "elevated.execute",
            nativeCode,
            Retryable: true);

    private static TalvoraError MapError(ElevatedOperationResponse response)
    {
        if (response.Error is null)
        {
            return new TalvoraError(
                "operation_failed",
                "Elevated Broker returned a failed operation without an error payload.",
                "elevated.execute");
        }

        IReadOnlyDictionary<string, string>? details = response.Error.Details.Count == 0
            ? null
            : response.Error.Details.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return new TalvoraError(
            response.Error.Code,
            response.Error.Message,
            response.Error.Operation,
            response.Error.HasNativeCode ? response.Error.NativeCode : null,
            response.Error.Retryable,
            details);
    }

    private static void AddEnvironment(
        Google.Protobuf.Collections.RepeatedField<EnvironmentVariable> target,
        IReadOnlyDictionary<string, string?>? environment)
    {
        if (environment is null)
        {
            return;
        }

        foreach (var (name, value) in environment)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var variable = new EnvironmentVariable { Name = name };
            if (value is not null)
            {
                variable.Value = value;
            }
            target.Add(variable);
        }
    }

    private static string ResolveOperationId(string? operationId) =>
        string.IsNullOrWhiteSpace(operationId)
            ? Guid.NewGuid().ToString("N")
            : operationId;

    private static long GetTimeoutMilliseconds(TimeSpan? timeout)
    {
        if (timeout is null)
        {
            return 0;
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout.Value, TimeSpan.Zero);
        return Math.Max(1, checked((long)Math.Ceiling(timeout.Value.TotalMilliseconds)));
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
