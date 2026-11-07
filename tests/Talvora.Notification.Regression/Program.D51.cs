using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>D51: a status or stop tool reports another process's exit code as data; the call itself did not fail.</summary>
    private static async Task AssertD51ReportedExitCodesAsync()
    {
        const string stopped = """{"found":true,"killIssued":true,"exited":true,"state":"Exited","exitCode":-1}""";
        foreach (var tool in new[] { "talvora_job_get", "talvora_job_stop", "talvora_dev_server_get", "talvora_dev_server_stop" })
        {
            if (ToolFailureGuidance.Apply(tool, D51Result(stopped)).IsError is true)
                throw new InvalidOperationException($"D51: {tool} reports another process's exit code; the call itself succeeded.");
        }

        foreach (var tool in new[] { "talvora_run_process", "talvora_dev_server_wait" })
        {
            if (ToolFailureGuidance.Apply(tool, D51Result("""{"exitCode":1}""")).IsError is not true)
                throw new InvalidOperationException($"D51: {tool} ran the process in this call; a non-zero exit code stays an error (H3).");
        }

        if (!await D51StepFailedAsync("talvora_run_process", """{"exitCode":1}"""))
            throw new InvalidOperationException("D51: a command that failed in this call is a failed step in Çalışma günlüğü.");
        if (await D51StepFailedAsync("talvora_job_stop", stopped))
            throw new InvalidOperationException("D51: a stopped job is not a failed step in Çalışma günlüğü.");

        Console.WriteLine("D51_REPORTED_EXIT_CODES_GREEN");
    }

    private static async Task<bool> D51StepFailedAsync(string tool, string json)
    {
        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(observed);
        try
        {
            _ = await notifier.RunToolCallAsync(tool, null,
                _ => ValueTask.FromResult(D51Result(json)), CancellationToken.None, "/mcp", "Claude");
        }
        finally
        {
            await notifier.DisposeAsync();
        }
        return observed.Any(message => message.Kind == DesktopProgressKind.Failed);
    }

    private static CallToolResult D51Result(string json) => new()
    {
        Content = [new TextContentBlock { Text = json }],
        StructuredContent = JsonDocument.Parse(json).RootElement.Clone(),
    };
}
