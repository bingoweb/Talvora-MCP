using System.Collections.Concurrent;
using System.Text.Json;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>WL1: a finished step carries its measured duration; an old frame without it still reads (null).</summary>
    private static async Task AssertWl1StepDurationAsync()
    {
        var project = Path.Combine(Path.GetTempPath(), "Talvora-WL1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(project, ".git"));
        var messages = new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(messages);
        try
        {
            var arguments = new Dictionary<string, JsonElement>
            {
                ["workspaceRoot"] = JsonSerializer.SerializeToElement(project),
            };
            await notifier.RunToolCallAsync<object>("talvora_dotnet_build", arguments,
                async _ =>
                {
                    await Task.Delay(400);
                    return new { success = true, exitCode = 0 };
                },
                CancellationToken.None);
            var entry = messages.Last(message => message.WorklogFrame is { Timeline.Count: > 0 }).WorklogFrame!.Timeline[0];
            if (entry.DurationSeconds is not { } seconds || seconds < 0.4 || seconds > 10)
                throw new InvalidOperationException($"WL1: a finished step must carry its measured duration, got {entry.DurationSeconds}.");
        }
        finally
        {
            await notifier.DisposeAsync();
            Directory.Delete(project, recursive: true);
        }

        var protocolOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
        var old = JsonSerializer.Deserialize<WorklogTimelineEntry>(
            """{"timestampUtc":"2026-11-01T10:00:00+00:00","state":"succeeded","label":"Eski"}""", protocolOptions)!;
        if (old.DurationSeconds is not null)
            throw new InvalidOperationException("WL1: a frame without a duration must read as null, never as 0.");
    }
}
