using System.Text.Json;

namespace Talvora;

/// <summary>Shared detection of structured tool failures that are not flagged isError.</summary>
internal static class StructuredResultFailure
{
    internal const string TimedOut = "timedOut";
    internal const string Unsuccessful = "success";
    internal const string NonZeroExit = "exitCode";

    // D51: these tools report a process this call did not run (a background job or a dev server); that process's
    // exit code is data, so only timedOut and success=false make such a call a failure.
    private static readonly HashSet<string> ReportedProcessTools = new(StringComparer.Ordinal)
    {
        "job_get", "job_stop", "dev_server_get", "dev_server_stop",
    };

    internal static bool IsFailure(JsonElement json, string? toolName = null) => Describe(json, toolName) is not null;

    internal static string? Describe(JsonElement json, string? toolName = null)
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

        if (!ReportsAnotherProcess(toolName) &&
            TryGetPropertyIgnoreCase(json, "exitCode", out var exitCode) &&
            exitCode.ValueKind == JsonValueKind.Number &&
            exitCode.TryGetInt32(out var code) &&
            code != 0)
        {
            return NonZeroExit;
        }

        return null;
    }

    internal static bool ReportsAnotherProcess(string? toolName)
    {
        var name = toolName?.Trim() ?? string.Empty;
        if (name.StartsWith("talvora_", StringComparison.OrdinalIgnoreCase))
        {
            name = name["talvora_".Length..];
        }

        return ReportedProcessTools.Contains(name);
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
