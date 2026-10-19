namespace Talvora.Tools;

internal static class GitArgumentParser
{
    private static readonly HashSet<string> GlobalOptionsWithValue = new(StringComparer.Ordinal)
    {
        "-C", "-c", "--git-dir", "--work-tree", "--namespace", "--super-prefix", "--config-env", "--exec-path",
    };

    public static string? GetSubcommand(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (string.IsNullOrWhiteSpace(a)) continue;
            if (GlobalOptionsWithValue.Contains(a)) { i++; continue; }
            if (a.StartsWith('-')) continue;
            return a;
        }
        return null;
    }
}
