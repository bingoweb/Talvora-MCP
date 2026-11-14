using System.Diagnostics;
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

            // D89: writes into the set are allowed now (SourceEditRegressionRunner.D89.cs); reads stay denied.
            var src = Path.Combine(secrets, "x.db").Replace('\\', '/');
            await Denied(() => SqliteTools.Query("file:" + src + "?mode=ro", "select 1"),
                "sqlite query");
            await Denied(() => SqliteTools.Schema("file:" + src + "?mode=ro"),
                "sqlite schema");
            await Denied(() => SqliteTools.Backup("file:" + src + "?mode=ro",
                Path.Combine(root, "backup.db")), "sqlite backup source");

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
