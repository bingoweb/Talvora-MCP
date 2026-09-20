using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    private static async Task GeneratedEditIdempotencyAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "generated.txt",
            "before\n");
        var engine =
            fixture.CreateEngine();
        var path =
            fixture.PathInWorkspace(
                "generated.txt");
        var revision =
            await RevisionAsync(
                engine,
                path);
        var transactionId =
            NewTransactionId();
        var requestHash =
            "sha256:" +
            new string(
                'a',
                64);
        var factoryCalls = 0;

        var first =
            await engine.ApplyGeneratedEditsAsync(
                fixture.Root,
                transactionId,
                requestHash,
                validateSyntax: true,
                (_, _) =>
                {
                    factoryCalls++;
                    return Task.FromResult<IReadOnlyList<SourceEditChangeInput>>(
                    [
                        Update(
                            "generated.txt",
                            revision,
                            0,
                            0,
                            0,
                            6,
                            "after",
                            "before"),
                    ]);
                },
                CancellationToken.None);
        Assert(
            first.Success,
            first.Error?.Message ??
            "Generated source edit failed.");
        AssertEqual(
            1,
            factoryCalls,
            "Generated edit factory call count mismatch.");

        var replay =
            await engine.ApplyGeneratedEditsAsync(
                fixture.Root,
                transactionId,
                requestHash,
                validateSyntax: true,
                (_, _) =>
                {
                    throw new InvalidOperationException(
                        "Generated edit factory must not run on durable receipt replay.");
                },
                CancellationToken.None);
        Assert(
            replay.Success &&
            replay.Replayed,
            replay.Error?.Message ??
            "Generated source edit replay failed.");
        AssertEqual(
            1,
            factoryCalls,
            "Generated edit factory reran during idempotent replay.");
        AssertEqual(
            "after\n",
            await File.ReadAllTextAsync(
                path),
            "Generated edit replay changed source content.");
    }

    private static Task StructuralUnicodePositionAsync()
    {
        const string line =
            "const x = \"😀\"; console.log(x);";
        var expectedStart =
            line.IndexOf(
                "console",
                StringComparison.Ordinal);
        var expectedEnd =
            line.IndexOf(
                ");",
                StringComparison.Ordinal) +
            1;
        var actualStart =
            AstGrepStructuralEditEngine
                .ScalarColumnToUtf16Index(
                    line,
                    15);
        var actualEnd =
            AstGrepStructuralEditEngine
                .ScalarColumnToUtf16Index(
                    line,
                    29);

        AssertEqual(
            expectedStart,
            actualStart,
            "Unicode scalar start column was not converted to UTF-16 correctly.");
        AssertEqual(
            expectedEnd,
            actualEnd,
            "Unicode scalar end column was not converted to UTF-16 correctly.");
        AssertEqual(
            "console.log(x)",
            line[actualStart..actualEnd],
            "Unicode-converted structural range does not select the expected match text.");
        return Task.CompletedTask;
    }
}
