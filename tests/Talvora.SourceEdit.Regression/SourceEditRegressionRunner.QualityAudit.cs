using System.Reflection;
using System.Text.Json;
using Talvora;
using Talvora.SourceEditing;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    public static async Task RunQualityAuditAsync()
    {
        await VerifyDeliveryEvidenceAsync();
        Console.WriteLine("PASS delivery-evidence-rejects-invalid-and-oversized");
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
        Directory.CreateDirectory(Path.Combine(root, ".talvora"));
        var contextPath = Path.Combine(root, ".context7", "verification.json");
        var designPath = Path.Combine(root, ".talvora", "awwwards-verification.json");
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("O");
            await File.WriteAllTextAsync(
                designPath,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    verifiedAtUtc = timestamp,
                    sourceUrl = "https://www.awwwards.com/",
                    references = new[] { "https://www.awwwards.com/" },
                    designDecisions = new[] { "No UI changes in this fixture." },
                }));

            async Task WriteContextAsync(
                string id = "/dotnet/docs",
                string? vendor = "Microsoft Learn .NET documentation checked.")
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
                            verified = "Verified supported .NET APIs.",
                        } },
                        vendorSources = new string?[] { vendor },
                    }));
            }

            static void ExpectBlocked(Action operation, string caseName)
            {
                try
                {
                    operation();
                }
                catch (InvalidOperationException ex)
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
            ExpectBlocked(
                () => ProjectDeliveryGate.EnsureSatisfied(root),
                "null vendor source");

            await WriteContextAsync(vendor: "   ");
            ExpectBlocked(
                () => ProjectDeliveryGate.EnsureSatisfied(root),
                "blank vendor source");

            await WriteContextAsync(id: "/");
            ExpectBlocked(
                () => ProjectDeliveryGate.EnsureSatisfied(root),
                "incomplete canonical library ID");

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
