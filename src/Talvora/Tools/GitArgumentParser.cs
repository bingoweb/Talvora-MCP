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

    // H6: a command-line alias would hide push/commit/reset from the delivery gate, the work contract and the source policy.
    public static bool DefinesAlias(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if ((a == "-c" || a == "--config-env") && i + 1 < args.Count &&
                args[i + 1].TrimStart().StartsWith("alias.", StringComparison.OrdinalIgnoreCase))
                return true;
            if (a.StartsWith("--config-env=alias.", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
