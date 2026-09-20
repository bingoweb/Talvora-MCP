using System.Text;
using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    public static async Task RunAllAsync()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("exact-patch-success", ExactPatchSuccessAsync),
            ("unified-diff-compatibility", UnifiedDiffCompatibilityAsync),
            ("unified-diff-exact-guards", UnifiedDiffExactGuardsAsync),
            ("context-missing", ContextMissingAsync),
            ("context-ambiguous", ContextAmbiguousAsync),
            ("stale-revision", StaleRevisionAsync),
            ("multi-file-preflight-zero-mutation", MultiFilePreflightFailureAsync),
            ("commit-time-concurrent-writer", ConcurrentWriterAsync),
            ("injected-commit-failure-rollback", InjectedCommitFailureAsync),
            ("crash-recovery-rollback", CrashRecoveryRollbackAsync),
            ("crash-recovery-preserves-unapplied-external-add", CrashRecoveryPreservesUnappliedExternalAddAsync),
            ("path-guard-blocks-delete-parent-rename", PathGuardBlocksDeleteParentRenameAsync),
            ("path-guard-blocks-move-parent-rename", PathGuardBlocksMoveParentRenameAsync),
            ("path-guard-blocks-new-add-parent-rename", PathGuardBlocksNewAddParentRenameAsync),
            ("path-guard-blocks-update-parent-rename", PathGuardBlocksUpdateParentRenameAsync),
            ("path-guard-blocks-edited-move-parent-rename", PathGuardBlocksEditedMoveParentRenameAsync),
            ("lost-response-idempotent-retry", LostResponseRetryAsync),
            ("wal-complete-invalid-tail-fails-closed", CompleteInvalidWalTailFailsClosedAsync),
            ("wal-corruption-isolated-to-related-workspace", CorruptWalIsolatedToRelatedWorkspaceAsync),
            ("wal-unterminated-tail-is-ignored", UnterminatedWalTailIsIgnoredAsync),
            ("expired-transaction-id-fails-closed", ExpiredTransactionIdFailsClosedAsync),
            ("transaction-id-reuse-mismatch", TransactionIdReuseMismatchAsync),
            ("add-delete-move", AddDeleteMoveAsync),
            ("encoding-newline-preservation", EncodingAndNewlineAsync),
            ("streaming-source-read-bounded", StreamingSourceReadBoundedAsync),
            ("watcher-bounds-and-resync-state", WatcherBoundsAndResyncStateAsync),
            ("http-mock-resource-bounds", HttpMockResourceBoundsAsync),
            ("read-only-rejection", ReadOnlyRejectionAsync),
            ("reparse-point-rejection", ReparsePointRejectionAsync),
            ("legacy-source-mutation-policy", LegacyPolicyAsync),
            ("generic-source-routing-policy", GenericSourceRoutingPolicyAsync),
            ("http-download-staged-overwrite-preserves-existing", HttpDownloadStagedOverwritePreservesExistingAsync),
            ("archive-transactional-rollback", ArchiveTransactionalRollbackAsync),
            ("archive-contained-reparse-rejection", ArchiveContainedReparseRejectionAsync),
            ("archive-resource-budgets", ArchiveResourceBudgetsAsync),
            ("typed-config-source-mutation-policy", TypedConfigPolicyAsync),
            ("agent-tool-routing-descriptions", ToolRoutingDescriptionsAsync),
            ("source-edit-routing-contract", SourceEditRoutingContractAsync),
            ("generated-edit-idempotency", GeneratedEditIdempotencyAsync),
            ("structural-unicode-position", StructuralUnicodePositionAsync),
            ("semantic-solution-rename-replay", SemanticSolutionRenameReplayAsync),
            ("semantic-project-scope-promotes-containing-solution", SemanticProjectScopePromotesContainingSolutionAsync),
            ("semantic-project-scope-rejects-unproven-multi-project", SemanticProjectScopeRejectsUnprovenMultiProjectAsync),
            ("semantic-baseline-compilation-errors-zero-mutation", SemanticBaselineCompilationErrorsAsync),
            ("semantic-rename-new-compiler-error-zero-mutation", SemanticRenameCompilerConflictAsync),
            ("semantic-stale-anchor-zero-mutation", SemanticStaleAnchorAsync),
            ("semantic-no-symbol-zero-mutation", SemanticNoSymbolAsync),
            ("semantic-linked-context-ambiguity", SemanticLinkedContextAmbiguityAsync),
            ("semantic-workspace-load-diagnostics", SemanticWorkspaceLoadDiagnosticsAsync),
            ("semantic-resource-bounds", SemanticResourceBoundsAsync),
            ("semantic-graph-stale-zero-mutation", SemanticGraphStaleZeroMutationAsync),
            ("semantic-toolchain-isolation", SemanticToolchainIsolationAsync),
            ("semantic-cancellation-zero-mutation", SemanticCancellationAsync),
            ("non-workspace-legacy-compatibility", NonWorkspaceCompatibilityAsync),
            ("syntax-validation-failure", SyntaxValidationFailureAsync),
            ("csharp-invalid-patch-zero-mutation", CSharpInvalidPatchAsync),
            ("csharp-valid-syntax-and-opt-out", CSharpValidSyntaxAsync),
            ("structural-yaml-control-isolation", StructuralYamlControlIsolationAsync),
            ("ast-grep-cache-online-integrity", () => CacheIntegrityAsync(offline: false)),
            ("ast-grep-cache-offline-integrity", () => CacheIntegrityAsync(offline: true)),
            ("overlapping-structured-edits", OverlappingStructuredEditsAsync),
            ("large-edit", LargeEditAsync),
            ("cancellation-before-commit", CancellationBeforeCommitAsync),
        };

        var failures = new List<string>();
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (SkipTestException ex)
            {
                Console.WriteLine($"SKIP {test.Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                failures.Add($"{test.Name}: {ex}");
                Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Source Edit regression failures:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine + Environment.NewLine,
                    failures));
        }
    }

    private static string NewTransactionId() =>
        Guid.NewGuid().ToString("N");

    private static void Assert(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(
        T expected,
        T actual,
        string message)
    {
        if (!EqualityComparer<T>.Default.Equals(
                expected,
                actual))
        {
            throw new InvalidOperationException(
                $"{message} Expected={expected}; Actual={actual}");
        }
    }

    private static void AssertError(
        SourceEditTransactionResult result,
        string code)
    {
        Assert(!result.Success, "Result unexpectedly succeeded.");
        Assert(
            result.Error is not null,
            "Expected structured source-edit error.");
        AssertEqual(
            code,
            result.Error!.Code,
            "Unexpected source-edit error code.");
    }

    private static string PatchUpdate(
        string relativePath,
        string revision,
        params string[] hunkLines) =>
        string.Join(
            Environment.NewLine,
            new[]
            {
                "*** Begin Talvora Patch",
                $"*** Update File: {relativePath}",
                $"*** Revision: {revision}",
                "@@",
            }
            .Concat(hunkLines)
            .Concat(["*** End Talvora Patch"]));

    private static async Task<string> RevisionAsync(
        SourceEditEngine engine,
        string path)
    {
        var read =
            await engine.ReadSourceAsync(
                path,
                1,
                0,
                CancellationToken.None);
        return read.Revision;
    }

    private static SourceEditChangeInput Update(
        string path,
        string revision,
        int startLine,
        int startCharacter,
        int endLine,
        int endCharacter,
        string newText,
        string? expectedText = null) =>
        new()
        {
            Operation = "update",
            Path = path,
            ExpectedRevision = revision,
            Edits =
            [
                new SourceEditTextRangeInput
                {
                    StartLine = startLine,
                    StartCharacter = startCharacter,
                    EndLine = endLine,
                    EndCharacter = endCharacter,
                    NewText = newText,
                    ExpectedText = expectedText,
                },
            ],
        };

    private sealed class TestWorkspace : IAsyncDisposable
    {
        private TestWorkspace(
            string root,
            string stateRoot,
            string ordinaryRoot)
        {
            Root = root;
            StateRoot = stateRoot;
            OrdinaryRoot = ordinaryRoot;
        }

        public string Root { get; }
        public string StateRoot { get; }
        public string OrdinaryRoot { get; }

        public static Task<TestWorkspace> CreateAsync()
        {
            var parent = Path.Combine(
                Path.GetTempPath(),
                "TalvoraSourceEditRegression",
                Guid.NewGuid().ToString("N"));
            var root = Path.Combine(parent, "workspace");
            var state = Path.Combine(parent, "state");
            var ordinary = Path.Combine(parent, "ordinary");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(
                Path.Combine(root, ".git"));
            Directory.CreateDirectory(state);
            Directory.CreateDirectory(ordinary);
            return Task.FromResult(
                new TestWorkspace(root, state, ordinary));
        }

        public SourceEditEngine CreateEngine(
            ISourceEditFaultInjector? faultInjector = null) =>
            new(
                new SourceEditEngineOptions
                {
                    StateRoot = StateRoot,
                },
                faultInjector);

        public string PathInWorkspace(string relativePath) =>
            Path.Combine(
                Root,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        public async Task WriteUtf8Async(
            string relativePath,
            string content)
        {
            var path = PathInWorkspace(relativePath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                path,
                content,
                new UTF8Encoding(false));
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            TryClearReadOnly(Root);
            try
            {
                if (Directory.Exists(
                        Directory.GetParent(Root)!.FullName))
                {
                    Directory.Delete(
                        Directory.GetParent(Root)!.FullName,
                        recursive: true);
                }
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static void TryClearReadOnly(string root)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(
                         root,
                         "*",
                         SearchOption.AllDirectories))
            {
                try
                {
                    var attributes =
                        File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(
                            file,
                            attributes &
                            ~FileAttributes.ReadOnly);
                    }
                }
                catch
                {
                }
            }
        }
    }

    private sealed class DelegateFaultInjector(
        Func<int, SourceEditPreparedFile, CancellationToken, ValueTask>? before = null,
        Func<int, SourceEditPreparedFile, CancellationToken, ValueTask>? after = null)
        : ISourceEditFaultInjector
    {
        public ValueTask BeforeCommitStepAsync(
            int index,
            SourceEditPreparedFile file,
            CancellationToken cancellationToken) =>
            before?.Invoke(
                index,
                file,
                cancellationToken) ??
            ValueTask.CompletedTask;

        public ValueTask AfterCommitStepAsync(
            int index,
            SourceEditPreparedFile file,
            CancellationToken cancellationToken) =>
            after?.Invoke(
                index,
                file,
                cancellationToken) ??
            ValueTask.CompletedTask;
    }

    private sealed class SkipTestException(string message)
        : Exception(message);
}
