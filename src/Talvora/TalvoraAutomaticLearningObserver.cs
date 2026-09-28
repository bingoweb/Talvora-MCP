using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora.Memory;

namespace Talvora;

internal sealed class TalvoraAutomaticLearningObserver
{
    private readonly ILogger<TalvoraAutomaticLearningObserver> _logger;

    public TalvoraAutomaticLearningObserver(
        ILogger<TalvoraAutomaticLearningObserver> logger)
    {
        _logger = logger;
    }

    public async ValueTask<T> RunToolCallAsync<T>(
        string toolName,
        IDictionary<string, JsonElement>? arguments,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!TalvoraMemoryStore.IsAutomaticLearningTool(toolName))
        {
            return await operation(cancellationToken);
        }

        var project = ExtractSafeProject(arguments);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await operation(cancellationToken);
            var classification = ClassifyResult(result);
            await TryObserveAsync(
                toolName,
                project,
                classification.Outcome,
                classification.FailureKind,
                stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            await TryObserveAsync(
                toolName,
                project,
                "cancelled",
                "cancelled",
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception)
        {
            await TryObserveAsync(
                toolName,
                project,
                "failure",
                "exception",
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task TryObserveAsync(
        string toolName,
        string? project,
        string outcome,
        string? failureKind,
        long elapsedMilliseconds)
    {
        if (project is null)
        {
            return;
        }

        try
        {
            using var timeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await TalvoraMemoryRuntime.Store.ObserveToolCallAsync(
                toolName,
                project,
                outcome,
                failureKind,
                elapsedMilliseconds,
                timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Automatic learning observation failed without affecting the tool result. Tool={ToolName}",
                toolName);
        }
    }

    private static string? ExtractSafeProject(
        IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null)
        {
            return null;
        }

        foreach (var key in new[]
                 {
                     "workspaceRoot",
                     "repositoryPath",
                     "workingDirectory",
                     "project",
                 })
        {
            if (TryGetArgument(arguments, key, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        return null;
    }

    private static (string Outcome, string? FailureKind)
        ClassifyResult<T>(T result)
    {
        if (result is CallToolResult callToolResult)
        {
            if (callToolResult.IsError is true)
            {
                return ("failure", "mcp-error");
            }
            if (callToolResult.StructuredContent is JsonElement structured)
            {
                return ClassifyStructured(structured);
            }
        }

        try
        {
            return ClassifyStructured(
                JsonSerializer.SerializeToElement(result));
        }
        catch
        {
            return ("success", null);
        }
    }

    private static (string Outcome, string? FailureKind)
        ClassifyStructured(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return ("success", null);
        }

        if (TryGetPropertyIgnoreCase(
                json,
                "timedOut",
                out var timedOut) &&
            timedOut.ValueKind == JsonValueKind.True)
        {
            return ("failure", "timeout");
        }

        if (TryGetPropertyIgnoreCase(
                json,
                "success",
                out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            return ("failure", "structured-failure");
        }

        if (TryGetPropertyIgnoreCase(
                json,
                "exitCode",
                out var exitCode) &&
            exitCode.ValueKind == JsonValueKind.Number &&
            exitCode.TryGetInt32(out var code) &&
            code != 0)
        {
            return ("failure", "exit-code");
        }

        return ("success", null);
    }

    private static bool TryGetArgument(
        IDictionary<string, JsonElement> arguments,
        string name,
        out JsonElement value)
    {
        foreach (var pair in arguments)
        {
            if (string.Equals(
                    pair.Key,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement json,
        string name,
        out JsonElement value)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
