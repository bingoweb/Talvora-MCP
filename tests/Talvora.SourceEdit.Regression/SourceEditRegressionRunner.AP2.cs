using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    // AP2: the agent dialect is accepted and every rejection says what to fix; matching itself stays exact.
    private static async Task Ap2AgentPatchToleranceAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async("a.txt", "one\n\n    two\nthree\n");
        var engine = fixture.CreateEngine();
        var path = fixture.PathInWorkspace("a.txt");

        var revision = await RevisionAsync(engine, path);
        var result = await engine.ApplyPatchAsync(fixture.Root, NewTransactionId(), string.Join("\n",
            "*** Begin Patch", "*** Update File: a.txt", $"*** Revision: {revision}", "@@",
            " one", "", "-    two", "+    TWO", " three", "*** End of File", "", "*** End Patch"),
            true, CancellationToken.None);
        Assert(result.Success, result.Error?.Message ?? "AP2 OpenAI envelope, blank context or End of File rejected.");
        AssertEqual("one\n\n    TWO\nthree\n", await File.ReadAllTextAsync(path), "AP2 patch applied wrongly.");

        revision = await RevisionAsync(engine, path);
        result = await engine.ApplyPatchAsync(fixture.Root, NewTransactionId(), string.Join("\n",
            "*** Begin Talvora Patch", "*** Update File: a.txt", $"*** Revision: {revision}", "@@",
            " one", " ", "-  TWO", "+  2", "*** End Talvora Patch"), true, CancellationToken.None);
        AssertError(result, SourceEditCodes.PatchContextNotFound);
        AssertEqual("    TWO", result.Error!.Details?["fileLine"], "AP2 whitespace hint missing.");
        AssertEqual("3", result.Error.Details?["fileLineNumber"], "AP2 whitespace hint line number missing.");

        result = await engine.ApplyPatchAsync(fixture.Root, NewTransactionId(), string.Join("\n",
            "*** Begin Talvora Patch", "*** Update File: a.txt", "@@", "-one", "+1", "*** End Talvora Patch"),
            true, CancellationToken.None);
        AssertError(result, SourceEditCodes.PatchParseError);
        Assert(result.Error!.Message.Contains("talvora_read_source", StringComparison.Ordinal),
            "AP2 missing-revision message does not say how to fix it.");

        result = await engine.ApplyPatchAsync(fixture.Root, NewTransactionId(), string.Join("\n",
            "*** Begin Talvora Patch", "*** Update File: a.txt", $"*** Revision: {revision}", "@@",
            "one", "-three", "*** End Talvora Patch"), true, CancellationToken.None);
        AssertError(result, SourceEditCodes.PatchParseError);
        Assert(result.Error!.Message.Contains("'one'", StringComparison.Ordinal),
            "AP2 hunk-prefix message does not quote the line.");

        result = await engine.ApplyPatchAsync(fixture.Root, NewTransactionId(), string.Join("\n",
            "*** Begin Talvora Patch", "*** Update File: " + path, $"*** Revision: {revision}", "@@",
            "-one", "+1", "*** End Talvora Patch"), true, CancellationToken.None);
        AssertError(result, SourceEditCodes.PathOutsideWorkspace);
        Assert(result.Error!.Message.Contains("'a.txt'", StringComparison.Ordinal),
            "AP2 absolute-path message does not suggest the relative path.");
        AssertEqual("one\n\n    TWO\nthree\n", await File.ReadAllTextAsync(path), "AP2 a rejected patch changed the file.");
    }
}
