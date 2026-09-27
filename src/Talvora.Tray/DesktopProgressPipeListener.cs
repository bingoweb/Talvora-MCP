using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Talvora.Shared;

namespace Talvora.Tray;

internal sealed class DesktopProgressPipeListener : IDisposable
{
    private readonly int _sessionId;
    private readonly Action<DesktopProgressMessage> _onMessage;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private Task? _listenerTask;
    private bool _disposed;

    public DesktopProgressPipeListener(
        int sessionId,
        Action<DesktopProgressMessage> onMessage)
    {
        _sessionId = sessionId;
        _onMessage = onMessage;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _listenerTask ??= Task.Run(
            () => ListenAsync(_lifetimeCts.Token));
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellationToken);

                using var reader = new StreamReader(
                    pipe,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 4096,
                    leaveOpen: true);

                var payload =
                    await reader.ReadLineAsync(cancellationToken);
                if (payload is not null &&
                    DesktopProgressProtocol.TryDeserialize(
                        payload,
                        out var message) &&
                    message is not null)
                {
                    _onMessage(message);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                TrayLog.Write(
                    "Desktop progress IPC listener recovered from a failure.",
                    ex);

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(1),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        var currentUser =
            WindowsIdentity.GetCurrent().User ??
            throw new InvalidOperationException(
                "The tray user SID could not be resolved.");
        var localSystem =
            new SecurityIdentifier(
                WellKnownSidType.LocalSystemSid,
                domainSid: null);

        security.AddAccessRule(
            new PipeAccessRule(
                currentUser,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        security.AddAccessRule(
            new PipeAccessRule(
                localSystem,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            DesktopProgressProtocol.GetPipeName(_sessionId),
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 0,
            security);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCts.Cancel();

        if (_listenerTask is not null)
        {
            try
            {
                _listenerTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException ex)
                when (ex.InnerExceptions.All(
                    static inner => inner is OperationCanceledException))
            {
            }
        }

        _lifetimeCts.Dispose();
    }
}
