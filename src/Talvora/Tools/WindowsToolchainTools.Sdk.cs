using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class WindowsToolchainTools
{
[McpServerTool(
        Name = "talvora_windows_sdk_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWindowsSdkInfoResponse)),
     Description("Return Windows SDK root/version discovery. Missing SDKs are reported structurally instead of throwing.")]
    public static TalvoraWindowsSdkInfoResponse WindowsSdkInfo()
    {
        var sdks = DiscoverWindowsSdks();
        var kitsRoot = ResolveKitsRoot10();

        return new TalvoraWindowsSdkInfoResponse(
            sdks.Count > 0 || !string.IsNullOrWhiteSpace(kitsRoot),
            kitsRoot,
            sdks.FirstOrDefault()?.Version,
            sdks);
    }

    private static string? ResolveKitsRoot10()
    {
        var candidates = new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10",
            @"C:\Program Files\Windows Kits\10",
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<TalvoraWindowsSdkEntry> DiscoverWindowsSdks()
    {
        var roots = new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10\bin",
            @"C:\Program Files\Windows Kits\10\bin",
        };

        var result = new List<TalvoraWindowsSdkEntry>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root)
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);
                if (!Version.TryParse(name, out _))
                {
                    continue;
                }

                result.Add(new TalvoraWindowsSdkEntry(
                    name,
                    directory,
                    Directory.Exists(Path.Combine(directory, "x64")),
                    Directory.Exists(Path.Combine(directory, "x86")),
                    Directory.Exists(Path.Combine(directory, "arm64")),
                    File.Exists(Path.Combine(directory, "x64", "rc.exe")),
                    File.Exists(Path.Combine(directory, "x64", "mt.exe")),
                    File.Exists(Path.Combine(directory, "x64", "signtool.exe"))));
            }
        }

        return result
            .GroupBy(item => item.Version, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => Version.Parse(item.Version))
            .ToArray();
    }
}
