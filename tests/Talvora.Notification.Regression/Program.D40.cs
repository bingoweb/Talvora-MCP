using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D40: a test run writes nothing into the signed-in user's Talvora folder.</summary>
    private static void AssertD40TestIsolation()
    {
        var userFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Talvora");
        if (TrayLog.PathName.StartsWith(userFolder, StringComparison.OrdinalIgnoreCase) ||
            new WorklogHistoryStore().PathName.StartsWith(userFolder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("D40: tests must not write into the user's Talvora folder.");
        Console.WriteLine("D40_TEST_ISOLATION_GREEN");
    }
}
