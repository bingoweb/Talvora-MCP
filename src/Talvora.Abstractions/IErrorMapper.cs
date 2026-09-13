namespace Talvora.Abstractions;

public interface IErrorMapper
{
    TalvoraError Map(string operation, Exception exception);
}
