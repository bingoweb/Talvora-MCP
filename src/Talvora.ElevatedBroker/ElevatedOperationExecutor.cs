using System.ComponentModel;
using System.Diagnostics;
using Talvora.Ipc.Contracts;
using Talvora.Ipc.Contracts.Grpc;
using Talvora.Modules.Registry;
using ModuleRegistryHive = Talvora.Modules.Registry.RegistryHiveId;
using ModuleRegistryValueType = Talvora.Modules.Registry.RegistryValueType;
using ModuleRegistryView = Talvora.Modules.Registry.RegistryViewId;

namespace Talvora.ElevatedBroker;

public sealed class ElevatedOperationExecutor
{
    private readonly IRegistryService? _registryService;
    private readonly TimeProvider _timeProvider;

    public ElevatedOperationExecutor(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ElevatedOperationExecutor(
        IRegistryService registryService,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(registryService);
        _registryService = registryService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ElevatedOperationResponse> ExecuteAsync(
        ElevatedOperationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ProtocolVersion != BrokerProtocol.CurrentVersion)
        {
            return Failure(
                request,
                "protocol_mismatch",
                $"Unsupported broker protocol version {request.ProtocolVersion}. Expected {BrokerProtocol.CurrentVersion}.",
                "elevated.execute");
        }

        if (string.IsNullOrWhiteSpace(request.OperationId))
        {
            return Failure(request, "invalid_input", "OperationId is required.", "elevated.execute");
        }

        var operationName = request.OperationCase switch
        {
            ElevatedOperationRequest.OperationOneofCase.Shell => "elevated.shell.execute",
            ElevatedOperationRequest.OperationOneofCase.Process => "elevated.process.execute",
            ElevatedOperationRequest.OperationOneofCase.Registry => "elevated.registry.execute",
            _ => "elevated.execute",
        };

        try
        {
            return request.OperationCase switch
            {
                ElevatedOperationRequest.OperationOneofCase.Shell =>
                    await ExecuteShellAsync(request, cancellationToken).ConfigureAwait(false),
                ElevatedOperationRequest.OperationOneofCase.Process =>
                    await ExecuteProcessAsync(request, cancellationToken).ConfigureAwait(false),
                ElevatedOperationRequest.OperationOneofCase.Registry =>
                    await ExecuteRegistryAsync(request, cancellationToken).ConfigureAwait(false),
                _ => Failure(
                    request,
                    "unsupported_operation",
                    $"Elevated operation '{request.OperationCase}' is not supported.",
                    operationName),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                request,
                "operation_cancelled",
                "The elevated operation was cancelled.",
                operationName);
        }
        catch (TimeoutException exception)
        {
            return Failure(request, "timeout", exception.Message, operationName, retryable: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(request, "access_denied", exception.Message, operationName, nativeCode: exception.HResult);
        }
        catch (FileNotFoundException exception)
        {
            return Failure(request, "not_found", exception.Message, operationName, nativeCode: exception.HResult);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Failure(request, "not_found", exception.Message, operationName, nativeCode: exception.HResult);
        }
        catch (ArgumentException exception)
        {
            return Failure(request, "invalid_input", exception.Message, operationName, nativeCode: exception.HResult);
        }
        catch (Win32Exception exception)
        {
            return Failure(request, "native_error", exception.Message, operationName, nativeCode: exception.NativeErrorCode);
        }
        catch (IOException exception)
        {
            return Failure(request, "io_error", exception.Message, operationName, nativeCode: exception.HResult, retryable: true);
        }
        catch (Exception exception)
        {
            return Failure(request, "operation_failed", exception.Message, operationName, nativeCode: exception.HResult);
        }
    }

    private Task<ElevatedOperationResponse> ExecuteShellAsync(
        ElevatedOperationRequest request,
        CancellationToken cancellationToken)
    {
        var shell = request.Shell;
        ArgumentException.ThrowIfNullOrWhiteSpace(shell.Command);
        return ExecuteProcessAndWaitAsync(request, CreateShellStartInfo(shell), cancellationToken);
    }

    private Task<ElevatedOperationResponse> ExecuteProcessAsync(
        ElevatedOperationRequest request,
        CancellationToken cancellationToken)
    {
        var operation = request.Process;
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.FileName);

        return operation.Mode switch
        {
            ProcessExecutionMode.WaitForExit =>
                ExecuteProcessAndWaitAsync(request, CreateProcessStartInfo(operation, redirectStandardStreams: true), cancellationToken),
            ProcessExecutionMode.StartOnly =>
                Task.FromResult(StartProcessOnly(request, CreateProcessStartInfo(operation, redirectStandardStreams: false), cancellationToken)),
            _ => throw new InvalidEnumArgumentException(nameof(operation.Mode), (int)operation.Mode, typeof(ProcessExecutionMode)),
        };
    }

    private async Task<ElevatedOperationResponse> ExecuteRegistryAsync(
        ElevatedOperationRequest request,
        CancellationToken cancellationToken)
    {
        var registryService = _registryService
            ?? throw new InvalidOperationException("Registry execution requires an IRegistryService.");
        var operation = request.Registry;
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.SubKeyPath);

        var hive = MapRegistryHive(operation.Hive);
        var view = MapRegistryView(operation.View);

        switch (operation.Kind)
        {
            case RegistryMutationKind.WriteValue:
                await registryService.WriteValueAsync(
                    CreateRegistryValue(operation, hive),
                    view,
                    cancellationToken).ConfigureAwait(false);
                break;
            case RegistryMutationKind.DeleteValue:
                await registryService.DeleteValueAsync(
                    hive,
                    operation.SubKeyPath,
                    operation.HasValueName ? operation.ValueName : null,
                    view,
                    cancellationToken).ConfigureAwait(false);
                break;
            case RegistryMutationKind.CreateKey:
                await registryService.CreateKeyAsync(
                    hive,
                    operation.SubKeyPath,
                    view,
                    cancellationToken).ConfigureAwait(false);
                break;
            case RegistryMutationKind.DeleteKey:
                await registryService.DeleteKeyAsync(
                    hive,
                    operation.SubKeyPath,
                    operation.Recursive,
                    view,
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidEnumArgumentException(
                    nameof(operation.Kind),
                    (int)operation.Kind,
                    typeof(RegistryMutationKind));
        }

        return new ElevatedOperationResponse
        {
            OperationId = request.OperationId,
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            Success = true,
            Registry = new RegistryOperationResult
            {
                Completed = true,
            },
        };
    }

    private ElevatedOperationResponse StartProcessOnly(
        ElevatedOperationRequest request,
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var started = _timeProvider.GetTimestamp();

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");
        }

        return new ElevatedOperationResponse
        {
            OperationId = request.OperationId,
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            Success = true,
            Execution = new ElevatedExecutionResult
            {
                ProcessId = process.Id,
                Stdout = string.Empty,
                Stderr = string.Empty,
                DurationMilliseconds = (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds,
            },
        };
    }

    private async Task<ElevatedOperationResponse> ExecuteProcessAndWaitAsync(
        ElevatedOperationRequest request,
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        var started = _timeProvider.GetTimestamp();

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");
        }

        using var timeoutSource = request.TimeoutMilliseconds > 0
            ? new CancellationTokenSource(TimeSpan.FromMilliseconds(request.TimeoutMilliseconds))
            : null;
        using var linkedSource = timeoutSource is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var effectiveToken = linkedSource?.Token ?? cancellationToken;
        using var killRegistration = effectiveToken.Register(() => TryKill(process));

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(effectiveToken);
            var stderrTask = process.StandardError.ReadToEndAsync(effectiveToken);

            await process.WaitForExitAsync(effectiveToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return new ElevatedOperationResponse
            {
                OperationId = request.OperationId,
                ProtocolVersion = BrokerProtocol.CurrentVersion,
                Success = true,
                Execution = new ElevatedExecutionResult
                {
                    ProcessId = process.Id,
                    ExitCode = process.ExitCode,
                    Stdout = stdout,
                    Stderr = stderr,
                    DurationMilliseconds = (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds,
                },
            };
        }
        catch (OperationCanceledException) when (
            timeoutSource?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"Elevated process exceeded the configured timeout of {request.TimeoutMilliseconds} ms.");
        }
    }

    private static RegistryValueData CreateRegistryValue(
        RegistryMutationOperation operation,
        ModuleRegistryHive hive) =>
        new(
            hive,
            operation.SubKeyPath,
            operation.HasValueName ? operation.ValueName : null,
            MapRegistryValueType(operation.ValueType),
            StringValue: operation.HasStringValue ? operation.StringValue : null,
            DWordValue: operation.HasDwordValue ? operation.DwordValue : null,
            QWordValue: operation.HasQwordValue ? operation.QwordValue : null,
            MultiStringValue: operation.ValueType == Talvora.Ipc.Contracts.Grpc.RegistryValueType.MultiText
                ? operation.MultiStringValue.ToArray()
                : null,
            BinaryValue: operation.HasBinaryValue ? operation.BinaryValue.ToByteArray() : null);

    private static ModuleRegistryHive MapRegistryHive(Talvora.Ipc.Contracts.Grpc.RegistryHive hive) => hive switch
    {
        Talvora.Ipc.Contracts.Grpc.RegistryHive.ClassesRoot => ModuleRegistryHive.ClassesRoot,
        Talvora.Ipc.Contracts.Grpc.RegistryHive.CurrentUser => ModuleRegistryHive.CurrentUser,
        Talvora.Ipc.Contracts.Grpc.RegistryHive.LocalMachine => ModuleRegistryHive.LocalMachine,
        Talvora.Ipc.Contracts.Grpc.RegistryHive.Users => ModuleRegistryHive.Users,
        Talvora.Ipc.Contracts.Grpc.RegistryHive.CurrentConfig => ModuleRegistryHive.CurrentConfig,
        _ => throw new InvalidEnumArgumentException(nameof(hive), (int)hive, typeof(Talvora.Ipc.Contracts.Grpc.RegistryHive)),
    };

    private static ModuleRegistryView MapRegistryView(Talvora.Ipc.Contracts.Grpc.RegistryView view) => view switch
    {
        Talvora.Ipc.Contracts.Grpc.RegistryView.Default => ModuleRegistryView.Default,
        Talvora.Ipc.Contracts.Grpc.RegistryView._32 => ModuleRegistryView.Registry32,
        Talvora.Ipc.Contracts.Grpc.RegistryView._64 => ModuleRegistryView.Registry64,
        _ => throw new InvalidEnumArgumentException(nameof(view), (int)view, typeof(Talvora.Ipc.Contracts.Grpc.RegistryView)),
    };

    private static ModuleRegistryValueType MapRegistryValueType(Talvora.Ipc.Contracts.Grpc.RegistryValueType type) => type switch
    {
        Talvora.Ipc.Contracts.Grpc.RegistryValueType.Unspecified => ModuleRegistryValueType.Unknown,
        Talvora.Ipc.Contracts.Grpc.RegistryValueType.None => ModuleRegistryValueType.None,
        Talvora.Ipc.Contracts.Grpc.RegistryValueType.Text => ModuleRegistryValueType.Text,
        Talvora.Ipc.Contracts.Grpc.RegistryValueType.ExpandableText => ModuleRegistryValueType.ExpandableText,
        Talvora.Ipc.Contracts.Grpc.RegistryValueType.Binary => ModuleRegistryValueType.Binary,
        Talvora.Ipc.Contracts.Grpc.RegistryValueType.Dword => ModuleRegistryValueType.DWord,
        Talvora.Ipc.Contracts.Grpc.RegistryValueType.MultiText => ModuleRegistryValueType.MultiText,
        Talvora.Ipc.Contracts.Grpc.RegistryValueType.Qword => ModuleRegistryValueType.QWord,
        _ => throw new InvalidEnumArgumentException(nameof(type), (int)type, typeof(Talvora.Ipc.Contracts.Grpc.RegistryValueType)),
    };

    private static ProcessStartInfo CreateShellStartInfo(ShellExecutionOperation shell)
    {
        var startInfo = shell.Shell switch
        {
            ElevatedShellKind.Powershell => CreateStartInfo("pwsh.exe", createNoWindow: true, redirectStandardStreams: true),
            ElevatedShellKind.Cmd => CreateStartInfo("cmd.exe", createNoWindow: true, redirectStandardStreams: true),
            _ => throw new ArgumentOutOfRangeException(nameof(shell), shell.Shell, "Unsupported elevated shell."),
        };

        if (shell.Shell == ElevatedShellKind.Powershell)
        {
            startInfo.ArgumentList.Add("-NoLogo");
            if (!shell.LoadProfile)
            {
                startInfo.ArgumentList.Add("-NoProfile");
            }

            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(shell.Command);
        }
        else
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(shell.Command);
        }

        ApplyWorkingDirectory(startInfo, shell.WorkingDirectory);
        ApplyEnvironment(startInfo, shell.Environment);
        return startInfo;
    }

    private static ProcessStartInfo CreateProcessStartInfo(
        ProcessExecutionOperation operation,
        bool redirectStandardStreams)
    {
        var startInfo = CreateStartInfo(operation.FileName, operation.CreateNoWindow, redirectStandardStreams);
        foreach (var argument in operation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ApplyWorkingDirectory(startInfo, operation.WorkingDirectory);
        ApplyEnvironment(startInfo, operation.Environment);
        return startInfo;
    }

    private static ProcessStartInfo CreateStartInfo(
        string fileName,
        bool createNoWindow,
        bool redirectStandardStreams) =>
        new()
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = redirectStandardStreams,
            RedirectStandardError = redirectStandardStreams,
            CreateNoWindow = createNoWindow,
        };

    private static void ApplyWorkingDirectory(ProcessStartInfo startInfo, string workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = Path.GetFullPath(workingDirectory);
        }
    }

    private static void ApplyEnvironment(
        ProcessStartInfo startInfo,
        IEnumerable<EnvironmentVariable> environment)
    {
        foreach (var variable in environment)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(variable.Name);
            if (variable.HasValue)
            {
                startInfo.Environment[variable.Name] = variable.Value;
            }
            else
            {
                startInfo.Environment.Remove(variable.Name);
            }
        }
    }

    private static ElevatedOperationResponse Failure(
        ElevatedOperationRequest request,
        string code,
        string message,
        string operation,
        int? nativeCode = null,
        bool retryable = false)
    {
        var error = new TalvoraErrorContract
        {
            Code = code,
            Message = message,
            Operation = operation,
            Retryable = retryable,
        };

        if (nativeCode is { } value)
        {
            error.NativeCode = value;
        }

        return new ElevatedOperationResponse
        {
            OperationId = request.OperationId,
            ProtocolVersion = BrokerProtocol.CurrentVersion,
            Success = false,
            Error = error,
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
