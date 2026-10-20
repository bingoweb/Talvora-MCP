using System.Text.Json;

namespace Talvora;

/// <summary>Shared detection of structured tool failures that are not flagged isError.</summary>
internal static class StructuredResultFailure
{
    internal const string TimedOut = "timedOut";
    internal const string Unsuccessful = "success";
    internal const string NonZeroExit = "exitCode";

    internal static bool IsFailure(JsonElement json) => Describe(json) is not null;

    internal static string? Describe(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryGetPropertyIgnoreCase(json, "timedOut", out var timedOut) &&
            timedOut.ValueKind == JsonValueKind.True)
        {
            return TimedOut;
        }

        if (TryGetPropertyIgnoreCase(json, "success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            return Unsuccessful;
        }

        if (TryGetPropertyIgnoreCase(json, "exitCode", out var exitCode) &&
            exitCode.ValueKind == JsonValueKind.Number &&
            exitCode.TryGetInt32(out var code) &&
            code != 0)
        {
            return NonZeroExit;
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement json, string name, out JsonElement value)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
