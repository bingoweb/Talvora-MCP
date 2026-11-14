using System.IO.Compression;
using System.Reflection;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    // D89 (user rule "Security stays proportionate"): the secret guard keeps only leak and loss protection.
    public static async Task D89ProportionateGuardAsync()
    {
        const string denied = "Bu konum gizli anahtarlar için ayrılmıştır; Talvora dosya araçları burayı okumaz ve değiştirmez.";
        var root = Path.Combine(Path.GetTempPath(), "talvora-d89-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(root, "protected-parent");
        var secrets = Path.Combine(parent, "Secrets");
        var key = Path.Combine(parent, "Context7", "api-key");
        var plain = Path.Combine(root, "plain");
        Directory.CreateDirectory(secrets);
        Directory.CreateDirectory(Path.GetDirectoryName(key)!);
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(secrets, "x.txt"), "needle fixture-only");
        File.WriteAllText(key, "fixture-key");
        for (var i = 0; i < 30; i++)
            File.WriteAllText(Path.Combine(plain, $"f{i:00}.txt"), "needle");

        var helper = typeof(FileTools).Assembly.GetType("Talvora.Tools.ProtectedSecretPaths")!;
        var overrideField = helper.GetField("EntriesOverrideForTests",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("D89: test entries override missing");
        static async Task Denied(Func<Task> action, string label)
        {
            try { await action(); }
            catch (UnauthorizedAccessException ex) when (ex.Message == denied) { return; }
            throw new InvalidOperationException("D89: a leak path is no longer denied: " + label);
        }
        static async Task Allowed(Func<Task> action, string label)
        {
            try { await action(); }
            catch (UnauthorizedAccessException ex) when (ex.Message == denied)
            {
                throw new InvalidOperationException("D89 RED: a write into the secret set is still refused: " + label);
            }
        }
        static Task Sync(Action action) { action(); return Task.CompletedTask; }

        overrideField.SetValue(null, new[] { secrets, key });
        try
        {
            // Writes into the set are allowed: they put nothing into the chat and lose nothing.
            var text = Path.Combine(secrets, "w.txt");
            await Allowed(() => FileTools.WriteText(text, "one"), "write_text");
            await Allowed(() => ConfigAssetTools.AppendText(text, " two"), "append_text");
            await Allowed(() => DeveloperTools.ReplaceText(text, "two", "three"), "replace_text");
            await Allowed(() => DeveloperTools.WriteBytes(Path.Combine(secrets, "b.bin"), "AAEC"), "write_bytes");
            File.WriteAllText(Path.Combine(secrets, "c.json"), "{}");
            File.WriteAllText(Path.Combine(secrets, "c.yaml"), "a: 1\n");
            File.WriteAllText(Path.Combine(secrets, "c.toml"), "a = 1\n");
            File.WriteAllText(Path.Combine(secrets, "c.ini"), "[a]\nk=1\n");
            File.WriteAllText(Path.Combine(secrets, "c.env"), "A=1\n");
            File.WriteAllText(Path.Combine(secrets, "c.xml"), "<root><x>0</x></root>");
            var writers = new (string, Func<Task>)[]
            {
                ("json-set", () => ConfigAssetTools.JsonSet(Path.Combine(secrets, "c.json"), "/x", "1")),
                ("json-delete", () => ConfigAssetTools.JsonDelete(Path.Combine(secrets, "c.json"), "/x")),
                ("yaml-set", () => StructuredConfigTools.YamlSet(Path.Combine(secrets, "c.yaml"), "/x", "1")),
                ("yaml-delete", () => StructuredConfigTools.YamlDelete(Path.Combine(secrets, "c.yaml"), "/x")),
                ("toml-set", () => StructuredConfigTools.TomlSet(Path.Combine(secrets, "c.toml"), "/x", "1")),
                ("toml-delete", () => StructuredConfigTools.TomlDelete(Path.Combine(secrets, "c.toml"), "/x")),
                ("ini-set", () => ConfigFormatTools.IniSet(Path.Combine(secrets, "c.ini"), "a", "x", "1")),
                ("ini-delete", () => ConfigFormatTools.IniDelete(Path.Combine(secrets, "c.ini"), "a", "x")),
                ("dotenv-set", () => ConfigFormatTools.DotenvSet(Path.Combine(secrets, "c.env"), "X", "1")),
                ("dotenv-delete", () => ConfigFormatTools.DotenvDelete(Path.Combine(secrets, "c.env"), "X")),
                ("xml-set", () => Sync(() => ConfigFormatTools.XmlSet(Path.Combine(secrets, "c.xml"), "/root/x", "1"))),
                ("xml-delete", () => Sync(() => ConfigFormatTools.XmlDelete(Path.Combine(secrets, "c.xml"), "/root/x"))),
            };
            foreach (var (label, action) in writers) await Allowed(action, label);

            var incoming = Path.Combine(root, "incoming");
            Directory.CreateDirectory(Path.Combine(incoming, "Secrets"));
            File.WriteAllText(Path.Combine(incoming, "Secrets", "y.txt"), "copied in");
            await Allowed(() => Sync(() => FileTools.Copy(incoming, parent, overwrite: true)), "copy into a parent");
            Assert(File.ReadAllText(Path.Combine(secrets, "y.txt")) == "copied in", "D89: copy into the set did not land");
            await Allowed(() => Sync(() => FileTools.Copy(Path.Combine(plain, "f00.txt"), Path.Combine(secrets, "f00.txt"))),
                "copy a file into the set");
            await Allowed(() => Sync(() => FileTools.Move(Path.Combine(plain, "f01.txt"), Path.Combine(secrets, "f01.txt"))),
                "move a file into the set");
            var zip = Path.Combine(root, "incoming.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("Secrets/z.txt").Open()))
                writer.Write("extracted in");
            await Allowed(() => Sync(() => ConfigAssetTools.ArchiveExtract(zip, parent, overwrite: true)), "extract into a parent");
            Assert(File.ReadAllText(Path.Combine(secrets, "z.txt")) == "extracted in", "D89: extract into the set did not land");
            await Allowed(() => DeveloperTools.FileHash(Path.Combine(secrets, "x.txt")), "file_hash (how secrets are compared)");
            await Allowed(() => SqliteTools.Execute(Path.Combine(secrets, "d89.db"), "create table t(x)"), "sqlite execute");
            var outsideDb = Path.Combine(root, "outside.db");
            await SqliteTools.Execute(outsideDb, "create table t(x)");
            await Allowed(() => SqliteTools.Backup(outsideDb, Path.Combine(secrets, "backup.db")), "sqlite backup into the set");
            try
            {
                await ConfigAssetTools.HttpDownload("http://127.0.0.1:9/never", Path.Combine(secrets, "download.bin"));
            }
            catch (UnauthorizedAccessException ex) when (ex.Message == denied)
            {
                throw new InvalidOperationException("D89 RED: a download into the secret set is still refused");
            }
            catch (Exception)
            {
                // The discard port refuses the connection: the guard is no longer what stops it.
            }

            // Leaks and losses stay denied.
            await Denied(() => FileTools.ReadText(Path.Combine(secrets, "x.txt")), "read_text");
            await Denied(() => DeveloperTools.ReadBytes(Path.Combine(secrets, "x.txt")), "read_bytes");
            await Denied(() => ConfigFormatTools.DotenvGet(Path.Combine(secrets, "c.env"), "A"), "dotenv_get");
            await Denied(() => SqliteTools.Query(Path.Combine(secrets, "d89.db"), "select 1"), "sqlite query");
            await Denied(() => Sync(() => FileTools.Copy(secrets, Path.Combine(root, "copied-out"))), "copy out of the set");
            await Denied(() => Sync(() => FileTools.Delete(parent)), "delete a folder that contains the set");
            await Denied(() => Sync(() => ConfigAssetTools.ArchiveCreate(parent, Path.Combine(root, "out.zip"))),
                "archive a folder that contains the set");

            // An ordinary search does not open every entry for the guard; a search above the set still skips it.
            var counterField = helper.GetField("FinalPathResolutionsForTests",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new InvalidOperationException("D89 RED: final-path resolution counter missing");
            counterField.SetValue(null, 0);
            var ordinary = await DeveloperTools.SearchText(plain, "needle");
            var opened = (int)counterField.GetValue(null)!;
            Assert(ordinary.Matches.Count == 29, $"D89: ordinary search found {ordinary.Matches.Count} matches");
            Assert(opened <= 8, $"D89 RED: an ordinary search opened {opened} entries for the secret guard");
            var above = await DeveloperTools.SearchText(root, "needle");
            Assert(above.Matches.All(m => !m.Path.StartsWith(secrets, StringComparison.OrdinalIgnoreCase)),
                "D89: a search above the set returned a protected file");
            Assert(above.Matches.Count >= 29, "D89: a search above the set lost ordinary matches");
        }
        finally
        {
            overrideField.SetValue(null, null);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
