namespace Talvora.Abstractions;

public sealed class TalvoraResult<T>
{
    internal TalvoraResult(bool isSuccess, T? value, TalvoraError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public TalvoraError? Error { get; }
}

public static class TalvoraResult
{
    public static TalvoraResult<T> Success<T>(T value) => new(true, value, null);

    public static TalvoraResult<T> Failure<T>(TalvoraError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, default, error);
    }
}
