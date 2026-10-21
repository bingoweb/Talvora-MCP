using System.ComponentModel;
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
        var sections = File.Exists(agentsPath)
            ? ExtractSections(await File.ReadAllTextAsync(
                agentsPath, cancellationToken))
            : string.Empty;
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
        if (excess > 0)
        {
            var fromContext = Math.Min(excess, context.Length);
            context = context[..^fromContext];
            excess -= fromContext;
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
        return string.Join("\n\n", selected);
    }
}
