using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    private static async Task ExactPatchSuccessAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "src/sample.txt",
            "alpha\r\nbeta\r\ngamma\r\n");
        var engine = fixture.CreateEngine();
        var path = fixture.PathInWorkspace("src/sample.txt");
        var revision = await RevisionAsync(engine, path);

        var result = await engine.ApplyPatchAsync(
            fixture.Root,
            NewTransactionId(),
            PatchUpdate(
                "src/sample.txt",
                revision,
                " alpha",
                "-beta",
                "+BETA",
                " gamma"),
            validateSyntax: true,
            CancellationToken.None);

        Assert(result.Success, result.Error?.Message ?? "Patch failed.");
        AssertEqual("committed", result.Status, "Unexpected status.");
        AssertEqual(
            "alpha\r\nBETA\r\ngamma\r\n",
            await File.ReadAllTextAsync(path),
            "Exact patch content mismatch.");
        AssertEqual(1, result.Files.Count, "Expected one changed file.");
        AssertEqual(
            revision,
            result.Files[0].BeforeRevision,
            "Receipt before revision mismatch.");
        Assert(
            !string.IsNullOrWhiteSpace(
                result.Files[0].AfterRevision),
            "Receipt after revision is missing.");
    }

    private static async Task ContextMissingAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "missing.cs",
            "one\ntwo\n");
        var engine = fixture.CreateEngine();
        var path = fixture.PathInWorkspace("missing.cs");
        var revision = await RevisionAsync(engine, path);
        var original = await File.ReadAllTextAsync(path);

        var result = await engine.ApplyPatchAsync(
            fixture.Root,
            NewTransactionId(),
            PatchUpdate(
                "missing.cs",
                revision,
                "-not-there",
                "+replacement"),
            true,
            CancellationToken.None);

        AssertError(
            result,
            SourceEditCodes.PatchContextNotFound);
        AssertEqual(
            original,
            await File.ReadAllTextAsync(path),
            "Context-missing patch modified the file.");
    }

    private static async Task ContextAmbiguousAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "ambiguous.cs",
            "old\nmid\nold\n");
        var engine = fixture.CreateEngine();
        var path = fixture.PathInWorkspace("ambiguous.cs");
        var revision = await RevisionAsync(engine, path);
        var original = await File.ReadAllTextAsync(path);

        var result = await engine.ApplyPatchAsync(
            fixture.Root,
            NewTransactionId(),
            PatchUpdate(
                "ambiguous.cs",
                revision,
                "-old",
                "+new"),
            true,
            CancellationToken.None);

        AssertError(
            result,
            SourceEditCodes.PatchContextAmbiguous);
        AssertEqual(
            original,
            await File.ReadAllTextAsync(path),
            "Ambiguous patch modified the file.");
    }

    private static async Task StaleRevisionAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "stale.cs",
            "before\n");
        var engine = fixture.CreateEngine();
        var path = fixture.PathInWorkspace("stale.cs");
        var revision = await RevisionAsync(engine, path);

        await File.WriteAllTextAsync(
            path,
            "external\n");

        var result = await engine.ApplyPatchAsync(
            fixture.Root,
            NewTransactionId(),
            PatchUpdate(
                "stale.cs",
                revision,
                "-before",
                "+after"),
            true,
            CancellationToken.None);

        AssertError(
            result,
            SourceEditCodes.ExpectedRevisionMismatch);
        AssertEqual(
            "external\n",
            await File.ReadAllTextAsync(path),
            "Stale transaction overwrote external content.");
    }

    private static async Task MultiFilePreflightFailureAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("a.cs", "A0\n");
        await fixture.WriteUtf8Async("b.cs", "B0\n");
        var engine = fixture.CreateEngine();
        var a = fixture.PathInWorkspace("a.cs");
        var b = fixture.PathInWorkspace("b.cs");
        var aRevision = await RevisionAsync(engine, a);
        var bRevision = await RevisionAsync(engine, b);

        await File.WriteAllTextAsync(b, "B-external\n");

        var result = await engine.ApplyEditsAsync(
            fixture.Root,
            NewTransactionId(),
            [
                Update(
                    "a.cs",
                    aRevision,
                    0,
                    0,
                    0,
                    2,
                    "A1",
                    "A0"),
                Update(
                    "b.cs",
                    bRevision,
                    0,
                    0,
                    0,
                    2,
                    "B1",
                    "B0"),
            ],
            true,
            CancellationToken.None);

        AssertError(
            result,
            SourceEditCodes.ExpectedRevisionMismatch);
        AssertEqual(
            "A0\n",
            await File.ReadAllTextAsync(a),
            "Preflight failure partially changed file A.");
        AssertEqual(
            "B-external\n",
            await File.ReadAllTextAsync(b),
            "Preflight failure overwrote external file B.");
    }

    private static async Task SyntaxValidationFailureAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "settings.json",
            "{\n  \"value\": 1\n}\n");
        var engine = fixture.CreateEngine();
        var path = fixture.PathInWorkspace("settings.json");
        var revision = await RevisionAsync(engine, path);
        var original = await File.ReadAllTextAsync(path);

        var result = await engine.ApplyEditsAsync(
            fixture.Root,
            NewTransactionId(),
            [
                Update(
                    "settings.json",
                    revision,
                    1,
                    11,
                    1,
                    12,
                    "}",
                    "1"),
            ],
            true,
            CancellationToken.None);

        AssertError(
            result,
            SourceEditCodes.ValidationFailed);
        AssertEqual(
            original,
            await File.ReadAllTextAsync(path),
            "Syntax validation failure modified JSON.");
    }

    private static async Task OverlappingStructuredEditsAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "overlap.cs",
            "abcdef\n");
        var engine = fixture.CreateEngine();
        var path = fixture.PathInWorkspace("overlap.cs");
        var revision = await RevisionAsync(engine, path);

        var result = await engine.ApplyEditsAsync(
            fixture.Root,
            NewTransactionId(),
            [
                new SourceEditChangeInput
                {
                    Operation = "update",
                    Path = "overlap.cs",
                    ExpectedRevision = revision,
                    Edits =
                    [
                        new SourceEditTextRangeInput
                        {
                            StartLine = 0,
                            StartCharacter = 1,
                            EndLine = 0,
                            EndCharacter = 4,
                            NewText = "X",
                        },
                        new SourceEditTextRangeInput
                        {
                            StartLine = 0,
                            StartCharacter = 3,
                            EndLine = 0,
                            EndCharacter = 5,
                            NewText = "Y",
                        },
                    ],
                },
            ],
            true,
            CancellationToken.None);

        AssertError(
            result,
            SourceEditCodes.PatchOverlappingEdits);
        AssertEqual(
            "abcdef\n",
            await File.ReadAllTextAsync(path),
            "Overlapping edits modified the source.");
    }
}
