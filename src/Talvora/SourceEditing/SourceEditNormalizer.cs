using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Talvora.SourceEditing;

internal static partial class SourceEditNormalizer
{
    public static async Task<NormalizedSourceEditChangeSet> FromPatchAsync(
        string workspaceRoot,
        string transactionId,
        string patch,
        bool validateSyntax,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var tx = NormalizeTransactionId(transactionId);
        var root = SourceWorkspaceClassifier.ResolveExplicitWorkspaceRoot(workspaceRoot);
        var parsed = TalvoraPatchParser.Parse(patch);
        var normalized = new List<NormalizedSourceEditChange>(parsed.Files.Count);
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in parsed.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath =
                SourceWorkspaceClassifier.ResolveWorkspacePath(root, block.Path);
            ClaimPath(occupied, fullPath, block.Path);

            switch (block.Kind)
            {
                case SourcePatchBlockKind.Update:
                {
                    var before = await RequireExistingTextAsync(
                        root,
                        block.Path,
                        fullPath,
                        block.ExpectedRevision,
                        cancellationToken);
                    var proposed = ApplyPatchHunks(before.Document!, block.Hunks);
                    string? destinationRelative = null;
                    string? destinationFull = null;
                    SourceFileSnapshot? destinationBefore = null;

                    if (block.MoveTo is not null)
                    {
                        destinationFull =
                            SourceWorkspaceClassifier.ResolveWorkspacePath(
                                root,
                                block.MoveTo);
                        ClaimPath(occupied, destinationFull, block.MoveTo);
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
                                "Move destination already exists. Talvora Patch v1 requires an absent destination.",
                                destinationFull);
                        }
                    }

                    normalized.Add(
                        new NormalizedSourceEditChange(
                            block.MoveTo is null
                                ? SourceEditOperationKind.Update
                                : SourceEditOperationKind.Move,
                            SourceWorkspaceClassifier.GetRelativePath(root, fullPath),
                            fullPath,
                            destinationRelative,
                            destinationFull,
                            block.ExpectedRevision,
                            before,
                            destinationBefore,
                            proposed));
                    break;
                }

                case SourcePatchBlockKind.Add:
                {
                    var relative =
                        SourceWorkspaceClassifier.GetRelativePath(root, fullPath);
                    var before = await SourceTextCodec.ReadSnapshotAsync(
                        fullPath,
                        relative,
                        requireText: false,
                        cancellationToken);
                    if (before.Exists)
                    {
                        throw new SourceEditDomainException(
                            SourceEditCodes.Conflict,
                            "Add-file destination already exists.",
                            fullPath);
                    }

                    var descriptor = SourceTextCodec.ParseEncoding(block.Encoding);
                    var newline = ParseNewFileNewline(block.Newline);
                    var text = block.AddedLines.Count == 0
                        ? string.Empty
                        : string.Join(newline, block.AddedLines) +
                          (block.FinalNewline ? newline : string.Empty);
                    var document = new SourceTextDocument(
                        text,
                        descriptor,
                        ClassifyRequestedNewline(newline, text),
                        SourceTextCodec.SplitLines(text).Count > 0 &&
                        (text.EndsWith("\r", StringComparison.Ordinal) ||
                         text.EndsWith("\n", StringComparison.Ordinal)),
                        0);

                    normalized.Add(
                        new NormalizedSourceEditChange(
                            SourceEditOperationKind.Add,
                            relative,
                            fullPath,
                            null,
                            null,
                            null,
                            before,
                            null,
                            document));
                    break;
                }

                case SourcePatchBlockKind.Delete:
                {
                    var before = await RequireExistingTextAsync(
                        root,
                        block.Path,
                        fullPath,
                        block.ExpectedRevision,
                        cancellationToken);
                    normalized.Add(
                        new NormalizedSourceEditChange(
                            SourceEditOperationKind.Delete,
                            SourceWorkspaceClassifier.GetRelativePath(root, fullPath),
                            fullPath,
                            null,
                            null,
                            block.ExpectedRevision,
                            before,
                            null,
                            null));
                    break;
                }

                default:
                    throw new UnreachableException();
            }
        }

        return new NormalizedSourceEditChangeSet(
            1,
            tx,
            root,
            "talvora-patch-v1",
            validateSyntax,
            requestHash,
            normalized);
    }

    public static async Task<NormalizedSourceEditChangeSet> FromStructuredAsync(
        string workspaceRoot,
        string transactionId,
        IReadOnlyList<SourceEditChangeInput> changes,
        bool validateSyntax,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var tx = NormalizeTransactionId(transactionId);
        var root = SourceWorkspaceClassifier.ResolveExplicitWorkspaceRoot(workspaceRoot);

        if (changes.Count == 0)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.PatchParseError,
                "At least one structured source change is required.");
        }

        var normalized = new List<NormalizedSourceEditChange>(changes.Count);
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = ParseOperation(input.Operation);
            var fullPath =
                SourceWorkspaceClassifier.ResolveWorkspacePath(root, input.Path);
            ClaimPath(occupied, fullPath, input.Path);
            var relative =
                SourceWorkspaceClassifier.GetRelativePath(root, fullPath);

            switch (operation)
            {
                case SourceEditOperationKind.Update:
                {
                    var before = await RequireExistingTextAsync(
                        root,
                        input.Path,
                        fullPath,
                        input.ExpectedRevision,
                        cancellationToken);
                    var edits = input.Edits ??
                                throw new SourceEditDomainException(
                                    SourceEditCodes.PatchParseError,
                                    "Structured update requires an edits array.",
                                    fullPath);
                    if (edits.Count == 0)
                    {
                        throw new SourceEditDomainException(
                            SourceEditCodes.PatchParseError,
                            "Structured update requires at least one text edit.",
                            fullPath);
                    }

                    var proposed = ApplyStructuredEdits(
                        before.Document!,
                        edits,
                        input.Newline);
                    normalized.Add(
                        new NormalizedSourceEditChange(
                            operation,
                            relative,
                            fullPath,
                            null,
                            null,
                            input.ExpectedRevision,
                            before,
                            null,
                            proposed));
                    break;
                }

                case SourceEditOperationKind.Add:
                {
                    if (input.Content is null)
                    {
                        throw new SourceEditDomainException(
                            SourceEditCodes.PatchParseError,
                            "Structured add requires content. Use an empty string for an empty file.",
                            fullPath);
                    }

                    var before = await SourceTextCodec.ReadSnapshotAsync(
                        fullPath,
                        relative,
                        requireText: false,
                        cancellationToken);
                    if (before.Exists)
                    {
                        throw new SourceEditDomainException(
                            SourceEditCodes.Conflict,
                            "Structured add destination already exists.",
                            fullPath);
                    }

                    var descriptor = SourceTextCodec.ParseEncoding(input.Encoding);
                    var normalizedContent =
                        SourceTextCodec.NormalizeInsertedNewlines(
                            input.Content,
                            string.IsNullOrWhiteSpace(input.Newline)
                                ? "auto"
                                : input.Newline,
                            existingDocument: null);
                    var document = new SourceTextDocument(
                        normalizedContent,
                        descriptor,
                        DetermineNewline(normalizedContent),
                        HasFinalNewline(normalizedContent),
                        0);
                    normalized.Add(
                        new NormalizedSourceEditChange(
                            operation,
                            relative,
                            fullPath,
                            null,
                            null,
                            null,
                            before,
                            null,
                            document));
                    break;
                }

                case SourceEditOperationKind.Delete:
                {
                    var before = await RequireExistingTextAsync(
                        root,
                        input.Path,
                        fullPath,
                        input.ExpectedRevision,
                        cancellationToken);
                    normalized.Add(
                        new NormalizedSourceEditChange(
                            operation,
                            relative,
                            fullPath,
                            null,
                            null,
                            input.ExpectedRevision,
                            before,
                            null,
                            null));
                    break;
                }

                case SourceEditOperationKind.Move:
                {
                    var before = await RequireExistingTextAsync(
                        root,
                        input.Path,
                        fullPath,
                        input.ExpectedRevision,
                        cancellationToken);
                    if (string.IsNullOrWhiteSpace(input.DestinationPath))
                    {
                        throw new SourceEditDomainException(
                            SourceEditCodes.PatchParseError,
                            "Structured move requires destinationPath.",
                            fullPath);
                    }

                    var destinationFull =
                        SourceWorkspaceClassifier.ResolveWorkspacePath(
                            root,
                            input.DestinationPath);
                    ClaimPath(occupied, destinationFull, input.DestinationPath);
                    if (!SameVolume(fullPath, destinationFull))
                    {
                        throw new SourceEditDomainException(
                            SourceEditCodes.CrossVolumeMoveUnsupported,
                            "Strict source-edit move v1 requires source and destination on the same volume.",
                            fullPath);
                    }

                    var destinationRelative =
                        SourceWorkspaceClassifier.GetRelativePath(
                            root,
                            destinationFull);
                    var destinationBefore =
                        await SourceTextCodec.ReadSnapshotAsync(
                            destinationFull,
                            destinationRelative,
                            requireText: false,
                            cancellationToken);
                    if (destinationBefore.Exists)
                    {
                        throw new SourceEditDomainException(
                            SourceEditCodes.Conflict,
                            "Structured move destination already exists. Strict v1 move does not overwrite an existing destination.",
                            destinationFull);
                    }

                    var proposed = before.Document!;
                    if (input.Edits is { Count: > 0 })
                    {
                        proposed = ApplyStructuredEdits(
                            before.Document!,
                            input.Edits,
                            input.Newline);
                    }

                    normalized.Add(
                        new NormalizedSourceEditChange(
                            operation,
                            relative,
                            fullPath,
                            destinationRelative,
                            destinationFull,
                            input.ExpectedRevision,
                            before,
                            destinationBefore,
                            proposed));
                    break;
                }

                default:
                    throw new UnreachableException();
            }
        }

        return new NormalizedSourceEditChangeSet(
            1,
            tx,
            root,
            "structured-edits-v1",
            validateSyntax,
            requestHash,
            normalized);
    }

    public static string NormalizeTransactionId(string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        var value = transactionId.Trim();
        if (!TransactionIdPattern().IsMatch(value))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.TransactionIdInvalid,
                "transactionId must be 1-128 characters using letters, digits, '.', '_', ':', or '-'. UUID/ULID-style IDs are recommended.");
        }

        return value;
    }

    private static async Task<SourceFileSnapshot> RequireExistingTextAsync(
        string root,
        string inputPath,
        string fullPath,
        string? expectedRevision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedRevision))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.ExpectedRevisionMismatch,
                "Existing-file source edits require expectedRevision from talvora_read_source.",
                fullPath);
        }

        var relative = SourceWorkspaceClassifier.GetRelativePath(root, fullPath);
        var before = await SourceTextCodec.ReadSnapshotAsync(
            fullPath,
            relative,
            requireText: true,
            cancellationToken);
        if (!before.Exists)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.Conflict,
                $"Source file does not exist: {inputPath}",
                fullPath);
        }

        if ((before.Attributes & FileAttributes.ReadOnly) != 0)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.ReadOnly,
                "Source file is read-only. Talvora Source Edit does not silently clear file attributes.",
                fullPath);
        }

        var normalizedExpected = SourceEditRevision.Normalize(expectedRevision);
        if (!string.Equals(
                normalizedExpected,
                before.Revision,
                StringComparison.Ordinal))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.ExpectedRevisionMismatch,
                "Source file revision does not match the version read by the agent. Re-read with talvora_read_source and create a fresh transaction.",
                fullPath,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["expectedRevision"] = normalizedExpected,
                    ["currentRevision"] = before.Revision!,
                });
        }

        return before;
    }

    private static SourceTextDocument ApplyPatchHunks(
        SourceTextDocument document,
        IReadOnlyList<SourcePatchHunk> hunks)
    {
        var lines = SourceTextCodec.SplitLines(document.Text).ToList();

        foreach (var hunk in hunks)
        {
            var oldSide = hunk.Lines
                .Where(item =>
                    item.Kind is
                        SourcePatchLineKind.Context or
                        SourcePatchLineKind.Delete)
                .Select(item => item.Text)
                .ToArray();

            var candidates = new List<int>();
            for (var start = 0; start + oldSide.Length <= lines.Count; start++)
            {
                var match = true;
                for (var offset = 0; offset < oldSide.Length; offset++)
                {
                    if (!string.Equals(
                            lines[start + offset].Content,
                            oldSide[offset],
                            StringComparison.Ordinal))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    candidates.Add(start);
                    if (candidates.Count > 1)
                    {
                        break;
                    }
                }
            }

            if (candidates.Count == 0)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchContextNotFound,
                    "Exact patch context was not found.",
                    details: HunkDetails(hunk));
            }

            if (candidates.Count > 1)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchContextAmbiguous,
                    "Exact patch context matched more than once. Add more exact context.",
                    details: HunkDetails(hunk));
            }

            var startIndex = candidates[0];
            var preferredNewline =
                SourceTextCodec.GetPreferredNewline(
                    document,
                    lines,
                    startIndex);
            var replacement = new List<SourceTextLine>();
            var originalIndex = startIndex;

            foreach (var item in hunk.Lines)
            {
                switch (item.Kind)
                {
                    case SourcePatchLineKind.Context:
                        replacement.Add(lines[originalIndex]);
                        originalIndex++;
                        break;
                    case SourcePatchLineKind.Delete:
                        originalIndex++;
                        break;
                    case SourcePatchLineKind.Add:
                        replacement.Add(
                            new SourceTextLine(
                                item.Text,
                                preferredNewline));
                        break;
                    default:
                        throw new UnreachableException();
                }
            }

            lines.RemoveRange(startIndex, oldSide.Length);
            lines.InsertRange(startIndex, replacement);
            NormalizeLineTerminators(
                lines,
                preferredNewline,
                document.HasFinalNewline);
        }

        var text = SourceTextCodec.JoinLines(lines);
        return document with
        {
            Text = text,
            Newline = DetermineNewline(text),
            HasFinalNewline = HasFinalNewline(text),
        };
    }

    private static SourceTextDocument ApplyStructuredEdits(
        SourceTextDocument document,
        IReadOnlyList<SourceEditTextRangeInput> edits,
        string? newlinePolicy)
    {
        var resolved = new List<ResolvedEdit>(edits.Count);
        foreach (var edit in edits)
        {
            var start = ResolveOffset(
                document.Text,
                edit.StartLine,
                edit.StartCharacter);
            var end = ResolveOffset(
                document.Text,
                edit.EndLine,
                edit.EndCharacter);
            if (end < start)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchParseError,
                    "Structured edit end position precedes start position.");
            }

            var actual = document.Text[start..end];
            if (edit.ExpectedText is not null &&
                !string.Equals(
                    edit.ExpectedText,
                    actual,
                    StringComparison.Ordinal))
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchContextNotFound,
                    "Structured edit expectedText does not exactly match the requested range.");
            }

            var inserted = SourceTextCodec.NormalizeInsertedNewlines(
                edit.NewText,
                string.IsNullOrWhiteSpace(newlinePolicy)
                    ? "preserve"
                    : newlinePolicy,
                document);
            resolved.Add(new ResolvedEdit(start, end, inserted));
        }

        resolved.Sort((left, right) =>
        {
            var compare = left.Start.CompareTo(right.Start);
            return compare != 0 ? compare : left.End.CompareTo(right.End);
        });

        for (var index = 1; index < resolved.Count; index++)
        {
            var previous = resolved[index - 1];
            var current = resolved[index];
            if (current.Start < previous.End ||
                (current.Start == previous.Start &&
                 current.End == current.Start &&
                 previous.End == previous.Start))
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.PatchOverlappingEdits,
                    "Structured text edits overlap or contain ambiguous insertions at the same position.");
            }
        }

        var text = document.Text;
        for (var index = resolved.Count - 1; index >= 0; index--)
        {
            var edit = resolved[index];
            text = string.Concat(
                text.AsSpan(0, edit.Start),
                edit.NewText,
                text.AsSpan(edit.End));
        }

        return document with
        {
            Text = text,
            Newline = DetermineNewline(text),
            HasFinalNewline = HasFinalNewline(text),
        };
    }

    private static int ResolveOffset(
        string text,
        int line,
        int character)
    {
        if (line < 0 || character < 0)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.PatchParseError,
                "Structured edit line/character positions cannot be negative.");
        }

        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                starts.Add(index + 1);
            }
            else if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        if (line >= starts.Count)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.PatchParseError,
                "Structured edit line is outside the document.");
        }

        var start = starts[line];
        var end = line + 1 < starts.Count
            ? starts[line + 1]
            : text.Length;

        var contentEnd = end;
        if (contentEnd > start && text[contentEnd - 1] == '\n')
        {
            contentEnd--;
            if (contentEnd > start && text[contentEnd - 1] == '\r')
            {
                contentEnd--;
            }
        }
        else if (contentEnd > start && text[contentEnd - 1] == '\r')
        {
            contentEnd--;
        }

        if (character > contentEnd - start)
        {
            throw new SourceEditDomainException(
                SourceEditCodes.PatchParseError,
                "Structured edit character is outside the logical line.");
        }

        return start + character;
    }

    private static void NormalizeLineTerminators(
        List<SourceTextLine> lines,
        string preferredNewline,
        bool preserveFinalNewline)
    {
        if (lines.Count == 0)
        {
            return;
        }

        for (var index = 0; index < lines.Count - 1; index++)
        {
            if (lines[index].Terminator.Length == 0)
            {
                lines[index] =
                    lines[index] with { Terminator = preferredNewline };
            }
        }

        var last = lines[^1];
        if (preserveFinalNewline)
        {
            if (last.Terminator.Length == 0)
            {
                lines[^1] = last with { Terminator = preferredNewline };
            }
        }
        else if (last.Terminator.Length > 0)
        {
            lines[^1] = last with { Terminator = string.Empty };
        }
    }

    private static IReadOnlyDictionary<string, string>? HunkDetails(
        SourcePatchHunk hunk) =>
        hunk.Label is null
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["hunk"] = hunk.Label,
            };

    private static SourceEditOperationKind ParseOperation(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "update" => SourceEditOperationKind.Update,
            "add" => SourceEditOperationKind.Add,
            "delete" => SourceEditOperationKind.Delete,
            "move" => SourceEditOperationKind.Move,
            _ => throw new SourceEditDomainException(
                SourceEditCodes.PatchParseError,
                $"Unsupported structured source operation '{value}'."),
        };

    public static string ComputePatchRequestHash(
        string workspaceRoot,
        string patch,
        bool validateSyntax)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var normalizedPatch = patch
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .TrimEnd('\n');

        return ComputeExternalRequestHash(
            "talvora-patch-v1",
            workspaceRoot,
            validateSyntax,
            new
            {
                patch = normalizedPatch,
            });
    }

    public static string ComputeStructuredRequestHash(
        string workspaceRoot,
        IReadOnlyList<SourceEditChangeInput> changes,
        bool validateSyntax)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var canonical = changes.Select(change => new
        {
            operation = change.Operation?.Trim().ToLowerInvariant(),
            path = change.Path?.Replace('\\', '/'),
            destinationPath =
                change.DestinationPath?.Replace('\\', '/'),
            expectedRevision =
                change.ExpectedRevision?.Trim().ToLowerInvariant(),
            content = change.Content,
            encoding = change.Encoding?.Trim().ToLowerInvariant(),
            newline = change.Newline?.Trim().ToLowerInvariant(),
            edits = change.Edits?.Select(edit => new
            {
                startLine = edit.StartLine,
                startCharacter = edit.StartCharacter,
                endLine = edit.EndLine,
                endCharacter = edit.EndCharacter,
                newText = edit.NewText,
                expectedText = edit.ExpectedText,
            }).ToArray(),
        }).ToArray();

        return ComputeExternalRequestHash(
            "structured-edits-v1",
            workspaceRoot,
            validateSyntax,
            canonical);
    }

    private static string ComputeExternalRequestHash(
        string inputKind,
        string workspaceRoot,
        bool validateSyntax,
        object request)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            inputKind,
            workspaceRoot =
                Path.GetFullPath(workspaceRoot).ToUpperInvariant(),
            validateSyntax,
            request,
        });
        return SourceEditRevision.Format(
            SHA256.HashData(payload));
    }

    private static void ClaimPath(
        HashSet<string> occupied,
        string fullPath,
        string inputPath)
    {
        if (!occupied.Add(Path.GetFullPath(fullPath)))
        {
            throw new SourceEditDomainException(
                SourceEditCodes.Conflict,
                "A source transaction cannot address the same source/destination path more than once in v1. Combine hunks/ranges for that file into a single operation.",
                inputPath);
        }
    }

    private static string ParseNewFileNewline(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
            ? Environment.NewLine
            : value.Trim().ToLowerInvariant() switch
            {
                "crlf" => "\r\n",
                "lf" => "\n",
                "cr" => "\r",
                _ => throw new SourceEditDomainException(
                    SourceEditCodes.PatchParseError,
                    $"Unsupported new-file newline policy '{value}'."),
            };

    private static string DetermineNewline(string text)
    {
        var document = SourceTextCodec.Decode(
            new System.Text.UTF8Encoding(false, true).GetBytes(text));
        return document.Newline;
    }

    private static string ClassifyRequestedNewline(
        string newline,
        string text)
    {
        if (text.Length == 0)
        {
            return "none";
        }

        return newline switch
        {
            "\r\n" => "crlf",
            "\n" => "lf",
            "\r" => "cr",
            _ => "mixed",
        };
    }

    private static bool HasFinalNewline(string text) =>
        text.EndsWith("\n", StringComparison.Ordinal) ||
        text.EndsWith("\r", StringComparison.Ordinal);

    private static bool SameVolume(string left, string right) =>
        string.Equals(
            Path.GetPathRoot(Path.GetFullPath(left)),
            Path.GetPathRoot(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private sealed record ResolvedEdit(
        int Start,
        int End,
        string NewText);

    [GeneratedRegex(
        "^[A-Za-z0-9._:-]{1,128}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TransactionIdPattern();
}
