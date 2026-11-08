using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora;

internal static partial class Program
{
    private static async Task AssertTw2CallTraceAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-tw2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var today = DateTime.UtcNow.Date;
        var oldPath = Path.Combine(root, $"calls-{today.AddDays(-15):yyyy-MM-dd}.jsonl");
        var todayPath = Path.Combine(root, $"calls-{today:yyyy-MM-dd}.jsonl");
        await File.WriteAllTextAsync(oldPath, "old\n");

        try
        {
            await using var trace = new ToolCallTrace(root);
            const string secret = "sk-proj-ABCDEF1234567890Q";
            var arguments = new Dictionary<string, JsonElement>
            {
                ["executable"] = JsonSerializer.SerializeToElement("whoami.exe"),
                ["arguments"] = JsonSerializer.SerializeToElement(new[] { "--api-key=" + secret }),
                ["workingDirectory"] = JsonSerializer.SerializeToElement(root),
                ["environment"] = JsonSerializer.SerializeToElement(new { PRIVATE_VALUE = "env-should-not-appear" }),
            };
            var result = new CallToolResult
            {
                IsError = false,
                StructuredContent = JsonSerializer.SerializeToElement(new { success = true, exitCode = 0 }),
            };

            trace.Record(DateTimeOffset.UtcNow, "ChatGPT", "talvora_user_run",
                arguments, 24, result);
            await trace.FlushAsync();
            var lines = await File.ReadAllLinesAsync(todayPath);
            if (lines.Length != 1)
                throw new InvalidOperationException("TW2: one call must append one JSON line.");
            using var first = JsonDocument.Parse(lines[0]);
            var json = first.RootElement;
            var hash = json.GetProperty("args").GetString();
            var command = json.GetProperty("cmd").GetString();
            if (!json.GetProperty("ts").GetString()!.EndsWith('Z') ||
                json.GetProperty("caller").GetString() != "ChatGPT" ||
                json.GetProperty("tool").GetString() != "talvora_user_run" ||
                json.GetProperty("ms").GetInt64() != 24 ||
                json.GetProperty("outcome").GetString() != "ok" ||
                json.GetProperty("exit").GetInt32() != 0 ||
                json.GetProperty("cwd").GetString() != root ||
                hash?.Length != 16 ||
                command is null ||
                !command.Contains("[REDACTED]", StringComparison.Ordinal) ||
                command.Contains(secret, StringComparison.Ordinal) ||
                lines[0].Contains("env-should-not-appear", StringComparison.Ordinal) ||
                json.EnumerateObject().Count() != 9)
                throw new InvalidOperationException("TW2: trace fields or secret redaction are incorrect.");

            trace.Record(DateTimeOffset.UtcNow, "ChatGPT", "talvora_user_run",
                arguments, 27, result);
            trace.Record(DateTimeOffset.UtcNow, "Talvora.Smoke", "talvora_user_run",
                arguments, 27, result);
            await trace.FlushAsync();
            lines = await File.ReadAllLinesAsync(todayPath);
            if (lines.Length != 2)
                throw new InvalidOperationException("TW2: own callers must not be traced.");
            using var second = JsonDocument.Parse(lines[1]);
            if (second.RootElement.GetProperty("args").GetString() != hash)
                throw new InvalidOperationException("TW2: identical arguments need identical hashes.");

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (File.Exists(oldPath) && DateTime.UtcNow < deadline)
                await Task.Delay(40);
            if (File.Exists(oldPath))
                throw new InvalidOperationException("TW2: the 15-day-old trace was not deleted.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Console.WriteLine("TW2_CALL_TRACE_GREEN");
    }
}
