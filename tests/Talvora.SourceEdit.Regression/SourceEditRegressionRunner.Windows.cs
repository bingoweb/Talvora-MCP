using System.Text;
using Talvora.Shared;
using Talvora.SourceEditing;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    // W1: console output is read as UTF-8 when it is valid UTF-8, otherwise through the OEM code page.
    public static Task RunConsoleOutputEncodingAsync()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var console = ConsoleOutputEncoding.Create(857);
        AssertEqual("arşiv düz ğİıÇÖÜ", console.GetString(Encoding.GetEncoding(857).GetBytes("arşiv düz ğİıÇÖÜ")),
            "W1: OEM 857 console output must read as Turkish.");
        AssertEqual("Proje dosyası yok", console.GetString(Encoding.UTF8.GetBytes("Proje dosyası yok")),
            "W1: UTF-8 console output must stay UTF-8.");
        var mixed = Encoding.GetEncoding(857).GetBytes("dir: arşiv\r\n").Concat(Encoding.UTF8.GetBytes("utf8: ğüş\r\n")).ToArray();
        using var reader = new StreamReader(new MemoryStream(mixed), console, detectEncodingFromByteOrderMarks: false, bufferSize: 1);
        AssertEqual("dir: arşiv\r\nutf8: ğüş\r\n", reader.ReadToEnd(), "W1: streamed mixed output must decode per byte run.");
        return Task.CompletedTask;
    }

    // W2: every Git call Talvora makes turns off octal quoting of non-ASCII paths.
    public static Task RunGitQuotePathAsync()
    {
        var method = typeof(GitTools).GetMethod("AddSafeDirectoryArgument",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("GitTools.AddSafeDirectoryArgument is missing.");
        foreach (var directory in new[] { Path.GetTempPath(), Directory.GetCurrentDirectory() })
        {
            var result = (string[])(method.Invoke(null, [directory, new[] { "status" }])
                ?? throw new InvalidOperationException("No Git arguments returned."));
            Assert(result.Length >= 3 && result[0] == "-c" && result[1] == "core.quotepath=false",
                "W2: Git arguments must start with -c core.quotepath=false.");
        }
        return Task.CompletedTask;
    }

    // W3: durable writes work beyond MAX_PATH even when LongPathsEnabled is off.
    public static async Task RunLongPathWriteAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-w3-" + Guid.NewGuid().ToString("N"));
        var deep = root;
        while (deep.Length < 300)
        {
            deep = Path.Combine(deep, "uzun_klasör_" + new string('x', 40));
        }
        Directory.CreateDirectory(deep);
        try
        {
            var target = Path.Combine(deep, "dosya ğ.txt");
            await AtomicFile.WriteAllTextAsync(target, "içerik", new UTF8Encoding(false), createBackup: false, CancellationToken.None);
            AssertEqual("içerik", await File.ReadAllTextAsync(target), "W3: AtomicFile must write beyond MAX_PATH.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
