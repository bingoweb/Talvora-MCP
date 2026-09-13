using Talvora.Abstractions;

namespace Talvora.Adapter.Mcp;

public sealed record ToolEnvelope<T>(bool Ok, T? Data, TalvoraError? Error);

public static class ToolEnvelope
{
    public static ToolEnvelope<T> From<T>(TalvoraResult<T> result) =>
        result.IsSuccess
            ? new ToolEnvelope<T>(true, result.Value, null)
            : new ToolEnvelope<T>(false, default, result.Error);
}
