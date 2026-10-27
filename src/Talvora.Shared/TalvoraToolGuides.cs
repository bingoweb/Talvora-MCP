namespace Talvora.Shared;

/// <summary>
/// H1: how a model calls a Talvora tool. Parameter lines become the tools/list parameter descriptions (only where
/// the schema has none) and Usage + Example are appended to every failed result of that tool. Examples are checked
/// against the live input schemas by the surface-policy smoke, so they cannot drift.
/// </summary>
public sealed record TalvoraToolGuide(
    string Usage,
    IReadOnlyDictionary<string, string> Parameters,
    string Example);

public static class TalvoraToolGuides
{
    private static Dictionary<string, string> P(params (string Name, string Text)[] items) =>
        items.ToDictionary(item => item.Name, item => item.Text, StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, TalvoraToolGuide> All { get; } =
        new Dictionary<string, TalvoraToolGuide>(StringComparer.Ordinal)
        {
            ["talvora_agent_brief"] = new(
                "Call once at the start of work in a repository that has AGENTS.md; follow the rules and the current card it returns.",
                P(("workspaceRoot", "Absolute repository root."),
                  ("card", "Optional card id (for example AP2); empty = the current card.")),
                """{"workspaceRoot":"C:\\Users\\tayla\\Talvora-MCP"}"""),
            ["talvora_read_source"] = new(
                "Read a window of a text file plus its whole-file revision (sha256:...). Call it before talvora_apply_patch changes or deletes the file, and copy 'revision' into the patch.",
                P(("path", "Absolute file path."),
                  ("startLine", "1-based first line to return."),
                  ("lineCount", "Number of lines; 0 = until maxCharacters."),
                  ("startCharacter", "0-based character offset; use instead of startLine for minified files."),
                  ("maxCharacters", "Upper bound of returned characters.")),
                """{"path":"C:\\Users\\tayla\\Talvora-MCP\\src\\Talvora\\Program.cs","startLine":40,"lineCount":80}"""),
            ["talvora_apply_patch"] = new(
                "Change files atomically. Default format: '*** Begin Talvora Patch', then per file '*** Update File: <relative path>' + '*** Revision: <revision from talvora_read_source>' + '@@' + lines starting with ' ' (unchanged), '-' (removed) or '+' (added); '*** Add File: <path>' + '+' lines; '*** Delete File: <path>' + '*** Revision: ...'; end with '*** End Talvora Patch'. Context must equal the file exactly and occur once. New transactionId per attempt.",
                P(("workspaceRoot", "Absolute repository root; paths in the patch are relative to it."),
                  ("transactionId", "New unique id per attempt, e.g. fix-login-1."),
                  ("validateSyntax", "Reject the change if C#/JSON/XML syntax breaks (default true)."),
                  ("expectedRevisions", "unified-diff only: {\"rel/path\":\"sha256:...\"}.")),
                """{"workspaceRoot":"C:\\Users\\tayla\\Talvora-MCP","transactionId":"example-1","patch":"*** Begin Talvora Patch\n*** Update File: docs/a.md\n*** Revision: sha256:0000000000000000000000000000000000000000000000000000000000000000\n@@\n unchanged line\n-old line\n+new line\n*** End Talvora Patch"}"""),
            ["talvora_search_text"] = new(
                "Find text in files under a folder. Use the narrowest root and a small maxMatches; then read a window with talvora_read_source.",
                P(("root", "Absolute folder (or file) to search."),
                  ("query", "Text or regex to find."),
                  ("regex", "Treat query as a .NET regular expression."),
                  ("caseSensitive", "Match case exactly."),
                  ("includePatterns", "File globs to include, e.g. [\"*.cs\"]."),
                  ("excludePatterns", "File globs to skip, e.g. [\"bin/**\",\"obj/**\"]."),
                  ("recursive", "Search subfolders."),
                  ("maxMatches", "Maximum matches returned (keep small, e.g. 50)."),
                  ("matchOffset", "Skip this many matches (paging).")),
                """{"root":"C:\\Users\\tayla\\Talvora-MCP\\src","query":"ApplyPatchAsync","includePatterns":["*.cs"],"maxMatches":30}"""),
            ["talvora_find_files"] = new(
                "List files by name pattern under a folder.",
                P(("root", "Absolute folder."),
                  ("patterns", "Globs matched against name and relative path, e.g. [\"*.csproj\"]."),
                  ("excludePatterns", "Globs to skip."),
                  ("recursive", "Include subfolders."),
                  ("maxResults", "Maximum entries (keep small)."),
                  ("resultOffset", "Skip this many entries (paging).")),
                """{"root":"C:\\Users\\tayla\\Talvora-MCP","patterns":["*.csproj"],"maxResults":50}"""),
            ["talvora_read_text_range"] = new(
                "Read lines of any text file (logs, notes). For files you will edit, use talvora_read_source instead.",
                P(("path", "Absolute file path."),
                  ("startLine", "1-based first line."),
                  ("lineCount", "Number of lines."),
                  ("startCharacter", "0-based character offset instead of a line.")),
                """{"path":"C:\\Users\\tayla\\Talvora-MCP\\AGENTS.md","startLine":1,"lineCount":140}"""),
            ["talvora_tail_text"] = new(
                "Read the last lines of a log or output file; poll it for detached runs.",
                P(("path", "Absolute file path."),
                  ("lineCount", "Number of last lines (keep small, e.g. 20)."),
                  ("beforeLine", "Exclusive upper line bound for paging backwards; 0 = end of file.")),
                """{"path":"C:\\Users\\tayla\\Talvora-MCP\\artifacts\\gate.log","lineCount":20}"""),
            ["talvora_list"] = new(
                "List one folder's entries.",
                P(("path", "Absolute folder."),
                  ("recursive", "Include subfolders (avoid on large trees).")),
                """{"path":"C:\\Users\\tayla\\Talvora-MCP\\scripts"}"""),
            ["talvora_user_run"] = new(
                "Run a program as the signed-in Windows user and wait (about 2 minutes or less). For longer runs use talvora_user_process_start with output redirected to a log, then talvora_tail_text.",
                P(("executable", "Program name or absolute path, e.g. C:\\Program Files\\PowerShell\\7\\pwsh.exe."),
                  ("arguments", "Array of separate arguments, not one command line."),
                  ("workingDirectory", "Absolute folder to run in."),
                  ("environment", "Extra environment variables {\"NAME\":\"value\"}."),
                  ("timeoutSeconds", "Kill the run after this many seconds."),
                  ("maxOutputCharacters", "Upper bound of returned output.")),
                """{"executable":"C:\\Program Files\\PowerShell\\7\\pwsh.exe","arguments":["-NoProfile","-File","scripts/Get-CiStatus.ps1","-TimeoutMinutes","0"],"workingDirectory":"C:\\Users\\tayla\\Talvora-MCP"}"""),
            ["talvora_user_process_start"] = new(
                "Start a long run as the signed-in user without waiting. Redirect its output to a file inside the command and read it with talvora_tail_text.",
                P(("executable", "Program name or absolute path."),
                  ("arguments", "Array of separate arguments."),
                  ("workingDirectory", "Absolute folder to run in."),
                  ("environment", "Extra environment variables."),
                  ("visible", "Show a window (use false for background work)."),
                  ("newConsole", "Give the process its own console."),
                  ("sessionId", "Windows session id; omit to use the active one.")),
                """{"executable":"C:\\Program Files\\PowerShell\\7\\pwsh.exe","arguments":["-NoProfile","-Command","& ./scripts/Invoke-QualityGate.ps1 -Scope Full *> artifacts/gate.log; exit $LASTEXITCODE"],"workingDirectory":"C:\\Users\\tayla\\Talvora-MCP","visible":false}"""),
            ["talvora_run_powershell"] = new(
                "Run a PowerShell script as LocalSystem (administration only). Builds, tests, git remotes and anything user-specific run with talvora_user_run instead; source edits use talvora_apply_patch.",
                P(("script", "PowerShell script text."),
                  ("workingDirectory", "Absolute folder to run in."),
                  ("timeoutSeconds", "Kill the script after this many seconds."),
                  ("maxOutputCharacters", "Upper bound of returned output."),
                  ("maxCapturedCharactersPerStream", "Capture bound per stream; keep 8000 for long tools.")),
                """{"script":"Get-Service Talvora | Select-Object Status","timeoutSeconds":60}"""),
            ["talvora_git_status"] = new(
                "Show changed and untracked files of a repository.",
                P(("repositoryPath", "Absolute repository root."),
                  ("includeUntracked", "List untracked files."),
                  ("includeRaw", "Also return raw porcelain output.")),
                """{"repositoryPath":"C:\\Users\\tayla\\Talvora-MCP"}"""),
            ["talvora_git_diff"] = new(
                "Show a diff; prefer paths and a small contextLines to keep it short.",
                P(("repositoryPath", "Absolute repository root."),
                  ("staged", "Diff the index instead of the working tree."),
                  ("revisionRange", "e.g. HEAD~1..HEAD; empty = working tree."),
                  ("paths", "Limit to these relative paths."),
                  ("contextLines", "Unchanged lines around each change.")),
                """{"repositoryPath":"C:\\Users\\tayla\\Talvora-MCP","paths":["docs/codex/STATUS.md"],"contextLines":2}"""),
            ["talvora_git_log"] = new(
                "Show recent commits.",
                P(("repositoryPath", "Absolute repository root."),
                  ("revision", "Branch, tag or range; empty = HEAD."),
                  ("maxCommits", "Number of commits (keep small)."),
                  ("skip", "Skip this many commits."),
                  ("all", "Include all refs.")),
                """{"repositoryPath":"C:\\Users\\tayla\\Talvora-MCP","maxCommits":5}"""),
            ["talvora_git_run"] = new(
                "Run one git command with separate arguments, e.g. commit or push. Commits must end with a 'Card: <ID>' trailer and stage docs/codex/STATUS.md (work contract).",
                P(("repositoryPath", "Absolute repository root."),
                  ("arguments", "Array of separate git arguments, without the word git."),
                  ("environment", "Extra environment variables."),
                  ("explicitAdmin", "Bypass routing to the source editor; administration only."),
                  ("timeoutSeconds", "Kill git after this many seconds.")),
                """{"repositoryPath":"C:\\Users\\tayla\\Talvora-MCP","arguments":["push","origin","main"]}"""),
            ["talvora_http_request"] = new(
                "Send one HTTP request, e.g. GET http://127.0.0.1:7676/healthz.",
                P(("method", "GET, POST, PUT, PATCH or DELETE."),
                  ("url", "Absolute URL."),
                  ("headers", "Request headers {\"Name\":\"value\"}."),
                  ("body", "Text body."),
                  ("contentType", "Body media type, e.g. application/json."),
                  ("timeoutSeconds", "Request timeout."),
                  ("responseMode", "text, base64 or none."),
                  ("maxResponseBytes", "Upper bound of the captured response.")),
                """{"method":"GET","url":"http://127.0.0.1:7676/healthz"}"""),
            ["talvora_worklog_note"] = new(
                "Set the one-line Turkish task note on the user's desktop worklog card.",
                P(("text", "One plain Turkish sentence about the current task."),
                  ("workspaceRoot", "Absolute repository root the note belongs to.")),
                """{"text":"AP2 — yama aracının hata mesajları anlaşılır hale geliyor","workspaceRoot":"C:\\Users\\tayla\\Talvora-MCP"}"""),
            ["talvora_gh_run"] = new(
                "Run one GitHub CLI command as the signed-in user, with separate arguments.",
                P(("workingDirectory", "Absolute repository root."),
                  ("arguments", "Array of separate gh arguments, without the word gh."),
                  ("timeoutSeconds", "Kill gh after this many seconds.")),
                """{"workingDirectory":"C:\\Users\\tayla\\Talvora-MCP","arguments":["run","list","--limit","3"]}"""),
        };

    public static string? FailureHint(string toolName) =>
        All.TryGetValue(toolName, out var guide)
            ? $"How to call {toolName}: {guide.Usage} Example arguments: {guide.Example}"
            : null;
}
