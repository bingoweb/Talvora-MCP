using System.Text;
using Talvora.Shared;

internal static partial class SourceEditRegressionRunner
{
    /// <summary>
    /// D22 (audit 2026-10-24 (17)): writers that publish the same new file at the same moment must all succeed
    /// (later ones replace the earlier version) instead of failing with "Durable file publication failed".
    /// </summary>
    private static async Task AtomicPublishConcurrentFirstWritersAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "Talvora-AtomicRace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var round = 0; round < 150; round++)
            {
                var path = Path.Combine(root, "manifest-" + round + ".json");
                using var start = new ManualResetEventSlim(false);
                var writers = Enumerable.Range(0, 4).Select(writer => Task.Run(async () =>
                {
                    start.Wait();
                    await AtomicFile.WriteAllTextAsync(path, "{\"writer\":" + writer + "}", new UTF8Encoding(false));
                })).ToArray();
                start.Set();
                await Task.WhenAll(writers);
                var text = await File.ReadAllTextAsync(path);
                Assert(text.StartsWith("{\"writer\":", StringComparison.Ordinal) && text.EndsWith('}'),
                    "A concurrently published file must hold one complete writer's content.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
