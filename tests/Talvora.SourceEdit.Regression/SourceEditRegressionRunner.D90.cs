using System.Reflection;
using System.Text.Json;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    public static async Task D90AgentBriefAsync()
    {
        var fixtures = Path.Combine(Path.GetTempPath(), "talvora-d90-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtures);
        var historyFile = Path.Combine(fixtures, "recent-workspaces.json");
        var overrideField = typeof(AgentBriefTools).GetField("RecentWorkspacesPathOverrideForTests",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        overrideField?.SetValue(null, historyFile); // Existing code lacks this test seam: red must demonstrate the original bug.

        static string Create(string baseFolder, string name, string text)
        {
            var folder = Path.Combine(baseFolder, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "AGENTS.md"), text);
            return folder;
        }

        static async Task<string> MissingMessageAsync(string folder)
        {
            try { await AgentBriefTools.Get(folder); }
            catch (FileNotFoundException ex) { return ex.Message; }
            throw new InvalidOperationException("D90 RED: missing AGENTS.md returned a successful empty brief: " + folder);
        }

        try
        {
            var mixed = Create(fixtures, "mixed",
                "## WHO MUST FOLLOW THESE RULES\nTalvora-specific content.\n" +
                "## Setup\nThis unrelated section must be omitted.\n" +
                "## CONTINUE PROTOCOL\nThe binding instructions.\n");
            var two = await AgentBriefTools.Get(mixed);
            Assert(two.AgentsSections.Contains("## WHO MUST FOLLOW THESE RULES", StringComparison.Ordinal) &&
                two.AgentsSections.Contains("## CONTINUE PROTOCOL", StringComparison.Ordinal) &&
                !two.AgentsSections.Contains("## Setup", StringComparison.Ordinal) &&
                two.Context == "" && !two.Truncated, "D90: mixed AGENTS.md must keep only the Talvora sections.");

            const string ordinaryAgents = "## Setup\nBuild with the project toolchain.\n## Code style\nUse small changes.\n";
            var ordinary = Create(fixtures, "ordinary", ordinaryAgents);
            var ordinaryBrief = await AgentBriefTools.Get(ordinary);
            Assert(ordinaryBrief.AgentsSections == ordinaryAgents && ordinaryBrief.Context == "" &&
                !ordinaryBrief.Truncated, "D90 RED: a non-Talvora AGENTS.md was discarded instead of returned whole.");

            var missing = Path.Combine(fixtures, "missing");
            Directory.CreateDirectory(missing);
            var prefix = $"No AGENTS.md in {missing}. workspaceRoot must be the repository folder that holds AGENTS.md.";
            var firstError = await MissingMessageAsync(missing);
            Assert(firstError == prefix + $" Workspaces briefed recently: {ordinary}; {mixed}.",
                "D90: missing AGENTS.md error did not name the two recent repositories newest first: " + firstError);

            File.Delete(Path.Combine(mixed, "AGENTS.md"));
            var afterDeletion = await MissingMessageAsync(missing);
            Assert(afterDeletion == prefix + $" Workspaces briefed recently: {ordinary}.",
                "D90: an AGENTS.md that was later deleted must disappear from suggestions: " + afterDeletion);

            var recent = new List<string>();
            for (var i = 0; i < 12; i++)
            {
                var folder = Create(fixtures, $"workspace{i:00}", ordinaryAgents);
                var current = await AgentBriefTools.Get(folder);
                Assert(current.AgentsSections == ordinaryAgents, "D90: normal AGENTS.md disappeared.");
                recent.Insert(0, folder);
            }
            // The case-insensitive root key deduplicates instead of adding an 11th record.
            await AgentBriefTools.Get(recent[0].ToUpperInvariant());

            Assert(File.Exists(historyFile), "D90: no recent-workspaces.json was written.");
            using (var document = JsonDocument.Parse(File.ReadAllText(historyFile)))
            {
                var values = document.RootElement.EnumerateArray().ToArray();
                Assert(values.Length == 10, $"D90: history must hold at most 10 entries, got {values.Length}.");
                for (var i = 0; i < values.Length; i++)
                {
                    var entry = values[i];
                    Assert(entry.TryGetProperty("root", out var storedRoot) &&
                        string.Equals(storedRoot.GetString(), recent[i], StringComparison.OrdinalIgnoreCase),
                        $"D90: history not newest first or case-insensitive at index {i}.");
                    Assert(entry.TryGetProperty("lastUsedUtc", out var usedAt) &&
                        DateTimeOffset.TryParse(usedAt.GetString(), out _),
                        "D90: missing timestamp in stored workspace.");
                }
            }
            var expected = prefix + " Workspaces briefed recently: " + string.Join("; ", recent.Take(5)) + ".";
            var lastError = await MissingMessageAsync(missing);
            Assert(lastError == expected,
                $"D90: missing AGENTS.md should name precisely the 5 most recent existing workspaces: {lastError}");

            // A blocked history destination cannot turn a valid AGENTS.md into a failed brief.
            var blocked = Path.Combine(fixtures, "blocked-history");
            File.WriteAllText(blocked, "not a directory");
            overrideField!.SetValue(null, Path.Combine(blocked, "recent-workspaces.json"));
            var stillValid = await AgentBriefTools.Get(ordinary);
            Assert(stillValid.AgentsSections == ordinaryAgents,
                "D90: failed persistence must not prevent a valid brief.");
        }
        finally
        {
            overrideField?.SetValue(null, null);
            if (Directory.Exists(fixtures))
                Directory.Delete(fixtures, recursive: true);
        }
    }
}
