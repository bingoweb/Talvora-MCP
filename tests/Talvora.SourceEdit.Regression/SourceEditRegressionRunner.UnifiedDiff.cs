using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    private static async Task UnifiedDiffCompatibilityAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("update.txt", "one\ntwo\n");
        await fixture.WriteUtf8Async("delete.txt", "gone\n");
        await fixture.WriteUtf8Async("rename.txt", "move\n");

        var engine = fixture.CreateEngine();
        var updatePath = fixture.PathInWorkspace("update.txt");
        var deletePath = fixture.PathInWorkspace("delete.txt");
        var renamePath = fixture.PathInWorkspace("rename.txt");
        var revisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["update.txt"] = await RevisionAsync(engine, updatePath),
            ["delete.txt"] = await RevisionAsync(engine, deletePath),
            ["rename.txt"] = await RevisionAsync(engine, renamePath),
        };

        var patch = string.Join(
            "\n",
            new[]
            {
                "diff --git a/update.txt b/update.txt",
                "--- a/update.txt",
                "+++ b/update.txt",
                "@@ -1,2 +1,2 @@",
                " one",
                "-two",
                "+TWO",
                "diff --git a/new.txt b/new.txt",
                "new file mode 100644",
                "--- /dev/null",
                "+++ b/new.txt",
                "@@ -0,0 +1 @@",
                "+new",
                "diff --git a/delete.txt b/delete.txt",
                "deleted file mode 100644",
                "--- a/delete.txt",
                "+++ /dev/null",
                "@@ -1 +0,0 @@",
                "-gone",
                "diff --git a/rename.txt b/renamed.txt",
                "similarity index 100%",
                "rename from rename.txt",
                "rename to renamed.txt",
            });

        var result = await engine.ApplyPatchAsync(
            fixture.Root,
            NewTransactionId(),
            patch,
            "unified-diff",
            revisions,
            validateSyntax: true,
            CancellationToken.None);

        Assert(result.Success, result.Error?.Message ?? "Unified diff failed.");
        AssertEqual("one\nTWO\n", await File.ReadAllTextAsync(updatePath), "Unified diff update mismatch.");
        Assert(!File.Exists(deletePath), "Unified diff delete did not remove source.");
        Assert(!File.Exists(renamePath), "Unified diff rename left source path.");
        AssertEqual(
            "move\n",
            await File.ReadAllTextAsync(fixture.PathInWorkspace("renamed.txt")),
            "Unified diff rename content mismatch.");
        AssertEqual(
            "new" + Environment.NewLine,
            await File.ReadAllTextAsync(fixture.PathInWorkspace("new.txt")),
            "Unified diff add content mismatch.");
        AssertEqual(4, result.Files.Count, "Unified diff receipt file count mismatch.");
    }

    private static async Task UnifiedDiffExactGuardsAsync()
    {
        try
        {
            _ = UnifiedDiffParser.Parse(
                string.Join(
                    "\n",
                    new[]
                    {
                        "diff --git a/source.txt b/copy.txt",
                        "similarity index 100%",
                        "copy from source.txt",
                        "copy to copy.txt",
                    }));
            throw new InvalidOperationException(
                "Git copy metadata unexpectedly parsed.");
        }
        catch (SourceEditDomainException ex)
        {
            AssertEqual(
                SourceEditCodes.PatchParseError,
                ex.Code,
                "Git copy metadata returned the wrong domain code.");
            Assert(
                ex.Message.Contains(
                    "talvora_apply_patch",
                    StringComparison.Ordinal) &&
                !ex.Message.Contains(
                    "talvora_apply_edits",
                    StringComparison.Ordinal),
                "Unified-diff copy fallback no longer stays on the primary source editor.");
        }

        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("guard.txt", "one\ntwo\n");
        var engine = fixture.CreateEngine();
        var path = fixture.PathInWorkspace("guard.txt");
        var revision = await RevisionAsync(engine, path);

        var patch = string.Join(
            "\n",
            new[]
            {
                "--- a/guard.txt",
                "+++ b/guard.txt",
                "@@ -1,2 +1,2 @@",
                " one",
                "-WRONG",
                "+TWO",
            });

        var missingRevision = await engine.ApplyPatchAsync(
            fixture.Root,
            NewTransactionId(),
            patch,
            "unified-diff",
            expectedRevisions: null,
            validateSyntax: true,
            CancellationToken.None);
        AssertError(missingRevision, SourceEditCodes.ExpectedRevisionMismatch);

        var wrongContext = await engine.ApplyPatchAsync(
            fixture.Root,
            NewTransactionId(),
            patch,
            "unified-diff",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["guard.txt"] = revision,
            },
            validateSyntax: true,
            CancellationToken.None);
        AssertError(wrongContext, SourceEditCodes.PatchContextNotFound);
        AssertEqual(
            "one\ntwo\n",
            await File.ReadAllTextAsync(path),
            "Unified diff exact guard modified source.");

        var stalePatch = patch.Replace("-WRONG", "-two", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, "external\n");
        var stale = await engine.ApplyPatchAsync(
            fixture.Root,
            NewTransactionId(),
            stalePatch,
            "unified-diff",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["guard.txt"] = revision,
            },
            validateSyntax: true,
            CancellationToken.None);
        AssertError(stale, SourceEditCodes.ExpectedRevisionMismatch);
        AssertEqual(
            "external\n",
            await File.ReadAllTextAsync(path),
            "Unified diff stale revision overwrote external content.");
    }
}
