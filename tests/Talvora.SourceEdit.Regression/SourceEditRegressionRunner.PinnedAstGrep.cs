using System.Reflection;
using System.Text;
using System.Text.Json;
using Talvora.SourceEditing;

internal static partial class SourceEditRegressionRunner
{
    public static async Task RunPinnedAstGrepCacheAsync()
    {
        var pinPath = Path.Combine(AppContext.BaseDirectory, "ast-grep-pin.json");
        using var pinJson = JsonDocument.Parse(await File.ReadAllTextAsync(pinPath));
        var pin = pinJson.RootElement;
        var version = pin.GetProperty("version").GetString()!;
        var platform = pin.GetProperty("platforms").GetProperty("win-x64");
        var packageName = platform.GetProperty("packageName").GetString()!;
        var integrity = platform.GetProperty("integrity").GetString()!;
        var pinnedSha = platform.GetProperty("executableSha256").GetString()!;

        var root = Path.Combine(Path.GetTempPath(), "TalvoraAstPinnedCache-" +
            Guid.NewGuid().ToString("N"));
        var versionRoot = Path.Combine(root, version);
        Directory.CreateDirectory(versionRoot);
        try
        {
            var executable = Path.Combine(versionRoot, "ast-grep.exe");
            await File.WriteAllTextAsync(executable,
                "TAMPERED_AST_GREP_EXE_MUST_NOT_BE_EXECUTED", Encoding.UTF8);
            await File.WriteAllTextAsync(
                Path.Combine(versionRoot, "toolchain.json"),
                JsonSerializer.Serialize(new
                {
                    packageName,
                    version,
                    integrity,
                    executableSha256 = pinnedSha,
                }));

            var method = typeof(AstGrepToolchainManager).GetMethod(
                "TryResolveInstalledAsync", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Cache validation entrypoint not found.");
            var task = (Task<AstGrepToolchain?>?)method.Invoke(null,
                [root, version, packageName, integrity, CancellationToken.None])
                ?? throw new InvalidOperationException("Cache validation returned no task.");

            try
            {
                _ = await task;
                throw new InvalidOperationException(
                    "Tampered cached ast-grep executable was accepted.");
            }
            catch (SourceEditDomainException ex) when (
                ex.Message.Contains(
                    "Pinned ast-grep cache failed integrity verification; execution refused.",
                    StringComparison.Ordinal))
            {
                Console.WriteLine("PASS ast-grep-tampered-cache-fails-closed");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
