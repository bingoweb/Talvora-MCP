using static SmokeSupport;

internal static partial class SmokeScenarios
{
    private static async Task RunWorkspaceCopyMoveAsync(SmokeWorkspaceContext context)
    {
        var byName = context.ByName;
        var smokeId = context.SmokeId;
        var root = context.Root;
        var file = context.File;
        var copySourceFile = context.CopySourceFile;
        var copyDestinationFile = context.CopyDestinationFile;
        var copyCollisionFile = context.CopyCollisionFile;
        var directorySource = context.DirectorySource;
        var directorySourceNested = context.DirectorySourceNested;
        var directoryDestination = context.DirectoryDestination;
        var directoryNonRecursiveDestination = context.DirectoryNonRecursiveDestination;
        var reparseSource = context.ReparseSource;
        var reparseLoop = context.ReparseLoop;
        var reparseDestination = context.ReparseDestination;
        var moveFileSource = context.MoveFileSource;
        var moveFileDestination = context.MoveFileDestination;
        var moveDirectorySource = context.MoveDirectorySource;
        var moveDirectoryDestination = context.MoveDirectoryDestination;
        var moveCollisionSource = context.MoveCollisionSource;
        var moveCollisionDestination = context.MoveCollisionDestination;
        await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = copySourceFile,
                    ["content"] = "copy-source-" + smokeId,
                });
                await EnsureSuccess(byName["talvora_copy"], new()
                {
                    ["source"] = copySourceFile,
                    ["destination"] = copyDestinationFile,
                });
                if (!string.Equals(
                    await ReadToolText(byName["talvora_read_text"], copyDestinationFile),
                    "copy-source-" + smokeId,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("copy did not preserve file content.");
                }
                
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = copyCollisionFile,
                    ["content"] = "copy-old-" + smokeId,
                });
                await EnsureError(byName["talvora_copy"], new()
                {
                    ["source"] = copySourceFile,
                    ["destination"] = copyCollisionFile,
                    ["overwrite"] = false,
                });
                if (!string.Equals(
                    await ReadToolText(byName["talvora_read_text"], copyCollisionFile),
                    "copy-old-" + smokeId,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("copy overwrite=false modified an existing destination.");
                }
                
                await EnsureSuccess(byName["talvora_copy"], new()
                {
                    ["source"] = copySourceFile,
                    ["destination"] = copyCollisionFile,
                    ["overwrite"] = true,
                });
                if (!string.Equals(
                    await ReadToolText(byName["talvora_read_text"], copyCollisionFile),
                    "copy-source-" + smokeId,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("copy overwrite=true did not replace the destination file.");
                }
                
                await EnsureSuccess(byName["talvora_create_directory"], new()
                {
                    ["path"] = directorySourceNested,
                });
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = Path.Combine(directorySource, "root.txt"),
                    ["content"] = "directory-root-" + smokeId,
                });
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = Path.Combine(directorySourceNested, "nested.txt"),
                    ["content"] = "directory-nested-" + smokeId,
                });
                await EnsureSuccess(byName["talvora_copy"], new()
                {
                    ["source"] = directorySource,
                    ["destination"] = directoryDestination,
                    ["recursive"] = true,
                });
                if (!string.Equals(
                    await ReadToolText(byName["talvora_read_text"], Path.Combine(directoryDestination, "child", "grandchild", "nested.txt")),
                    "directory-nested-" + smokeId,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("recursive directory copy did not preserve nested content.");
                }
                
                await EnsureError(byName["talvora_copy"], new()
                {
                    ["source"] = directorySource,
                    ["destination"] = directoryNonRecursiveDestination,
                    ["recursive"] = false,
                });
                if (Directory.Exists(directoryNonRecursiveDestination))
                {
                    throw new InvalidOperationException("recursive=false must fail before creating a partial directory copy.");
                }
                
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = Path.Combine(directoryDestination, "child", "grandchild", "nested.txt"),
                    ["content"] = "directory-stale-" + smokeId,
                });
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = Path.Combine(directoryDestination, "keep.txt"),
                    ["content"] = "directory-keep-" + smokeId,
                });
                await EnsureError(byName["talvora_copy"], new()
                {
                    ["source"] = directorySource,
                    ["destination"] = directoryDestination,
                    ["overwrite"] = false,
                    ["recursive"] = true,
                });
                await EnsureSuccess(byName["talvora_copy"], new()
                {
                    ["source"] = directorySource,
                    ["destination"] = directoryDestination,
                    ["overwrite"] = true,
                    ["recursive"] = true,
                });
                if (!string.Equals(
                    await ReadToolText(byName["talvora_read_text"], Path.Combine(directoryDestination, "child", "grandchild", "nested.txt")),
                    "directory-nested-" + smokeId,
                    StringComparison.Ordinal) ||
                    !string.Equals(
                        await ReadToolText(byName["talvora_read_text"], Path.Combine(directoryDestination, "keep.txt")),
                        "directory-keep-" + smokeId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("directory overwrite=true must replace conflicting entries while merging non-conflicting destination entries.");
                }
                
                await EnsureSuccess(byName["talvora_create_directory"], new()
                {
                    ["path"] = reparseSource,
                });
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = Path.Combine(reparseSource, "payload.txt"),
                    ["content"] = "reparse-payload-" + smokeId,
                });
                var reparsePathBase64 = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(reparseLoop));
                var reparseTargetBase64 = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(reparseSource));
                await EnsureSuccess(byName["talvora_run_powershell"], new()
                {
                    ["script"] = $"""
                        $link = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{reparsePathBase64}'))
                        $target = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{reparseTargetBase64}'))
                        New-Item -ItemType Junction -Path $link -Target $target -Force | Out-Null
                        """,
                    ["engine"] = "auto",
                    ["timeoutSeconds"] = 30,
                });
                await EnsureError(byName["talvora_copy"], new()
                {
                    ["source"] = reparseSource,
                    ["destination"] = reparseDestination,
                    ["overwrite"] = true,
                    ["recursive"] = true,
                });
                if (Directory.Exists(reparseDestination))
                {
                    throw new InvalidOperationException("reparse-point rejection must occur before a partial destination tree is created.");
                }
                
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = moveFileSource,
                    ["content"] = "move-file-" + smokeId,
                });
                await EnsureSuccess(byName["talvora_move"], new()
                {
                    ["source"] = moveFileSource,
                    ["destination"] = moveFileDestination,
                });
                if (File.Exists(moveFileSource) ||
                    !string.Equals(
                        await ReadToolText(byName["talvora_read_text"], moveFileDestination),
                        "move-file-" + smokeId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("move did not relocate the source file.");
                }
                
                await EnsureSuccess(byName["talvora_create_directory"], new()
                {
                    ["path"] = Path.Combine(moveDirectorySource, "nested"),
                });
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = Path.Combine(moveDirectorySource, "nested", "moved.txt"),
                    ["content"] = "move-directory-" + smokeId,
                });
                await EnsureSuccess(byName["talvora_move"], new()
                {
                    ["source"] = moveDirectorySource,
                    ["destination"] = moveDirectoryDestination,
                });
                if (Directory.Exists(moveDirectorySource) ||
                    !string.Equals(
                        await ReadToolText(byName["talvora_read_text"], Path.Combine(moveDirectoryDestination, "nested", "moved.txt")),
                        "move-directory-" + smokeId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("move did not relocate the source directory.");
                }
                
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = moveCollisionSource,
                    ["content"] = "move-new-" + smokeId,
                });
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = moveCollisionDestination,
                    ["content"] = "move-old-" + smokeId,
                });
                await EnsureError(byName["talvora_move"], new()
                {
                    ["source"] = moveCollisionSource,
                    ["destination"] = moveCollisionDestination,
                    ["overwrite"] = false,
                });
                if (!File.Exists(moveCollisionSource) ||
                    !string.Equals(
                        await ReadToolText(byName["talvora_read_text"], moveCollisionDestination),
                        "move-old-" + smokeId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("move overwrite=false changed source or destination on collision.");
                }
                
                await EnsureSuccess(byName["talvora_move"], new()
                {
                    ["source"] = moveCollisionSource,
                    ["destination"] = moveCollisionDestination,
                    ["overwrite"] = true,
                });
                if (File.Exists(moveCollisionSource) ||
                    !string.Equals(
                        await ReadToolText(byName["talvora_read_text"], moveCollisionDestination),
                        "move-new-" + smokeId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("move overwrite=true did not replace the existing destination.");
                }
    }
}
