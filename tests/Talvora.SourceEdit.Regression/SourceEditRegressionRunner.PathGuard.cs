using System.Text;
using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    private static async Task PathGuardBlocksDeleteParentRenameAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "delete-parent/item.txt",
            "delete-me\n");
        var source =
            fixture.PathInWorkspace(
                "delete-parent/item.txt");
        var parent =
            Path.GetDirectoryName(source)!;
        var renamed =
            fixture.PathInWorkspace(
                "delete-parent-renamed");
        var engineForRevision = fixture.CreateEngine();
        var revision =
            await RevisionAsync(
                engineForRevision,
                source);
        var renameBlocked = false;

        var injector =
            new DelegateFaultInjector(
                before: (_, _, _) =>
                {
                    try
                    {
                        Directory.Move(
                            parent,
                            renamed);
                    }
                    catch (Exception ex) when (
                        ex is IOException or
                        UnauthorizedAccessException)
                    {
                        renameBlocked = true;
                    }

                    return ValueTask.CompletedTask;
                });
        var engine = fixture.CreateEngine(injector);
        var result =
            await engine.ApplyEditsAsync(
                fixture.Root,
                NewTransactionId(),
                [
                    new SourceEditChangeInput
                    {
                        Operation = "delete",
                        Path = "delete-parent/item.txt",
                        ExpectedRevision = revision,
                    },
                ],
                true,
                CancellationToken.None);

        Assert(result.Success, result.Error?.Message ?? "Delete transaction failed.");
        Assert(
            renameBlocked,
            "The guarded parent directory was still renameable during delete commit.");
        Assert(
            Directory.Exists(parent),
            "The guarded delete parent moved unexpectedly.");
        Assert(
            !Directory.Exists(renamed),
            "The blocked external rename still created the renamed parent.");
        Assert(
            !File.Exists(source),
            "The guarded delete did not remove the intended source file.");
    }

    private static async Task PathGuardBlocksMoveParentRenameAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "move-source/item.txt",
            "move-me\n");
        Directory.CreateDirectory(
            fixture.PathInWorkspace(
                "move-destination"));
        var source =
            fixture.PathInWorkspace(
                "move-source/item.txt");
        var destination =
            fixture.PathInWorkspace(
                "move-destination/item.txt");
        var parent =
            Path.GetDirectoryName(source)!;
        var renamed =
            fixture.PathInWorkspace(
                "move-source-renamed");
        var engineForRevision = fixture.CreateEngine();
        var revision =
            await RevisionAsync(
                engineForRevision,
                source);
        var renameBlocked = false;

        var injector =
            new DelegateFaultInjector(
                before: (_, _, _) =>
                {
                    try
                    {
                        Directory.Move(
                            parent,
                            renamed);
                    }
                    catch (Exception ex) when (
                        ex is IOException or
                        UnauthorizedAccessException)
                    {
                        renameBlocked = true;
                    }

                    return ValueTask.CompletedTask;
                });
        var engine = fixture.CreateEngine(injector);
        var result =
            await engine.ApplyEditsAsync(
                fixture.Root,
                NewTransactionId(),
                [
                    new SourceEditChangeInput
                    {
                        Operation = "move",
                        Path = "move-source/item.txt",
                        DestinationPath =
                            "move-destination/item.txt",
                        ExpectedRevision = revision,
                    },
                ],
                true,
                CancellationToken.None);

        Assert(result.Success, result.Error?.Message ?? "Move transaction failed.");
        Assert(
            renameBlocked,
            "The guarded move parent directory was still renameable during commit.");
        Assert(
            Directory.Exists(parent),
            "The guarded move source parent moved unexpectedly.");
        Assert(
            !Directory.Exists(renamed),
            "The blocked external move-parent rename still created the renamed path.");
        Assert(
            !File.Exists(source),
            "The guarded move source still exists after commit.");
        AssertEqual(
            "move-me\n",
            await File.ReadAllTextAsync(destination),
            "The guarded move destination content is incorrect.");
    }

    private static async Task PathGuardBlocksNewAddParentRenameAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        var parent =
            fixture.PathInWorkspace(
                "new-parent");
        var renamed =
            fixture.PathInWorkspace(
                "new-parent-renamed");
        var target =
            fixture.PathInWorkspace(
                "new-parent/item.txt");
        Assert(
            !Directory.Exists(parent),
            "Add parent unexpectedly existed before the test.");
        var renameBlocked = false;

        var injector =
            new DelegateFaultInjector(
                before: (_, _, _) =>
                {
                    Assert(
                        Directory.Exists(parent),
                        "The guarded add parent was not created before the commit step.");
                    try
                    {
                        Directory.Move(
                            parent,
                            renamed);
                    }
                    catch (Exception ex) when (
                        ex is IOException or
                        UnauthorizedAccessException)
                    {
                        renameBlocked = true;
                    }

                    return ValueTask.CompletedTask;
                });
        var engine = fixture.CreateEngine(injector);
        var result =
            await engine.ApplyEditsAsync(
                fixture.Root,
                NewTransactionId(),
                [
                    new SourceEditChangeInput
                    {
                        Operation = "add",
                        Path = "new-parent/item.txt",
                        Content = "new-content\n",
                        Encoding = "utf-8",
                        Newline = "lf",
                    },
                ],
                true,
                CancellationToken.None);

        Assert(result.Success, result.Error?.Message ?? "Add transaction failed.");
        Assert(
            renameBlocked,
            "The newly created guarded parent directory was still renameable during commit.");
        Assert(
            Directory.Exists(parent),
            "The guarded add parent moved unexpectedly.");
        Assert(
            !Directory.Exists(renamed),
            "The blocked external new-parent rename still created the renamed path.");
        AssertEqual(
            "new-content\n",
            await File.ReadAllTextAsync(target),
            "The guarded add target content is incorrect.");
    }

    private static async Task PathGuardBlocksUpdateParentRenameAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "update-parent/item.txt",
            "before\n");
        var source =
            fixture.PathInWorkspace(
                "update-parent/item.txt");
        var parent =
            Path.GetDirectoryName(source)!;
        var renamed =
            fixture.PathInWorkspace(
                "update-parent-renamed");
        var engineForRevision = fixture.CreateEngine();
        var revision =
            await RevisionAsync(
                engineForRevision,
                source);
        var renameBlocked = false;

        var injector =
            new DelegateFaultInjector(
                before: (_, _, _) =>
                {
                    try
                    {
                        Directory.Move(
                            parent,
                            renamed);
                    }
                    catch (Exception ex) when (
                        ex is IOException or
                        UnauthorizedAccessException)
                    {
                        renameBlocked = true;
                    }

                    return ValueTask.CompletedTask;
                });
        var engine = fixture.CreateEngine(injector);
        var result =
            await engine.ApplyEditsAsync(
                fixture.Root,
                NewTransactionId(),
                [
                    Update(
                        "update-parent/item.txt",
                        revision,
                        0,
                        0,
                        0,
                        6,
                        "after",
                        "before"),
                ],
                true,
                CancellationToken.None);

        Assert(result.Success, result.Error?.Message ?? "Update transaction failed.");
        Assert(
            renameBlocked,
            "The guarded update parent directory was still renameable during File.Replace.");
        Assert(
            Directory.Exists(parent),
            "The guarded update parent moved unexpectedly.");
        Assert(
            !Directory.Exists(renamed),
            "The blocked external update-parent rename still created the renamed path.");
        AssertEqual(
            "after\n",
            await File.ReadAllTextAsync(source),
            "The guarded update did not commit the expected content.");
    }

    private static async Task PathGuardBlocksEditedMoveParentRenameAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "edited-move-source/item.txt",
            "before\n");
        Directory.CreateDirectory(
            fixture.PathInWorkspace(
                "edited-move-destination"));
        var source =
            fixture.PathInWorkspace(
                "edited-move-source/item.txt");
        var destination =
            fixture.PathInWorkspace(
                "edited-move-destination/item.txt");
        var parent =
            Path.GetDirectoryName(source)!;
        var renamed =
            fixture.PathInWorkspace(
                "edited-move-source-renamed");
        var engineForRevision = fixture.CreateEngine();
        var revision =
            await RevisionAsync(
                engineForRevision,
                source);
        var renameBlocked = false;

        var injector =
            new DelegateFaultInjector(
                before: (_, _, _) =>
                {
                    try
                    {
                        Directory.Move(
                            parent,
                            renamed);
                    }
                    catch (Exception ex) when (
                        ex is IOException or
                        UnauthorizedAccessException)
                    {
                        renameBlocked = true;
                    }

                    return ValueTask.CompletedTask;
                });
        var engine = fixture.CreateEngine(injector);
        var result =
            await engine.ApplyEditsAsync(
                fixture.Root,
                NewTransactionId(),
                [
                    new SourceEditChangeInput
                    {
                        Operation = "move",
                        Path = "edited-move-source/item.txt",
                        DestinationPath =
                            "edited-move-destination/item.txt",
                        ExpectedRevision = revision,
                        Edits =
                        [
                            new SourceEditTextRangeInput
                            {
                                StartLine = 0,
                                StartCharacter = 0,
                                EndLine = 0,
                                EndCharacter = 6,
                                NewText = "after",
                                ExpectedText = "before",
                            },
                        ],
                    },
                ],
                true,
                CancellationToken.None);

        Assert(result.Success, result.Error?.Message ?? "Edited move transaction failed.");
        Assert(
            renameBlocked,
            "The guarded edited-move source parent was still renameable during commit.");
        Assert(
            Directory.Exists(parent),
            "The guarded edited-move source parent moved unexpectedly.");
        Assert(
            !Directory.Exists(renamed),
            "The blocked external edited-move rename still created the renamed path.");
        Assert(
            !File.Exists(source),
            "The edited-move source still exists after commit.");
        AssertEqual(
            "after\n",
            await File.ReadAllTextAsync(destination),
            "The guarded edited-move destination content is incorrect.");
    }
}
