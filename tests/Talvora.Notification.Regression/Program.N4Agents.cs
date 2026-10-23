using Talvora.Shared;

internal static partial class Program
{
    private static async Task AssertN4AgentEventsAsync()
    {
        var fixture = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "N4-codex-events.jsonl")));
        var parsed = CodexCliEventSummary.Parse(fixture);
        if (parsed.CompletedCommands != 3 || parsed.McpToolCalls != 1 ||
            parsed.ChangedFiles.Count != 1 || parsed.ChangedFiles[0] != "Demo.cs")
            throw new InvalidOperationException("N4: completed file-change paths or event totals are wrong.");
        var actual = string.Join(" ", parsed.RecentActivities);
        foreach (var expected in new[] { "Testleri çalıştırıyor", "Derliyor", "Git işlemi yapıyor", "Dosya düzenliyor · Demo.cs" })
            if (!actual.Contains(expected, StringComparison.Ordinal))
                throw new InvalidOperationException("N4: required Turkish activity missing: " + expected);
        if (actual.Contains("SECRET_COMMAND_SENTINEL", StringComparison.Ordinal) ||
            actual.Contains("Rejected.cs", StringComparison.Ordinal) ||
            System.Text.RegularExpressions.Regex.IsMatch(actual, "gelistir|dogrul|cagri|gecen sure", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new InvalidOperationException("N4: raw command, failed change or non-Turkish activity leaked.");
        await AssertN4CodexSnapshotAsync(fixture, parsed);
        await AssertN4ClaudeAsync();
    }
}
