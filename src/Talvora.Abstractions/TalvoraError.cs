namespace Talvora.Abstractions;

public sealed record TalvoraError(
    string Code,
    string Message,
    string Operation,
    int? NativeCode = null,
    bool Retryable = false,
    IReadOnlyDictionary<string, string>? Details = null);
