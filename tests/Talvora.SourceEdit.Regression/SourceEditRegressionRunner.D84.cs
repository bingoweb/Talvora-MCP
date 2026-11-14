using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    public static async Task D84MaskingAsync() => await RedactionIsIdempotentAsync();

    public static Task D84ProtectedPathsAsync()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "talvora-d84-guard-" + Guid.NewGuid().ToString("N"));
        var secrets = Path.Combine(workspace, "Secrets");
        var context7 = Path.Combine(workspace, "Context7");
        var key = Path.Combine(context7, "api-key");
        var nested = Path.Combine(secrets, "first", "second", "fixture.txt");
        var junction = Path.Combine(workspace, "outside-junction");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        Directory.CreateDirectory(context7);
        File.WriteAllText(nested, "fixture-only");

        try
        {
            var helper = typeof(FileTools).Assembly.GetType("Talvora.Tools.ProtectedSecretPaths")
                ?? throw new InvalidOperationException("D84 RED: protected path helper does not exist.");
            var check = helper.GetMethod("IsProtected", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("D84: IsProtected missing.");
            var thrower = helper.GetMethod("ThrowIfProtected", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("D84: ThrowIfProtected missing.");
            string[] entries = [secrets, key];

            bool Protected(string path) => (bool)(check.Invoke(null, [path, entries])
                ?? throw new InvalidOperationException("D84: IsProtected returned null."));

            foreach (var value in new[]
            {
                secrets,
                nested,
                nested.ToUpperInvariant(),
                nested.Replace('\\', '/'),
                nested + ".",
                key,
            })
                Assert(Protected(value), "D84 should protect fixture path: " + value);

            foreach (var value in new[]
            {
                Path.Combine(workspace, "Secrets2", "x"),
                Path.Combine(context7, "lookups-2026-11-14.jsonl"),
                Path.Combine(context7, "api-key.bak"),
            })
                Assert(!Protected(value), "D84 should allow unrelated fixture path: " + value);

            var shortName = new StringBuilder(1024);
            var shortLength = GetShortPathNameW(secrets, shortName, (uint)shortName.Capacity);
            if (shortLength > 0 && shortLength < shortName.Capacity &&
                !string.Equals(shortName.ToString(), secrets, StringComparison.OrdinalIgnoreCase))
                Assert(Protected(Path.Combine(shortName.ToString(), "first", "second", "fixture.txt")),
                    "D84 should protect the 8.3 spelling.");
            else
                Console.WriteLine("SKIP D84 8.3 path: short names are disabled on this volume.");

            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = $"/c mklink /J \"{junction}\" \"{secrets}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("D84: junction process could not start."))
            {
                process.WaitForExit();
                Assert(process.ExitCode == 0, "D84 fixture junction could not be created.");
            }
            Assert(Protected(Path.Combine(junction, "first", "second", "fixture.txt")),
                "D84 must resolve a path through a junction.");

            try
            {
                thrower.Invoke(null, [Path.Combine(secrets, "not-created.txt"), entries]);
                throw new InvalidOperationException("D84: guard allowed a missing file in Secrets.");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is UnauthorizedAccessException denied)
            {
                AssertEqual(
                    "Bu konum gizli anahtarlar için ayrılmıştır; Talvora dosya araçları burayı okumaz ve değiştirmez.",
                    denied.Message, "D84 guard error must match the approved Turkish text.");
            }
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
            if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
        }
        return Task.CompletedTask;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint bufferLength);
}
