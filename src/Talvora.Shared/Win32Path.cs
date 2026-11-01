namespace Talvora.Shared;

// W3: Win32 calls made through P/Invoke do not get .NET's automatic long-path handling; the \\?\ form lifts MAX_PATH.
public static class Win32Path
{
    public static string Extended(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return path;
        }

        var full = Path.GetFullPath(path);
        return full.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + full[2..]
            : @"\\?\" + full;
    }
}
