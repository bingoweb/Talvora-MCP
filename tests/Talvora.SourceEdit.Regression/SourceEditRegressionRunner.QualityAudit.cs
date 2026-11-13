using System.Reflection;
using System.Text.Json;
using Talvora;
using Talvora.SourceEditing;
using Talvora.Shared;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    public static async Task RunQualityAuditAsync()
    {
        await VerifyDeliveryEvidenceAsync();
        Console.WriteLine("PASS delivery-evidence-rejects-invalid-and-oversized");
        await VerifyCtx2PushEvidenceAsync();
        Console.WriteLine("CTX2_PUSH_EVIDENCE_GREEN");
        await VerifyJobGenerationSidecarAsync();
        Console.WriteLine("PASS job-generation-sidecar-bounds");
        await VerifyJobSnapshotFailureDisposesFileAsync();
        Console.WriteLine("PASS job-snapshot-failure-releases-handle");
        await VerifyRuntimeMetadataBoundsAsync();
        Console.WriteLine("PASS runtime-metadata-is-bounded");
        await VerifyHostingerMetadataBoundsAsync();
        Console.WriteLine("PASS hostinger-secret-and-package-bounds");
    }

    private static async Task VerifyDeliveryEvidenceAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Delivery-Audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, ".context7"));
        var contextPath = Path.Combine(root, ".context7", "verification.json");
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("O");
            async Task WriteContextAsync(
                string id = "/dotnet/docs",
                string? vendor = "https://learn.microsoft.com/dotnet/")
            {
                await File.WriteAllTextAsync(
                    contextPath,
                    JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        verifiedAtUtc = timestamp,
                        scope = "isolated quality regression",
                        libraries = new[] { new
                        {
                            libraryId = id,
                            verified = "Verified .NET APIs (query: \"HttpClient Timeout and HttpMessageHandler for tests with a capped response read\")",
                        } },
                        vendorSources = vendor is null ? Array.Empty<string>() : new[] { vendor },
                    }));
            }

            static void ExpectBlocked(Action operation, string caseName)
            {
                try
                {
                    operation();
                }
                catch (ModelContextProtocol.McpException ex)
                    when (ex.Message.StartsWith(
                        "TALVORA_DELIVERY_GATE_BLOCKED:",
                        StringComparison.Ordinal))
                {
                    return;
                }
                throw new InvalidOperationException(
                    $"Delivery gate accepted invalid evidence: {caseName}.");
            }

            await WriteContextAsync();
            ProjectDeliveryGate.EnsureSatisfied(root);

            await WriteContextAsync(vendor: null);
            ProjectDeliveryGate.EnsureSatisfied(root);

            await WriteContextAsync(vendor: "   ");
            ExpectBlocked(
                () => ProjectDeliveryGate.EnsureSatisfied(root),
                "blank vendor source");

            await WriteContextAsync(id: "/");
            ExpectBlocked(
                () => ProjectDeliveryGate.EnsureSatisfied(root),
                "incomplete canonical library ID");

            await WriteContextAsync();
            await File.WriteAllTextAsync(
                contextPath,
                (await File.ReadAllTextAsync(contextPath)).TrimEnd()[..^1] + ",}");
            ExpectBlocked(
                () => ProjectDeliveryGate.EnsureSatisfied(root),
                "trailing comma");

            await WriteContextAsync(vendor: new string('X', 300 * 1024));
            ExpectBlocked(
                () => ProjectDeliveryGate.EnsureSatisfied(root),
                "oversized evidence file");

            await WriteContextAsync();
            ProjectDeliveryGate.EnsureSatisfied(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task VerifyCtx2PushEvidenceAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-ctx2-" + Guid.NewGuid().ToString("N"));
        var remote = Path.Combine(root, "remote.git");
        var clone = Path.Combine(root, "clone");
        var ledgerPath = Path.Combine(root, "ledger");
        Directory.CreateDirectory(root);
        const string id = "/websites/git-scm";
        const string question = "git diff --name-only between a remote-tracking branch and HEAD";
        var push = new[] { "push", "origin", "main" };

        async Task GitAsync(string directory, params string[] args)
        {
            var safe = directory.Replace('\\', '/');
            var result = await ProcessRunner.RunAsync("git", directory,
                ["-c", $"safe.directory={safe}", .. args], timeoutSeconds: 30);
            if (result.ExitCode != 0 || result.TimedOut)
                throw new InvalidOperationException("CTX2: fixture git " +
                    string.Join(" ", args) + " failed: " + result.StandardError);
        }

        async Task CommitAsync(string message)
        {
            await GitAsync(clone, "add", "-A");
            await GitAsync(clone, "-c", "user.name=CTX2 Test",
                "-c", "user.email=ctx2@example.invalid", "commit", "-m", message);
        }

        void MustBlock(string reason, string? expected = null)
        {
            try
            {
                ProjectDeliveryGate.EnsureSatisfied(clone, push, ledgerPath);
            }
            catch (ModelContextProtocol.McpException ex) when (
                ex.Message.Contains("TALVORA_DELIVERY_GATE_BLOCKED", StringComparison.Ordinal))
            {
                if (expected is not null && !ex.Message.Contains(expected, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        (reason.StartsWith("D64", StringComparison.Ordinal) ? "D64: " : "CTX2: ") +
                        reason + ": " + ex.Message);
                return;
            }
            throw new InvalidOperationException(
                (reason.StartsWith("D64", StringComparison.Ordinal) ? "D64: " : "CTX2: ") +
                "accepted " + reason);
        }

        async Task WriteEvidenceAsync(
            string? noLookup = null, string? vendor = "https://git-scm.com/docs/git-diff",
            string? secondQuestion = null)
        {
            Directory.CreateDirectory(Path.Combine(clone, ".context7"));
            var evidence = new Dictionary<string, object?>
            {
                ["schemaVersion"] = 1,
                ["verifiedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["scope"] = "CTX2 test of recorded questions",
                ["libraries"] = noLookup is null
                    ? new object[] { new
                    {
                        libraryId = id,
                        verified = $"Git diff range (query: \"{question}\")" +
                            (secondQuestion is null ? "" : $" and additional docs (query: \"{secondQuestion}\")"),
                    } }
                    : Array.Empty<object>(),
                ["vendorSources"] = vendor is null ? Array.Empty<string>() : new[] { vendor },
            };
            if (noLookup is not null)
                evidence["noLookup"] = noLookup;
            await File.WriteAllTextAsync(Path.Combine(clone, ".context7", "verification.json"),
                JsonSerializer.Serialize(evidence));
        }

        try
        {
            await GitAsync(root, "init", "--bare", remote);
            await GitAsync(root, "clone", remote, clone);
            await GitAsync(clone, "checkout", "-b", "main");
            await File.WriteAllTextAsync(Path.Combine(clone, "README.md"), "documentation\n");
            Directory.CreateDirectory(Path.Combine(clone, "docs"));
            await File.WriteAllTextAsync(Path.Combine(clone, "docs", "x.md"), "notes\n");
            await CommitAsync("Docs only");
            ProjectDeliveryGate.EnsureSatisfied(clone, push, ledgerPath);
            await GitAsync(clone, "push", "-u", "origin", "main");

            Directory.CreateDirectory(Path.Combine(clone, "src"));
            await File.WriteAllTextAsync(Path.Combine(clone, "src", "a.cs"), "class A {}\n");
            await CommitAsync("Source without evidence");
            MustBlock("code with no committed evidence");

            await WriteEvidenceAsync();
            await CommitAsync("Unrecorded question");
            MustBlock("unrecorded question", id + " \"" + question + "\"");
            MustBlock("unrecorded question names the only Context7 client", "ChatGPT's own Context7 app or any other Context7 client do not count");

            await using (var ledger = new Context7Ledger(ledgerPath))
            {
                ledger.Record(new Context7LookupRecord(DateTimeOffset.UtcNow.AddHours(-25), "ChatGPT",
                    id, question, 200, 12, "0123456789abcdef"));
                ledger.Record(new Context7LookupRecord(DateTimeOffset.UtcNow, "ChatGPT",
                    id, question, 404, 12, "0123456789abcdef"));
                await ledger.FlushAsync();
                MustBlock("old or unsuccessful Context7 evidence", id + " \"" + question + "\"");

                ledger.Record(new Context7LookupRecord(DateTimeOffset.UtcNow, "Talvora.Smoke",
                    id, question, 200, 12, "0123456789abcdef"));
                await ledger.FlushAsync();
                MustBlock("D64: own-client lookup is not valid evidence",
                    id + " \"" + question + "\"");

                ledger.Record(new Context7LookupRecord(DateTimeOffset.UtcNow, "ChatGPT",
                    id, question, 200, 12, "0123456789abcdef"));
                await ledger.FlushAsync();
            }
            ProjectDeliveryGate.EnsureSatisfied(clone, push, ledgerPath);

            const string secondQuestion = "git diff path filtering between commits and working trees";
            await WriteEvidenceAsync(secondQuestion: secondQuestion);
            await CommitAsync("Two claimed questions");
            MustBlock("D64: second question has no proof", id + " \"" + secondQuestion + "\"");

            await WriteEvidenceAsync();
            await CommitAsync("Restored single question");
            ProjectDeliveryGate.EnsureSatisfied(clone, push, ledgerPath);

            await WriteEvidenceAsync("short");
            await CommitAsync("No lookup too short");
            MustBlock("noLookup shorter than 20 characters");

            await WriteEvidenceAsync("CTX2: only renames a local variable");
            await CommitAsync("Valid no-lookup reason");
            ProjectDeliveryGate.EnsureSatisfied(clone, push, ledgerPath);

            await WriteEvidenceAsync("CTX2: only renames a local variable", "http://example.invalid/docs");
            await CommitAsync("Invalid vendor scheme");
            MustBlock("non-HTTPS vendor source");

            await WriteEvidenceAsync("CTX2: only renames a local variable");
            MustBlock("working-tree evidence must not replace committed evidence");
            ProjectDeliveryGate.EnsureSatisfied(clone);

            await CommitAsync("D69 restore valid evidence baseline");
            await GitAsync(clone, "push", "origin", "main");
            await File.AppendAllTextAsync(Path.Combine(clone, "src", "a.cs"),
                "// D69: source change without new evidence\n");
            await CommitAsync("D69 source change without evidence");
            await File.AppendAllTextAsync(Path.Combine(clone, "docs", "x.md"),
                "D69 documentation-only follow-up\n");
            await CommitAsync("D69 documentation-only latest commit");
            foreach (var refspec in new[] { "HEAD:main", "+main", "HEAD:refs/heads/main", "HEAD:" })
            {
                push[2] = refspec;
                MustBlock("D69: source change hidden behind documentation-only tip in " + refspec,
                    "Context7 evidence must be committed");
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task VerifyJobGenerationSidecarAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Job-Generation-Audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "stdout.log");
        var generation = path + ".generation";
        var method = typeof(JobTools).GetMethod(
            "ReadLogGenerationAsync",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Job generation reader was not found.");

        Task<long> ReadAsync() =>
            (Task<long>)method.Invoke(
                null,
                [path, CancellationToken.None])!;
        try
        {
            AssertEqual(0L, await ReadAsync(), "Absent generation should be zero.");
            await File.WriteAllTextAsync(generation, "42");
            AssertEqual(42L, await ReadAsync(), "Valid generation changed.");

            await File.WriteAllTextAsync(
                generation,
                "42" + new string(' ', 128 * 1024));
            try
            {
                await ReadAsync();
                throw new InvalidOperationException(
                    "An oversized job-generation sidecar was accepted.");
            }
            catch (InvalidDataException)
            {
                // The counter is tiny by contract; a padded sidecar must be rejected before reading it.
            }

            await File.WriteAllTextAsync(generation, "42");
            AssertEqual(42L, await ReadAsync(), "Valid generation was not restored.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task VerifyJobSnapshotFailureDisposesFileAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Job-Handle-Audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "stdout.log");
        try
        {
            await File.WriteAllTextAsync(path, "sample");
            var method = typeof(JobTools).GetMethod(
                "OpenStableJobLogSnapshotAsync",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Job snapshot reader was not found.");
            var callCount = 0;
            Func<string, CancellationToken, Task<long>> injectedRead = (_, _) =>
            {
                if (Interlocked.Increment(ref callCount) == 2)
                {
                    throw new IOException("Injected post-open generation failure.");
                }
                return Task.FromResult(7L);
            };
            try
            {
                var task = (Task<(FileStream File, long Generation)>)method.Invoke(
                    null, [path, CancellationToken.None, injectedRead])!;
                var result = await task;
                await result.File.DisposeAsync();
                throw new InvalidOperationException(
                    "Injected post-open generation failure did not propagate.");
            }
            catch (IOException ex) when (
                ex.Message == "Injected post-open generation failure.")
            {
                // Verify that the stream opened before the exception was disposed.
            }
            AssertEqual(2, callCount, "The injection did not reach the post-open read.");
            using (var exclusive = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                AssertEqual(6L, exclusive.Length, "The fixture log was modified.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task VerifyRuntimeMetadataBoundsAsync()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Runtime-Audit-" + Guid.NewGuid().ToString("N") + ".json");
        var reader = typeof(TalvoraRuntimeMetadata).GetMethod(
            "ReadFromPath",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Bounded runtime metadata reader is missing.");
        TalvoraRuntimeMetadata Load() =>
            (TalvoraRuntimeMetadata)reader.Invoke(null, [path])!;
        try
        {
            await File.WriteAllTextAsync(
                path, "{\"sourceCommit\":\"quality-audit\",\"installedAtUtc\":\"2026-10-09\"}");
            AssertEqual("quality-audit", Load().SourceCommit,
                "Small runtime metadata did not load.");
            await File.WriteAllTextAsync(
                path, "{\"sourceCommit\":\"" + new string('X', 128 * 1024) + "\"}");
            AssertEqual(null, Load().SourceCommit,
                "An oversized runtime metadata file was accepted.");
            await File.WriteAllTextAsync(path, "{");
            AssertEqual(null, Load().SourceCommit,
                "Malformed runtime metadata did not fail soft.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task VerifyHostingerMetadataBoundsAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Talvora-Hostinger-Audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var secretPath = Path.Combine(root, "token.txt");
        var packagePath = Path.Combine(root, "package.json");
        var tokenReader = typeof(HostingerMcpTools).GetMethod(
            "ReadToken", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Hostinger credential reader missing.");
        var packageReader = typeof(HostingerMcpTools).GetMethod(
            "ReadPackageVersion", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Hostinger package reader missing.");
        var previousToken = Environment.GetEnvironmentVariable("TALVORA_HOSTINGER_API_TOKEN");
        try
        {
            // Only isolate this test process's environment; no real stored secret is read.
            Environment.SetEnvironmentVariable("TALVORA_HOSTINGER_API_TOKEN", null);
            await File.WriteAllTextAsync(secretPath, "fixture-token");
            AssertEqual("fixture-token",
                tokenReader.Invoke(null, [secretPath]) as string,
                "Small credential file did not load.");
            await File.WriteAllTextAsync(secretPath, new string('X', 128 * 1024));
            ExpectInvalidData(() => tokenReader.Invoke(null, [secretPath]),
                "Oversized Hostinger credential was accepted.");

            await File.WriteAllTextAsync(packagePath, "{\"version\":\"1.2.3\"}");
            AssertEqual("1.2.3",
                packageReader.Invoke(null, [packagePath]) as string,
                "Small Hostinger package metadata did not load.");
            await File.WriteAllTextAsync(
                packagePath,
                JsonSerializer.Serialize(new {
                    version = "1.2.3",
                    notes = new string('X', 300 * 1024),
                }));
            ExpectInvalidData(() => packageReader.Invoke(null, [packagePath]),
                "Oversized Hostinger package metadata was accepted.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TALVORA_HOSTINGER_API_TOKEN", previousToken);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ExpectInvalidData(Action operation, string message)
    {
        try
        {
            operation();
        }
        catch (TargetInvocationException ex)
            when (ex.InnerException is InvalidDataException)
        {
            return;
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }
}
