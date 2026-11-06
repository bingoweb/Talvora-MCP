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

    // W5: ini/dotenv edits never add a BOM; json edits keep Turkish text unescaped.
    public static async Task RunConfigWritersKeepTextAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-w5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ini = Path.Combine(root, "ayar.ini");
            await File.WriteAllTextAsync(ini, "[Bölüm]\r\nŞehir=İzmir\r\n", new UTF8Encoding(false));
            await ConfigFormatTools.IniSet(ini, "Bölüm", "İlçe", "Bornova");
            Assert((await File.ReadAllBytesAsync(ini))[0] == (byte)'[', "W5: ini_set must not add a BOM.");
            var env = Path.Combine(root, ".env");
            await File.WriteAllTextAsync(env, "ŞEHİR=İzmir\n", new UTF8Encoding(false));
            await ConfigFormatTools.DotenvSet(env, "YENİ", "değer");
            Assert((await File.ReadAllBytesAsync(env))[0] == 0xC5, "W5: dotenv_set must not add a BOM.");
            var json = Path.Combine(root, "ayar.json");
            await File.WriteAllTextAsync(json, "{\"şehir\":\"İzmir\"}", new UTF8Encoding(false));
            await ConfigAssetTools.JsonSet(json, "/ilçe", "\"Karşıyaka\"");
            var text = await File.ReadAllTextAsync(json);
            Assert(text.Contains("\"şehir\"", StringComparison.Ordinal) && text.Contains("\"Karşıyaka\"", StringComparison.Ordinal),
                "W5: json_set must keep non-ASCII text unescaped.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // W6: Turkish case-insensitive literal search.
    public static async Task RunTurkishSearchAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-w6-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "İzmir\nsatır\nşehir\nfile\n", new UTF8Encoding(false));
            foreach (var (query, line) in new[] { ("izmir", 1), ("SATIR", 2), ("ŞEHİR", 3), ("FILE", 4), ("IZMIR", 1) })
            {
                var result = await DeveloperTools.SearchText(root, query);
                Assert(result.MatchCount == 1 && result.Matches[0].LineNumber == line,
                    $"W6: '{query}' must match line {line} once.");
            }
            var exact = await DeveloperTools.SearchText(root, "izmir", caseSensitive: true);
            Assert(exact.MatchCount == 0, "W6: case-sensitive search stays exact.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // W7: a bare name finds a .cmd on PATH, never a same-named script in the working directory.
    public static async Task RunBareCommandResolutionAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-w7-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "bin");
        var work = Path.Combine(root, "iş");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(work);
        var name = "talvora-w7-" + Guid.NewGuid().ToString("N")[..8];
        await File.WriteAllTextAsync(Path.Combine(bin, name + ".cmd"), "@echo path-ok\r\n");
        await File.WriteAllTextAsync(Path.Combine(work, name + ".cmd"), "@echo cwd-hijack\r\n");
        var previous = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", bin + ";" + previous);
        try
        {
            var result = await ProcessRunner.RunAsync(name, work, [], timeoutSeconds: 30, cancellationToken: CancellationToken.None);
            AssertEqual("path-ok", result.StandardOutput.Trim(), "W7: bare names resolve through PATH and PATHEXT, not the working directory.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previous);
            Directory.Delete(root, recursive: true);
        }
    }

    // L1: BOM-less non-UTF-8 text is Windows-1254 and is written back as Windows-1254.
    public static Task RunLegacyTurkishEncodingAsync()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var legacy = Encoding.GetEncoding(1254).GetBytes("echo Türkçe ğüşİ\r\n");
        var document = SourceTextCodec.Decode(legacy);
        AssertEqual("echo Türkçe ğüşİ\r\n", document.Text, "L1: Windows-1254 text must decode.");
        AssertEqual("windows-1254", document.Encoding.Name, "L1: the encoding must be reported as windows-1254.");
        var edited = SourceTextCodec.Encode(document with { Text = "echo Türkçe değişti\r\n" });
        AssertEqual("echo Türkçe değişti\r\n", Encoding.GetEncoding(1254).GetString(edited), "L1: edits must be written as Windows-1254.");
        var utf8 = SourceTextCodec.Decode(Encoding.UTF8.GetBytes("ğüş"));
        AssertEqual("utf-8", utf8.Encoding.Name, "L1: valid UTF-8 stays UTF-8.");
        return Task.CompletedTask;
    }
}
