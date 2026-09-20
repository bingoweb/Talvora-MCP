using System.Diagnostics;
using System.Security.Cryptography;

namespace Talvora.SourceEditing;

internal static partial class SourceEditNormalizer
{
    public static string ComputeUnifiedDiffRequestHash(
        string workspaceRoot,
        string patch,
        IReadOnlyDictionary<string, string>? expectedRevisions,
        bool validateSyntax)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var revisions = NormalizeExpectedRevisionMap(
            expectedRevisions,
            requireAny: false);
        var normalizedPatch = patch
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .TrimEnd('\n');

        return ComputeExternalRequestHash(
            "unified-diff-v1",
            workspaceRoot,
            validateSyntax,
            new
            {
                patch = normalizedPatch,
                expectedRevisions =
                    revisions
                        .OrderBy(
                            pair => pair.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(
                            pair => new
                            {
                                path = pair.Key,
                                revision = pair.Value,
                            })
                        .ToArray(),
            });
    }

    public static async Task<NormalizedSourceEditChangeSet> FromUnifiedDiffAsync(
        string workspaceRoot,
        string transactionId,
        string patch,
        IReadOnlyDictionary<string, string>? expectedRevisions,
        bool validateSyntax,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var tx = NormalizeTransactionId(transactionId);
        var root =
            SourceWorkspaceClassifier.ResolveExplicitWorkspaceRoot(
                workspaceRoot);
        var parsed = UnifiedDiffParser.Parse(patch);
        var revisions =
            NormalizeExpectedRevisionMap(
                expectedRevisions,
                requireAny: false);
        var normalized =
            new List<NormalizedSourceEditChange>(
                parsed.Files.Count);
        var occupied =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var file in parsed.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.IsNewFile)
            {
                var newPath =
                    file.NewPath ??
                    throw new SourceEditDomainException(
                        SourceEditCodes.PatchParseError,
                        "Unified diff new-file change has no destination path.");
                var fullPath =
                    SourceWorkspaceClassifier.ResolveWorkspacePath(
                        root,
                        newPath);
                ClaimPath(
                    occupied,
                    fullPath,
                    newPath);
                var relative =
                    SourceWorkspaceClassifier.GetRelativePath(
                        root,
                        fullPath);
                var addBefore =
                    await SourceTextCodec.ReadSnapshotAsync(
                        fullPath,
                        relative,
                        requireText: false,
                        cancellationToken);
                if (addBefore.Exists)
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.Conflict,
                        "Unified diff new-file destination already exists.",
                        fullPath);
                }

                var document =
                    BuildUnifiedDiffNewFile(
                        file,
                        fullPath);
                normalized.Add(
                    new NormalizedSourceEditChange(
                        SourceEditOperationKind.Add,
                        relative,
                        fullPath,
                        null,
                        null,
                        null,
                        addBefore,
                        null,
                        document));
                continue;
            }

            var oldPath =
                file.OldPath ??
                file.RenameFrom ??
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchParseError,
                    "Unified diff existing-file change has no source path.");
            var sourceFull =
                SourceWorkspaceClassifier.ResolveWorkspacePath(
                    root,
                    oldPath);
            ClaimPath(
                occupied,
                sourceFull,
                oldPath);

            var expectedRevision =
                GetExpectedRevision(
                    revisions,
                    oldPath,
                    sourceFull);
            var before =
                await RequireExistingTextAsync(
                    root,
                    oldPath,
                    sourceFull,
                    expectedRevision,
                    cancellationToken);

            var destinationPath =
                file.IsDeletedFile
                    ? null
                    : file.RenameTo ??
                      file.NewPath ??
                      oldPath;
            var isMove =
                destinationPath is not null &&
                !string.Equals(
                    NormalizeRevisionMapPath(oldPath),
                    NormalizeRevisionMapPath(destinationPath),
                    StringComparison.OrdinalIgnoreCase);

            string? destinationFull = null;
            string? destinationRelative = null;
            SourceFileSnapshot? destinationBefore = null;

            if (isMove)
            {
                destinationFull =
                    SourceWorkspaceClassifier.ResolveWorkspacePath(
                        root,
                        destinationPath!);
                ClaimPath(
                    occupied,
                    destinationFull,
                    destinationPath!);

                if (!SameVolume(
                        sourceFull,
                        destinationFull))
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.CrossVolumeMoveUnsupported,
                        "Unified diff rename/move must remain on the same volume in Source Edit v1.",
                        sourceFull);
                }

                destinationRelative =
                    SourceWorkspaceClassifier.GetRelativePath(
                        root,
                        destinationFull);
                destinationBefore =
                    await SourceTextCodec.ReadSnapshotAsync(
                        destinationFull,
                        destinationRelative,
                        requireText: false,
                        cancellationToken);
                if (destinationBefore.Exists)
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.Conflict,
                        "Unified diff rename destination already exists. Compatibility v1 does not overwrite rename targets.",
                        destinationFull);
                }
            }

            if (file.IsDeletedFile)
            {
                var deletedProposal =
                    ApplyUnifiedDiffHunks(
                        before.Document!,
                        file.Hunks,
                        sourceFull);
                if (deletedProposal.Text.Length != 0)
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.PatchParseError,
                        "Deleted-file unified diff does not remove the complete source content.",
                        sourceFull);
                }

                normalized.Add(
                    new NormalizedSourceEditChange(
                        SourceEditOperationKind.Delete,
                        SourceWorkspaceClassifier.GetRelativePath(
                            root,
                            sourceFull),
                        sourceFull,
                        null,
                        null,
                        expectedRevision,
                        before,
                        null,
                        null));
                continue;
            }

            var proposed =
                file.Hunks.Count == 0
                    ? before.Document!
                    : ApplyUnifiedDiffHunks(
                        before.Document!,
                        file.Hunks,
                        sourceFull);

            normalized.Add(
                new NormalizedSourceEditChange(
                    isMove
                        ? SourceEditOperationKind.Move
                        : SourceEditOperationKind.Update,
                    SourceWorkspaceClassifier.GetRelativePath(
                        root,
                        sourceFull),
                    sourceFull,
                    destinationRelative,
                    destinationFull,
                    expectedRevision,
                    before,
                    destinationBefore,
                    proposed));
        }

        return new NormalizedSourceEditChangeSet(
            1,
            tx,
            root,
            "unified-diff-v1",
            validateSyntax,
            requestHash,
            normalized);
    }

    private static SourceTextDocument BuildUnifiedDiffNewFile(
        UnifiedDiffFile file,
        string fullPath)
    {
        if (file.Hunks.Count == 0)
        {
            return new SourceTextDocument(
                string.Empty,
                SourceTextCodec.ParseEncoding("utf-8"),
                "none",
                false,
                0);
        }

        var newline =
            Environment.NewLine;
        var lines =
            new List<SourceTextLine>();
        var nextLine = 1;

        foreach (var hunk in file.Hunks.OrderBy(
                     item => item.NewStart))
        {
            if (hunk.OldCount != 0 ||
                hunk.Lines.Any(
                    line =>
                        line.Kind != UnifiedDiffLineKind.Add))
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchParseError,
                    "New-file unified diff hunks may contain only added lines and must declare oldCount=0.",
                    fullPath);
            }

            var expectedStart =
                hunk.NewCount == 0
                    ? nextLine - 1
                    : nextLine;
            if (hunk.NewStart != expectedStart)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchParseError,
                    "New-file unified diff hunks contain a line-number gap that cannot be reconstructed without hidden content.",
                    fullPath);
            }

            foreach (var line in hunk.Lines)
            {
                lines.Add(
                    new SourceTextLine(
                        line.Text,
                        line.NoNewlineAtEnd
                            ? string.Empty
                            : newline));
                nextLine++;
            }
        }

        ValidateNoInternalMissingNewline(
            lines,
            fullPath);

        var text =
            SourceTextCodec.JoinLines(lines);
        return new SourceTextDocument(
            text,
            SourceTextCodec.ParseEncoding("utf-8"),
            DetermineNewline(text),
            HasFinalNewline(text),
            0);
    }

    private static SourceTextDocument ApplyUnifiedDiffHunks(
        SourceTextDocument document,
        IReadOnlyList<UnifiedDiffHunk> hunks,
        string fullPath)
    {
        if (hunks.Count == 0)
        {
            return document;
        }

        var originalRanges =
            hunks
                .Select(
                    hunk => new
                    {
                        Hunk = hunk,
                        Start =
                            hunk.OldCount == 0
                                ? hunk.OldStart
                                : hunk.OldStart - 1,
                        End =
                            (hunk.OldCount == 0
                                ? hunk.OldStart
                                : hunk.OldStart - 1) +
                            hunk.OldCount,
                    })
                .OrderBy(item => item.Start)
                .ToArray();

        for (var index = 0;
             index < originalRanges.Length;
             index++)
        {
            var range = originalRanges[index];
            if (range.Start < 0 ||
                range.Hunk.OldStart < 0)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchParseError,
                    "Unified diff hunk has an invalid old-file line start.",
                    fullPath);
            }

            if (index > 0 &&
                range.Start <
                originalRanges[index - 1].End)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchOverlappingEdits,
                    "Unified diff hunks overlap in old-file coordinates.",
                    fullPath);
            }
        }

        var lines =
            SourceTextCodec.SplitLines(
                    document.Text)
                .ToList();

        foreach (var range in originalRanges
                     .OrderByDescending(item => item.Start))
        {
            var hunk = range.Hunk;
            var oldSide =
                hunk.Lines
                    .Where(
                        line =>
                            line.Kind is
                                UnifiedDiffLineKind.Context or
                                UnifiedDiffLineKind.Delete)
                    .ToArray();
            var newSide =
                hunk.Lines
                    .Where(
                        line =>
                            line.Kind is
                                UnifiedDiffLineKind.Context or
                                UnifiedDiffLineKind.Add)
                    .ToArray();

            if (oldSide.Length != hunk.OldCount ||
                newSide.Length != hunk.NewCount)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchParseError,
                    "Unified diff hunk body does not match its declared line counts.",
                    fullPath);
            }

            if (range.Start > lines.Count ||
                range.Start + hunk.OldCount >
                lines.Count)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchContextNotFound,
                    "Unified diff hunk old-file coordinates are outside the current source.",
                    fullPath,
                    HunkLineDetails(hunk));
            }

            for (var offset = 0;
                 offset < oldSide.Length;
                 offset++)
            {
                if (!string.Equals(
                        lines[range.Start + offset].Content,
                        oldSide[offset].Text,
                        StringComparison.Ordinal))
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.PatchContextNotFound,
                        "Unified diff hunk does not exactly match the declared old-file coordinates. Offset/fuzzy application is not permitted.",
                        fullPath,
                        HunkLineDetails(hunk));
                }

                if (oldSide[offset].NoNewlineAtEnd &&
                    lines[range.Start + offset]
                        .Terminator.Length != 0)
                {
                    throw new SourceEditDomainException(
                        SourceEditCodes.PatchContextNotFound,
                        "Unified diff expected no newline at end of the old file, but the current source has one.",
                        fullPath,
                        HunkLineDetails(hunk));
                }
            }

            var preferredNewline =
                SourceTextCodec.GetPreferredNewline(
                    document,
                    lines,
                    Math.Min(
                        range.Start,
                        Math.Max(
                            0,
                            lines.Count - 1)));
            var replacement =
                new List<SourceTextLine>();
            var oldIndex = range.Start;

            foreach (var line in hunk.Lines)
            {
                switch (line.Kind)
                {
                    case UnifiedDiffLineKind.Context:
                    {
                        var original =
                            lines[oldIndex++];
                        replacement.Add(
                            line.NoNewlineAtEnd
                                ? original with
                                {
                                    Terminator =
                                        string.Empty,
                                }
                                : original);
                        break;
                    }

                    case UnifiedDiffLineKind.Delete:
                        oldIndex++;
                        break;

                    case UnifiedDiffLineKind.Add:
                        replacement.Add(
                            new SourceTextLine(
                                line.Text,
                                line.NoNewlineAtEnd
                                    ? string.Empty
                                    : preferredNewline));
                        break;

                    default:
                        throw new UnreachableException();
                }
            }

            ValidateNoInternalMissingNewline(
                replacement,
                fullPath);

            lines.RemoveRange(
                range.Start,
                hunk.OldCount);
            lines.InsertRange(
                range.Start,
                replacement);
        }

        ValidateNoInternalMissingNewline(
            lines,
            fullPath);

        var text =
            SourceTextCodec.JoinLines(lines);
        return document with
        {
            Text = text,
            Newline = DetermineNewline(text),
            HasFinalNewline = HasFinalNewline(text),
        };
    }

    private static IReadOnlyDictionary<string, string>
        NormalizeExpectedRevisionMap(
            IReadOnlyDictionary<string, string>? input,
            bool requireAny)
    {
        if (input is null ||
            input.Count == 0)
        {
            if (requireAny)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.ExpectedRevisionMismatch,
                    "Unified diff existing-file changes require expectedRevisions from talvora_read_source.");
            }

            return new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        }

        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        foreach (var pair in input)
        {
            var path =
                NormalizeRevisionMapPath(
                    pair.Key);
            if (!result.TryAdd(
                    path,
                    SourceEditRevision.Normalize(
                        pair.Value)))
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchParseError,
                    $"Duplicate unified-diff expectedRevisions path '{pair.Key}'.");
            }
        }

        return result;
    }

    private static string GetExpectedRevision(
        IReadOnlyDictionary<string, string> revisions,
        string relativePath,
        string fullPath)
    {
        var key =
            NormalizeRevisionMapPath(
                relativePath);
        if (!revisions.TryGetValue(
                key,
                out var revision))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.ExpectedRevisionMismatch,
                $"Unified diff existing-file change '{relativePath}' requires expectedRevisions['{relativePath}'] from talvora_read_source.",
                fullPath);
        }

        return revision;
    }

    private static string NormalizeRevisionMapPath(
        string path) =>
        path
            .Replace('\\', '/')
            .TrimStart('.', '/');

    private static IReadOnlyDictionary<string, string>?
        HunkLineDetails(
            UnifiedDiffHunk hunk)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["oldStart"] =
                    hunk.OldStart.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                ["oldCount"] =
                    hunk.OldCount.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
            };

        if (!string.IsNullOrWhiteSpace(
                hunk.Label))
        {
            result["hunk"] =
                hunk.Label!;
        }

        return result;
    }

    private static void ValidateNoInternalMissingNewline(
        IReadOnlyList<SourceTextLine> lines,
        string fullPath)
    {
        for (var index = 0;
             index < lines.Count - 1;
             index++)
        {
            if (lines[index].Terminator.Length == 0)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchParseError,
                    "A no-newline marker may only describe the final logical line.",
                    fullPath);
            }
        }
    }
}
