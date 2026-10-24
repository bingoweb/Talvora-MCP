using System.Reflection;
using System.Text.Json;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    public static async Task RunToolchainConsolidationAsync()
    {
        var type = typeof(BuildRunnerTools).Assembly.GetType("Talvora.Tools.ToolchainTools")
            ?? throw new InvalidOperationException("Q2a replacement tool type is missing.");
        var info = type.GetMethod("Info") ?? throw new InvalidOperationException("Q2a info method is missing.");
        var run = type.GetMethod("Run") ?? throw new InvalidOperationException("Q2a run method is missing.");
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var probes = new (string Tool, Func<Task<object>> Read)[]
        {
            ("pnpm", async () => await BuildRunnerTools.PnpmInfo()),
            ("yarn", async () => await BuildRunnerTools.YarnInfo()),
            ("bun", async () => await BuildRunnerTools.BunInfo()),
            ("cmake", async () => await WindowsToolchainTools.CMakeInfo()),
            ("ninja", async () => await WindowsToolchainTools.NinjaInfo()),
        };
        foreach (var probe in probes)
        {
            var expected = JsonSerializer.SerializeToElement(await probe.Read(), jsonOptions);
            var task = (Task<Dictionary<string, JsonElement>>)info.Invoke(null, [probe.Tool, CancellationToken.None])!;
            var actual = JsonSerializer.SerializeToElement(await task, jsonOptions);
            if (expected.GetRawText() != actual.GetRawText())
            {
                // The first call of a shim (Corepack yarn/pnpm on CI) may provision the tool or time out under load and
                // change "found" between the two probes (audit 16); compare one fresh pair taken back to back.
                expected = JsonSerializer.SerializeToElement(await probe.Read(), jsonOptions);
                task = (Task<Dictionary<string, JsonElement>>)info.Invoke(null, [probe.Tool, CancellationToken.None])!;
                actual = JsonSerializer.SerializeToElement(await task, jsonOptions);
            }
            var expectedNames = expected.EnumerateObject().Select(property => property.Name).Order().ToArray();
            Q2aCheck(expectedNames.SequenceEqual(actual.EnumerateObject().Select(property => property.Name).Order()),
                probe.Tool + " info changed its output fields.");
            foreach (var property in expected.EnumerateObject())
            {
                Q2aCheck(property.Value.GetRawText() == actual.GetProperty(property.Name).GetRawText(),
                    probe.Tool + " info changed " + property.Name + ".");
            }
            Console.WriteLine("PASS Q2a " + probe.Tool + " info fields and discovery");
        }

        var root = Path.Combine(Path.GetTempPath(), "Talvora-Q2a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sentinel = "Q2a-" + Guid.NewGuid().ToString("N");
            var environment = new Dictionary<string, string?> { ["Q2A_SENTINEL"] = sentinel };
            var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            var ninjaFile = Path.Combine(root, "fixture with spaces.ninja");
            await File.WriteAllTextAsync(ninjaFile,
                "rule witness\n  command = \"" + command + "\" /d /c \"echo %Q2A_SENTINEL%>output.txt\"\nbuild output.txt: witness\n");
            foreach (var tool in new[] { "cmake", "ninja" })
            {
                var arguments = tool == "cmake"
                    ? new[] { "-E", "env", command, "/d", "/c", "echo %Q2A_SENTINEL%" }
                    : new[] { "-f", Path.GetFileName(ninjaFile), "output.txt" };
                Task<TalvoraCliCommandResponse> Original() => tool == "cmake"
                    ? WindowsToolchainTools.CMakeRun(root, arguments, environment, 30)
                    : WindowsToolchainTools.NinjaRun(root, arguments, environment, 30);
                Task<TalvoraCliCommandResponse> Replacement() =>
                    (Task<TalvoraCliCommandResponse>)run.Invoke(null,
                        [tool, arguments, root, environment, 30, CancellationToken.None])!;
                var expected = await Q2aCaptureRunAsync(Original);
                File.Delete(Path.Combine(root, "output.txt"));
                var actual = await Q2aCaptureRunAsync(Replacement);
                if (expected.Error is not null)
                {
                    Q2aCheck(expected.Error == typeof(FileNotFoundException) && actual.Error == expected.Error,
                        tool + " missing-executable behavior changed.");
                    Console.WriteLine("PASS Q2a " + tool + " run missing-executable behavior");
                    continue;
                }
                Q2aCheck(actual.Error is null && actual.Result is not null,
                    tool + " replacement failed to execute.");
                foreach (var result in new[] { expected.Result!, actual.Result! })
                {
                    Q2aCheck(result.ExitCode == 0 && !result.TimedOut,
                        tool + " did not execute the witness command.");
                    Q2aCheck(result.WorkingDirectory == root && result.Arguments.SequenceEqual(arguments),
                        tool + " lost its working directory or argument vector.");
                    Q2aCheck(result.ProcessId > 0 && !string.IsNullOrWhiteSpace(result.Executable),
                        tool + " lost process identity output fields.");
                    if (tool == "cmake")
                    {
                        Q2aCheck(result.StandardOutput.Trim() == sentinel,
                            "cmake did not pass the environment override to the child.");
                    }
                }
                if (tool == "ninja")
                {
                    Q2aCheck((await File.ReadAllTextAsync(Path.Combine(root, "output.txt"))).Trim() == sentinel,
                        "ninja did not execute the spaced build-file path with the environment override.");
                }
                var oldFields = JsonSerializer.SerializeToElement(expected.Result, jsonOptions)
                    .EnumerateObject().Select(property => property.Name).Order();
                var newFields = JsonSerializer.SerializeToElement(actual.Result, jsonOptions)
                    .EnumerateObject().Select(property => property.Name).Order();
                Q2aCheck(oldFields.SequenceEqual(newFields), tool + " run changed its output fields.");
                Console.WriteLine("PASS Q2a " + tool + " run argv, environment, working directory and effect");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(TalvoraCliCommandResponse? Result, Type? Error)> Q2aCaptureRunAsync(
        Func<Task<TalvoraCliCommandResponse>> run)
    {
        try { return (await run(), null); }
        catch (TargetInvocationException ex) when (ex.InnerException is FileNotFoundException)
        { return (null, ex.InnerException.GetType()); }
        catch (FileNotFoundException ex) { return (null, ex.GetType()); }
    }

    private static void Q2aCheck(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
