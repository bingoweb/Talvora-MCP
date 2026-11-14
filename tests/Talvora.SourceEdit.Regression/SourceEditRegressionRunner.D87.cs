using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    public static async Task D87ProtectedPathsAsync()
    {
        const string denied = "Bu konum gizli anahtarlar için ayrılmıştır; Talvora dosya araçları burayı okumaz ve değiştirmez.";
        var root = Path.Combine(Path.GetTempPath(), "talvora-d87-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(root, "protected-parent");
        var secrets = Path.Combine(parent, "Secrets");
        var key = Path.Combine(parent, "Context7", "api-key");
        var search = Path.Combine(root, "search");
        var broken = Path.Combine(search, "broken");
        var alias = Path.Combine(root, "alias-parent");
        var innerBroken = Path.Combine(secrets, "broken");
        var sibling = Path.Combine(root, "sibling");
        Directory.CreateDirectory(secrets);
        Directory.CreateDirectory(Path.GetDirectoryName(key)!);
        Directory.CreateDirectory(search);
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(search, "a.txt"), "needle");
        File.WriteAllText(Path.Combine(search, "z.txt"), "needle");
        File.WriteAllText(Path.Combine(secrets, "x.txt"), "fixture-only");
        File.WriteAllText(Path.Combine(sibling, "safe.txt"), "safe");
        var helper = typeof(FileTools).Assembly.GetType("Talvora.Tools.ProtectedSecretPaths")!;
        var overrideField = helper.GetField("EntriesOverrideForTests",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("D87 RED: test entries override missing");
        var checker = helper.GetMethod("IsProtected", BindingFlags.Static | BindingFlags.NonPublic)!
            ?? throw new InvalidOperationException("D87: IsProtected missing");
        bool Protected(string path) => (bool)checker.Invoke(null, [path, null])!;
        static void Junction(string link, string target)
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            }) ?? throw new InvalidOperationException("D87: mklink did not start");
            proc.WaitForExit();
            Assert(proc.ExitCode == 0, "D87: mklink failed: " + link);
        }
        static Task Sync(Action action) { action(); return Task.CompletedTask; }
        static async Task Denied(Func<Task> action, string label)
        {
            try { await action(); }
            catch (UnauthorizedAccessException ex) when (ex.Message == denied) { return; }
            throw new InvalidOperationException("D87: not denied: " + label);
        }
        overrideField.SetValue(null, new[] { secrets, key });
        try
        {
            Junction(broken, Path.Combine(root, "missing-target"));
            Junction(innerBroken, Path.Combine(root, "other-missing-target"));
            Junction(alias, parent);
            Assert(!Protected(broken) && !Protected(Path.Combine(broken, "x")),
                "D87: broken junction outside secrets must not throw");
            Assert(Protected(Path.Combine(alias, "Secrets", "broken")),
                "D87: broken junction inside secrets through alias must be denied");
            var found = await DeveloperTools.SearchText(search, "needle");
            Assert(found.Matches.Count == 2, "D87: a broken junction must not fail the whole search");
            FileTools.Delete(broken);
            Assert(new DirectoryInfo(broken).LinkTarget is null,
                "D87: delete must unlink a broken directory junction");
            await Denied(() => Sync(() => FileTools.Delete(parent)), "parent delete");
            await Denied(() => Sync(() => FileTools.Move(parent, Path.Combine(root, "moved"))), "parent move");
            await Denied(() => Sync(() => FileTools.Copy(parent, Path.Combine(root, "copied"))), "parent copy");
            await Denied(() => Sync(() => ConfigAssetTools.ArchiveCreate(parent, Path.Combine(root, "secret.zip"))),
                "parent archive");
            Assert(File.Exists(Path.Combine(secrets, "x.txt")) && !File.Exists(Path.Combine(root, "secret.zip")),
                "D87: protected source folder was changed");

            var incoming = Path.Combine(root, "incoming");
            Directory.CreateDirectory(Path.Combine(incoming, "Secrets"));
            File.WriteAllText(Path.Combine(incoming, "Secrets", "x.txt"), "overwrite");
            await Denied(() => Sync(() => FileTools.Copy(incoming, parent, overwrite: true)),
                "copy destination manifest");
            Assert(File.ReadAllText(Path.Combine(secrets, "x.txt")) == "fixture-only",
                "D87: copy wrote protected fixture");
            var zip = Path.Combine(root, "incoming.zip");
            using (var a = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var safe = new StreamWriter(a.CreateEntry("safe.txt").Open()))
                    safe.Write("safe");
                using (var secret = new StreamWriter(a.CreateEntry("Secrets/x.txt").Open()))
                    secret.Write("overwrite");
            }
            await Denied(() => Sync(() => ConfigAssetTools.ArchiveExtract(zip, parent, overwrite: true)),
                "zip protected manifest");
            Assert(!File.Exists(Path.Combine(parent, "safe.txt")),
                "D87: archive partially extracted its safe entry");
            var escapeZip = Path.Combine(root, "escape.zip");
            using (var a = ZipFile.Open(escapeZip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(a.CreateEntry("../protected-parent/Secrets/x.txt").Open()))
                w.Write("overwrite");
            await Denied(() => Sync(() => ConfigAssetTools.ArchiveExtract(escapeZip,
                Path.Combine(root, "landing"), allowOutsideDestination: true, overwrite: true)),
                "zip path traversal");

            var p = Path.Combine(secrets, "fixture.json");
            var writers = new (string, Func<Task>)[]
            {
                ("json-set", () => ConfigAssetTools.JsonSet(p, "/x", "1")),
                ("json-delete", () => ConfigAssetTools.JsonDelete(p, "/x")),
                ("yaml-set", () => StructuredConfigTools.YamlSet(p, "/x", "1")),
                ("yaml-delete", () => StructuredConfigTools.YamlDelete(p, "/x")),
                ("toml-set", () => StructuredConfigTools.TomlSet(p, "/x", "1")),
                ("toml-delete", () => StructuredConfigTools.TomlDelete(p, "/x")),
                ("ini-set", () => ConfigFormatTools.IniSet(p, "a", "x", "1")),
                ("ini-delete", () => ConfigFormatTools.IniDelete(p, "a", "x")),
                ("dotenv-set", () => ConfigFormatTools.DotenvSet(p, "X", "1")),
                ("dotenv-delete", () => ConfigFormatTools.DotenvDelete(p, "X")),
                ("xml-set", () => Sync(() => ConfigFormatTools.XmlSet(p, "/root/x", "1"))),
                ("xml-delete", () => Sync(() => ConfigFormatTools.XmlDelete(p, "/root/x"))),
            };
            foreach (var item in writers) await Denied(item.Item2, item.Item1);
            await Denied(() => ConfigAssetTools.HttpDownload(
                "http://127.0.0.1:9/never", Path.Combine(secrets, "download.bin")),
                "http download destination");
            var src = Path.Combine(secrets, "x.db").Replace('\\', '/');
            await Denied(() => SqliteTools.Query("file:" + src + "?mode=ro", "select 1"),
                "sqlite query");
            await Denied(() => SqliteTools.Execute("file:///" + src + "?mode=ro", "select 1"),
                "sqlite execute file URI");
            await Denied(() => SqliteTools.Schema("file:" + src + "?mode=ro"),
                "sqlite schema");
            await Denied(() => SqliteTools.Backup("file:" + src + "?mode=ro",
                Path.Combine(root, "backup.db")), "sqlite backup source");
            Assert(!File.Exists(p) && !File.Exists(Path.Combine(secrets, "download.bin")),
                "D87: config writer or download created protected file");

            var siblingCopy = Path.Combine(root, "copy-sibling");
            FileTools.Copy(sibling, siblingCopy);
            Assert(File.Exists(Path.Combine(siblingCopy, "safe.txt")), "D87: safe copy stopped working");
            FileTools.Delete(siblingCopy);
            var safeArchive = Path.Combine(root, "safe.zip");
            ConfigAssetTools.ArchiveCreate(sibling, safeArchive);
            ConfigAssetTools.ArchiveExtract(safeArchive, parent);
            Assert(File.Exists(Path.Combine(parent, "safe.txt")), "D87: safe extraction stopped working");
            FileTools.Delete(sibling);
        }
        finally
        {
            overrideField.SetValue(null, null);
            foreach (var link in new[] { broken, innerBroken, alias })
                try { Directory.Delete(link); } catch (Exception) { }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
