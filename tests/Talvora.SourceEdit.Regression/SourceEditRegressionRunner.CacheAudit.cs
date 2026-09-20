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
                offline ? "TryResolveNewestCachedAsync" : "TryResolveInstalledAsync",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var parameters = offline
                ? new object[] { cache, toolchain.PackageName, token }
                : new object[] { cache, toolchain.Version, toolchain.PackageName, toolchain.Integrity, token };
            return await (Task<AstGrepToolchain?>)method.Invoke(null, parameters)!;
        }

        await PrepareAsync(new string('0', 64));
        Assert(await ResolveAsync() is null, "Cache with a mismatched executable hash was accepted.");
        await PrepareAsync(null);
        Assert(await ResolveAsync() is null, "Legacy cache without executable hash was accepted.");
        await PrepareAsync(expectedSha, "@different/package");
        Assert(await ResolveAsync() is null, "Cache with another package identity was accepted.");
        foreach (var invalidManifest in new[] { "[]", "{", "null" })
        {
            await PrepareAsync(expectedSha);
            await File.WriteAllTextAsync(manifest, invalidManifest);
            Assert(await ResolveAsync() is null, "Malformed cache provenance was accepted.");
        }
        await PrepareAsync(expectedSha);
        File.Delete(manifest);
        Assert(await ResolveAsync() is null, "Cache without provenance was accepted.");
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
