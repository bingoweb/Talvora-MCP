using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraAgentBriefResponse(
    string WorkspaceRoot,
    string AgentsSections,
    string Context,
    bool Truncated);

[McpServerToolType]
public static class AgentBriefTools
{
    private const int MaxTotalCharacters = 60_000;
    private const int MaxRecentWorkspaces = 10;
    private static readonly SemaphoreSlim RecentWorkspaceGate = new(1, 1);
    private static readonly JsonSerializerOptions RecentJsonOptions = new(JsonSerializerDefaults.Web);
    internal static string? RecentWorkspacesPathOverrideForTests;
    private sealed record RecentWorkspace(string Root, DateTimeOffset LastUsedUtc);
    private const string TrimMarker =
        "\n[brief trimmed here: the rest of STATUS is in docs/codex/STATUS.md]";
    private static readonly string[] RequiredHeadings =
    [
        "## PERMANENT USER-APPROVED AGENT PROMPT",
        "## WHO MUST FOLLOW THESE RULES",
        "## Language policy",
        "## EXECUTION MANDATE",
        "## TOKEN ECONOMY",
        "## QUEUE ADDITIONS",
        "## CONTINUE PROTOCOL",
    ];

    [McpServerTool(
        Name = "talvora_agent_brief",
        Title = "Proje talimatlarını getir",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraAgentBriefResponse)),
     Description("Return the binding agent rules (AGENTS.md mandate, token economy, continue protocol) and the current card for a workspace. Call this first in every session before changing files in that workspace.")]
    public static async Task<TalvoraAgentBriefResponse> Get(
        string workspaceRoot,
        string? card = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Workspace does not exist: {root}");
        }

        var agentsPath = Path.Combine(root, "AGENTS.md");
        if (!File.Exists(agentsPath))
        {
            throw new FileNotFoundException(DescribeMissingAgents(root));
        }
        var sections = ExtractSections(await File.ReadAllTextAsync(agentsPath, cancellationToken));
        await RecordRecentWorkspaceAsync(root);
        var context = string.Empty;
        var scriptPath = Path.Combine(
            root, "scripts", "Get-CodexContext.ps1");
        if (File.Exists(scriptPath))
        {
            var pwsh = CommandResolver.Resolve(
                ["pwsh.exe", "pwsh"],
                [@"C:\Program Files\PowerShell\7\pwsh.exe"])
                ?? throw new FileNotFoundException(
                    "PowerShell 7 is required for Get-CodexContext.ps1.");
            var arguments = new List<string>
            {
                "-NoProfile",
                "-NonInteractive",
                "-File",
                scriptPath,
            };
            if (card is not null)
            {
                arguments.Add("-Card");
                arguments.Add(card);
            }

            var run = await InteractiveUserProcessRunner.RunAsync(
                pwsh,
                root,
                arguments,
                timeoutSeconds: 60,
                cancellationToken: cancellationToken,
                maxCapturedCharactersPerStream: MaxTotalCharacters);
            if (run.TimedOut || run.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Get-CodexContext.ps1 failed under the interactive user.");
            }
            context = run.StandardOutput;
        }

        var excess = Math.Max(
            0, root.Length + sections.Length + context.Length -
                MaxTotalCharacters);
        var truncated = excess > 0;
        if (excess > 0 && context.Length > 0)
        {
            // D30: STATUS is the last context section, so trimming cuts STATUS, never the card; say so.
            var fromContext = Math.Min(excess + TrimMarker.Length, context.Length);
            context = context[..^fromContext] + TrimMarker;
            excess = Math.Max(0, excess + TrimMarker.Length - fromContext);
        }
        if (excess > 0)
        {
            var fromSections = Math.Min(excess, sections.Length);
            sections = sections[..^fromSections];
        }

        return new TalvoraAgentBriefResponse(
            root, sections, context, truncated);
    }

    private static string ExtractSections(string agents)
    {
        var headings = Regex.Matches(
            agents, @"^## [^\r\n]+", RegexOptions.Multiline);
        var selected = new List<string>();
        for (var index = 0; index < headings.Count; index++)
        {
            var current = headings[index];
            if (!RequiredHeadings.Any(prefix =>
                    current.Value.StartsWith(
                        prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            var end = index + 1 < headings.Count
                ? headings[index + 1].Index
                : agents.Length;
            selected.Add(agents[current.Index..end].TrimEnd('\r', '\n'));
        }
        return selected.Count == 0 ? agents : string.Join("\n\n", selected);
    }

    private static string RecentWorkspacesPath =>
        RecentWorkspacesPathOverrideForTests ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora", "AgentBrief", "recent-workspaces.json");

    private static List<RecentWorkspace> ReadRecentWorkspaces(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<RecentWorkspace>>(File.ReadAllText(path), RecentJsonOptions)
                ?? [];
        }
        catch (Exception)
        {
            // Briefs still work if the optional list cannot be read.
            return [];
        }
    }

    private static string DescribeMissingAgents(string root)
    {
        var message = $"No AGENTS.md in {root}. workspaceRoot must be the repository folder that holds AGENTS.md.";
        var alternatives = ReadRecentWorkspaces(RecentWorkspacesPath)
            .Select(entry => entry.Root)
            .Where(candidate => !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(candidate, "AGENTS.md")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
        return alternatives.Length == 0
            ? message
            : message + " Workspaces briefed recently: " + string.Join("; ", alternatives) + ".";
    }

    private static async Task RecordRecentWorkspaceAsync(string root)
    {
        var path = RecentWorkspacesPath;
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var enteredGate = false;
        try
        {
            await RecentWorkspaceGate.WaitAsync();
            enteredGate = true;
            var existing = ReadRecentWorkspaces(path);
            // Keep the original spelling of a root seen earlier; comparison is case insensitive.
            var canonicalRoot = existing.FirstOrDefault(entry =>
                string.Equals(entry.Root, root, StringComparison.OrdinalIgnoreCase))?.Root ?? root;
            var recent = existing.Where(entry =>
                    !string.Equals(entry.Root, root, StringComparison.OrdinalIgnoreCase))
                .Take(MaxRecentWorkspaces - 1)
                .ToList();
            recent.Insert(0, new RecentWorkspace(canonicalRoot, DateTimeOffset.UtcNow));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(recent, RecentJsonOptions));
            if (File.Exists(path))
                File.Replace(tempPath, path, destinationBackupFileName: null);
            else
                File.Move(tempPath, path);
        }
        catch (Exception)
        {
            // Recent-workspace hints are best effort; a blocked state directory never blocks a brief.
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (Exception) { /* No impact on the brief. */ }
            if (enteredGate) RecentWorkspaceGate.Release();
        }
    }
}
