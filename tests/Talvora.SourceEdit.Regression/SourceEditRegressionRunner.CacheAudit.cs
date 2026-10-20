using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    public static async Task RunCacheAuditAsync()
    {
        var failures = new List<string>();
        foreach (var offline in new[] { false, true })
        {
            try
            {
                await CacheIntegrityAsync(offline);
                Console.WriteLine($"PASS ast-grep-cache-{(offline ? "offline" : "online")}-integrity");
            }
            catch (Exception ex)
            {
                failures.Add(ex.ToString());
                Console.WriteLine($"FAIL ast-grep-cache-{(offline ? "offline" : "online")}-integrity: {ex.Message}");
            }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static async Task CacheIntegrityAsync(bool offline)
    {
        var toolchain = await AstGrepToolchainManager.ResolveAsync(CancellationToken.None);
        await using var fixture = await TestWorkspace.CreateAsync();
        var cache = fixture.OrdinaryRoot;
        var versionDirectory = Path.Combine(cache, toolchain.Version);
        var executable = Path.Combine(versionDirectory, "ast-grep.exe");
        var manifest = Path.Combine(versionDirectory, "toolchain.json");
        var expectedSha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(toolchain.ExecutablePath)));

        async Task PrepareAsync(string? sha, string? package = null)
        {
            Directory.CreateDirectory(versionDirectory);
            File.Copy(toolchain.ExecutablePath, executable, overwrite: true);
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new
            {
                packageName = package ?? toolchain.PackageName,
                version = toolchain.Version,
                integrity = toolchain.Integrity,
                executableSha256 = sha,
            }));
        }

        async Task<AstGrepToolchain?> ResolveAsync(CancellationToken token = default)
        {
            var method = typeof(AstGrepToolchainManager).GetMethod(
                "TryResolveInstalledAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
            return await (Task<AstGrepToolchain?>)method.Invoke(null,
                [cache, toolchain.Version, toolchain.PackageName, toolchain.Integrity, token])!;
        }

        async Task ExpectRejectedAsync(string explanation)
        {
            try
            {
                _ = await ResolveAsync();
            }
            catch (SourceEditDomainException ex) when (
                ex.Message.Contains("Pinned ast-grep cache failed integrity verification; execution refused.",
                    StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException("Pinned cache did not fail closed: " + explanation);
        }

        await PrepareAsync(new string('0', 64));
        await ExpectRejectedAsync("mismatched executable SHA-256");
        await PrepareAsync(null);
        await ExpectRejectedAsync("legacy cache without executable hash");
        await PrepareAsync(expectedSha, "@different/package");
        await ExpectRejectedAsync("another package identity");
        foreach (var invalidManifest in new[] { "[]", "{", "null" })
        {
            await PrepareAsync(expectedSha);
            await File.WriteAllTextAsync(manifest, invalidManifest);
            await ExpectRejectedAsync("malformed provenance");
        }
        await PrepareAsync(expectedSha);
        File.Delete(manifest);
        await ExpectRejectedAsync("missing provenance");
        await PrepareAsync(expectedSha);
        var valid = await ResolveAsync();
        Assert(valid is not null && valid.Integrity == toolchain.Integrity, "Verified cache could not be reused.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await ResolveAsync(cancellation.Token);
            throw new InvalidOperationException("Cache resolution swallowed caller cancellation.");
        }
        catch (OperationCanceledException) { }
        Assert(File.Exists(executable), "Cancellation removed the valid cache.");
    }
}
