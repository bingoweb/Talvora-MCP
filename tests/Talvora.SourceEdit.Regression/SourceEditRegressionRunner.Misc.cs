using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Talvora.SourceEditing;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    private static async Task EncodingAndNewlineAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        var engine = fixture.CreateEngine();

        var utf8Path =
            fixture.PathInWorkspace("utf8-bom.txt");
        var utf8Encoding =
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(
            utf8Path,
            "one\r\ntwo\r\n",
            utf8Encoding);
        var utf8Before =
            await File.ReadAllBytesAsync(utf8Path);
        var utf8Revision =
            await RevisionAsync(engine, utf8Path);

        var utf8Result = await engine.ApplyEditsAsync(
            fixture.Root,
            NewTransactionId(),
            [
                Update(
                    "utf8-bom.txt",
                    utf8Revision,
                    1,
                    0,
                    1,
                    3,
                    "TWO",
                    "two"),
            ],
            true,
            CancellationToken.None);
        Assert(
            utf8Result.Success,
            utf8Result.Error?.Message ??
            "UTF-8 BOM edit failed.");

        var utf8After =
            await File.ReadAllBytesAsync(utf8Path);
        Assert(
            utf8After.AsSpan().StartsWith(
                new byte[] { 0xEF, 0xBB, 0xBF }),
            "UTF-8 BOM was not preserved.");
        AssertEqual(
            "one\r\nTWO\r\n",
            await File.ReadAllTextAsync(utf8Path),
            "CRLF preservation failed.");
        Assert(
            utf8Before.AsSpan(0, 3)
                .SequenceEqual(
                    utf8After.AsSpan(0, 3)),
            "UTF-8 BOM bytes changed.");

        var utf16Path =
            fixture.PathInWorkspace("utf16.txt");
        var utf16 =
            new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: true);
        var preamble = utf16.GetPreamble();
        var body = utf16.GetBytes(
            "a\r\nb\nc\r");
        var utf16Bytes =
            new byte[preamble.Length + body.Length];
        preamble.CopyTo(utf16Bytes, 0);
        body.CopyTo(
            utf16Bytes,
            preamble.Length);
        await File.WriteAllBytesAsync(
            utf16Path,
            utf16Bytes);
        var utf16Revision =
            await RevisionAsync(engine, utf16Path);

        var utf16Result = await engine.ApplyEditsAsync(
            fixture.Root,
            NewTransactionId(),
            [
                Update(
                    "utf16.txt",
                    utf16Revision,
                    1,
                    0,
                    1,
                    1,
                    "B",
                    "b"),
            ],
            true,
            CancellationToken.None);
        Assert(
            utf16Result.Success,
            utf16Result.Error?.Message ??
            "UTF-16 edit failed.");

        var utf16After =
            await File.ReadAllBytesAsync(
                utf16Path);
        Assert(
            utf16After.AsSpan().StartsWith(
                new byte[] { 0xFF, 0xFE }),
            "UTF-16 LE BOM was not preserved.");
        var read =
            await engine.ReadSourceAsync(
                utf16Path,
                1,
                0,
                CancellationToken.None);
        AssertEqual(
            "mixed",
            read.Newline,
            "Mixed newline classification changed.");
        Assert(
            read.Text.Contains(
                "a\r\nB\nc\r",
                StringComparison.Ordinal),
            "Mixed newline layout was not preserved.");
    }

    private static async Task StreamingSourceReadBoundedAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        var path =
            fixture.PathInWorkspace("large-single-line.cs");
        await File.WriteAllTextAsync(
            path,
            new string('x', 32 * 1024) +
            "\r\nsecond\n",
            new UTF8Encoding(false));

        var engine = fixture.CreateEngine();
        var first =
            await engine.ReadSourceAsync(
                path,
                startLine: 1,
                startCharacter: 0,
                lineCount: 0,
                maxCharacters: 4096,
                CancellationToken.None);
        AssertEqual(
            4096,
            first.Text.Length,
            "Streaming source read did not honor response budget.");
        Assert(
            first.ResponseLimited,
            "Streaming source read did not expose response limiting.");
        Assert(
            !first.EndReached,
            "Bounded first source-read page incorrectly reported EOF.");
        AssertEqual(
            1,
            first.NextStartLine,
            "Single-line continuation changed the logical line.");
        AssertEqual(
            4096,
            first.NextStartCharacter,
            "Single-line continuation character was incorrect.");

        var expectedRevision =
            await SourceTextCodec.ComputeRevisionAsync(
                path,
                CancellationToken.None);
        AssertEqual(
            expectedRevision,
            first.Revision,
            "Streaming source read revision did not cover the whole file.");

        var second =
            await engine.ReadSourceAsync(
                path,
                first.NextStartLine,
                first.NextStartCharacter,
                lineCount: 0,
                maxCharacters: 4096,
                CancellationToken.None);
        AssertEqual(
            4096,
            second.Text.Length,
            "Streaming continuation page length was incorrect.");
        Assert(
            second.Text.All(
                character => character == 'x'),
            "Streaming continuation did not resume at the exact UTF-16 coordinate.");

        var boundedPath =
            fixture.PathInWorkspace(
                "bounded-materialization.cs");
        await File.WriteAllTextAsync(
            boundedPath,
            new string('b', 8192),
            new UTF8Encoding(false));
        var boundedThrew = false;
        try
        {
            _ = await SourceTextStreamingReader.ReadAllBytesBoundedAsync(
                boundedPath,
                maximumBytes: 4096,
                CancellationToken.None);
        }
        catch (SourceEditDomainException ex)
        {
            boundedThrew =
                string.Equals(
                    ex.Code,
                    SourceEditCodes.ResourceLimit,
                    StringComparison.Ordinal);
        }

        Assert(
            boundedThrew,
            "Full-document materialization did not fail before exceeding its hard byte budget.");
    }

    private static async Task ReadOnlyRejectionAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "readonly.cs",
            "before\n");
        var engine = fixture.CreateEngine();
        var path =
            fixture.PathInWorkspace("readonly.cs");
        var revision =
            await RevisionAsync(engine, path);
        File.SetAttributes(
            path,
            File.GetAttributes(path) |
            FileAttributes.ReadOnly);

        try
        {
            var result =
                await engine.ApplyPatchAsync(
                    fixture.Root,
                    NewTransactionId(),
                    PatchUpdate(
                        "readonly.cs",
                        revision,
                        "-before",
                        "+after"),
                    true,
                    CancellationToken.None);

            AssertError(
                result,
                SourceEditCodes.ReadOnly);
            AssertEqual(
                "before\n",
                await File.ReadAllTextAsync(path),
                "Read-only rejection modified source.");
        }
        finally
        {
            File.SetAttributes(
                path,
                File.GetAttributes(path) &
                ~FileAttributes.ReadOnly);
        }
    }

    private static async Task ReparsePointRejectionAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        var realDirectory =
            fixture.PathInWorkspace("real");
        Directory.CreateDirectory(realDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(realDirectory, "linked.cs"),
            "linked\n");

        var link =
            fixture.PathInWorkspace("linked-dir");
        try
        {
            Directory.CreateSymbolicLink(
                link,
                realDirectory);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
                IOException or
                PlatformNotSupportedException)
        {
            throw new SkipTestException(
                $"Symbolic link creation unavailable: {ex.Message}");
        }

        var engine = fixture.CreateEngine();
        var threw = false;
        try
        {
            _ = await engine.ReadSourceAsync(
                Path.Combine(link, "linked.cs"),
                1,
                0,
                CancellationToken.None);
        }
        catch (SourceEditDomainException ex)
        {
            threw = true;
            AssertEqual(
                SourceEditCodes.ReparsePointUnsupported,
                ex.Code,
                "Unexpected reparse-point error code.");
        }

        Assert(
            threw,
            "Reparse-point source path was not rejected.");
    }

    private static async Task LegacyPolicyAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "legacy.cs",
            "legacy\n");
        var path =
            fixture.PathInWorkspace("legacy.cs");

        ExpectPolicyViolation(
            () =>
                SourceMutationPolicy
                    .EnsureLegacyTextMutationAllowed(
                        path,
                        "talvora_write_text"));

        ExpectPolicyViolation(
            () =>
                SourceMutationPolicy
                    .EnsureLegacyTextMutationAllowed(
                        path,
                        "talvora_replace_text"));

        ExpectPolicyViolation(
            () =>
                SourceMutationPolicy
                    .EnsureLegacyTextMutationAllowed(
                        path,
                        "talvora_append_text"));

        ExpectPolicyViolation(
            () =>
                SourceMutationPolicy
                    .EnsureLegacyByteMutationAllowed(
                        path,
                        Encoding.UTF8.GetBytes(
                            "replacement"),
                        "talvora_write_bytes"));

        var deletePath =
            fixture.PathInWorkspace("delete-me.cs");
        await File.WriteAllTextAsync(
            deletePath,
            "delete-me\n");
        ExpectPolicyViolation(
            () => _ = FileTools.Delete(deletePath));
        Assert(
            File.Exists(deletePath),
            "Direct source-file delete mutated the workspace despite routing policy.");

        var moveSource =
            fixture.PathInWorkspace("move-me.cs");
        var moveDestination =
            fixture.PathInWorkspace("moved.cs");
        await File.WriteAllTextAsync(
            moveSource,
            "move-me\n");
        ExpectPolicyViolation(
            () => _ = FileTools.Move(
                moveSource,
                moveDestination));
        Assert(
            File.Exists(moveSource) &&
            !File.Exists(moveDestination),
            "Same-workspace source move bypassed the Source Edit Engine.");

        var outsideDestination =
            Path.Combine(
                fixture.OrdinaryRoot,
                "moved-out.cs");
        ExpectPolicyViolation(
            () => _ = FileTools.Move(
                moveSource,
                outsideDestination));
        Assert(
            File.Exists(moveSource) &&
            !File.Exists(outsideDestination),
            "Default cross-workspace source move was not routed to canonical Source Edit.");

        var outsideMove =
            FileTools.Move(
                moveSource,
                outsideDestination,
                explicitAdmin: true);
        Assert(
            outsideMove.Changed &&
            !File.Exists(moveSource) &&
            File.Exists(outsideDestination),
            "Explicit-admin cross-workspace source move capability regressed.");
    }

    private static async Task GenericSourceRoutingPolicyAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        await fixture.WriteUtf8Async(
            "App/App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n");
        await fixture.WriteUtf8Async(
            "Lib/Lib.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n");

        var nestedMoveSource =
            fixture.PathInWorkspace(
                "App/Use.cs");
        var nestedMoveDestination =
            fixture.PathInWorkspace(
                "Lib/UseMoved.cs");
        await File.WriteAllTextAsync(
            nestedMoveSource,
            "class Use {}\n");
        ExpectPolicyViolation(
            () => _ = FileTools.Move(
                nestedMoveSource,
                nestedMoveDestination));
        Assert(
            File.Exists(nestedMoveSource) &&
            !File.Exists(nestedMoveDestination),
            "Nested-project move mutated source outside canonical routing.");

        var copySource =
            fixture.PathInWorkspace(
                "Widget.cs");
        var copyDestination =
            fixture.PathInWorkspace(
                "WidgetCopy.cs");
        await File.WriteAllTextAsync(
            copySource,
            "class Widget {}\n");
        ExpectPolicyViolation(
            () => _ = FileTools.Copy(
                copySource,
                copyDestination));
        Assert(
            !File.Exists(copyDestination),
            "Default source copy created a development source destination.");
        var adminCopy =
            FileTools.Copy(
                copySource,
                copyDestination,
                explicitAdmin: true);
        Assert(
            adminCopy.Changed &&
            File.Exists(copyDestination),
            "Explicit-admin source copy capability regressed.");

        var ingressSource =
            Path.Combine(
                fixture.OrdinaryRoot,
                "Incoming.cs");
        var ingressDestination =
            fixture.PathInWorkspace(
                "Ingress.cs");
        await File.WriteAllTextAsync(
            ingressSource,
            "class Incoming {}\n");
        ExpectPolicyViolation(
            () => _ = FileTools.Move(
                ingressSource,
                ingressDestination));
        Assert(
            File.Exists(ingressSource) &&
            !File.Exists(ingressDestination),
            "Default destination-side source ingress mutated the workspace.");
        var adminIngress =
            FileTools.Move(
                ingressSource,
                ingressDestination,
                explicitAdmin: true);
        Assert(
            adminIngress.Changed &&
            File.Exists(ingressDestination),
            "Explicit-admin destination ingress capability regressed.");

        ExpectPolicyViolation(
            () => _ = GitTools.Run(
                fixture.Root,
                ["restore", "--", "Widget.cs"]));

        var archiveSource =
            Path.Combine(
                fixture.OrdinaryRoot,
                "archive-source");
        Directory.CreateDirectory(
            archiveSource);
        await File.WriteAllTextAsync(
            Path.Combine(
                archiveSource,
                "Archive.cs"),
            "class ArchiveItem {}\n");
        var archivePath =
            Path.Combine(
                fixture.OrdinaryRoot,
                "source.zip");
        ZipFile.CreateFromDirectory(
            archiveSource,
            archivePath);
        ExpectPolicyViolation(
            () => _ = ConfigAssetTools.ArchiveExtract(
                archivePath,
                fixture.Root));
        Assert(
            !File.Exists(
                fixture.PathInWorkspace(
                    "Archive.cs")),
            "Archive routing policy did not fail before source extraction.");
        var adminArchive =
            ConfigAssetTools.ArchiveExtract(
                archivePath,
                fixture.Root,
                explicitAdmin: true);
        Assert(
            adminArchive.EntriesExtracted == 1 &&
            File.Exists(
                fixture.PathInWorkspace(
                    "Archive.cs")),
            "Explicit-admin archive extraction capability regressed.");

        await ExpectPolicyViolationAsync(
            async () =>
            {
                _ = await ConfigAssetTools.HttpDownload(
                    "http://127.0.0.1:1/should-not-connect",
                    fixture.PathInWorkspace(
                        "Downloaded.cs"),
                    cancellationToken:
                        CancellationToken.None);
            });
        Assert(
            !File.Exists(
                fixture.PathInWorkspace(
                    "Downloaded.cs")),
            "HTTP download routing policy did not reject before network/file mutation.");
    }

    private static async Task HttpDownloadStagedOverwritePreservesExistingAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        var destination =
            Path.Combine(
                fixture.OrdinaryRoot,
                "Partial.cs");
        const string original =
            "public class Partial { public int Value => 1; }\n";
        await File.WriteAllTextAsync(
            destination,
            original);

        var listener =
            new TcpListener(
                IPAddress.Loopback,
                0);
        listener.Start();
        var port =
            ((IPEndPoint)listener.LocalEndpoint).Port;
        var server =
            Task.Run(
                async () =>
                {
                    try
                    {
                        using var client =
                            await listener.AcceptTcpClientAsync();
                        await using var stream =
                            client.GetStream();
                        var requestBuffer =
                            new byte[4096];
                        _ =
                            await stream.ReadAsync(
                                requestBuffer);
                        var responseBytes =
                            Encoding.ASCII.GetBytes(
                                "HTTP/1.1 200 OK\r\n" +
                                "Content-Length: 1000\r\n" +
                                "Connection: close\r\n" +
                                "\r\n" +
                                "BROKENBODY");
                        await stream.WriteAsync(
                            responseBytes);
                        await stream.FlushAsync();
                    }
                    finally
                    {
                        listener.Stop();
                    }
                });

        var failed = false;
        try
        {
            _ =
                await ConfigAssetTools.HttpDownload(
                    $"http://127.0.0.1:{port}/partial",
                    destination,
                    overwrite: true,
                    timeoutSeconds: 30,
                    cancellationToken:
                        CancellationToken.None);
        }
        catch (Exception ex) when (
            ex is IOException or
                HttpRequestException)
        {
            failed = true;
        }

        await server;
        Assert(
            failed,
            "Truncated HTTP response unexpectedly committed as a successful download.");
        AssertEqual(
            original,
            await File.ReadAllTextAsync(
                destination),
            "Failed HTTP overwrite changed the existing destination.");
        Assert(
            !Directory.EnumerateFiles(
                    fixture.OrdinaryRoot,
                    ".Partial.cs.talvora-download-*.tmp",
                    SearchOption.TopDirectoryOnly)
                .Any(),
            "Failed HTTP overwrite left a stage artifact.");
    }

    private static async Task ArchiveTransactionalRollbackAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        var destination =
            Path.Combine(
                fixture.OrdinaryRoot,
                "archive-transaction");
        Directory.CreateDirectory(
            destination);
        var first =
            Path.Combine(
                destination,
                "first.txt");
        var second =
            Path.Combine(
                destination,
                "second.txt");
        await File.WriteAllTextAsync(
            first,
            "first-before\n");
        await File.WriteAllTextAsync(
            second,
            "second-before\n");
        var archivePath =
            Path.Combine(
                fixture.OrdinaryRoot,
                "transaction.zip");
        using (var archive =
               ZipFile.Open(
                   archivePath,
                   ZipArchiveMode.Create))
        {
            var firstEntry =
                archive.CreateEntry(
                    "first.txt");
            await using (var stream =
                         firstEntry.Open())
            await using (var writer =
                         new StreamWriter(
                             stream,
                             Encoding.UTF8,
                             leaveOpen: false))
            {
                await writer.WriteAsync(
                    "first-after\n");
            }

            var secondEntry =
                archive.CreateEntry(
                    "second.txt");
            await using (var stream =
                         secondEntry.Open())
            await using (var writer =
                         new StreamWriter(
                             stream,
                             Encoding.UTF8,
                             leaveOpen: false))
            {
                await writer.WriteAsync(
                    "second-after\n");
            }
        }

        await using var lockedSecond =
            new FileStream(
                second,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
        var failed = false;
        try
        {
            _ =
                ConfigAssetTools.ArchiveExtract(
                    archivePath,
                    destination,
                    overwrite: true);
        }
        catch (IOException)
        {
            failed = true;
        }

        Assert(
            failed,
            "Archive publication unexpectedly succeeded while the second destination was locked.");
        AssertEqual(
            "first-before\n",
            await File.ReadAllTextAsync(
                first),
            "Archive rollback did not restore the first file after a later commit failure.");
        AssertEqual(
            "second-before\n",
            await File.ReadAllTextAsync(
                second),
            "Archive failure changed the locked second destination.");
        Assert(
            !Directory.EnumerateFiles(
                    destination,
                    ".*.talvora-archive-*.tmp",
                    SearchOption.TopDirectoryOnly)
                .Any(),
            "Archive failure left commit artifacts in the destination.");
    }

    private static async Task ArchiveContainedReparseRejectionAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        var destination =
            Path.Combine(
                fixture.OrdinaryRoot,
                "archive-contained");
        var outside =
            Path.Combine(
                fixture.OrdinaryRoot,
                "archive-outside");
        Directory.CreateDirectory(
            destination);
        Directory.CreateDirectory(
            outside);
        var link =
            Path.Combine(
                destination,
                "link");
        try
        {
            Directory.CreateSymbolicLink(
                link,
                outside);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
                IOException or
                PlatformNotSupportedException)
        {
            throw new SkipTestException(
                $"Directory symbolic-link fixture is unavailable: {ex.Message}");
        }

        var archivePath =
            Path.Combine(
                fixture.OrdinaryRoot,
                "contained.zip");
        using (var archive =
               ZipFile.Open(
                   archivePath,
                   ZipArchiveMode.Create))
        {
            var entry =
                archive.CreateEntry(
                    "link/contained.txt");
            await using var stream =
                entry.Open();
            await stream.WriteAsync(
                Encoding.UTF8.GetBytes(
                    "contained\n"));
        }

        var failed = false;
        try
        {
            _ =
                ConfigAssetTools.ArchiveExtract(
                    archivePath,
                    destination,
                    overwrite: true);
        }
        catch (SourceEditDomainException ex)
            when (ex.Code ==
                  SourceEditCodes.ReparsePointUnsupported)
        {
            failed = true;
        }

        Assert(
            failed,
            "Contained archive extraction did not reject the reparse-point destination component.");
        Assert(
            !File.Exists(
                Path.Combine(
                    outside,
                    "contained.txt")),
            "Contained archive extraction wrote through the reparse-point destination.");
    }

    private static async Task ArchiveResourceBudgetsAsync()
    {
        await using var fixture =
            await TestWorkspace.CreateAsync();
        var multiArchive =
            Path.Combine(
                fixture.OrdinaryRoot,
                "entry-budget.zip");
        using (var archive =
               ZipFile.Open(
                   multiArchive,
                   ZipArchiveMode.Create))
        {
            foreach (var name in new[]
                     {
                         "one.txt",
                         "two.txt",
                     })
            {
                var entry =
                    archive.CreateEntry(
                        name);
                await using var stream =
                    entry.Open();
                await stream.WriteAsync(
                    Encoding.UTF8.GetBytes(
                        "12345"));
            }
        }

        var entryDestination =
            Path.Combine(
                fixture.OrdinaryRoot,
                "entry-budget-destination");
        var entryFailed = false;
        try
        {
            _ =
                ConfigAssetTools.ArchiveExtract(
                    multiArchive,
                    entryDestination,
                    maxEntries: 1);
        }
        catch (InvalidDataException)
        {
            entryFailed = true;
        }

        Assert(
            entryFailed &&
            !Directory.Exists(
                entryDestination),
            "Archive entry-count budget did not reject before destination mutation.");

        var byteDestination =
            Path.Combine(
                fixture.OrdinaryRoot,
                "byte-budget-destination");
        var byteFailed = false;
        try
        {
            _ =
                ConfigAssetTools.ArchiveExtract(
                    multiArchive,
                    byteDestination,
                    maxTotalBytes: 9);
        }
        catch (InvalidDataException)
        {
            byteFailed = true;
        }

        Assert(
            byteFailed &&
            !Directory.Exists(
                byteDestination),
            "Archive decompressed-byte budget did not reject before destination mutation.");
    }

    private static async Task TypedConfigPolicyAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();

        var json = fixture.PathInWorkspace("config.json");
        var yaml = fixture.PathInWorkspace("config.yaml");
        var toml = fixture.PathInWorkspace("config.toml");
        var dotenv = fixture.PathInWorkspace(".env");
        var ini = fixture.PathInWorkspace("config.ini");
        var xml = fixture.PathInWorkspace("config.xml");

        await File.WriteAllTextAsync(json, "{\"enabled\":false}\n");
        await File.WriteAllTextAsync(yaml, "enabled: false\n");
        await File.WriteAllTextAsync(toml, "enabled = false\n");
        await File.WriteAllTextAsync(dotenv, "MODE=old\n");
        await File.WriteAllTextAsync(ini, "[app]\nmode=old\n");
        await File.WriteAllTextAsync(xml, "<root><mode>old</mode></root>");

        await ExpectPolicyViolationAsync(async () =>
        {
            _ = await ConfigAssetTools.JsonSet(json, "/enabled", "true", cancellationToken: CancellationToken.None);
        });
        await ExpectPolicyViolationAsync(async () =>
        {
            _ = await StructuredConfigTools.YamlSet(yaml, "/enabled", "true", cancellationToken: CancellationToken.None);
        });
        await ExpectPolicyViolationAsync(async () =>
        {
            _ = await StructuredConfigTools.TomlSet(toml, "/enabled", "true", cancellationToken: CancellationToken.None);
        });
        await ExpectPolicyViolationAsync(async () =>
        {
            _ = await ConfigFormatTools.DotenvSet(dotenv, "MODE", "new", cancellationToken: CancellationToken.None);
        });
        await ExpectPolicyViolationAsync(async () =>
        {
            _ = await ConfigFormatTools.IniSet(ini, "app", "mode", "new", cancellationToken: CancellationToken.None);
        });
        ExpectPolicyViolation(() =>
        {
            _ = ConfigFormatTools.XmlSet(xml, "/root/mode", "new", expectedMatches: 1);
        });

        AssertEqual("{\"enabled\":false}\n", await File.ReadAllTextAsync(json), "JSON typed mutation bypassed SourceMutationPolicy.");
        AssertEqual("enabled: false\n", await File.ReadAllTextAsync(yaml), "YAML typed mutation bypassed SourceMutationPolicy.");
        AssertEqual("enabled = false\n", await File.ReadAllTextAsync(toml), "TOML typed mutation bypassed SourceMutationPolicy.");
        AssertEqual("MODE=old\n", await File.ReadAllTextAsync(dotenv), "dotenv typed mutation bypassed SourceMutationPolicy.");
        AssertEqual("[app]\nmode=old\n", await File.ReadAllTextAsync(ini), "INI typed mutation bypassed SourceMutationPolicy.");
        AssertEqual("<root><mode>old</mode></root>", await File.ReadAllTextAsync(xml), "XML typed mutation bypassed SourceMutationPolicy.");

        var ordinaryJson = Path.Combine(fixture.OrdinaryRoot, "ordinary.json");
        await File.WriteAllTextAsync(ordinaryJson, "{\"enabled\":false}\n");
        var ordinaryResult = await ConfigAssetTools.JsonSet(
            ordinaryJson,
            "/enabled",
            "true",
            cancellationToken: CancellationToken.None);
        Assert(ordinaryResult.Changed, "Non-workspace typed JSON mutation was unexpectedly blocked.");
    }

    private static Task ToolRoutingDescriptionsAsync()
    {
        var canonical = new (Type Type, string Method)[]
        {
            (typeof(FileTools), nameof(FileTools.WriteText)),
            (typeof(DeveloperTools), nameof(DeveloperTools.ReplaceText)),
            (typeof(DeveloperTools), nameof(DeveloperTools.WriteBytes)),
            (typeof(ConfigAssetTools), nameof(ConfigAssetTools.AppendText)),
            (typeof(ConfigAssetTools), nameof(ConfigAssetTools.JsonSet)),
            (typeof(ConfigAssetTools), nameof(ConfigAssetTools.JsonDelete)),
            (typeof(StructuredConfigTools), nameof(StructuredConfigTools.YamlSet)),
            (typeof(StructuredConfigTools), nameof(StructuredConfigTools.YamlDelete)),
            (typeof(StructuredConfigTools), nameof(StructuredConfigTools.TomlSet)),
            (typeof(StructuredConfigTools), nameof(StructuredConfigTools.TomlDelete)),
            (typeof(ConfigFormatTools), nameof(ConfigFormatTools.DotenvSet)),
            (typeof(ConfigFormatTools), nameof(ConfigFormatTools.DotenvDelete)),
            (typeof(ConfigFormatTools), nameof(ConfigFormatTools.IniSet)),
            (typeof(ConfigFormatTools), nameof(ConfigFormatTools.IniDelete)),
            (typeof(ConfigFormatTools), nameof(ConfigFormatTools.XmlSet)),
            (typeof(ConfigFormatTools), nameof(ConfigFormatTools.XmlDelete)),
        };

        foreach (var item in canonical)
        {
            var method = item.Type.GetMethod(
                item.Method,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"Missing routed tool method {item.Type.Name}.{item.Method}.");
            var description =
                Attribute.GetCustomAttribute(
                    method,
                    typeof(System.ComponentModel.DescriptionAttribute))
                as System.ComponentModel.DescriptionAttribute;
            Assert(
                description?.Description.Contains(
                    "talvora_apply_patch",
                    StringComparison.Ordinal) == true &&
                description.Description.Contains(
                    "talvora_apply_edits",
                    StringComparison.Ordinal) == true &&
                description.Description.Contains(
                    "PRIMARY/default",
                    StringComparison.Ordinal) == true &&
                description.Description.Contains(
                    "only",
                    StringComparison.OrdinalIgnoreCase) == true,
                $"Tool description does not route to canonical source edit: {item.Type.Name}.{item.Method}");
        }

        foreach (var item in new[]
                 {
                     (Type: typeof(PowerShellTools), Method: nameof(PowerShellTools.RunPowerShell)),
                     (Type: typeof(ProcessTools), Method: nameof(ProcessTools.RunProcess)),
                 })
        {
            var method = item.Type.GetMethod(
                item.Method,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"Missing escape-hatch method {item.Type.Name}.{item.Method}.");
            var description =
                Attribute.GetCustomAttribute(
                    method,
                    typeof(System.ComponentModel.DescriptionAttribute))
                as System.ComponentModel.DescriptionAttribute;
            Assert(
                description?.Description.Contains(
                    "unrestricted administration/escape-hatch",
                    StringComparison.OrdinalIgnoreCase) == true,
                $"Escape-hatch routing description regressed: {item.Type.Name}.{item.Method}");
        }

        foreach (var item in new[]
                 {
                     (Type: typeof(FileTools), Method: nameof(FileTools.Delete)),
                     (Type: typeof(FileTools), Method: nameof(FileTools.Copy)),
                     (Type: typeof(FileTools), Method: nameof(FileTools.Move)),
                     (Type: typeof(GitTools), Method: nameof(GitTools.Run)),
                     (Type: typeof(ConfigAssetTools), Method: nameof(ConfigAssetTools.ArchiveExtract)),
                     (Type: typeof(ConfigAssetTools), Method: nameof(ConfigAssetTools.HttpDownload)),
                 })
        {
            var method = item.Type.GetMethod(
                item.Method,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"Missing source lifecycle method {item.Type.Name}.{item.Method}.");
            var description =
                Attribute.GetCustomAttribute(
                    method,
                    typeof(System.ComponentModel.DescriptionAttribute))
                as System.ComponentModel.DescriptionAttribute;
            Assert(
                description?.Description.Contains(
                    "talvora_apply_patch",
                    StringComparison.Ordinal) == true,
                $"Source lifecycle tool does not route to talvora_apply_patch: {item.Type.Name}.{item.Method}");
        }

        var semanticMethod =
            typeof(SemanticEditTools).GetMethod(
                nameof(SemanticEditTools.SemanticEdit),
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "Missing semantic edit tool method.");
        var semanticDescription =
            Attribute.GetCustomAttribute(
                semanticMethod,
                typeof(System.ComponentModel.DescriptionAttribute))
            as System.ComponentModel.DescriptionAttribute;
        Assert(
            semanticDescription?.Description.Contains(
                "SPECIALIST",
                StringComparison.Ordinal) == true &&
            semanticDescription.Description.Contains(
                "PRIMARY/default",
                StringComparison.Ordinal) == true &&
            semanticDescription.Description.Contains(
                "Roslyn",
                StringComparison.Ordinal) == true &&
            semanticDescription.Description.Contains(
                "talvora_structural_edit",
                StringComparison.Ordinal) == true,
            "Semantic tool description no longer preserves deterministic source-edit routing.");

        return Task.CompletedTask;
    }

    private static Task SourceEditRoutingContractAsync()
    {
        var guide =
            SourceEditTools.SourceEditGuide();

        AssertEqual(
            "talvora_apply_patch",
            guide.PrimaryTool,
            "Source edit primary tool regressed.");
        AssertEqual(
            "talvora_read_source",
            guide.ReadTool,
            "Source edit read handshake regressed.");
        AssertEqual(
            "talvora_apply_edits",
            guide.ExactRangeTool,
            "Exact-range tool regressed.");
        AssertEqual(
            "talvora_structural_edit",
            guide.StructuralTool,
            "Structural specialist tool regressed.");
        AssertEqual(
            "talvora_semantic_edit",
            guide.SemanticTool,
            "Semantic specialist tool regressed.");
        Assert(
            guide.Policy.Contains(
                "trial-and-error",
                StringComparison.OrdinalIgnoreCase),
            "Routing guide no longer forbids mutator trial-and-error.");
        Assert(
            guide.Rules.Any(
                rule =>
                    rule.Tool == "talvora_apply_patch" &&
                    rule.Guidance.Contains(
                        "Multi-file scope alone",
                        StringComparison.Ordinal)),
            "Routing guide no longer keeps ordinary multi-file work on talvora_apply_patch.");
        Assert(
            guide.Rules.Any(
                rule =>
                    rule.Tool == "talvora_structural_edit" &&
                    rule.Guidance.Contains(
                        "structural specialist",
                        StringComparison.OrdinalIgnoreCase)),
            "Routing guide no longer routes repetitive AST-shaped work to talvora_structural_edit.");
        Assert(
            guide.Rules.Any(
                rule =>
                    rule.Tool == "talvora_semantic_edit" &&
                    rule.Guidance.Contains(
                        "Roslyn semantic specialist",
                        StringComparison.OrdinalIgnoreCase)),
            "Routing guide no longer routes true C# semantic work to talvora_semantic_edit.");
        Assert(
            SourceEditRoutingContract.ServerInstructions.Contains(
                "PRIMARY/default",
                StringComparison.Ordinal) &&
            SourceEditRoutingContract.ServerInstructions.Contains(
                "talvora_structural_edit",
                StringComparison.Ordinal) &&
            SourceEditRoutingContract.ServerInstructions.Contains(
                "talvora_semantic_edit",
                StringComparison.Ordinal) &&
            SourceEditRoutingContract.ServerInstructions.Contains(
                "talvora_source_edit_guide",
                StringComparison.Ordinal) &&
            SourceEditRoutingContract.ApplyEditsDescription.Contains(
                "Do not choose it merely because multiple files change",
                StringComparison.Ordinal),
            "Server/tool routing instructions regressed.");

        foreach (var toolName in new[]
                 {
                     "talvora_copy",
                     "talvora_move",
                     "talvora_git_run",
                     "talvora_archive_extract",
                     "talvora_http_download",
                 })
        {
            Assert(
                guide.AvoidForNormalSourceEditing.Any(
                    item =>
                        item.Contains(
                            toolName,
                            StringComparison.Ordinal)),
                $"Routing guide no longer marks {toolName} as non-canonical for ordinary source editing.");
        }

        return Task.CompletedTask;
    }

    private static async Task NonWorkspaceCompatibilityAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        var path =
            Path.Combine(
                fixture.OrdinaryRoot,
                "ordinary.txt");
        await File.WriteAllTextAsync(
            path,
            "ordinary\n");

        SourceMutationPolicy
            .EnsureLegacyTextMutationAllowed(
                path,
                "talvora_write_text");
        SourceMutationPolicy
            .EnsureLegacyTextMutationAllowed(
                path,
                "talvora_replace_text");
        SourceMutationPolicy
            .EnsureLegacyTextMutationAllowed(
                path,
                "talvora_append_text");
        SourceMutationPolicy
            .EnsureLegacyByteMutationAllowed(
                path,
                Encoding.UTF8.GetBytes(
                    "ordinary"),
                "talvora_write_bytes");
    }

    private static async Task LargeEditAsync()
    {
        await using var fixture = await TestWorkspace.CreateAsync();
        var large =
            new string('a', 6 * 1024 * 1024) +
            "\n";
        await fixture.WriteUtf8Async(
            "large.txt",
            large);
        var engine = fixture.CreateEngine();
        var path =
            fixture.PathInWorkspace("large.txt");
        var revision =
            await RevisionAsync(engine, path);

        var result =
            await engine.ApplyEditsAsync(
                fixture.Root,
                NewTransactionId(),
                [
                    Update(
                        "large.txt",
                        revision,
                        0,
                        large.Length - 2,
                        0,
                        large.Length - 1,
                        "Z",
                        "a"),
                ],
                true,
                CancellationToken.None);

        Assert(
            result.Success,
            result.Error?.Message ??
            "Large edit failed.");
        var updated =
            await File.ReadAllTextAsync(path);
        AssertEqual(
            large.Length,
            updated.Length,
            "Large edit changed document length unexpectedly.");
        Assert(
            updated.EndsWith(
                "Z\n",
                StringComparison.Ordinal),
            "Large edit did not change the requested tail byte.");
    }

    public static async Task RunRuntimeBoundsAsync()
    {
        await WatcherBoundsAndResyncStateAsync();
        Console.WriteLine("PASS watcher-bounds-and-resync-state");
        await HttpMockResourceBoundsAsync();
        Console.WriteLine("PASS http-mock-resource-bounds");
        await HttpMockReplyCancellationCanRetryAsync();
        Console.WriteLine("PASS http-mock-reply-cancellation-retry");
        await HttpMockStopClosesPendingWithServiceUnavailableAsync();
        Console.WriteLine("PASS http-mock-stop-pending-503");
        await HttpMockRequestEncodingDoesNotChangeDefaultResponseEncodingAsync();
        Console.WriteLine("PASS http-mock-request-encoding-does-not-change-response");
        await SqliteZeroTimeoutIsRejectedAsync();
        Console.WriteLine("PASS sqlite-zero-timeout-rejected");
    }

    private static async Task SqliteZeroTimeoutIsRejectedAsync()
    {
        static async Task AssertRejectedAsync(
            Func<Task> action,
            string operation)
        {
            var rejected = false;
            try
            {
                await action();
            }
            catch (ArgumentOutOfRangeException ex)
                when (ex.ParamName == "timeoutSeconds")
            {
                rejected = true;
            }

            Assert(
                rejected,
                $"SQLite {operation} accepted timeoutSeconds=0, which disables the provider lock timeout.");
        }

        await AssertRejectedAsync(
            async () =>
            {
                _ = await SqliteTools.Query(
                    ":memory:",
                    "SELECT 1;",
                    timeoutSeconds: 0);
            },
            "query");
        await AssertRejectedAsync(
            async () =>
            {
                _ = await SqliteTools.Execute(
                    ":memory:",
                    "CREATE TABLE t(value INTEGER);",
                    timeoutSeconds: 0);
            },
            "execute");
        await AssertRejectedAsync(
            async () =>
            {
                _ = await SqliteTools.Schema(
                    ":memory:",
                    timeoutSeconds: 0);
            },
            "schema");
        await AssertRejectedAsync(
            async () =>
            {
                _ = await SqliteTools.Backup(
                    "source-does-not-matter.sqlite",
                    "destination-does-not-matter.sqlite",
                    timeoutSeconds: 0);
            },
            "backup");
    }

    private static Task WatcherBoundsAndResyncStateAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "TalvoraWatchBounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var runtime = new TalvoraWatchRuntime
        {
            WatchId = Guid.NewGuid().ToString("N"),
            Path = root,
            Filter = "*",
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName,
            MaxQueuedEvents = 2,
            StartedAtUtc = DateTime.UtcNow,
            Watcher = new FileSystemWatcher(root),
        };

        try
        {
            runtime.Enqueue("Created", Path.Combine(root, "1.txt"), "1.txt");
            runtime.Enqueue("Created", Path.Combine(root, "2.txt"), "2.txt");
            runtime.Enqueue("Created", Path.Combine(root, "3.txt"), "3.txt");
            AssertEqual(
                2,
                Volatile.Read(ref runtime.QueuedEvents),
                "Watcher queue exceeded its configured bound.");
            AssertEqual(
                1L,
                Interlocked.Read(ref runtime.DroppedEvents),
                "Watcher queue did not report a dropped event.");

            runtime.RecordWatcherError(
                root,
                new InternalBufferOverflowException("buffer limit fixture"));
            AssertEqual(
                1L,
                Interlocked.Read(ref runtime.OverflowCount),
                "Watcher buffer-limit counter was not updated.");
            AssertEqual(
                1,
                Volatile.Read(ref runtime.ResyncRequired),
                "Watcher did not expose resync-required state.");
        }
        finally
        {
            runtime.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }

        return Task.CompletedTask;
    }

    private static Task HttpMockResourceBoundsAsync()
    {
        var baselineQueued = HttpMockTools.GlobalQueuedRequests;
        var baselineBytes = HttpMockTools.GlobalQueuedRetainedBytes;
        var handlerSlots = new SemaphoreSlim(1, 1);
        var pendingSlots = new SemaphoreSlim(1, 1);
        var runtime = new TalvoraHttpMockRuntime
        {
            ListenerId = Guid.NewGuid().ToString("N"),
            Listener = new HttpListener(),
            Prefixes = [],
            AutoReply = true,
            DefaultResponse = new TalvoraHttpMockResponseDefinition(
                200,
                "text/plain",
                new Dictionary<string, string>(),
                []),
            MaxQueuedRequests = 2,
            MaxRequestBodyBytes = 1024,
            MaxConcurrentRequests = 1,
            MaxPendingRequests = 1,
            RequestBodyMode = "text",
            RequestEncoding = Encoding.UTF8,
            PendingResponseTimeoutSeconds = 5,
            StartedAtUtc = DateTime.UtcNow,
            HandlerSlots = handlerSlots,
            PendingSlots = pendingSlots,
        };
        CancellationToken? lifetimeToken = null;

        try
        {
            var lifetimeTokenProperty =
                typeof(TalvoraHttpMockRuntime).GetProperty(
                    "LifetimeToken");
            Assert(
                lifetimeTokenProperty is not null,
                "HTTP mock runtime does not expose a cached lifetime token.");
            lifetimeToken =
                (CancellationToken)lifetimeTokenProperty!
                    .GetValue(runtime)!;

            runtime.Enqueue(CreateMockRequestForBounds(1));
            runtime.Enqueue(CreateMockRequestForBounds(2));
            runtime.Enqueue(CreateMockRequestForBounds(3));
            AssertEqual(
                2,
                Volatile.Read(ref runtime.QueuedRequests),
                "HTTP mock queue exceeded its configured bound.");
            AssertEqual(
                1L,
                Interlocked.Read(ref runtime.DroppedRequests),
                "HTTP mock queue did not report a dropped request.");
            AssertEqual(
                baselineQueued + 2,
                HttpMockTools.GlobalQueuedRequests,
                "HTTP mock global queue accounting drifted.");
            Assert(
                HttpMockTools.GlobalQueuedRetainedBytes > baselineBytes,
                "HTTP mock retained-memory accounting did not increase.");
            Assert(
                handlerSlots.Wait(0),
                "HTTP mock local handler slot was unexpectedly unavailable.");
            Assert(
                !handlerSlots.Wait(0),
                "HTTP mock local handler concurrency was not bounded.");
            handlerSlots.Release();
            Assert(
                HttpMockTools.GlobalHandlerSlots.CurrentCount > 0,
                "HTTP mock global handler ceiling is unavailable.");
        }
        finally
        {
            runtime.Dispose();
            handlerSlots.Dispose();
            pendingSlots.Dispose();
        }

        AssertEqual(
            baselineQueued,
            HttpMockTools.GlobalQueuedRequests,
            "HTTP mock queue accounting leaked after dispose.");
        AssertEqual(
            baselineBytes,
            HttpMockTools.GlobalQueuedRetainedBytes,
            "HTTP mock retained-memory accounting leaked after dispose.");
        Assert(
            lifetimeToken is { IsCancellationRequested: true },
            "HTTP mock cached lifetime token was not safely cancelled across runtime disposal.");
        return Task.CompletedTask;
    }

    private static async Task HttpMockReplyCancellationCanRetryAsync()
    {
        using var portProbe =
            new TcpListener(
                IPAddress.Loopback,
                0);
        portProbe.Start();
        var port =
            ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();

        var prefix =
            $"http://127.0.0.1:{port}/";
        var started =
            HttpMockTools.Start(
                [prefix],
                autoReply: false,
                pendingResponseTimeoutSeconds: 10);

        try
        {
            using var client =
                new HttpClient();
            var requestTask =
                client.GetAsync(prefix);

            TalvoraHttpMockRequest? captured = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var read =
                    HttpMockTools.Read(
                        started.ListenerId,
                        consume: false);
                captured =
                    read.Requests.FirstOrDefault();
                if (captured is not null)
                {
                    break;
                }

                await Task.Delay(20);
            }

            Assert(
                captured is not null,
                "HTTP mock manual request was not captured.");

            using var cancelled =
                new CancellationTokenSource();
            cancelled.Cancel();

            try
            {
                _ = await HttpMockTools.Reply(
                    started.ListenerId,
                    captured!.RequestId,
                    body: "cancelled-attempt",
                    cancellationToken: cancelled.Token);
            }
            catch (OperationCanceledException)
            {
            }

            var retry =
                await HttpMockTools.Reply(
                    started.ListenerId,
                    captured!.RequestId,
                    body: "retry-ok",
                    cancellationToken: CancellationToken.None);
            Assert(
                retry.Replied,
                "HTTP mock reply cancellation permanently consumed the reply claim.");

            using var response =
                await requestTask.WaitAsync(
                    TimeSpan.FromSeconds(5));
            var responseBody =
                await response.Content.ReadAsStringAsync();
            AssertEqual(
                "retry-ok",
                responseBody,
                "HTTP mock retry did not deliver the replacement response.");
        }
        finally
        {
            _ = HttpMockTools.Stop(
                started.ListenerId);
        }
    }

    private static async Task HttpMockStopClosesPendingWithServiceUnavailableAsync()
    {
        using var portProbe =
            new TcpListener(
                IPAddress.Loopback,
                0);
        portProbe.Start();
        var port =
            ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();

        var prefix =
            $"http://127.0.0.1:{port}/";
        const int requestCount = 64;
        var started =
            HttpMockTools.Start(
                [prefix],
                autoReply: false,
                defaultStatusCode: 200,
                defaultBody: "timeout-default",
                pendingResponseTimeoutSeconds: 30,
                maxConcurrentRequests: requestCount,
                maxPendingRequests: requestCount);

        using var client =
            new HttpClient();
        var requests =
            Enumerable.Range(0, requestCount)
                .Select(_ => client.GetAsync(prefix))
                .ToArray();

        try
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var read =
                    HttpMockTools.Read(
                        started.ListenerId,
                        consume: false,
                        maxRequests: 0);
                if (read.PendingRequests == requestCount)
                {
                    break;
                }

                await Task.Delay(20);
            }

            var beforeStop =
                HttpMockTools.Get(started.ListenerId);
            AssertEqual(
                requestCount,
                beforeStop.PendingRequests,
                "HTTP mock stop regression did not reach the requested pending concurrency.");

            var stopped =
                HttpMockTools.Stop(started.ListenerId);
            Assert(
                stopped.Stopped,
                "HTTP mock stop regression failed to stop the listener.");

            var responses =
                await Task.WhenAll(
                    requests.Select(
                        task => task.WaitAsync(
                            TimeSpan.FromSeconds(5))));
            try
            {
                Assert(
                    responses.All(
                        response =>
                            response.StatusCode ==
                            HttpStatusCode.ServiceUnavailable),
                    "HTTP mock stop allowed a pending request to receive the timeout default instead of 503.");
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }
        }
        finally
        {
            _ = HttpMockTools.Stop(
                started.ListenerId);
        }
    }

    private static async Task HttpMockRequestEncodingDoesNotChangeDefaultResponseEncodingAsync()
    {
        using var portProbe =
            new TcpListener(
                IPAddress.Loopback,
                0);
        portProbe.Start();
        var port =
            ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();

        var prefix =
            $"http://127.0.0.1:{port}/";
        const string requestText = "request-✓";
        const string responseText = "response-✓";
        var started =
            HttpMockTools.Start(
                [prefix],
                autoReply: true,
                defaultBody: responseText,
                requestEncoding: "utf-16");

        try
        {
            using var client =
                new HttpClient();
            using var content =
                new ByteArrayContent(
                    Encoding.Unicode.GetBytes(requestText));
            using var response =
                await client.PostAsync(
                    prefix,
                    content);
            var responseBody =
                await response.Content.ReadAsStringAsync();
            AssertEqual(
                responseText,
                responseBody,
                "HTTP mock requestEncoding changed the UTF-8 default response body.");

            TalvoraHttpMockRequest? captured = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                captured =
                    HttpMockTools.Read(
                        started.ListenerId,
                        consume: false)
                    .Requests
                    .FirstOrDefault();
                if (captured is not null)
                {
                    break;
                }

                await Task.Delay(20);
            }

            AssertEqual(
                requestText,
                captured?.Body,
                "HTTP mock requestEncoding no longer decoded the request body.");
        }
        finally
        {
            _ = HttpMockTools.Stop(
                started.ListenerId);
        }
    }

    private static TalvoraHttpMockRequest CreateMockRequestForBounds(
        long sequence) =>
        new(
            sequence,
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            "GET",
            "http://loopback.invalid/test",
            "/test",
            "1.1",
            new Dictionary<string, string[]>(),
            new Dictionary<string, string[]>(),
            null,
            null,
            "text",
            new string('x', 64),
            64,
            false,
            false);

    public static async Task RunResponseBoundsAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "TalvoraResponseBounds-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var binaryPath =
                Path.Combine(root, "large.bin");
            await using (var stream = new FileStream(
                binaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.SetLength(
                    DeveloperTools.AbsoluteReadBytesResponseBytes +
                    257L);
            }

            var bytes = await DeveloperTools.ReadBytes(
                binaryPath,
                count: 0);
            AssertEqual(
                DeveloperTools.AbsoluteReadBytesResponseBytes,
                bytes.Count,
                "Binary response exceeded its absolute response budget.");
            Assert(
                bytes.ResponseLimited &&
                bytes.NextOffset ==
                    DeveloperTools.AbsoluteReadBytesResponseBytes,
                "Binary continuation metadata was not emitted.");

            var longLinePath =
                Path.Combine(root, "long-line.txt");
            await File.WriteAllTextAsync(
                longLinePath,
                new string(
                    'x',
                    ConfigAssetTools.AbsoluteTextResponseCharacters +
                    37));
            var firstRange =
                await ConfigAssetTools.ReadTextRange(
                    longLinePath,
                    startLine: 1,
                    lineCount: 1,
                    startCharacter: 0);
            AssertEqual(
                ConfigAssetTools.AbsoluteTextResponseCharacters,
                firstRange.Text.Length,
                "Text range exceeded its character budget.");
            Assert(
                firstRange.ResponseLimited &&
                firstRange.NextStartLine == 1 &&
                firstRange.NextStartCharacter ==
                    ConfigAssetTools.AbsoluteTextResponseCharacters,
                "Text range did not return a same-line continuation.");
            var secondRange =
                await ConfigAssetTools.ReadTextRange(
                    longLinePath,
                    startLine: 1,
                    lineCount: 1,
                    startCharacter:
                        firstRange.NextStartCharacter!.Value);
            AssertEqual(
                37,
                secondRange.Text.Length,
                "Text range continuation lost trailing characters.");
            Assert(
                secondRange.EndReached &&
                !secondRange.ResponseLimited,
                "Text range continuation did not reach EOF cleanly.");

            var legacyReadPath =
                Path.Combine(
                    root,
                    "legacy-read.txt");
            const string legacyReadText =
                "alpha\r\nbeta\r\n";
            await File.WriteAllTextAsync(
                legacyReadPath,
                legacyReadText);
            AssertEqual(
                legacyReadText,
                await FileTools.ReadText(
                    legacyReadPath),
                "Legacy whole-file read changed small-file content.");

            var legacyReadRejected = false;
            try
            {
                await FileTools.ReadText(
                    longLinePath);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains(
                    "talvora_read_text_range",
                    StringComparison.Ordinal))
            {
                legacyReadRejected = true;
            }
            Assert(
                legacyReadRejected,
                "Legacy whole-file read did not reject an over-budget response.");

            var tailPath =
                Path.Combine(root, "tail.txt");
            await using (var writer =
                new StreamWriter(
                    tailPath,
                    append: false,
                    Encoding.UTF8))
            {
                for (var index = 1;
                     index <=
                         ConfigAssetTools.AbsoluteTextResponseLines + 5;
                     index++)
                {
                    await writer.WriteLineAsync(
                        $"line-{index}");
                }
            }
            var tail =
                ConfigAssetTools.TailText(
                    tailPath,
                    lineCount: 0);
            AssertEqual(
                ConfigAssetTools.AbsoluteTextResponseLines,
                tail.LinesRead,
                "Tail response exceeded its line budget.");
            AssertEqual(
                6,
                tail.StartLine,
                "Tail response did not retain the expected final window.");
            Assert(
                tail.ResponseLimited,
                "Tail response did not expose the server ceiling.");
            Assert(
                tail.HasEarlierLines &&
                tail.NextBeforeLine == 6,
                "Tail response did not expose backward continuation metadata.");

            var earlierTail =
                ConfigAssetTools.TailText(
                    tailPath,
                    lineCount: 0,
                    beforeLine:
                        tail.NextBeforeLine!.Value);
            AssertEqual(
                5,
                earlierTail.LinesRead,
                "Tail continuation returned the wrong earlier window size.");
            AssertEqual(
                1L,
                earlierTail.StartLine,
                "Tail continuation did not reach the beginning of the file.");
            Assert(
                !earlierTail.HasEarlierLines &&
                earlierTail.NextBeforeLine is null &&
                !earlierTail.ResponseLimited,
                "Tail continuation metadata did not terminate cleanly.");

            var giantTailPath =
                Path.Combine(
                    root,
                    "giant-tail-line.txt");
            await File.WriteAllTextAsync(
                giantTailPath,
                new string(
                    'q',
                    ConfigAssetTools.AbsoluteTextResponseCharacters +
                    17) +
                "TAIL");
            var giantTail =
                ConfigAssetTools.TailText(
                    giantTailPath,
                    lineCount: 1);
            AssertEqual(
                ConfigAssetTools.AbsoluteTextResponseCharacters,
                giantTail.Text.Length,
                "Tail giant-line window exceeded its character budget.");
            Assert(
                giantTail.ResponseLimited &&
                giantTail.Text.EndsWith(
                    "TAIL",
                    StringComparison.Ordinal),
                "Tail giant-line reader did not retain the bounded suffix.");

            var searchRoot =
                Path.Combine(
                    root,
                    "search-pages");
            Directory.CreateDirectory(
                searchRoot);
            foreach (var name in
                     new[]
                     {
                         "c.txt",
                         "a.txt",
                         "b.txt",
                     })
            {
                await File.WriteAllTextAsync(
                    Path.Combine(
                        searchRoot,
                        name),
                    "needle");
            }

            var legacyList =
                FileTools.List(
                    searchRoot);
            AssertEqual(
                3,
                legacyList.Count,
                "Legacy list changed normal small-directory behavior.");

            var legacyListRejected = false;
            try
            {
                FileTools.ListBounded(
                    searchRoot,
                    recursive: false,
                    maxResults: 2,
                    maxResponseCharacters:
                        DeveloperTools.AbsoluteSearchResponseCharacters,
                    CancellationToken.None);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains(
                    "talvora_find_files",
                    StringComparison.Ordinal))
            {
                legacyListRejected = true;
            }
            Assert(
                legacyListRejected,
                "Legacy list did not reject an over-budget compatibility response.");

            var archiveListPath =
                Path.Combine(
                    root,
                    "archive-list-pages.zip");
            using (var archive =
                   ZipFile.Open(
                       archiveListPath,
                       ZipArchiveMode.Create))
            {
                archive.CreateEntry("a.txt");
                archive.CreateEntry("b.txt");
                archive.CreateEntry("c.txt");
            }
            var firstArchivePage =
                ConfigAssetTools.ArchiveList(
                    archiveListPath,
                    maxResults: 2);
            Assert(
                firstArchivePage.Count == 2 &&
                firstArchivePage.TotalEntries == 3 &&
                firstArchivePage.ResultOffset == 0 &&
                firstArchivePage.Truncated &&
                firstArchivePage.NextResultOffset == 2 &&
                firstArchivePage.Entries[0].FullName == "a.txt" &&
                firstArchivePage.Entries[1].FullName == "b.txt",
                "Archive list did not expose a deterministic bounded first page.");
            var secondArchivePage =
                ConfigAssetTools.ArchiveList(
                    archiveListPath,
                    maxResults: 2,
                    resultOffset:
                        firstArchivePage.NextResultOffset!.Value);
            Assert(
                secondArchivePage.Count == 1 &&
                secondArchivePage.TotalEntries == 3 &&
                secondArchivePage.ResultOffset == 2 &&
                !secondArchivePage.Truncated &&
                secondArchivePage.NextResultOffset is null &&
                secondArchivePage.Entries[0].FullName == "c.txt",
                "Archive list continuation did not terminate cleanly.");
            Assert(
                ConfigAssetTools.ArchiveAbsoluteListResults > 0 &&
                ConfigAssetTools.ArchiveAbsoluteListResponseCharacters > 0,
                "Archive list server ceilings must remain finite and positive.");

            var firstFilePage =
                DeveloperTools.FindFiles(
                    searchRoot,
                    patterns: ["*.txt"],
                    maxResults: 2);
            AssertEqual(
                2,
                firstFilePage.Count,
                "File search first page size is incorrect.");
            Assert(
                firstFilePage.Truncated &&
                firstFilePage.NextResultOffset == 2,
                "File search did not expose continuation offset.");
            Assert(
                firstFilePage.Entries[0].Name == "a.txt" &&
                firstFilePage.Entries[1].Name == "b.txt",
                "File search traversal is not deterministic.");

            var secondFilePage =
                DeveloperTools.FindFiles(
                    searchRoot,
                    patterns: ["*.txt"],
                    maxResults: 2,
                    resultOffset:
                        firstFilePage.NextResultOffset!.Value);
            AssertEqual(
                1,
                secondFilePage.Count,
                "File search continuation page size is incorrect.");
            Assert(
                !secondFilePage.Truncated &&
                secondFilePage.NextResultOffset is null &&
                secondFilePage.Entries[0].Name == "c.txt",
                "File search continuation did not terminate cleanly.");

            var firstTextPage =
                await DeveloperTools.SearchText(
                    searchRoot,
                    "needle",
                    maxMatches: 2,
                    maxFileBytes: 0,
                    maxLineChars: 0);
            AssertEqual(
                2,
                firstTextPage.MatchCount,
                "Text search first page size is incorrect.");
            Assert(
                firstTextPage.Truncated &&
                firstTextPage.NextMatchOffset == 2,
                "Text search did not expose continuation offset.");
            var secondTextPage =
                await DeveloperTools.SearchText(
                    searchRoot,
                    "needle",
                    maxMatches: 2,
                    maxFileBytes: 0,
                    maxLineChars: 0,
                    matchOffset:
                        firstTextPage.NextMatchOffset!.Value);
            AssertEqual(
                1,
                secondTextPage.MatchCount,
                "Text search continuation page size is incorrect.");
            Assert(
                !secondTextPage.Truncated &&
                secondTextPage.NextMatchOffset is null,
                "Text search continuation did not terminate cleanly.");

            var xmlPath =
                Path.Combine(root, "paged.xml");
            await File.WriteAllTextAsync(
                xmlPath,
                "<root><item id=\"1\"/><item id=\"2\"/><item id=\"3\"/></root>");
            var firstXmlPage =
                ConfigFormatTools.XmlQuery(
                    xmlPath,
                    "/root/item",
                    maxResults: 2);
            Assert(
                firstXmlPage.Count == 2 &&
                firstXmlPage.Truncated &&
                firstXmlPage.NextResultOffset == 2,
                "XML query did not expose a bounded first page.");
            var secondXmlPage =
                ConfigFormatTools.XmlQuery(
                    xmlPath,
                    "/root/item",
                    maxResults: 2,
                    resultOffset:
                        firstXmlPage.NextResultOffset!.Value);
            Assert(
                secondXmlPage.Count == 1 &&
                !secondXmlPage.Truncated &&
                secondXmlPage.NextResultOffset is null,
                "XML query continuation did not terminate cleanly.");

            var junitPath =
                Path.Combine(root, "paged-junit.xml");
            await File.WriteAllTextAsync(
                junitPath,
                "<testsuite tests=\"3\" failures=\"3\">" +
                "<testcase classname=\"C\" name=\"a\"><failure message=\"a\">a</failure></testcase>" +
                "<testcase classname=\"C\" name=\"b\"><failure message=\"b\">b</failure></testcase>" +
                "<testcase classname=\"C\" name=\"c\"><failure message=\"c\">c</failure></testcase>" +
                "</testsuite>");
            var firstFailurePage =
                ConfigFormatTools.TestReportSummary(
                    junitPath,
                    format: "junit",
                    maxFailures: 2);
            Assert(
                firstFailurePage.FailureCount == 2 &&
                firstFailurePage.FailuresTruncated &&
                firstFailurePage.NextFailureOffset == 2,
                "Test report did not expose a bounded first failure page.");
            var secondFailurePage =
                ConfigFormatTools.TestReportSummary(
                    junitPath,
                    format: "junit",
                    maxFailures: 2,
                    failureOffset:
                        firstFailurePage.NextFailureOffset!.Value);
            Assert(
                secondFailurePage.FailureCount == 1 &&
                !secondFailurePage.FailuresTruncated &&
                secondFailurePage.NextFailureOffset is null,
                "Test report failure continuation did not terminate cleanly.");

            var projectRoot =
                Path.Combine(root, "projects");
            foreach (var name in
                     new[] { "a", "b", "c" })
            {
                var projectDirectory =
                    Path.Combine(projectRoot, name);
                Directory.CreateDirectory(projectDirectory);
                await File.WriteAllTextAsync(
                    Path.Combine(
                        projectDirectory,
                        "package.json"),
                    "{\"name\":\"" + name + "\",\"scripts\":{\"test\":\"echo test\"}}");
            }

            var firstProjectPage =
                DeveloperTools.ProjectDiscover(
                    projectRoot,
                    maxResults: 2);
            Assert(
                firstProjectPage.Count == 2 &&
                firstProjectPage.Truncated &&
                firstProjectPage.NextResultOffset == 2,
                "Project discovery did not expose a bounded first page.");
            var secondProjectPage =
                DeveloperTools.ProjectDiscover(
                    projectRoot,
                    maxResults: 2,
                    resultOffset:
                        firstProjectPage.NextResultOffset!.Value);
            Assert(
                secondProjectPage.Count == 1 &&
                !secondProjectPage.Truncated &&
                secondProjectPage.NextResultOffset is null,
                "Project discovery continuation did not terminate cleanly.");

            var firstWorkspacePage =
                DeveloperTools.WorkspaceInspect(
                    projectRoot,
                    maxProjects: 2);
            Assert(
                firstWorkspacePage.Count == 2 &&
                firstWorkspacePage.Truncated &&
                firstWorkspacePage.NextProjectOffset == 2,
                "Workspace inspection did not expose a bounded first page.");
            var secondWorkspacePage =
                DeveloperTools.WorkspaceInspect(
                    projectRoot,
                    maxProjects: 2,
                    projectOffset:
                        firstWorkspacePage.NextProjectOffset!.Value);
            Assert(
                secondWorkspacePage.Count == 1 &&
                !secondWorkspacePage.Truncated &&
                secondWorkspacePage.NextProjectOffset is null,
                "Workspace inspection continuation did not terminate cleanly.");

            var firstCommandPage =
                DeveloperTools.WorkspaceCommands(
                    projectRoot,
                    maxProjects: 10,
                    maxCommands: 2);
            Assert(
                firstCommandPage.Count == 2 &&
                firstCommandPage.Truncated &&
                firstCommandPage.NextCommandOffset == 2,
                "Workspace commands did not expose a bounded first command page.");
            var secondCommandPage =
                DeveloperTools.WorkspaceCommands(
                    projectRoot,
                    maxProjects: 10,
                    maxCommands: 2,
                    commandOffset:
                        firstCommandPage.NextCommandOffset!.Value);
            Assert(
                secondCommandPage.Count > 0,
                "Workspace command continuation returned no remaining commands.");

            var artifactRoot =
                Path.Combine(root, "artifacts");
            Directory.CreateDirectory(artifactRoot);
            foreach (var name in
                     new[] { "a.dll", "b.dll", "c.dll" })
            {
                await File.WriteAllTextAsync(
                    Path.Combine(artifactRoot, name),
                    name);
            }
            var firstArtifactPage =
                await QualityTools.ArtifactInventory(
                    artifactRoot,
                    maxResults: 2,
                    includeVersionInfo: false);
            Assert(
                firstArtifactPage.Count == 2 &&
                firstArtifactPage.Truncated &&
                firstArtifactPage.NextResultOffset == 2,
                "Artifact inventory did not expose a bounded first page.");
            var secondArtifactPage =
                await QualityTools.ArtifactInventory(
                    artifactRoot,
                    maxResults: 2,
                    resultOffset:
                        firstArtifactPage.NextResultOffset!.Value,
                    includeVersionInfo: false);
            Assert(
                secondArtifactPage.Count == 1 &&
                !secondArtifactPage.Truncated &&
                secondArtifactPage.NextResultOffset is null,
                "Artifact inventory continuation did not terminate cleanly.");

            var failingArtifactRoot =
                Path.Combine(root, "artifact-error-pages");
            Directory.CreateDirectory(failingArtifactRoot);
            foreach (var name in
                     new[] { "a.dll", "b.dll", "c.dll", "d.dll" })
            {
                await File.WriteAllTextAsync(
                    Path.Combine(failingArtifactRoot, name),
                    name);
            }
            var lockedArtifactPath =
                Path.Combine(failingArtifactRoot, "b.dll");
            await using (var lockedArtifact =
                new FileStream(
                    lockedArtifactPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None))
            {
                var firstErrorArtifactPage =
                    await QualityTools.ArtifactInventory(
                        failingArtifactRoot,
                        maxResults: 1,
                        includeVersionInfo: false);
                Assert(
                    firstErrorArtifactPage.Count == 1 &&
                    firstErrorArtifactPage.Truncated &&
                    firstErrorArtifactPage.NextResultOffset == 1,
                    "Artifact error fixture first page continuation is incorrect.");

                var secondErrorArtifactPage =
                    await QualityTools.ArtifactInventory(
                        failingArtifactRoot,
                        maxResults: 1,
                        resultOffset:
                            firstErrorArtifactPage.NextResultOffset!.Value,
                        includeVersionInfo: false);
                Assert(
                    secondErrorArtifactPage.Count == 1 &&
                    secondErrorArtifactPage.Errors.Count == 1 &&
                    secondErrorArtifactPage.Truncated &&
                    secondErrorArtifactPage.NextResultOffset == 3,
                    "Artifact error page did not advance past a consumed unreadable artifact.");

                var thirdErrorArtifactPage =
                    await QualityTools.ArtifactInventory(
                        failingArtifactRoot,
                        maxResults: 1,
                        resultOffset:
                            secondErrorArtifactPage.NextResultOffset!.Value,
                        includeVersionInfo: false);
                Assert(
                    thirdErrorArtifactPage.Count == 1 &&
                    !thirdErrorArtifactPage.Truncated &&
                    thirdErrorArtifactPage.NextResultOffset is null,
                    "Artifact error continuation did not terminate cleanly.");
            }

            var diagnosticText =
                "a.cs(1,1): error CS0001: first\n" +
                "b.cs(2,1): warning CS0002: second\n" +
                "c.cs(3,1): error CS0003: third\n";
            var firstDiagnosticPage =
                QualityTools.DiagnosticsParse(
                    text: diagnosticText,
                    maxDiagnostics: 2);
            Assert(
                firstDiagnosticPage.Count == 2 &&
                firstDiagnosticPage.Truncated &&
                firstDiagnosticPage.NextDiagnosticOffset == 2,
                "Diagnostics parser did not expose a bounded first page.");
            var secondDiagnosticPage =
                QualityTools.DiagnosticsParse(
                    text: diagnosticText,
                    maxDiagnostics: 2,
                    diagnosticOffset:
                        firstDiagnosticPage.NextDiagnosticOffset!.Value);
            Assert(
                secondDiagnosticPage.Count == 1 &&
                !secondDiagnosticPage.Truncated &&
                secondDiagnosticPage.NextDiagnosticOffset is null,
                "Diagnostics continuation did not terminate cleanly.");

            var gitRoot =
                Path.Combine(root, "git-pages");
            Directory.CreateDirectory(gitRoot);
            AssertEqual(
                0,
                (await GitTools.Run(
                    gitRoot,
                    ["init"],
                    explicitAdmin: true)).ExitCode,
                "Git pagination fixture init failed.");
            for (var index = 1; index <= 3; index++)
            {
                var gitFile =
                    Path.Combine(
                        gitRoot,
                        $"commit-{index}.txt");
                await File.WriteAllTextAsync(
                    gitFile,
                    index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                AssertEqual(
                    0,
                    (await GitTools.Run(
                        gitRoot,
                        ["add", "--", Path.GetFileName(gitFile)],
                        explicitAdmin: true)).ExitCode,
                    "Git pagination fixture add failed.");
                AssertEqual(
                    0,
                    (await GitTools.Run(
                        gitRoot,
                        [
                            "-c",
                            "user.name=Talvora Regression",
                            "-c",
                            "user.email=talvora@example.invalid",
                            "commit",
                            "-m",
                            $"commit-{index}",
                        ],
                        explicitAdmin: true)).ExitCode,
                    "Git pagination fixture commit failed.");
            }
            var firstGitPage =
                await GitTools.Log(
                    gitRoot,
                    maxCount: 2);
            Assert(
                firstGitPage.Count == 2 &&
                firstGitPage.Truncated &&
                firstGitPage.NextSkip == 2,
                "Git log did not expose a bounded first page.");
            var secondGitPage =
                await GitTools.Log(
                    gitRoot,
                    maxCount: 2,
                    skip:
                        firstGitPage.NextSkip!.Value);
            Assert(
                secondGitPage.Count == 1 &&
                !secondGitPage.Truncated &&
                secondGitPage.NextSkip is null,
                "Git log continuation did not terminate cleanly.");

            var jobPage =
                await JobTools.List(
                    maxResults: 0);
            Assert(
                jobPage.Count <=
                    JobTools.AbsoluteJobListResults,
                "Job list exceeded its finite server ceiling.");
            var devServerPage =
                await DevServerTools.List(
                    maxResults: 0);
            Assert(
                devServerPage.Count <=
                    DevServerTools.AbsoluteDevServerListResults,
                "Dev-server list exceeded its finite server ceiling.");

            var sqlite =
                await SqliteTools.Query(
                    ":memory:",
                    "WITH RECURSIVE seq(value) AS (SELECT 1 UNION ALL SELECT value + 1 FROM seq WHERE value < 10001) SELECT value FROM seq;",
                    maxRows: 0);
            AssertEqual(
                10_000,
                sqlite.RowCount,
                "SQLite zero row limit exceeded its server ceiling.");
            Assert(
                sqlite.Truncated &&
                sqlite.NextRowOffset == 10_000 &&
                sqlite.ValueLimitBytes ==
                    SqliteTools.AbsoluteQueryValueBytes,
                "SQLite response did not expose continuation/value-limit metadata.");

            var sqliteContinuation =
                await SqliteTools.Query(
                    ":memory:",
                    "WITH RECURSIVE seq(value) AS (SELECT 1 UNION ALL SELECT value + 1 FROM seq WHERE value < 10001) SELECT value FROM seq;",
                    maxRows: 0,
                    rowOffset:
                        sqlite.NextRowOffset!.Value);
            AssertEqual(
                1,
                sqliteContinuation.RowCount,
                "SQLite continuation did not return the final row.");
            Assert(
                !sqliteContinuation.Truncated &&
                sqliteContinuation.NextRowOffset is null,
                "SQLite continuation did not terminate cleanly.");

            var oversizedSqliteValueRejected = false;
            try
            {
                await SqliteTools.Query(
                    ":memory:",
                    $"SELECT zeroblob({SqliteTools.AbsoluteQueryValueBytes + 1}) AS value;");
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
                when (ex.SqliteErrorCode == 18)
            {
                oversizedSqliteValueRejected = true;
            }
            Assert(
                oversizedSqliteValueRejected,
                "SQLite native value ceiling did not reject an oversized BLOB before materialization.");

            await RunWebSocketResponseBoundAsync();

            Assert(
                DeveloperTools.AbsoluteHttpResponseBytes > 0 &&
                DeveloperTools.AbsoluteFileSearchResults > 0 &&
                DeveloperTools.AbsoluteTextSearchMatches > 0 &&
                DeveloperTools.AbsoluteProjectDiscoverResults > 0 &&
                DeveloperTools.AbsoluteWorkspaceProjects > 0 &&
                DeveloperTools.AbsoluteWorkspaceCommands > 0 &&
                ConfigFormatTools.AbsoluteXmlQueryResults > 0 &&
                ConfigFormatTools.AbsoluteTestReportFailures > 0 &&
                QualityTools.AbsoluteArtifactResults > 0 &&
                QualityTools.AbsoluteDiagnosticResults > 0 &&
                JobTools.AbsoluteJobListResults > 0 &&
                DevServerTools.AbsoluteDevServerListResults > 0 &&
                NetworkDiagnosticTools.AbsoluteTcpResponseBytes > 0 &&
                NetworkDiagnosticTools.AbsoluteWebSocketMessageBytes > 0 &&
                NetworkDiagnosticTools.AbsoluteWebSocketResponseBytes > 0,
                "One or more response-budget ceilings are not finite.");
        }
        finally
        {
            try
            {
                Directory.Delete(
                    root,
                    recursive: true);
            }
            catch
            {
            }
        }
    }

    private static async Task RunWebSocketResponseBoundAsync()
    {
        using var listener =
            new TcpListener(
                IPAddress.Loopback,
                0);
        listener.Start();
        var port =
            ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask =
            Task.Run(
                async () =>
                {
                    using var client =
                        await listener.AcceptTcpClientAsync();
                    await using var stream =
                        client.GetStream();
                    var requestBytes =
                        new List<byte>();
                    var buffer =
                        new byte[1024];

                    while (true)
                    {
                        var read =
                            await stream.ReadAsync(
                                buffer);
                        if (read == 0)
                        {
                            throw new InvalidOperationException(
                                "Loopback WebSocket handshake ended early.");
                        }

                        requestBytes.AddRange(
                            buffer.AsSpan(
                                0,
                                read).ToArray());
                        var requestText =
                            Encoding.ASCII.GetString(
                                requestBytes.ToArray());
                        if (requestText.Contains(
                                "\r\n\r\n",
                                StringComparison.Ordinal))
                        {
                            break;
                        }
                        if (requestBytes.Count >
                            32 * 1024)
                        {
                            throw new InvalidOperationException(
                                "Loopback WebSocket handshake exceeded its fixture budget.");
                        }
                    }

                    var headers =
                        Encoding.ASCII
                            .GetString(
                                requestBytes.ToArray())
                            .Split(
                                "\r\n",
                                StringSplitOptions.RemoveEmptyEntries);
                    var keyHeader =
                        headers.FirstOrDefault(
                            static line =>
                                line.StartsWith(
                                    "Sec-WebSocket-Key:",
                                    StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(
                            "Loopback WebSocket handshake omitted Sec-WebSocket-Key.");
                    var key =
                        keyHeader[
                            (keyHeader.IndexOf(':') + 1)..]
                            .Trim();
                    var accept =
                        Convert.ToBase64String(
                            System.Security.Cryptography.SHA1.HashData(
                                Encoding.ASCII.GetBytes(
                                    key +
                                    "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                    var handshake =
                        Encoding.ASCII.GetBytes(
                            "HTTP/1.1 101 Switching Protocols\r\n" +
                            "Upgrade: websocket\r\n" +
                            "Connection: Upgrade\r\n" +
                            $"Sec-WebSocket-Accept: {accept}\r\n\r\n");
                    await stream.WriteAsync(
                        handshake);

                    var payload =
                        Enumerable.Range(
                                0,
                                32)
                            .Select(
                                static value =>
                                    (byte)value)
                            .ToArray();
                    var frame =
                        new byte[
                            payload.Length +
                            2];
                    frame[0] = 0x82;
                    frame[1] =
                        (byte)payload.Length;
                    Buffer.BlockCopy(
                        payload,
                        0,
                        frame,
                        2,
                        payload.Length);
                    await stream.WriteAsync(
                        frame);
                    await stream.FlushAsync();
                    await Task.Delay(100);
                });

        try
        {
            var response =
                await NetworkDiagnosticTools.WebSocketExchange(
                    $"ws://127.0.0.1:{port}/",
                    receiveMessages: 1,
                    maxMessageBytes: 8,
                    timeoutSeconds: 5,
                    closeAfter: false);
            AssertEqual(
                1,
                response.MessagesReceived,
                "WebSocket bounded fixture returned the wrong message count.");
            Assert(
                response.ResponseTruncated &&
                response.Messages[0].Truncated &&
                response.Messages[0].Bytes == 8 &&
                response.MessageLimitBytes == 8 &&
                response.ResponseLimitBytes ==
                    NetworkDiagnosticTools.AbsoluteWebSocketResponseBytes &&
                !response.ContinuationSupported,
                "WebSocket truncation metadata did not describe the bounded capture.");
            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task ExpectPolicyViolationAsync(
        Func<Task> action)
    {
        try
        {
            await action();
            throw new InvalidOperationException(
                "Expected SOURCE_EDIT_POLICY_VIOLATION.");
        }
        catch (SourceEditDomainException ex)
        {
            AssertEqual(
                SourceEditCodes.PolicyViolation,
                ex.Code,
                "Unexpected legacy policy error code.");
        }
    }

    private static void ExpectPolicyViolation(
        Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException(
                "Expected SOURCE_EDIT_POLICY_VIOLATION.");
        }
        catch (SourceEditDomainException ex)
        {
            AssertEqual(
                SourceEditCodes.PolicyViolation,
                ex.Code,
                "Unexpected legacy policy error code.");
        }
    }
}
