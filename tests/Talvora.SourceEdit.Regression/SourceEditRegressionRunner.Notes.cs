using System.Diagnostics;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    // OB1: the vault note writer writes plain notes and refuses secrets, escapes, hidden folders and junctions.
    private static async Task RunVaultNoteWriterAsync()
    {
        var parent = Path.Combine(Path.GetTempPath(), "talvora-ob1-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "Kasa");
        var outside = Path.Combine(parent, "repo-docs");
        var link = Path.Combine(root, "Bağlantı");
        Directory.CreateDirectory(Path.Combine(root, ".obsidian"));
        Directory.CreateDirectory(outside);
        try
        {
            var created = await NoteTools.WriteAsync(root, "40 Oturum Notları/İlk not.md", "# Başlık\n", "create");
            AssertEqual(Path.Combine("40 Oturum Notları", "İlk not.md"), created.Path, "OB1: create returns the vault-relative path.");
            await NoteTools.WriteAsync(root, "40 Oturum Notları/İlk not.md", "ikinci satır\n", "append");
            AssertEqual(
                "# Başlık\nikinci satır\n",
                await File.ReadAllTextAsync(Path.Combine(root, "40 Oturum Notları", "İlk not.md")),
                "OB1: append adds after the existing text.");

            await AssertNoteRefusedAsync(root, "a.md", "api_key=abcdef123456", "create", "OB1: secrets are refused.");
            await AssertNoteRefusedAsync(root, "b.md", "şifre: Gizli123", "create", "OB1: Turkish password lines are refused.");
            await AssertNoteRefusedAsync(root, "c.md", "-----BEGIN OPENSSH PRIVATE KEY-----", "create", "OB1: private keys are refused.");
            await AssertNoteRefusedAsync(root, "e.md", "<% tp.date.now() %>", "create", "OB1: Templater code is refused.");
            await AssertNoteRefusedAsync(root, "../dışarı.md", "x", "create", "OB1: parent segments are refused.");
            await AssertNoteRefusedAsync(root, ".obsidian/app.md", "x", "create", "OB1: hidden folders are refused.");
            await AssertNoteRefusedAsync(root, "Repo belgeleri/STATUS.md", "x", "create", "OB1: linked docs folders are refused by name.");
            await AssertNoteRefusedAsync(root, "not.txt", "x", "create", "OB1: only .md notes are written.");
            await AssertNoteRefusedAsync(root, "40 Oturum Notları/İlk not.md", "x", "create", "OB1: create never overwrites.");
            await AssertNoteRefusedAsync(root, "yok.md", "x", "append", "OB1: append needs an existing note.");
            await AssertNoteRefusedAsync(root, "d.md", "x", "replace", "OB1: unknown modes are refused.");

            var mklink = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, outside })
            {
                mklink.ArgumentList.Add(argument);
            }

            using (var process = Process.Start(mklink)!)
            {
                await process.WaitForExitAsync();
                AssertEqual(0, process.ExitCode, "OB1: the test junction was created.");
            }

            await AssertNoteRefusedAsync(root, "Bağlantı/not.md", "x", "create", "OB1: junction folders are refused.");
            Assert(!File.Exists(Path.Combine(outside, "not.md")), "OB1: nothing was written through the junction.");
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            Directory.Delete(parent, recursive: true);
        }
    }

    private static async Task AssertNoteRefusedAsync(string root, string path, string content, string mode, string message)
    {
        try
        {
            await NoteTools.WriteAsync(root, path, content, mode);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
