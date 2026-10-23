using System.Text.Json;
using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    public static async Task RunReceiptCountsAsync()
    {
        await RunN3bCommittedDiffsAsync();
        await using var workspace = await TestWorkspace.CreateAsync();
        await workspace.WriteUtf8Async("one.txt", "old\nkeep\n");
        await workspace.WriteUtf8Async("two.txt", "left\n");
        var engine = workspace.CreateEngine();
        var one = await engine.ReadSourceAsync(workspace.PathInWorkspace("one.txt"), 1, 20, CancellationToken.None);
        var two = await engine.ReadSourceAsync(workspace.PathInWorkspace("two.txt"), 1, 20, CancellationToken.None);
        const string patch = "--- a/one.txt\n+++ b/one.txt\n@@ -1,2 +1,2 @@\n-old\n+new\n keep\n--- a/two.txt\n+++ b/two.txt\n@@ -1 +1,2 @@\n left\n+right\n";
        var revisions = new Dictionary<string, string>
        {
            ["one.txt"] = one.Revision, ["two.txt"] = two.Revision,
        };
        var result = await engine.ApplyPatchAsync(workspace.Root, "n3-receipt-counts", patch,
            "unified-diff", revisions, false, CancellationToken.None);
        Assert(result.Success, "N3 receipt fixture failed: " + result.Error?.Message);
        var json = JsonSerializer.SerializeToElement(result);
        var receipts = json.GetProperty("Files").EnumerateArray().ToArray();
        Assert(receipts.Length == 2, "N3 receipt fixture must commit two files.");
        Assert(receipts[0].TryGetProperty("AddedLines", out var added),
            "N3: committed receipts do not expose AddedLines.");
        AssertEqual(1, added.GetInt32(), "N3 changed-line additions");
        AssertEqual(1, receipts[0].GetProperty("RemovedLines").GetInt32(), "N3 changed-line removals");
        AssertEqual(1, receipts[1].GetProperty("AddedLines").GetInt32(), "N3 inserted-line additions");
        AssertEqual(0, receipts[1].GetProperty("RemovedLines").GetInt32(), "N3 inserted-line removals");
        var replay = await engine.ApplyPatchAsync(workspace.Root, "n3-receipt-counts", patch,
            "unified-diff", revisions, false, CancellationToken.None);
        Assert(replay.Success && replay.Replayed, "N3 receipt replay failed.");
        AssertEqual(JsonSerializer.Serialize(result.Files), JsonSerializer.Serialize(replay.Files),
            "N3 persisted replay counts");
        await workspace.WriteUtf8Async("gone.txt", "remove\n");
        var gone = await engine.ReadSourceAsync(workspace.PathInWorkspace("gone.txt"), 1, 10, CancellationToken.None);
        const string lifecyclePatch = "--- /dev/null\n+++ b/added.txt\n@@ -0,0 +1,2 @@\n+one\n+two\n--- a/gone.txt\n+++ /dev/null\n@@ -1 +0,0 @@\n-remove\n";
        var lifecycle = await engine.ApplyPatchAsync(workspace.Root, "n3-add-delete-counts", lifecyclePatch,
            "unified-diff", new Dictionary<string, string> { ["gone.txt"] = gone.Revision }, false, CancellationToken.None);
        Assert(lifecycle.Success, "N3 add/delete fixture failed: " + lifecycle.Error?.Message);
        AssertEqual((int?)2, lifecycle.Files.Single(file => file.Operation == "add").AddedLines, "N3 added file count");
        AssertEqual((int?)1, lifecycle.Files.Single(file => file.Operation == "delete").RemovedLines, "N3 deleted file count");
        foreach (var example in new[] {
            (Before: "", After: "first\n", Added: 1, Removed: 0),
            (Before: "", After: "\n", Added: 1, Removed: 0),
            (Before: "remove\n", After: "", Added: 0, Removed: 1),
            (Before: "-- comment\nkeep\n", After: "keep\n", Added: 0, Removed: 1),
            (Before: "same\r\n", After: "same\n", Added: 0, Removed: 0),
            (Before: "same", After: "same\n", Added: 0, Removed: 0),
        })
        {
            var counts = Talvora.WorklogDiff.TextDiffBuilder.Count(example.Before, example.After);
            AssertEqual((int?)example.Added, counts.Added, "N3 boundary additions");
            AssertEqual((int?)example.Removed, counts.Removed, "N3 boundary removals");
        }
    }
}
