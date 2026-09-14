using System.ComponentModel;
using Talvora.Abstractions;

namespace Talvora.Core;

public sealed class DefaultErrorMapper : IErrorMapper
{
    public TalvoraError Map(string operation, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            UnauthorizedAccessException => Create("access_denied", exception.Message, operation, exception.HResult),
            FileNotFoundException => Create("not_found", exception.Message, operation, exception.HResult),
            DirectoryNotFoundException => Create("not_found", exception.Message, operation, exception.HResult),
            KeyNotFoundException => Create("not_found", exception.Message, operation, exception.HResult),
            ArgumentException => Create("invalid_input", exception.Message, operation, exception.HResult),
            TimeoutException => Create("timeout", exception.Message, operation, exception.HResult, retryable: true),
            Win32Exception win32 => Create("native_error", win32.Message, operation, win32.NativeErrorCode),
            IOException => Create("io_error", exception.Message, operation, exception.HResult, retryable: true),
            _ => Create("operation_failed", exception.Message, operation, exception.HResult),
        };
    }

    private static TalvoraError Create(
        string code,
        string message,
        string operation,
        int? nativeCode,
        bool retryable = false) =>
        new(code, message, operation, nativeCode, retryable);
}
