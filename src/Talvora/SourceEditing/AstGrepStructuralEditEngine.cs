using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Talvora.SourceEditing;

internal static class AstGrepStructuralEditEngine
{
    private static readonly UTF8Encoding MirrorEncoding =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    public static async Task<SourceEditTransactionResult> ApplyAsync(
        string workspaceRoot,
        string transactionId,
        string? rewrite,
        string? ruleFile,
        string? pattern,
        string? kind,
        string? language,
        string? selector,
        string? strictness,
        IReadOnlyList<string>? paths,
        IReadOnlyList<string>? globs,
        bool includeGenerated,
        bool includeHidden,
        int threads,
        int maxMatches,
        int maxFiles,
        long maxSingleFileBytes,
        long maxTotalBytes,
        int maxOutputCharacters,
        int timeoutSeconds,
        bool validateSyntax,
        CancellationToken cancellationToken)
    {
        var root =
            SourceWorkspaceClassifier.ResolveExplicitWorkspaceRoot(
                workspaceRoot);
        var request =
            StructuralRequest.Normalize(
                root,
                rewrite,
                ruleFile,
                pattern,
                kind,
                language,
                selector,
                strictness,
                paths,
                globs,
                includeGenerated,
                includeHidden,
                threads,
                maxMatches,
                maxFiles,
                maxSingleFileBytes,
                maxTotalBytes,
                maxOutputCharacters,
                timeoutSeconds,
                validateSyntax);
        if (request.RuleFile is not null)
        {
            var rulePath =
                SourceWorkspaceClassifier.ResolveWorkspacePath(
                    root,
                    request.RuleFile);
            SourceWorkspaceClassifier.EnsureNoReparsePoint(
                root,
                rulePath);
            if (!File.Exists(rulePath))
            {
                throw InvalidProposal(
                    "Structural ruleFile was not found.",
                    rulePath);
            }

            var ruleInfo =
                new FileInfo(rulePath);
            if (request.MaxSingleFileBytes > 0 &&
                ruleInfo.Length >
                    request.MaxSingleFileBytes)
            {
                throw ResourceLimit(
                    $"Structural ruleFile exceeds maxSingleFileBytes: {rulePath}");
            }

            byte[] ruleContent;
            try
            {
                ruleContent =
                    await File.ReadAllBytesAsync(
                        rulePath,
                        cancellationToken);
            }
            catch (Exception ex) when (
                ex is IOException or
                    UnauthorizedAccessException or
                    OutOfMemoryException)
            {
                throw InvalidProposal(
                    "Structural ruleFile could not be snapshotted safely.",
                    rulePath,
                    ex);
            }

            request =
                request with
                {
                    RuleFileContent =
                        ruleContent,
                    RuleFileRevision =
                        SourceEditRevision.Format(
                            SHA256.HashData(
                                ruleContent)),
                };
        }

        var requestHash =
            request.ComputeHash();

        return await SourceEditRuntime.Engine.ApplyGeneratedEditsAsync(
            root,
            transactionId,
            requestHash,
            validateSyntax,
            (canonicalRoot, token) =>
                GenerateChangesAsync(
                    canonicalRoot,
                    request,
                    token),
            cancellationToken);
    }

    internal static int ScalarColumnToUtf16Index(
        string line,
        int scalarColumn)
    {
        if (scalarColumn < 0)
        {
            throw InvalidProposal(
                "ast-grep returned a negative column.");
        }

        var scalarIndex = 0;
        var utf16Index = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (scalarIndex == scalarColumn)
            {
                return utf16Index;
            }

            utf16Index +=
                rune.Utf16SequenceLength;
            scalarIndex++;
        }

        if (scalarIndex == scalarColumn)
        {
            return utf16Index;
        }

        throw InvalidProposal(
            $"ast-grep column {scalarColumn} is outside the logical source line.");
    }

    private static async Task<IReadOnlyList<SourceEditChangeInput>> GenerateChangesAsync(
        string workspaceRoot,
        StructuralRequest request,
        CancellationToken cancellationToken)
    {
        var toolchain =
            await AstGrepToolchainManager.ResolveAsync(
                cancellationToken);
        var stagingRoot =
            Path.Combine(
                Path.GetTempPath(),
                "Talvora",
                "Structural",
                Guid.NewGuid().ToString("N"));
        var mirror = Path.Combine(stagingRoot, "source");
        Directory.CreateDirectory(mirror);

        try
        {
            var snapshots =
                await BuildMirrorAsync(
                    workspaceRoot,
                    mirror,
                    request,
                    cancellationToken);
            if (snapshots.Count == 0)
            {
                throw NoMatches(
                    "No eligible text/source files were found under the requested structural-edit paths.");
            }

            var matches =
                await RunProposalAsync(
                    toolchain.ExecutablePath,
                    mirror,
                    request,
                    await PrepareRuleFileAsync(
                        stagingRoot,
                        request,
                        cancellationToken),
                    cancellationToken);
            if (matches.Count == 0)
            {
                throw NoMatches(
                    "ast-grep found no structural matches. No source files were modified.");
            }

            return BuildStructuredChanges(
                mirror,
                snapshots,
                matches);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private static async Task<Dictionary<string, SourceFileSnapshot>> BuildMirrorAsync(
        string workspaceRoot,
        string mirrorRoot,
        StructuralRequest request,
        CancellationToken cancellationToken)
    {
        var discovered =
            new SortedSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var requested in request.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target =
                ResolveRequestedPath(
                    workspaceRoot,
                    requested);
            SourceWorkspaceClassifier.EnsureNoReparsePoint(
                workspaceRoot,
                target);

            if (File.Exists(target))
            {
                discovered.Add(target);
                continue;
            }

            if (!Directory.Exists(target))
            {
                throw InvalidProposal(
                    $"Structural-edit path was not found: {requested}",
                    target);
            }

            var pending =
                new Stack<string>();
            pending.Push(target);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory =
                    pending.Pop();

                foreach (var entry in Directory
                             .EnumerateFileSystemEntries(
                                 directory)
                             .OrderBy(
                                 path => path,
                                 StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attributes =
                        File.GetAttributes(entry);
                    if ((attributes &
                         FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((attributes &
                         FileAttributes.Directory) != 0)
                    {
                        var classification =
                            SourceWorkspaceClassifier.Classify(
                                entry);
                        if (!request.IncludeGenerated &&
                            classification.IsGeneratedLocation)
                        {
                            continue;
                        }

                        pending.Push(entry);
                        continue;
                    }

                    discovered.Add(entry);
                }
            }
        }

        var snapshots =
            new Dictionary<string, SourceFileSnapshot>(
                StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;

        foreach (var fullPath in discovered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The active rule is an input to the transformation, never its target.
            if (request.RuleFile is not null &&
                string.Equals(fullPath,
                    SourceWorkspaceClassifier.ResolveWorkspacePath(workspaceRoot, request.RuleFile),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var classification =
                SourceWorkspaceClassifier.Classify(
                    fullPath);
            if (!request.IncludeGenerated &&
                classification.IsGeneratedLocation)
            {
                continue;
            }

            if (!classification.IsKnownSourcePath &&
                !classification.IsLikelyText)
            {
                continue;
            }

            var info =
                new FileInfo(fullPath);
            if (request.MaxSingleFileBytes > 0 &&
                info.Length >
                request.MaxSingleFileBytes)
            {
                throw ResourceLimit(
                    $"Structural source file exceeds maxSingleFileBytes: {fullPath}");
            }

            if (request.MaxFiles > 0 &&
                snapshots.Count >=
                request.MaxFiles)
            {
                throw ResourceLimit(
                    $"Structural mirror exceeds maxFiles={request.MaxFiles}. Set maxFiles=0 for an explicit unbounded scan.");
            }

            if (request.MaxTotalBytes > 0 &&
                totalBytes + info.Length >
                request.MaxTotalBytes)
            {
                throw ResourceLimit(
                    $"Structural mirror exceeds maxTotalBytes={request.MaxTotalBytes}. Set maxTotalBytes=0 for an explicit unbounded scan.");
            }

            var relative =
                SourceWorkspaceClassifier.GetRelativePath(
                    workspaceRoot,
                    fullPath);
            SourceFileSnapshot snapshot;
            try
            {
                snapshot =
                    await SourceTextCodec.ReadSnapshotAsync(
                        fullPath,
                        relative,
                        requireText: true,
                        cancellationToken);
            }
            catch (SourceEditDomainException ex) when (
                ex.Code ==
                SourceEditCodes.UnsupportedEncoding &&
                !classification.IsKnownSourcePath)
            {
                continue;
            }

            if (!snapshot.Exists ||
                snapshot.Document is null ||
                snapshot.Revision is null)
            {
                continue;
            }

            var mirrorPath =
                Path.Combine(
                    mirrorRoot,
                    relative.Replace(
                        '/',
                        Path.DirectorySeparatorChar));
            var parent =
                Path.GetDirectoryName(
                    mirrorPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await File.WriteAllTextAsync(
                mirrorPath,
                snapshot.Document.Text,
                MirrorEncoding,
                cancellationToken);
            snapshots[relative] =
                snapshot;
            totalBytes +=
                snapshot.Length;
        }

        return snapshots;
    }

    private static async Task<IReadOnlyList<AstGrepMatch>> RunProposalAsync(
        string executable,
        string mirrorRoot,
        StructuralRequest request,
        string? mirrorRuleFile,
        CancellationToken cancellationToken)
    {
        var arguments =
            new List<string>();
        if (mirrorRuleFile is not null)
        {
            arguments.Add("scan");
            arguments.Add("--rule");
            arguments.Add(mirrorRuleFile);
        }
        else
        {
            arguments.Add("run");
            if (request.Pattern is not null)
            {
                arguments.Add("--pattern");
                arguments.Add(request.Pattern);
            }
            else
            {
                arguments.Add("--kind");
                arguments.Add(request.Kind!);
            }

            arguments.Add("--rewrite");
            arguments.Add(request.Rewrite!);

            if (request.Language is not null)
            {
                arguments.Add("--lang");
                arguments.Add(request.Language);
            }
            if (request.Selector is not null)
            {
                arguments.Add("--selector");
                arguments.Add(request.Selector);
            }
            if (request.Strictness is not null)
            {
                arguments.Add("--strictness");
                arguments.Add(request.Strictness);
            }
        }
        foreach (var glob in request.Globs)
        {
            arguments.Add("--globs");
            arguments.Add(glob);
        }
        if (request.Threads > 0)
        {
            arguments.Add("--threads");
            arguments.Add(
                request.Threads.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        if (request.IncludeHidden)
        {
            arguments.Add("--no-ignore");
            arguments.Add("hidden");
        }

        arguments.Add("--json=stream");
        arguments.Add("--color");
        arguments.Add("never");
        arguments.Add(".");

        var startInfo =
            new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = mirrorRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        using var process =
            new Process
            {
                StartInfo = startInfo,
            };
        if (!process.Start())
        {
            throw ToolFailed(
                "ast-grep process could not be started.");
        }

        using var timeoutCts =
            new CancellationTokenSource();
        if (request.TimeoutSeconds > 0)
        {
            timeoutCts.CancelAfter(
                TimeSpan.FromSeconds(
                    request.TimeoutSeconds));
        }
        using var linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

        var stderrTask =
            ReadBoundedTextAsync(
                process.StandardError,
                1_048_576,
                linked.Token);
        var matches =
            new List<AstGrepMatch>();
        long outputCharacters = 0;

        try
        {
            while (true)
            {
                var line =
                    await process.StandardOutput
                        .ReadLineAsync(
                            linked.Token);
                if (line is null)
                {
                    break;
                }

                outputCharacters +=
                    line.Length + 1L;
                if (request.MaxOutputCharacters > 0 &&
                    outputCharacters >
                    request.MaxOutputCharacters)
                {
                    throw ResourceLimit(
                        $"ast-grep JSON output exceeded maxOutputCharacters={request.MaxOutputCharacters}.");
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                matches.Add(
                    ParseMatch(line));
                if (request.MaxMatches > 0 &&
                    matches.Count >
                    request.MaxMatches)
                {
                    throw ResourceLimit(
                        $"ast-grep produced more than maxMatches={request.MaxMatches}. Set maxMatches=0 for an explicit unbounded transformation.");
                }
            }

            await process.WaitForExitAsync(
                linked.Token);
        }
        catch (OperationCanceledException) when (
            request.TimeoutSeconds > 0 &&
            timeoutCts.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw ToolFailed(
                $"ast-grep exceeded timeoutSeconds={request.TimeoutSeconds}.");
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var stderr =
            await stderrTask;
        if (process.ExitCode == 1 &&
            matches.Count == 0 &&
            string.IsNullOrWhiteSpace(stderr))
        {
            return matches;
        }

        if (process.ExitCode != 0)
        {
            throw ToolFailed(
                $"ast-grep failed with exitCode={process.ExitCode}: {stderr.Trim()}");
        }

        return matches;
    }

    private static IReadOnlyList<SourceEditChangeInput> BuildStructuredChanges(
        string mirrorRoot,
        IReadOnlyDictionary<string, SourceFileSnapshot> snapshots,
        IReadOnlyList<AstGrepMatch> matches)
    {
        var grouped =
            new Dictionary<string, List<SourceEditTextRangeInput>>(
                StringComparer.OrdinalIgnoreCase);
        var textMaps =
            new Dictionary<string, StructuralTextMap>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var match in matches)
        {
            var proposalPath =
                Path.IsPathRooted(match.File)
                    ? Path.GetFullPath(match.File)
                    : Path.GetFullPath(
                        Path.Combine(
                            mirrorRoot,
                            match.File));
            if (!SourceWorkspaceClassifier.IsContainedPath(
                    proposalPath,
                    mirrorRoot))
            {
                throw InvalidProposal(
                    "ast-grep returned a proposal outside the isolated structural mirror.",
                    proposalPath);
            }

            var relative =
                Path.GetRelativePath(
                        mirrorRoot,
                        proposalPath)
                    .Replace(
                        Path.DirectorySeparatorChar,
                        '/');
            if (!snapshots.TryGetValue(
                    relative,
                    out var snapshot) ||
                snapshot.Document is null ||
                snapshot.Revision is null)
            {
                throw InvalidProposal(
                    "ast-grep returned a file that is not part of the source snapshot.",
                    relative);
            }

            if (!textMaps.TryGetValue(
                    relative,
                    out var textMap))
            {
                textMap =
                    StructuralTextMap.Create(
                        snapshot.Document.Text);
                textMaps[relative] =
                    textMap;
            }

            var lines =
                textMap.Lines;
            if (match.Start.Line < 0 ||
                match.Start.Line >= lines.Count ||
                match.End.Line < 0 ||
                match.End.Line >= lines.Count)
            {
                throw InvalidProposal(
                    "ast-grep returned a line outside the source snapshot.",
                    snapshot.FullPath);
            }

            var startCharacter =
                ScalarColumnToUtf16Index(
                    lines[match.Start.Line].Content,
                    match.Start.Column);
            var endCharacter =
                ScalarColumnToUtf16Index(
                    lines[match.End.Line].Content,
                    match.End.Column);
            var matchStart =
                new AstGrepPosition(
                    match.Start.Line,
                    startCharacter);
            var matchEnd =
                new AstGrepPosition(
                    match.End.Line,
                    endCharacter);
            var actualStartByte =
                textMap.GetUtf8ByteOffset(
                    match.Start.Line,
                    startCharacter);
            var actualEndByte =
                textMap.GetUtf8ByteOffset(
                    match.End.Line,
                    endCharacter);
            if (actualStartByte !=
                    match.ByteStart ||
                actualEndByte !=
                    match.ByteEnd)
            {
                throw InvalidProposal(
                    "ast-grep line/column and UTF-8 byte offsets disagree with the isolated source snapshot.",
                    snapshot.FullPath);
            }

            var actualMatchText =
                textMap.GetText(
                    matchStart,
                    matchEnd);
            if (!string.Equals(
                    actualMatchText,
                    match.Text,
                    StringComparison.Ordinal))
            {
                throw InvalidProposal(
                    "ast-grep match text does not equal the isolated source snapshot.",
                    snapshot.FullPath);
            }

            var replacementStart =
                textMap.GetPositionFromUtf8ByteOffset(
                    match.ReplacementByteStart);
            var replacementEnd =
                textMap.GetPositionFromUtf8ByteOffset(
                    match.ReplacementByteEnd);
            var replacementExpectedText =
                textMap.GetText(
                    replacementStart,
                    replacementEnd);
            if (string.Equals(
                    replacementExpectedText,
                    match.Replacement,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!grouped.TryGetValue(
                    relative,
                    out var edits))
            {
                edits = [];
                grouped[relative] = edits;
            }

            edits.Add(
                new SourceEditTextRangeInput
                {
                    StartLine = replacementStart.Line,
                    StartCharacter = replacementStart.Column,
                    EndLine = replacementEnd.Line,
                    EndCharacter = replacementEnd.Column,
                    ExpectedText = replacementExpectedText,
                    NewText = match.Replacement,
                });
        }

        return grouped
            .OrderBy(
                pair => pair.Key,
                StringComparer.Ordinal)
            .Select(pair =>
            {
                var snapshot =
                    snapshots[pair.Key];
                return new SourceEditChangeInput
                {
                    Operation = "update",
                    Path = pair.Key,
                    ExpectedRevision =
                        snapshot.Revision,
                    Edits = pair.Value,
                };
            })
            .ToArray();
    }

    private static AstGrepMatch ParseMatch(
        string jsonLine)
    {
        try
        {
            using var json =
                JsonDocument.Parse(
                    jsonLine);
            var root =
                json.RootElement;
            var file =
                RequireString(
                    root,
                    "file");
            var text =
                RequireString(
                    root,
                    "text",
                    allowEmpty: true);
            var replacement =
                RequireString(
                    root,
                    "replacement",
                    allowEmpty: true);
            if (!root.TryGetProperty(
                    "range",
                    out var range) ||
                range.ValueKind !=
                JsonValueKind.Object)
            {
                throw InvalidProposal(
                    "ast-grep match is missing range.");
            }

            return new AstGrepMatch(
                file,
                text,
                replacement,
                ParseByteOffset(
                    range,
                    "start"),
                ParseByteOffset(
                    range,
                    "end"),
                ParsePosition(
                    range,
                    "start"),
                ParsePosition(
                    range,
                    "end"),
                root.TryGetProperty(
                    "replacementOffsets",
                    out var replacementOffsets) &&
                replacementOffsets.ValueKind ==
                    JsonValueKind.Object
                    ? ParseDirectByteOffset(
                        replacementOffsets,
                        "start")
                    : ParseByteOffset(
                        range,
                        "start"),
                root.TryGetProperty(
                    "replacementOffsets",
                    out replacementOffsets) &&
                replacementOffsets.ValueKind ==
                    JsonValueKind.Object
                    ? ParseDirectByteOffset(
                        replacementOffsets,
                        "end")
                    : ParseByteOffset(
                        range,
                        "end"));
        }
        catch (JsonException ex)
        {
            throw InvalidProposal(
                "ast-grep emitted invalid JSON.",
                innerException: ex);
        }
    }

    private static long ParseByteOffset(
        JsonElement range,
        string property)
    {
        if (!range.TryGetProperty(
                "byteOffset",
                out var byteOffset) ||
            byteOffset.ValueKind !=
                JsonValueKind.Object ||
            !byteOffset.TryGetProperty(
                property,
                out var node) ||
            !node.TryGetInt64(
                out var value) ||
            value < 0)
        {
            throw InvalidProposal(
                $"ast-grep match is missing range.byteOffset.{property}.");
        }

        return value;
    }

    private static long ParseDirectByteOffset(
        JsonElement offsets,
        string property)
    {
        if (!offsets.TryGetProperty(
                property,
                out var node) ||
            !node.TryGetInt64(
                out var value) ||
            value < 0)
        {
            throw InvalidProposal(
                $"ast-grep match is missing replacementOffsets.{property}.");
        }

        return value;
    }

    private static AstGrepPosition ParsePosition(
        JsonElement range,
        string property)
    {
        if (!range.TryGetProperty(
                property,
                out var node) ||
            node.ValueKind !=
            JsonValueKind.Object ||
            !node.TryGetProperty(
                "line",
                out var lineNode) ||
            !lineNode.TryGetInt32(
                out var line) ||
            !node.TryGetProperty(
                "column",
                out var columnNode) ||
            !columnNode.TryGetInt32(
                out var column))
        {
            throw InvalidProposal(
                $"ast-grep match is missing range.{property}.line/column.");
        }

        return new AstGrepPosition(
            line,
            column);
    }

    private static string RequireString(
        JsonElement element,
        string property,
        bool allowEmpty = false)
    {
        if (!element.TryGetProperty(
                property,
                out var node) ||
            node.ValueKind !=
            JsonValueKind.String)
        {
            throw InvalidProposal(
                $"ast-grep match is missing '{property}'.");
        }

        var value =
            node.GetString() ??
            string.Empty;
        if (!allowEmpty &&
            string.IsNullOrWhiteSpace(value))
        {
            throw InvalidProposal(
                $"ast-grep match contains an empty '{property}'.");
        }

        return value;
    }

    private static string ResolveRequestedPath(
        string workspaceRoot,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            string.Equals(
                relativePath.Trim(),
                ".",
                StringComparison.Ordinal))
        {
            return workspaceRoot;
        }

        return SourceWorkspaceClassifier.ResolveWorkspacePath(
            workspaceRoot,
            relativePath);
    }

    private static async Task<string?> PrepareRuleFileAsync(
        string mirrorRoot,
        StructuralRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RuleFile is null)
        {
            return null;
        }

        if (request.RuleFileContent is null ||
            request.RuleFileRevision is null)
        {
            throw InvalidProposal(
                "Structural ruleFile snapshot/revision is missing.");
        }

        var destination =
            Path.Combine(
                mirrorRoot,
                ".talvora-structural-rule.yml");
        await File.WriteAllBytesAsync(
            destination,
            request.RuleFileContent,
            cancellationToken);
        return destination;
    }

    private static async Task<string> ReadBoundedTextAsync(
        StreamReader reader,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        var builder =
            new StringBuilder();
        var buffer =
            new char[4096];
        while (true)
        {
            var read =
                await reader.ReadAsync(
                    buffer,
                    cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (builder.Length + read >
                maxCharacters)
            {
                throw ResourceLimit(
                    "ast-grep stderr exceeded the diagnostic output limit.");
            }

            builder.Append(
                buffer,
                0,
                read);
        }

        return builder.ToString();
    }

    private static void TryKill(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void TryDeleteDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static SourceEditDomainException NoMatches(
        string message) =>
        new(
            SourceEditCodes.StructuralNoMatches,
            $"{SourceEditCodes.StructuralNoMatches}: {message}");

    private static SourceEditDomainException InvalidProposal(
        string message,
        string? path = null,
        Exception? innerException = null) =>
        new(
            SourceEditCodes.StructuralProposalInvalid,
            $"{SourceEditCodes.StructuralProposalInvalid}: {message}",
            path,
            innerException: innerException);

    private static SourceEditDomainException ToolFailed(
        string message) =>
        new(
            SourceEditCodes.StructuralToolFailed,
            $"{SourceEditCodes.StructuralToolFailed}: {message}");

    private static SourceEditDomainException ResourceLimit(
        string message) =>
        new(
            SourceEditCodes.ResourceLimit,
            $"{SourceEditCodes.ResourceLimit}: {message}");

    private sealed record AstGrepMatch(
        string File,
        string Text,
        string Replacement,
        long ByteStart,
        long ByteEnd,
        AstGrepPosition Start,
        AstGrepPosition End,
        long ReplacementByteStart,
        long ReplacementByteEnd);

    private sealed record AstGrepPosition(
        int Line,
        int Column);

    private sealed record StructuralTextMap(
        string Text,
        IReadOnlyList<SourceTextLine> Lines,
        IReadOnlyList<long> Utf8LineStarts)
    {
        public static StructuralTextMap Create(
            string text)
        {
            var lines =
                SourceTextCodec.SplitLines(
                    text);
            var starts =
                new long[lines.Count];
            long offset = 0;
            for (var index = 0;
                 index < lines.Count;
                 index++)
            {
                starts[index] =
                    offset;
                offset +=
                    MirrorEncoding.GetByteCount(
                        lines[index].Content);
                offset +=
                    MirrorEncoding.GetByteCount(
                        lines[index].Terminator);
            }

            return new StructuralTextMap(
                text,
                lines,
                starts);
        }

        public long GetUtf8ByteOffset(
            int line,
            int utf16Character)
        {
            if (line < 0 ||
                line >= Lines.Count ||
                utf16Character < 0 ||
                utf16Character >
                    Lines[line].Content.Length)
            {
                throw InvalidProposal(
                    "Structural byte-offset verification received an invalid source position.");
            }

            return Utf8LineStarts[line] +
                   MirrorEncoding.GetByteCount(
                       Lines[line].Content.AsSpan(
                           0,
                           utf16Character));
        }

        public AstGrepPosition GetPositionFromUtf8ByteOffset(
            long byteOffset)
        {
            if (byteOffset < 0)
            {
                throw InvalidProposal(
                    "Structural byte offset cannot be negative.");
            }

            for (var line = 0;
                 line < Lines.Count;
                 line++)
            {
                var item =
                    Lines[line];
                var lineStart =
                    Utf8LineStarts[line];
                var contentBytes =
                    MirrorEncoding.GetByteCount(
                        item.Content);
                var contentEnd =
                    lineStart +
                    contentBytes;
                if (byteOffset >= lineStart &&
                    byteOffset <= contentEnd)
                {
                    var relative =
                        checked(
                            (int)(
                                byteOffset -
                                lineStart));
                    var encoded =
                        MirrorEncoding.GetBytes(
                            item.Content);
                    int utf16;
                    try
                    {
                        utf16 =
                            MirrorEncoding.GetCharCount(
                                encoded.AsSpan(
                                    0,
                                    relative));
                    }
                    catch (DecoderFallbackException ex)
                    {
                        throw InvalidProposal(
                            "Structural byte offset splits a UTF-8 scalar boundary.",
                            innerException: ex);
                    }

                    return new AstGrepPosition(
                        line,
                        utf16);
                }

                var terminatorEnd =
                    contentEnd +
                    MirrorEncoding.GetByteCount(
                        item.Terminator);
                if (byteOffset < terminatorEnd)
                {
                    throw InvalidProposal(
                        "Structural byte offset points inside a newline terminator.");
                }
            }

            throw InvalidProposal(
                "Structural byte offset is outside the UTF-8 mirror.");
        }

        public string GetText(
            AstGrepPosition start,
            AstGrepPosition end)
        {
            var startOffset =
                GetAbsoluteUtf16Offset(
                    start);
            var endOffset =
                GetAbsoluteUtf16Offset(
                    end);
            if (endOffset < startOffset)
            {
                throw InvalidProposal(
                    "Structural range end precedes its start.");
            }

            return Text[
                startOffset..
                endOffset];
        }

        private int GetAbsoluteUtf16Offset(
            AstGrepPosition position)
        {
            if (position.Line < 0 ||
                position.Line >= Lines.Count ||
                position.Column < 0 ||
                position.Column >
                    Lines[position.Line].Content.Length)
            {
                throw InvalidProposal(
                    "Structural source position is outside the isolated snapshot.");
            }

            var offset = 0;
            for (var line = 0;
                 line < position.Line;
                 line++)
            {
                offset +=
                    Lines[line].Content.Length +
                    Lines[line].Terminator.Length;
            }

            return offset +
                   position.Column;
        }
    }

    private sealed record StructuralRequest(
        string WorkspaceRoot,
        string? Rewrite,
        string? RuleFile,
        byte[]? RuleFileContent,
        string? RuleFileRevision,
        string? Pattern,
        string? Kind,
        string? Language,
        string? Selector,
        string? Strictness,
        IReadOnlyList<string> Paths,
        IReadOnlyList<string> Globs,
        bool IncludeGenerated,
        bool IncludeHidden,
        int Threads,
        int MaxMatches,
        int MaxFiles,
        long MaxSingleFileBytes,
        long MaxTotalBytes,
        int MaxOutputCharacters,
        int TimeoutSeconds,
        bool ValidateSyntax)
    {
        public static StructuralRequest Normalize(
            string workspaceRoot,
            string? rewrite,
            string? ruleFile,
            string? pattern,
            string? kind,
            string? language,
            string? selector,
            string? strictness,
            IReadOnlyList<string>? paths,
            IReadOnlyList<string>? globs,
            bool includeGenerated,
            bool includeHidden,
            int threads,
            int maxMatches,
            int maxFiles,
            long maxSingleFileBytes,
            long maxTotalBytes,
            int maxOutputCharacters,
            int timeoutSeconds,
            bool validateSyntax)
        {
            var normalizedRuleFile =
                string.IsNullOrWhiteSpace(ruleFile)
                    ? null
                    : ruleFile.Trim()
                        .Replace(
                            '\\',
                            '/');
            var normalizedPattern =
                string.IsNullOrWhiteSpace(pattern)
                    ? null
                    : pattern;
            var normalizedKind =
                string.IsNullOrWhiteSpace(kind)
                    ? null
                    : kind.Trim();
            var queryCount =
                (normalizedRuleFile is null ? 0 : 1) +
                (normalizedPattern is null ? 0 : 1) +
                (normalizedKind is null ? 0 : 1);
            if (queryCount != 1)
            {
                throw InvalidProposal(
                    "Specify exactly one structural query source: ruleFile, pattern, or kind.");
            }

            if (normalizedRuleFile is null &&
                rewrite is null)
            {
                throw InvalidProposal(
                    "Pattern/kind structural mode requires rewrite. Rule-file mode reads fix from the YAML rule.");
            }
            if (normalizedRuleFile is not null &&
                rewrite is not null)
            {
                throw InvalidProposal(
                    "ruleFile mode reads fix from the YAML rule; do not also supply rewrite.");
            }

            var normalizedSelector =
                string.IsNullOrWhiteSpace(selector)
                    ? null
                    : selector.Trim();
            var normalizedStrictness =
                string.IsNullOrWhiteSpace(strictness)
                    ? null
                    : strictness.Trim().ToLowerInvariant();
            if (normalizedRuleFile is not null &&
                (!string.IsNullOrWhiteSpace(language) ||
                 normalizedSelector is not null ||
                 normalizedStrictness is not null))
            {
                throw InvalidProposal(
                    "language/selector/strictness belong to pattern/kind mode; ruleFile mode defines its matcher and language in YAML.");
            }
            if (normalizedPattern is null &&
                normalizedRuleFile is null &&
                (normalizedSelector is not null ||
                 normalizedStrictness is not null))
            {
                throw InvalidProposal(
                    "selector/strictness require a pattern-based structural query.");
            }

            if (threads < 0 ||
                maxMatches < 0 ||
                maxFiles < 0 ||
                maxSingleFileBytes < 0 ||
                maxTotalBytes < 0 ||
                maxOutputCharacters < 0 ||
                timeoutSeconds < 0)
            {
                throw InvalidProposal(
                    "Structural limits, thread count, and timeout cannot be negative; use 0 for the documented automatic/unbounded behavior.");
            }

            var normalizedPaths =
                (paths is null ||
                 paths.Count == 0
                    ? new[] { "." }
                    : paths)
                .Select(path =>
                    string.IsNullOrWhiteSpace(path)
                        ? "."
                        : path.Trim()
                            .Replace(
                                '\\',
                                '/'))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var normalizedGlobs =
                (globs ?? [])
                .Where(glob =>
                    !string.IsNullOrWhiteSpace(glob))
                .Select(glob =>
                    glob.Trim())
                .ToArray();

            return new StructuralRequest(
                Path.GetFullPath(
                    workspaceRoot),
                rewrite,
                normalizedRuleFile,
                null,
                null,
                normalizedPattern,
                normalizedKind,
                string.IsNullOrWhiteSpace(language)
                    ? null
                    : language.Trim(),
                normalizedSelector,
                normalizedStrictness,
                normalizedPaths,
                normalizedGlobs,
                includeGenerated,
                includeHidden,
                threads,
                maxMatches,
                maxFiles,
                maxSingleFileBytes,
                maxTotalBytes,
                maxOutputCharacters,
                timeoutSeconds,
                validateSyntax);
        }

        public string ComputeHash()
        {
            var payload =
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        schemaVersion = 1,
                        inputKind =
                            "ast-grep-structural-edit-v2",
                        workspaceRoot =
                            WorkspaceRoot.ToUpperInvariant(),
                        rewrite = Rewrite,
                        ruleFile = RuleFile,
                        ruleFileRevision =
                            RuleFileRevision,
                        pattern = Pattern,
                        kind = Kind,
                        language = Language,
                        selector = Selector,
                        strictness = Strictness,
                        paths = Paths,
                        globs = Globs,
                        includeGenerated = IncludeGenerated,
                        includeHidden = IncludeHidden,
                        threads = Threads,
                        maxMatches = MaxMatches,
                        maxFiles = MaxFiles,
                        maxSingleFileBytes =
                            MaxSingleFileBytes,
                        maxTotalBytes =
                            MaxTotalBytes,
                        maxOutputCharacters =
                            MaxOutputCharacters,
                        timeoutSeconds =
                            TimeoutSeconds,
                        validateSyntax =
                            ValidateSyntax,
                    });
            return SourceEditRevision.Format(
                SHA256.HashData(payload));
        }
    }
}
