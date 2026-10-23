using Talvora.SourceEditing;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    private static async Task RunN3bCommittedDiffsAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        var engine = fixture.CreateEngine();
        await fixture.WriteUtf8Async("one.sql", "-- comment\nkeep\n");
        await fixture.WriteUtf8Async("two.txt", "before\n");
        var one = await engine.ReadSourceAsync(fixture.PathInWorkspace("one.sql"), 1, 20, CancellationToken.None);
        var two = await engine.ReadSourceAsync(fixture.PathInWorkspace("two.txt"), 1, 20, CancellationToken.None);
        const string patch = "--- a/one.sql\n+++ b/one.sql\n@@ -1,2 +1 @@\n--- comment\n keep\n--- a/two.txt\n+++ b/two.txt\n@@ -1 +1 @@\n-before\n+after\n";
        var revisions = new Dictionary<string, string> { ["one.sql"] = one.Revision, ["two.txt"] = two.Revision };
        var applied = await engine.ApplyPatchAsync(fixture.Root, "n3b-real-preview", patch,
            "unified-diff", revisions, false, CancellationToken.None);
        Assert(applied.Success, "N3b unified commit failed: " + applied.Error?.Message);
        AssertEqual(2, applied.Files.Count(file => file.CodePreview?.Contains("@@ FILE ") == true), "N3b committed file headers");
        AssertEqual((int?)1, applied.Files.Single(file => file.NewPath!.EndsWith("one.sql")).RemovedLines, "N3b SQL comment removal");
        Assert(applied.Files[0].CodePreview!.Contains("--- comment"), "N3b actual removed comment missing");
        var replay = await engine.ApplyPatchAsync(fixture.Root, "n3b-real-preview", patch,
            "unified-diff", revisions, false, CancellationToken.None);
        Assert(replay.Replayed && replay.Files[0].CodePreview == applied.Files[0].CodePreview,
            "N3b replay did not retain committed preview");
        var rejected = await engine.ApplyPatchAsync(fixture.Root, "n3b-stale-preview", patch,
            "unified-diff", revisions, false, CancellationToken.None);
        Assert(!rejected.Success && rejected.Files.All(file => file.CodePreview is null), "N3b stale edit leaked preview");
        AssertEqual("keep\n", await File.ReadAllTextAsync(fixture.PathInWorkspace("one.sql")), "N3b stale edit changed file");

        await fixture.WriteUtf8Async("sample.json", "{\"value\":1}\n");
        foreach (var run in new Func<Task>[] {
            () => DeveloperTools.ReplaceText(fixture.PathInWorkspace("two.txt"), "after", "wrong"),
            () => ConfigAssetTools.JsonSet(fixture.PathInWorkspace("sample.json"), "value", "2")
        })
        {
            try { await run(); throw new InvalidOperationException("N3b legacy workspace edit was accepted."); }
            catch (SourceEditDomainException ex) when (ex.Code == SourceEditCodes.PolicyViolation) { }
        }
        var copied = FileTools.Copy(fixture.PathInWorkspace("two.txt"),
            fixture.PathInWorkspace("copy.txt"), explicitAdmin: true);
        Assert(copied.CodeEvidence?.CodePreview?.Contains("+after") == true, "N3b allowed workspace copy lacks actual diff");
        var moved = FileTools.Move(fixture.PathInWorkspace("copy.txt"),
            fixture.PathInWorkspace("renamed.txt"), explicitAdmin: true);
        Assert(moved.CodeEvidence?.CodePreview?.Contains("--- a/copy.txt") == true &&
            moved.CodeEvidence.CodePreview.Contains("+++ b/renamed.txt"), "N3b workspace move lacks rename headers");
        var ordinary = Path.Combine(fixture.OrdinaryRoot, "private.txt");
        var outside = FileTools.Copy(fixture.PathInWorkspace("two.txt"), ordinary);
        Assert(outside.CodeEvidence?.CodePreview is null && outside.CodeEvidence?.Summary == "1 dosya değişti",
            "N3b outside-workspace copy leaked code");
    }
}
