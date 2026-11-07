# Talvora — Codex Roadmap (task cards)

> **Only open cards live here** (user decision 2026-11-07). Claude removes finished cards and older audit sections at every audit; git keeps them (`git show audit/<tag>^:docs/codex/ROADMAP.md` is this file before that audit's cleanup; the first cleanup is `audit/2026-11-07-40`).
> Every card is **one agent task and one commit**. Order = `docs/codex/STATUS.md` → "Agent queue".
> Card template: **Why** (finding + evidence) · **Files** · **Do** · **Don't** · **Acceptance**.
> Language: this file is English. Everything Talvora shows to people (Control Center, tray, desktop worklog, error messages returned to users) is **ordinary Turkish** (AGENTS.md). The agent reports to the user in **Turkish**.
> Design specs: `docs/codex/DESIGN-SANTRAL.md` (Control Center, current), `docs/codex/DESIGN-WORKLOG.md` (desktop worklog), `docs/codex/DESIGN-CONTROL-CENTER.md` (Gözlemevi: information architecture, copy deck §9 and accessibility §10 stay valid). New UI and worklog cards are written in this file.

---

## General rules for every card

- AGENTS.md applies in full (Context7 gate, modern-by-default, delivery gates, desktop progress contract, `talvora_apply_patch` as primary editor).
- **Test first** (red), then code. Prefer **behavioural** tests. Do **not** add new regex-over-source assertions unless the card prescribes one; when you touch a file covered by a `*SourceRegression.ps1`, replace the affected assertions with behaviour where feasible, otherwise update them in the same commit and say so in the Log.
- **Definition of done** (every card):
  1. `dotnet restore Talvora.slnx --locked-mode` and the installer project restore pass **without** env overrides.
  2. `dotnet build Talvora.slnx -c Release` → 0 warnings, 0 errors (`TreatWarningsAsErrors`).
  3. Targeted tests for the card + the always-on set (`scripts/Invoke-QualityGate.ps1`, last line `Hepsi geçti.`).
  4. Runtime/packaging/shared-infrastructure cards: canonical installer (`scripts/Deploy-Windows-Installer.ps1`) + exact-installed smoke; record the installed `sourceCommit` from `/healthz`.
  5. `.context7/verification.json` holds this change only (AGENTS.md → CONTEXT7 PROTOCOL). There is no Awwwards evidence.
  6. `docs/codex/STATUS.md` updated (queue tick + one Log entry, English), one commit with trailer `Card: <ID>`; mirror the evidence to the active Gitea issue.
- User-facing strings: ordinary Turkish, no tool names / exit codes / stack traces / transport terms. Errors say what went wrong, what is protected, what happens next. Example: `"Bu istek Talvora'ya yerel bir uygulamadan gelmediği için reddedildi. Sistem güvende; bağlantınızı Talvora ayarlarından yeniden yapın."`
- Tool **descriptions** stay English (model-facing). Tool **titles** (human-facing in client approval dialogs) are short Turkish.
- Never print, copy or commit secrets. Never read files in `C:\ProgramData\Talvora\Secrets` content; listing names and ACLs is enough.
- Push as STATUS → Fixed facts → "Push" says.

---

## Phase OB — Obsidian vault (user request 2026-11-01)

The user installed Obsidian. Claude built one vault for every project at `C:\Users\tayla\Obsidian\Taylan` (local git history, no remote, nothing paid). Each project's `docs/` folder is linked into the vault as a directory junction named `Repo belgeleri`, so the repository stays the only source (AGENTS.md → `### OBSIDIAN VAULT`). Agents read notes with the existing `talvora_read_source` / `talvora_search_text` (the vault is a git folder, so it is a recognised workspace). OB1 adds the one missing piece: a write tool that refuses secrets and Templater code (`<%`, which the vault's Templater plugin could execute) by code.

### OB1 — `talvora_note_write`: write vault notes, refuse secrets and the linked repo docs

**Why:** user decision 2026-11-01 (ChatGPT and Claude read and write vault notes; no secret may enter the vault). Secret protection must be code, not convention.

**Files:**
- new `src/Talvora/Tools/NoteTools.cs` (reference copy)
- new `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.Notes.cs` (reference copy)
- `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.cs`
- `src/Talvora.Shared/FileLog.cs`
- `src/Talvora.Shared/TalvoraToolManifest.cs`
- `src/Talvora.Shared/TalvoraToolTitles.cs`
- `src/Talvora.Shared/TalvoraToolGuides.cs`
- `src/Talvora.Shared/TalvoraMcpToolSurfacePolicy.cs`
- `src/Talvora.Shared/TalvoraMcpToolMetadataPolicy.cs`
- `tests/Talvora.Smoke/SmokeScenarios.SurfacePolicy.cs`
- `tests/CodexCliIntegrationSourceRegression.ps1`
- `tests/FocusedMcpSurfaceSourceRegression.ps1`
- `README.md`, `docs/ARCHITECTURE.md`
- `.context7/verification.json`

**Context7:** `/modelcontextprotocol/csharp-sdk`: "McpServerTool async Task method with CancellationToken parameter binding, ModelContextProtocol 2.2.0" (the SDK binds the token; it is not part of the input schema). `/dotnet/docs`: "FileAttributes.ReparsePoint DirectoryInfo.Attributes junction detection .NET 10". Vendor: the two Official links in `docs/codex/CONTEXT7.md`.

#### Exact implementation

Step 1 (red first). Copy `docs/codex/reference/ob1/SourceEditRegressionRunner.Notes.cs` → `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.Notes.cs` (AGENTS.md rule 5 "Reference copies"; normalised SHA-256 `617e0451159036130bd64d11b0f130016380883654e8e5db1a8fe4cb34b3c0ab`). In `SourceEditRegressionRunner.cs`, directly after the line `("ap2-agent-patch-dialect", Ap2AgentPatchToleranceAsync),` add:

```csharp
            ("ob1-vault-note-writer", RunVaultNoteWriterAsync),
```

Build the test project: the red state is the compile error `CS0103` for `NoteTools`; quote it in the Log.

Step 2. Copy `docs/codex/reference/ob1/NoteTools.cs` → `src/Talvora/Tools/NoteTools.cs` (normalised SHA-256 `bac639e6a6d458d3ec48786829a9aee8e3a984915c047646e511c2e207c82ae6`).

Step 3. `src/Talvora.Shared/FileLog.cs`: insert directly before `    private static string FormatExceptionDetails(string value)`:

```csharp
    public static bool ContainsSensitiveData(string? value) =>
        !string.IsNullOrEmpty(value) &&
        !string.Equals(RedactSensitiveData(value), value, StringComparison.Ordinal);

```

Claude verified steps 1–3 in a scratch clone on 2026-11-01 (re-verified after adding the `<%` refusal): the test project builds with 0 warnings and `ob1-vault-note-writer` is green, including the real-junction refusal.

Step 4. `src/Talvora.Shared/TalvoraToolManifest.cs`: directly after `"talvora_worklog_note",` add `"talvora_note_write",`.

Step 5. `src/Talvora.Shared/TalvoraToolTitles.cs`: directly after `["talvora_worklog_note"] = "Görev satırını ayarla",` add:

```csharp
        ["talvora_note_write"] = "Kasaya not yaz",
```

Step 6. `src/Talvora.Shared/TalvoraToolGuides.cs`: directly before `["talvora_gh_run"] = new(` add:

```csharp
            ["talvora_note_write"] = new(
                "Create or append a Markdown note in the user's Obsidian vault; secret-looking text is refused.",
                P(("vaultRoot", "Absolute vault root, C:\\Users\\tayla\\Obsidian\\Taylan."),
                  ("path", "Vault-relative .md path, e.g. 40 Oturum Notları/2026-11-01 Talvora OB1.md."),
                  ("content", "Markdown text, at most 64000 characters, no secrets."),
                  ("mode", "create (new note) or append (existing note).")),
                """{"vaultRoot":"C:\\Users\\tayla\\Obsidian\\Taylan","path":"40 Oturum Notları/2026-11-01 Talvora OB1.md","content":"# OB1\n","mode":"create"}"""),
```

Step 7. Counts (the tool is Dev-only: it is in neither `DevelopmentExcludedTools` nor `AdministrationTools`; it is not open-world and not destructive):
- `TalvoraMcpToolSurfacePolicy.cs`: `ExpectedFullToolCount = 250` → `251`; `ExpectedDevelopmentToolCount = 213` → `214`. Admin `88` and shared `51` stay.
- `TalvoraMcpToolMetadataPolicy.cs`: `ExpectedReviewedToolCount = 250` → `251`. Open-world `85` and destructive `100` stay.
- `tests/CodexCliIntegrationSourceRegression.ps1` and `tests/FocusedMcpSurfaceSourceRegression.ps1`: the strings `'ExpectedFullToolCount = 250'` → `'ExpectedFullToolCount = 251'` and `'ExpectedDevelopmentToolCount = 213'` → `'ExpectedDevelopmentToolCount = 214'`.
- `README.md`: every Full count `250` → `251` and every Dev count `213` → `214` in the places `tests/ToolManifestDocumentationRegression.ps1` checks: badge text, badge URL, the summary line (`**251 total**, **214 Dev**, …`), the focused diagram line, the full diagram line, the `/mcp` and `/mcp/dev` table rows.
- `docs/ARCHITECTURE.md`: `/mcp` (**251 tools**), `/mcp/dev` (**214 tools**), "complete 251-tool capability set".

Step 8. `tests/Talvora.Smoke/SmokeScenarios.SurfacePolicy.cs`: directly after the statement that asserts `"talvora_worklog_note"` (it spans four lines: `AssertSurface(`, `"talvora_worklog_note",`, `development: true,`, `administration: false);`) add:

```csharp
        AssertSurface(
            "talvora_note_write",
            development: true,
            administration: false);
```

Step 9. Refresh `.context7/verification.json` with the two lookups above (replace, never append).

**Don't:**
- add read, search or list note tools (the existing tools cover reading);
- redact and then write: secret-looking content is refused whole, never partly written;
- relax the `<%` refusal (Templater in the vault executes such code; refuse, never escape);
- add folder checks beyond the reference file (the allowed folders are an AGENTS.md rule);
- run git in the vault, add a remote, or touch Obsidian settings;
- add a vault path setting or a default `vaultRoot` (the service runs as LocalSystem; the caller always passes it).

**Acceptance:**
- Red (CS0103), then green: `ob1-vault-note-writer` passes.
- Full gate `Hepsi geçti.` as the signed-in user (the Q7 dev payload budget included; if it is exceeded, stop with a proposal and never raise the budget yourself).
- Deploy; `/healthz.sourceCommit` = HEAD. Live check on the installed runtime: `talvora_note_write {vaultRoot: "C:\\Users\\tayla\\Obsidian\\Taylan", path: "40 Oturum Notları/<today as yyyy-MM-dd> Talvora OB1.md", content: <a session note with the template properties tür: oturum, proje: Talvora, yazan: ChatGPT, tarih: <today as yyyy-MM-dd>>, mode: "create"}` succeeds; the same tool with path `01 Gelen Kutusu/OB1 deneme.md` and content `api_key=abcdef123456` is refused with the Turkish secret message and creates no file.
- Push; CI once.

## Audit fixes (external audits by Claude)

D-cards go to the top of the agent queue. Newest audit first.

### Audit 2026-11-07 (41, full check) · range `audit/2026-11-07-40..1302b75` plus runtime records · result: D40, D41, D42, design improvements D43 and D44, rule for waiting checks, OB1, D35 and D38 card fixes, D34 closed

User request: "Talvora'yı yine tam kontrolden geçir.. saatlik kontrol nedeniyle takıldı kaldı chatgpt". Method: the range line by line, signed-in Full gate, CI, then the runtime records of the installed version (tray.log, Windows Application log, the live worklog history, `/healthz/traffic`), then every open card read the way the agent will.

Range: D30, D31, D29, D32, D33 and D34 match their cards line for line; one commit per card, Turkish subjects, STATUS in each commit, Context7 evidence real (D34: `/dotnet/docs` and two Microsoft Learn pages); signed-in Full 69/0 `Hepsi geçti.` (Claude, 15:21); CI success on every commit (latest 42371088200); installed 1302b75 at 15:09. D34 safety holds: the 15 s health probe (`GetTunnelDiagnosticStatesAsync` → `GetSnapshotCachedAsync`) drops the 10-minute ready status, so a dead tunnel is still noticed within about two rounds. D34 count (Claude, from tray.log): 22 `tunnel-client runtimes status` lines from 15:09:50 to 16:09:50 (limit 30; before 92), 9 of them from three smoke runs, otherwise one every 5 minutes (each tunnel every 10 minutes); slowest call 176 ms; tunnels ready on the Control Center. D34 closed.

| # | Severity | Where | Finding (verified) | Card |
|---|---|---|---|---|
| 1 | CRITICAL (workflow, fixed by Claude) | D34 Acceptance + AGENTS.md CONTINUE PROTOCOL step 8 | D34 asked for a count "after one hour of normal running", and step 8 makes a `[~] live pending` check the first task of the next message: every "Talvora'ya devam et" in that hour re-checked, found the hour unfinished and stopped (ChatGPT session 15:14–15:17: brief, pull, CI read, three counts, stop). Fixed: a check that needs a wait is Claude's (`[~] live pending (Claude)`, skipped by step 5); the card-writing rules forbid waiting acceptance steps; Claude ran D34's count. | — |
| 2 | HIGH (data truth) | Notification regression → `DesktopProgressNotificationPresenter`, `TrayLog` | Every gate run writes test data into the user's files: the presenter test leaves a finished "Project A · ChatGPT · Dev · Kod güncellendi" session in `worklog-sessions.jsonl` (rewritten at 15:24:17 by this audit's own gate), and the deliberately stalled client of `AssertPipeReadTimeoutRecoveryAsync` writes "Desktop progress IPC client timed out while sending a frame." into the real `tray.log` (one line per gate run; 15:24:05 in this audit's run). Audit 40 finding 4 counted these lines as real drops; the Application log shows no dropped frame since D33. | D40 |
| 3 | HIGH (data truth, user decided) | `TalvoraDesktopProgressNotifier.RunToolCallAsync`, `ResolveActor` | The installed smoke's deliberate failing calls (`Talvora.Smoke`) become ChatGPT sessions in the worklog ("ChatGPT · Dev — cmd.exe hata ile bitti", "ChatGPT · Yönetim — Arka plan işi başlat · tamamlanamadı", 13:53) and can open desktop cards: the actor comes from the surface path, D32 cleaned only the counters. User: never shown ("Hiç görünmesin"). | D41 |
| 4 | MEDIUM (data truth, user decided) | `Program.cs` call filter | Claude's calls through the Claude Desktop "Talvora Chat" bridge (mcp-remote, stateless HTTP) carry no client name and a generic User-Agent: the Santral shows "Diğer", the worklog "Talvora", and after D38 Claude's cards would be filtered as non-agent work (proven with two live pings: `core · Diğer`). The meter and the worklog also resolve the caller differently. User: "Köprü kendini tanıtsın". Claude added `--header X-Talvora-Client:Claude` to the bridge (installed copy under Claude Desktop's extensions and the source copy in `%LOCALAPPDATA%\Talvora\ClaudeDesktopChatExtension`, backups in its `backups` folder; a live call through the changed bridge succeeded); Talvora must read it. | D41 |
| 5 | MEDIUM (data truth) | `OverviewViewModel.RecoveryCount`, repair event keys | "son 24 saatte 14 otomatik onarım" was 11 repairs plus 3 "Gitea MCP zaten hazır" results: a recovery that finds the service already ready repairs nothing but is recorded as `recovery:gitea:success` and counted. | D42 |
| 6 | LOW (agent stop risk, fixed by Claude) | OB1, D38 card text | OB1 Step 8 quoted a one-line `AssertSurface(…)` statement that the file writes on four lines, and its live check wrote a note dated 2026-11-01; D38's non-agent test actor becomes "Diğer" after D41. Card texts fixed. | — |

Look-ahead: the anchors of D35–D39 match the current source (D35's two cache checks and `return tag;`, D36's catch line, all 23 D37 texts, D38's two test lines and presenter condition, D39's `RedactSensitiveText`); the OB1 reference files match `SHA256SUMS.txt`; Dev tools/list is 194,569 bytes of its 203,776 budget (OB1 adds one Dev tool). Checked and sound: the Application log's "Unknown tool" errors at 12:55 and 13:53 are the smoke's deliberate surface probes; the upstream probe postponed once at 15:29 and recovered on the next round.

### D40 — Tests never write into the user's Talvora files

**Why:** audit 41 finding 2. The Notification regression runs in the signed-in user's profile and writes into the real files: the presenter test's finished "Project A · ChatGPT · Dev · Kod güncellendi" session lands in `%LOCALAPPDATA%\Talvora\ControlCenter\worklog-sessions.jsonl` (Çalışma günlüğü lists it as real work) and test listeners write lines such as "Desktop progress IPC client timed out while sending a frame." into `%LOCALAPPDATA%\Talvora\Tray\tray.log`. Cause: `DesktopProgressNotificationPresenter` falls back to `new WorklogHistoryStore()` (the user's file), `TrayLog` writes to the user's log unless `TrayLog.PathOverrideForTests` is set, and `AssertRecoveryLogContract` resets that override to `null`.

**Files:** `src/Talvora.Tray/WorklogHistoryStore.cs`, `tests/Talvora.Notification.Regression/Program.cs`, `tests/Talvora.Notification.Regression/Program.D40.cs` (new), `.context7/verification.json`.

**Context7:** record the map's .NET entry with the one fact used (`Path.GetTempPath()` returns the current user's temporary folder), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). Create `Program.D40.cs`:

```csharp
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
```

In `Program.cs` (`Main`, full run) add `AssertD40TestIsolation();` on its own line directly above the `Console.WriteLine(` statement that prints `"DESKTOP_PROGRESS_BEHAVIOR_GREEN"`, so it runs after every test that could reset the override. Red: CS1061 (`PathName` does not exist yet).

Step 2. `WorklogHistoryStore.cs`: directly below `private List<WorklogHistorySession>? _sessions;` add

```csharp
    // D40: a test process points every default store at its own folder.
    internal static string? DefaultPathOverrideForTests { get; set; }

    internal string PathName => _path;
```

and in the constructor replace `_path = path ?? Path.Combine(` with `_path = path ?? DefaultPathOverrideForTests ?? Path.Combine(`.

Step 3. `Program.cs`:

(a) Insert as the first statements of `Main`, directly above `if (Environment.GetCommandLineArgs().Contains("--ui7-only", StringComparer.Ordinal))`:

```csharp
        // D40: this process never writes into the signed-in user's Talvora files (tray.log, worklog
        // history); child runs (--n8-only and the others) start here too and get their own folder.
        var isolatedRoot = Path.Combine(Path.GetTempPath(), "talvora-notification-" + Guid.NewGuid().ToString("N"));
        TrayLog.PathOverrideForTests = Path.Combine(isolatedRoot, "tray.log");
        WorklogHistoryStore.DefaultPathOverrideForTests = Path.Combine(isolatedRoot, "worklog-sessions.jsonl");
```

(b) In `AssertRecoveryLogContract`, directly below `var path = Path.Combine(root, "recovery.log");` add `var previousLogPath = TrayLog.PathOverrideForTests;`, and in its `finally` replace `TrayLog.PathOverrideForTests = null;` with `TrayLog.PathOverrideForTests = previousLogPath;`.

(c) Directly below the `AssertD40TestIsolation();` line from Step 1 add:

```csharp
        // D40: best effort; the folder exists only when a test wrote into it.
        try
        {
            if (Directory.Exists(isolatedRoot)) Directory.Delete(isolatedRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
```

**Don't:** change the Control Center smoke (the gate reads its stage lines from the real tray.log on purpose), the presenter, `TrayLog` itself or any existing assertion.

**Acceptance:** Notification regression green (`D40_TEST_ISOLATION_GREEN`); signed-in Full `Hepsi geçti.`; build and deploy. Live, inside this message: read the newest "Desktop progress IPC client timed out while sending a frame." time in `%LOCALAPPDATA%\Talvora\Tray\tray.log` and the `endedAtUtc` of the "Project A" session in `%LOCALAPPDATA%\Talvora\ControlCenter\worklog-sessions.jsonl` before and after the signed-in Full gate; both stay the same (paste the four values in the Log entry). Claude removes the old "Project A" session at the next audit.

### D41 — One caller name: Talvora's own calls leave no trace, Claude is named

**Why:** audit 41 findings 3 and 4, user decisions 2026-11-07 ("Hiç görünmesin", "Köprü kendini tanıtsın"). (1) The installed smoke's deliberate failing calls (`Talvora.Smoke`) become ChatGPT sessions in the worklog and can open desktop cards, because the worklog takes the actor from the surface path; D32 cleaned only the counters. (2) Claude's calls through the Claude Desktop "Talvora Chat" bridge arrive stateless, without a client name and with a generic User-Agent, so the Santral shows "Diğer" and the worklog "Talvora". Claude added `--header X-Talvora-Client:Claude` to the bridge; Talvora must read it. (3) The traffic meter falls back to the User-Agent and the worklog does not.

**Files:** `src/Talvora/Program.cs`, `src/Talvora/TalvoraLineTrafficMeter.cs`, `src/Talvora/TalvoraDesktopProgressNotifier.cs`, `src/Talvora/TalvoraDesktopWorklogState.cs`, `tests/Talvora.Notification.Regression/Program.D41.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/aspnetcore.docs` — "IHeaderDictionary indexer returns StringValues.Empty for a missing header; IHeaderDictionary.UserAgent"; record the query.

#### Exact implementation

Step 1 (red first). Create `Program.D41.cs`:

```csharp
using System.Collections.Concurrent;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>D41: one caller name; Talvora's own calls leave no trace; the bridge header names Claude.</summary>
    private static async Task AssertD41CallerIdentityAsync()
    {
        if (TalvoraLineTrafficMeter.ResolveCallerName("Claude", "bridge", "node") != "Claude" ||
            TalvoraLineTrafficMeter.ResolveCallerName(null, "Talvora.Smoke", "node") != "Talvora.Smoke" ||
            TalvoraLineTrafficMeter.ResolveCallerName(" ", null, "codex_cli_rs/0.1") != "codex_cli_rs/0.1" ||
            TalvoraLineTrafficMeter.ResolveCallerName(null, null, "") is not null ||
            !TalvoraLineTrafficMeter.IsOwnClient("Talvora.Smoke") || TalvoraLineTrafficMeter.IsOwnClient("Claude"))
            throw new InvalidOperationException("D41: the caller name order is header, client name, User-Agent.");

        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        var notifier = CreateNotifier(observed);
        try
        {
            _ = await notifier.RunToolCallAsync<int>("talvora_apply_patch", null,
                _ => ValueTask.FromResult(1), CancellationToken.None, "/mcp/dev", "Talvora.Smoke");
            if (!observed.IsEmpty)
                throw new InvalidOperationException("D41: Talvora's own calls must leave no desktop card or worklog session.");
            foreach (var (client, actor) in new[] { ("Claude", "Claude"), ("curl/8.9.1", "Diğer") })
            {
                _ = await notifier.RunToolCallAsync<int>("talvora_apply_patch", null,
                    _ => ValueTask.FromResult(1), CancellationToken.None, "/mcp", client);
                var named = observed.LastOrDefault(message => message.WorklogFrame is not null)
                    ?.WorklogFrame?.Header.ActorName;
                if (named != actor)
                    throw new InvalidOperationException($"D41: caller {client} must be named {actor}, got {named}.");
            }
        }
        finally
        {
            await notifier.DisposeAsync();
        }
        Console.WriteLine("D41_CALLER_IDENTITY_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `await AssertD41CallerIdentityAsync();` on its own line directly below `await AssertD25SessionRestartResetsTotalsAsync();`.

Step 2. `TalvoraLineTrafficMeter.cs`: directly above `public void Record(string? surfacePath, string? clientName, bool ok)` add

```csharp
    // D41: one caller name for the worklog and the traffic meter: the X-Talvora-Client header (the Claude
    // Desktop "Talvora Chat" bridge sends "Claude"), else the MCP client name, else the HTTP User-Agent.
    internal static string? ResolveCallerName(string? clientHeader, string? clientInfoName, string? userAgent) =>
        !string.IsNullOrWhiteSpace(clientHeader) ? clientHeader.Trim() :
        !string.IsNullOrWhiteSpace(clientInfoName) ? clientInfoName :
        string.IsNullOrWhiteSpace(userAgent) ? null : userAgent;

    // D41: Talvora's own clients (installed smoke, tray) are not activity anywhere (user decisions 2026-11-07).
    internal static bool IsOwnClient(string? clientName) =>
        clientName?.StartsWith("Talvora.", StringComparison.OrdinalIgnoreCase) == true;
```

and in `Record` replace `if (clientName?.StartsWith("Talvora.", StringComparison.OrdinalIgnoreCase) == true) return;` with `if (IsOwnClient(clientName)) return;`.

Step 3. `src/Talvora/Program.cs`, call-tool filter:

(a) Replace `var clientName = request.Server.ClientInfo?.Name;` with

```csharp
            var headers = request.Services?.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()
                ?.HttpContext?.Request.Headers;
            var clientName = TalvoraLineTrafficMeter.ResolveCallerName(
                headers?["X-Talvora-Client"].ToString(),
                request.Server.ClientInfo?.Name,
                headers?.UserAgent.ToString());
```

(b) In the `finally` block replace the four lines from `// Stateless MCP sessions often carry no client name; the HTTP User-Agent still says who called.` through `?.HttpContext?.Request.Headers.UserAgent.ToString(), ok);` (inclusive) with

```csharp
                request.Services?.GetService<TalvoraLineTrafficMeter>()?.Record(surfacePath, clientName, ok);
```

The `// SN1: …` comment line above them stays.

Step 4. `TalvoraDesktopProgressNotifier.cs`, `RunToolCallAsync`: directly below `ArgumentNullException.ThrowIfNull(operation);` add

```csharp
        // D41: Talvora's own clients leave no desktop card and no worklog session (user decision 2026-11-07).
        if (TalvoraLineTrafficMeter.IsOwnClient(clientName))
        {
            return await operation(cancellationToken);
        }
```

Step 5. `TalvoraDesktopWorklogState.cs`, `ResolveActor`: replace its last statement `return "Talvora";` with `return "Diğer";` (an unknown caller gets the Santral's name; Talvora's own calls never reach it after Step 4).

**Don't:** change `ResolveClient`, the surface paths, the D32 test, the worklog layout or any other user-facing text.

**Acceptance:** Notification regression green (`D41_CALLER_IDENTITY_GREEN`, `SN1_LINE_TRAFFIC_GREEN`); signed-in Full `Hepsi geçti.`; build and deploy; installed smoke green. Claude checks at the next audit (no agent step): a call through the Talvora Chat bridge shows `core · Claude` in `/healthz/traffic` and "Claude" in the worklog, and the installed smoke left no worklog session.

### D42 — "Zaten hazır" is not an automatic repair

**Why:** audit 41 finding 5. The overview line "son 24 saatte 14 otomatik onarım" counted 11 repairs and 3 "Gitea MCP zaten hazır" results: when a status check fails and the recovery then finds the service already ready, nothing is repaired, but the event is recorded under the same `recovery:<id>:success` key as a real repair, and `OverviewViewModel` counts every Info "recovery" event (D26 removed only the failed attempts).

**Files:** `src/Talvora.Tray/ControlCenterRepairService.cs`, `src/Talvora.Tray/TrayApplicationContext.cs`, `src/Talvora.Tray/ViewModels/OverviewViewModel.cs`, `tests/Talvora.Notification.Regression/Program.UI3.cs`, `.context7/verification.json`.

**Context7:** record the map's .NET entry with the one fact used (`string.EndsWith(string, StringComparison.Ordinal)`), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). `Program.UI3.cs`, `AssertD26RecoveryCount`: replace

```csharp
            Event(Talvora.Tray.ControlCenterEventSeverity.Error, "Talvora Dev: müdahale gerekiyor", 1),
        ]);
        Ui3Check(model.RecoveryCount == 2, "D26: only successful repairs count as otomatik onarım.");
```

with

```csharp
            Event(Talvora.Tray.ControlCenterEventSeverity.Error, "Talvora Dev: müdahale gerekiyor", 1),
            // D42: a recovery that found the service already ready repaired nothing.
            Event(Talvora.Tray.ControlCenterEventSeverity.Info, "Gitea MCP zaten hazır", 3) with
            {
                DedupKey = "recovery:gitea:already-ready",
            },
        ]);
        Ui3Check(model.RecoveryCount == 2, "D26/D42: only applied repairs count as otomatik onarım.");
        var ready = new Talvora.Tray.ManagedMcpDashboardState(core, Talvora.Tray.ControlCenterHealthState.Ready, "Hazır", "Hazır");
        var offline = ready with { Health = Talvora.Tray.ControlCenterHealthState.Offline };
        Ui3Check(
            !new Talvora.Tray.ManagedMcpRepairResult("Doğrulama", "", "", false, ready, ready).RepairApplied &&
            new Talvora.Tray.ManagedMcpRepairResult("Yeniden başlat", "", "", false, offline, ready).RepairApplied,
            "D42: only a repair that started from a non-ready state was applied.");
```

Red: CS1061 (`RepairApplied` does not exist yet).

Step 2. `ControlCenterRepairService.cs`: replace the last parameter line of the `ManagedMcpRepairResult` record, `    ManagedMcpDashboardState FinalState);`, with

```csharp
    ManagedMcpDashboardState FinalState)
{
    // D42: a recovery that found the service already ready applied no repair.
    public bool RepairApplied => InitialState.Health != ControlCenterHealthState.Ready;
}
```

Step 3. `TrayApplicationContext.cs` (each old text occurs once): in the Gitea recovery replace `"recovery:gitea:success");` with

```csharp
                    // D42: an already-ready result is recorded under its own key and never counted as a repair.
                    repair.RepairApplied ? "recovery:gitea:success" : "recovery:gitea:already-ready");
```

and in the managed-MCP recovery replace `$"recovery:{registration.Id}:success");` with

```csharp
                        // D42: an already-ready result is recorded under its own key and never counted as a repair.
                        repair.RepairApplied
                            ? $"recovery:{registration.Id}:success"
                            : $"recovery:{registration.Id}:already-ready");
```

Step 4. `OverviewViewModel.cs`, the `RecoveryCount` query: replace

```csharp
                string.Equals(entry.Category, "recovery", StringComparison.OrdinalIgnoreCase))
            .Sum(entry => entry.Count);
```

with

```csharp
                string.Equals(entry.Category, "recovery", StringComparison.OrdinalIgnoreCase) &&
                // D42: "zaten hazır" results repaired nothing.
                !entry.DedupKey.EndsWith(":already-ready", StringComparison.Ordinal))
            .Sum(entry => entry.Count);
```

**Don't:** change event titles or texts (D37 owns them), the dedup window, the repair logic, or the core recovery's `recovery:talvora:success` (it always repairs).

**Acceptance:** Notification regression green (`UI3_NARRATOR_GREEN`); signed-in Full `Hepsi geçti.`; build and deploy; installed Control Center smoke green.

### D43 — Çalışma günlüğü names the day once ("Bugün 15:44 · 3 sn")

**Why:** audit 41 design improvement (user: "Evet, yaz"). Every session shows its date twice ("07.11.2026 15:44 → 07.11.2026 15:44"), in the list and in the detail. The list names the day once, then the start time and the measured duration ("Bugün 15:44 · 3 sn"); the detail adds seconds and the end ("Bugün 15:44:23 → 15:44:26 · 3 sn"; across midnight both days are named). Day words are the Olaylar page's ("Bugün", "Dün", "5 Kasım Perşembe"; another year adds the year). The duration is the measured span `EndedAtUtc − StartedAtUtc`, never an estimate. The literal blocks below were built and run by Claude in a scratch clone (Release 0 warnings, Notification regression and Control Center smoke green).

**Files:** `src/Talvora.Tray/LocalTimeText.cs` (new), `src/Talvora.Tray/WorklogHistoryStore.cs`, `src/Talvora.Tray/Views/WorklogHistoryView.xaml`, `tests/Talvora.Notification.Regression/Program.D43.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs` — "custom date and time format strings d MMMM dddd with CultureInfo tr-TR"; record the query.

#### Exact implementation

Step 1 (red first). Create `Program.D43.cs`:

```csharp
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D43: a session names its day once, then its time and its measured duration.</summary>
    private static void AssertD43SessionTimeText()
    {
        var offset = TimeSpan.FromHours(3);
        var now = new DateTimeOffset(2026, 11, 7, 18, 0, 0, offset);
        var start = new DateTimeOffset(2026, 11, 7, 15, 44, 23, offset);
        var checks = new (string Actual, string Expected)[]
        {
            (LocalTimeText.SessionShort(start, start.AddSeconds(3.4), now), "Bugün 15:44 · 3 sn"),
            (LocalTimeText.SessionLong(start, start.AddSeconds(3.4), now), "Bugün 15:44:23 → 15:44:26 · 3 sn"),
            (LocalTimeText.SessionShort(start, start.AddSeconds(0.007), now), "Bugün 15:44 · 1 sn'den kısa"),
            (LocalTimeText.SessionShort(start.AddDays(-1), start.AddDays(-1).AddSeconds(292), now),
                "Dün 15:44 · 4 dk 52 sn"),
            (LocalTimeText.SessionLong(new DateTimeOffset(2026, 11, 6, 23, 58, 10, offset),
                new DateTimeOffset(2026, 11, 7, 0, 3, 2, offset), now), "Dün 23:58:10 → Bugün 00:03:02 · 4 dk 52 sn"),
            (LocalTimeText.SessionShort(start.AddDays(-2), start.AddDays(-2).AddSeconds(3780), now),
                "5 Kasım Perşembe 15:44 · 1 sa 3 dk"),
            (LocalTimeText.SessionShort(new DateTimeOffset(2025, 12, 31, 9, 5, 0, offset),
                new DateTimeOffset(2025, 12, 31, 9, 6, 0, offset), now), "31 Aralık 2025 Çarşamba 09:05 · 1 dk"),
        };
        foreach (var (actual, expected) in checks)
        {
            if (actual != expected)
                throw new InvalidOperationException($"D43: expected \"{expected}\", got \"{actual}\".");
        }
        Console.WriteLine("D43_SESSION_TIME_TEXT_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `AssertD43SessionTimeText();` on its own line directly below `await AssertD25SessionRestartResetsTotalsAsync();`. Red: CS0103 (`LocalTimeText` does not exist yet).

Step 2. Create `src/Talvora.Tray/LocalTimeText.cs`:

```csharp
using System.Globalization;

namespace Talvora.Tray;

/// <summary>D43: a session's day once, then its time and its measured duration (the Olaylar page's day words).</summary>
internal static class LocalTimeText
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"Bugün", "Dün", "5 Kasım Perşembe"; another year adds the year: "31 Aralık 2025 Çarşamba".</summary>
    internal static string Day(DateTime localDate, DateTime today) =>
        localDate == today ? "Bugün" :
        localDate == today.AddDays(-1) ? "Dün" :
        localDate.Year == today.Year ? localDate.ToString("d MMMM dddd", Turkish) :
        localDate.ToString("d MMMM yyyy dddd", Turkish);

    /// <summary>List row: "Bugün 15:44 · 3 sn".</summary>
    internal static string SessionShort(DateTimeOffset startUtc, DateTimeOffset endUtc, DateTimeOffset now)
    {
        var start = startUtc.ToOffset(now.Offset);
        return $"{Day(start.Date, now.Date)} {start.ToString("HH:mm", Turkish)} · {Duration(endUtc - startUtc)}";
    }

    /// <summary>Detail: "Bugün 15:44:23 → 15:44:26 · 3 sn"; a session across midnight names both days.</summary>
    internal static string SessionLong(DateTimeOffset startUtc, DateTimeOffset endUtc, DateTimeOffset now)
    {
        var start = startUtc.ToOffset(now.Offset);
        var end = endUtc.ToOffset(now.Offset);
        var endDay = end.Date == start.Date ? string.Empty : Day(end.Date, now.Date) + " ";
        return $"{Day(start.Date, now.Date)} {start.ToString("HH:mm:ss", Turkish)} → " +
            $"{endDay}{end.ToString("HH:mm:ss", Turkish)} · {Duration(endUtc - startUtc)}";
    }

    /// <summary>The measured span in whole seconds: "1 sn'den kısa", "3 sn", "4 dk 52 sn", "1 dk", "1 sa 3 dk".</summary>
    internal static string Duration(TimeSpan span)
    {
        var total = (long)Math.Round(Math.Max(0, span.TotalSeconds), MidpointRounding.AwayFromZero);
        if (total == 0) return "1 sn'den kısa";
        if (total < 60) return $"{total} sn";
        if (total < 3600) return total % 60 == 0 ? $"{total / 60} dk" : $"{total / 60} dk {total % 60} sn";
        var minutes = total / 60 % 60;
        return minutes == 0 ? $"{total / 3600} sa" : $"{total / 3600} sa {minutes} dk";
    }
}
```

Step 3. `WorklogHistoryStore.cs`: replace

```csharp
    [JsonIgnore]
    public string StartedLocal => StartedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    [JsonIgnore]
    public string EndedLocal => EndedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
```

with

```csharp
    // D43: the day once, then the time and the measured duration (the Olaylar page's day words).
    [JsonIgnore]
    public string ListTimeText => LocalTimeText.SessionShort(StartedAtUtc, EndedAtUtc, DateTimeOffset.Now);

    [JsonIgnore]
    public string DetailTimeText => LocalTimeText.SessionLong(StartedAtUtc, EndedAtUtc, DateTimeOffset.Now);
```

Step 4. `WorklogHistoryView.xaml`: in the list item template replace

```xml
                                    <StackPanel Orientation="Horizontal">
                                        <TextBlock Text="{Binding StartedLocal}" Style="{StaticResource Tv.Type.Caption}" />
                                        <TextBlock Text=" → " Style="{StaticResource Tv.Type.Caption}" />
                                        <TextBlock Text="{Binding EndedLocal}" Style="{StaticResource Tv.Type.Caption}" />
                                    </StackPanel>
```

with

```xml
                                    <TextBlock Text="{Binding ListTimeText}" Style="{StaticResource Tv.Type.Caption}" />
```

and in the detail column replace

```xml
                        <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                            <TextBlock Text="{Binding StartedLocal}" Style="{StaticResource Tv.Type.Caption}" />
                            <TextBlock Text=" → " Style="{StaticResource Tv.Type.Caption}" />
                            <TextBlock Text="{Binding EndedLocal}" Style="{StaticResource Tv.Type.Caption}" />
                        </StackPanel>
```

with

```xml
                        <TextBlock Text="{Binding DetailTimeText}" Style="{StaticResource Tv.Type.Caption}"
                                   Margin="0,8,0,0" />
```

**Don't:** change the timeline rows (`HH:mm:ss`), the Olaylar page, the stored format, the filters or any other text.

**Acceptance:** Notification regression green (`D43_SESSION_TIME_TEXT_GREEN`); signed-in Full `Hepsi geçti.`; build and deploy; installed Control Center smoke green; attach the `worklog-session` and `wl2-empty` captures of both themes; record `[~] visual check pending (Claude)`.

### D44 — The repair count opens Olaylar on "Onarımlar"

**Why:** audit 41 design improvement (user: "Evet, yaz"). "son 24 saatte N otomatik onarım" on the overview is a fact the user wants to check. It becomes a quiet link inside the status line: Mono text in `Sn.InkMuted` with a dotted `Sn.Brass` underline as wide as the text; on hover `Sn.Ink` and a solid underline; on keyboard focus the 2 px `Sn.Focus` ring. A click opens Olaylar with only the "Onarımlar" filter on (search and service filter cleared). Requires D42: its test lines are this card's anchor. The literal blocks below were built and run by Claude in a scratch clone (Release 0 warnings, Notification regression and Control Center smoke green; captures checked in both themes: the link stays on the status line and the Olaylar page opens on "Onarımlar").

**Files:** `src/Talvora.Tray/ViewModels/OverviewViewModel.cs`, `src/Talvora.Tray/Views/OverviewView.xaml`, `src/Talvora.Tray/Views/OverviewView.xaml.cs`, `src/Talvora.Tray/Views/EventsView.xaml`, `src/Talvora.Tray/Views/EventsView.xaml.cs`, `src/Talvora.Tray/Views/ShellWindow.UI3.cs`, `src/Talvora.Tray/ControlCenterSmoke.D44.cs` (new), `src/Talvora.Tray/ControlCenterSmoke.UI3.cs`, `tests/Talvora.Notification.Regression/Program.UI3.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs-desktop` — "ControlTemplate.Triggers IsKeyboardFocused Setter TargetName, and a Line whose X2 binds to ActualWidth with ElementName inside a template"; record the query.

#### Exact implementation

Step 1 (red first). `Program.UI3.cs`, `AssertD26RecoveryCount`: directly below the statement that ends with `"D42: only a repair that started from a non-ready state was applied.");` add

```csharp
        // D44: the repair count is its own link text; the summary keeps only the states.
        Ui3Check(model.RecoveryText == "son 24 saatte 2 otomatik onarım" &&
            !model.Summary.Contains("onarım", StringComparison.Ordinal),
            "D44: the repair count must leave the summary and become the link text.");
```

Red: CS1061 (`RecoveryText` does not exist yet).

Step 2. `OverviewViewModel.cs`:

(a) directly below `private bool _hasRecovery;` add `private string _recoveryText = string.Empty;`;

(b) directly below the `HasRecovery` property line add

```csharp
    // D44: the repair count is its own link text; it opens Olaylar on "Onarımlar".
    public string RecoveryText { get => _recoveryText; private set => SetProperty(ref _recoveryText, value); }
```

(c) directly below `HasRecovery = RecoveryCount > 0;` add `RecoveryText = HasRecovery ? $"son 24 saatte {RecoveryCount} otomatik onarım" : string.Empty;`;

(d) in the `Summary` array delete the line `HasRecovery ? $"son 24 saatte {RecoveryCount} otomatik onarım" : null,`.

Step 3. `OverviewView.xaml`: replace

```xml
                    <TextBlock Text="{Binding Summary}" Style="{StaticResource Tv.Type.Mono}"
                               Foreground="{DynamicResource Sn.InkMuted}" Margin="0,8,0,0" TextWrapping="Wrap"/>
```

with

```xml
                    <!-- D44: the repair count is a link to Olaylar → Onarımlar; on a narrow window it wraps to its own line. -->
                    <WrapPanel Margin="0,8,0,0">
                        <TextBlock Text="{Binding Summary}" Style="{StaticResource Tv.Type.Mono}"
                                   Foreground="{DynamicResource Sn.InkMuted}"/>
                        <TextBlock Text=" · " Style="{StaticResource Tv.Type.Mono}"
                                   Foreground="{DynamicResource Sn.InkMuted}"
                                   Visibility="{Binding HasRecovery, Converter={StaticResource OverviewBool}}"/>
                        <Button Click="OnShowRepairs" Cursor="Hand" FocusVisualStyle="{x:Null}" Margin="-4,-2,0,-2"
                                Visibility="{Binding HasRecovery, Converter={StaticResource OverviewBool}}"
                                AutomationProperties.Name="{Binding RecoveryText}"
                                AutomationProperties.HelpText="Olaylar sayfasını Onarımlar süzgeciyle açar">
                            <Button.Template>
                                <ControlTemplate TargetType="Button">
                                    <Border x:Name="Ring" BorderThickness="2" BorderBrush="Transparent"
                                            CornerRadius="2" Padding="2,0,2,1" Background="Transparent">
                                        <Grid>
                                            <TextBlock x:Name="Label" Text="{Binding RecoveryText}"
                                                       Style="{StaticResource Tv.Type.Mono}"
                                                       Foreground="{DynamicResource Sn.InkMuted}"/>
                                            <Line x:Name="Rule" X1="0" Y1="0.5" Y2="0.5" VerticalAlignment="Bottom"
                                                  X2="{Binding ActualWidth, ElementName=Label}"
                                                  Stroke="{DynamicResource Sn.Brass}" StrokeThickness="1"
                                                  StrokeDashArray="1 2" SnapsToDevicePixels="True"/>
                                        </Grid>
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsMouseOver" Value="True">
                                            <Setter TargetName="Label" Property="Foreground" Value="{DynamicResource Sn.Ink}"/>
                                            <Setter TargetName="Rule" Property="StrokeDashArray" Value="{x:Null}"/>
                                        </Trigger>
                                        <Trigger Property="IsKeyboardFocused" Value="True">
                                            <Setter TargetName="Ring" Property="BorderBrush" Value="{DynamicResource Sn.Focus}"/>
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </Button.Template>
                        </Button>
                    </WrapPanel>
```

Step 4. `OverviewView.xaml.cs`: directly below `public event Action? ShowEventsRequested;` add `public event Action? ShowRepairsRequested;`, and directly below the `OnShowEvents` method add `private void OnShowRepairs(object sender, RoutedEventArgs e) => ShowRepairsRequested?.Invoke();`.

Step 5. `EventsView.xaml`: replace `<WrapPanel Grid.Row="0" Grid.ColumnSpan="2" Margin="0,0,0,16">` with `<WrapPanel x:Name="CategoryChips" Grid.Row="0" Grid.ColumnSpan="2" Margin="0,0,0,16">`. `EventsView.xaml.cs`: directly below `internal void OpenRawLog() => Model.RawLogVisible = true;` add a blank line and

```csharp
    // D44: opens a category the way a click on its chip does; the search and the service filter are cleared.
    internal void ShowCategory(string label)
    {
        Model.SearchText = string.Empty;
        Model.SelectedServiceId = null;
        foreach (var chip in CategoryChips.Children.OfType<System.Windows.Controls.RadioButton>())
        {
            if (chip.Tag is string tag && tag == label)
                chip.IsChecked = true;
        }
    }
```

Step 6. `ShellWindow.UI3.cs`: directly below `_overviewView.ShowEventsRequested += () => _shellModel?.SelectPage(4);` add

```csharp
                // D44: the repair count opens Olaylar on "Onarımlar".
                _overviewView.ShowRepairsRequested += () =>
                {
                    _shellModel?.SelectPage(4);
                    _eventsView?.ShowCategory("Onarımlar");
                };
```

Step 7. Create `src/Talvora.Tray/ControlCenterSmoke.D44.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    /// <summary>D44: the overview's repair count is a link that opens Olaylar on "Onarımlar".</summary>
    private static void AssertD44RepairCountOpensRepairs(ShellWindow window, Views.OverviewView overview,
        ControlCenterDashboardSnapshot snapshot, string variant)
    {
        var now = DateTimeOffset.Now;
        overview.ApplySnapshot(snapshot, now,
        [
            new ControlCenterEventRecord
            {
                Severity = ControlCenterEventSeverity.Info, Category = "recovery", Title = "Talvora Admin onarıldı",
                Count = 2, McpId = "talvora-admin", DedupKey = "recovery:talvora-admin:success",
                FirstOccurredAtUtc = now.AddMinutes(-30), LastOccurredAtUtc = now.AddMinutes(-10),
            },
        ]);
        window.UpdateLayout();
        var link = EnumerateVisualElements(overview).OfType<System.Windows.Controls.Button>()
            .FirstOrDefault(button => AutomationProperties.GetName(button) == "son 24 saatte 2 otomatik onarım")
            ?? throw new InvalidOperationException("D44: the repair count is not a named link.");
        if (!link.IsVisible || overview.Model.Summary.Contains("onarım", StringComparison.Ordinal))
            throw new InvalidOperationException("D44: the repair count must be its own visible link.");
        SaveVisualSnapshotIfRequested(window, "d44-link-" + variant);
        link.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.UpdateLayout();
        if (window.ShellModel.SelectedIndex != 4 ||
            window.EventsContentHost.Content is not Views.EventsView events ||
            events.Model.SelectedCategory != "Onarımlar")
            throw new InvalidOperationException("D44: the repair count must open Olaylar on Onarımlar.");
        SaveVisualSnapshotIfRequested(window, "d44-repairs-" + variant);
        events.ShowCategory("Tümü");
        window.ShellModel.SelectPage(0);
        overview.ApplySnapshot(snapshot, now, []);
        window.UpdateLayout();
    }
}
```

and in `ControlCenterSmoke.UI3.cs` directly below `SaveVisualSnapshotIfRequested(window, "ui3-overview-" + variant);` add `AssertD44RepairCountOpensRepairs(window, overview, snapshot, variant);`.

**Don't:** change the count itself (D42 owns it), the other parts of the summary, the event slip, the Olaylar filter logic or any text.

**Acceptance:** Notification regression green (`UI3_NARRATOR_GREEN`); signed-in Full `Hepsi geçti.` (its Control Center smoke runs the D44 check); build and deploy; installed Control Center smoke green; attach the `d44-link-*` and `d44-repairs-*` captures of both themes; record `[~] visual check pending (Claude)`.

### D35 — The GitHub release lookup waits after a failure

**Why:** audit 40 finding 6.

**Files:** `src/Talvora.Tray/ManagedMcpTunnelProvisioningService.cs`, `tests/Talvora.Notification.Regression/Program.D35.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs` — "HttpMessageHandler SendAsync test double for HttpClient"; record the query.

#### Exact implementation

Step 1. Test seams: change `private static readonly HttpClient TunnelClientReleaseHttp = TalvoraHttp.CreateClient(` to `internal static HttpClient TunnelClientReleaseHttp { get; set; } = TalvoraHttp.CreateClient(` (its arguments and closing `);` stay), and `private static async Task<string> GetLatestTunnelClientTagAsync(` to `internal static async Task<string?> GetLatestTunnelClientTagAsync(`.

Step 2 (red first). Create `Program.D35.cs`:

```csharp
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D35: a failed GitHub release lookup waits 30 minutes instead of retrying every round.</summary>
    private static async Task AssertD35LatestBackoffAsync()
    {
        var handler = new D35FailingHandler();
        var original = ManagedMcpTunnelProvisioningService.TunnelClientReleaseHttp;
        ManagedMcpTunnelProvisioningService.TunnelClientReleaseHttp = new HttpClient(handler);
        try
        {
            var first = await ManagedMcpTunnelProvisioningService.GetLatestTunnelClientTagAsync(CancellationToken.None);
            var second = await ManagedMcpTunnelProvisioningService.GetLatestTunnelClientTagAsync(CancellationToken.None);
            if (first is not null || second is not null || handler.Requests != 1)
                throw new InvalidOperationException($"D35: expected one request and no tag, got {handler.Requests} requests.");
        }
        finally
        {
            ManagedMcpTunnelProvisioningService.TunnelClientReleaseHttp = original;
        }
        Console.WriteLine("D35_LATEST_BACKOFF_GREEN");
    }

    private sealed class D35FailingHandler : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            throw new HttpRequestException("D35 offline");
        }
    }
}
```

In `Program.cs` (`Main`, full run) add `await AssertD35LatestBackoffAsync();` on its own line directly below `await AssertD25SessionRestartResetsTotalsAsync();`.

Step 3. Add `private static DateTimeOffset _tunnelClientLatestFailedAtUtc;` directly below `_cachedTunnelClientLatestTag`.

Step 4. In `GetLatestTunnelClientTagAsync`, directly after each of the two 6-hour cache checks (the one before `await TunnelClientReleaseGate` and the one inside the `try` after `now = DateTimeOffset.UtcNow;`), insert:

```csharp
        // D35: after a failed lookup, ask GitHub again only after 30 minutes.
        if (now - _tunnelClientLatestFailedAtUtc < TimeSpan.FromMinutes(30))
        {
            return _cachedTunnelClientLatestTag;
        }
```

Step 5. In `GetLatestTunnelClientTagAsync` (the file has a second `using var request = new HttpRequestMessage(` in another method; leave that one alone), wrap the lines from its `using var request = new HttpRequestMessage(` to `return tag;` (inclusive) in:

```csharp
            try
            {
                // (the existing lines, unchanged)
            }
            catch (Exception ex) when (
                ex is HttpRequestException or InvalidDataException or JsonException ||
                (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // D35: one log line per failure window; the running client stays in use.
                _tunnelClientLatestFailedAtUtc = now;
                TrayLog.Write("tunnel-client latest release check failed; next try in 30 minutes", ex);
                return _cachedTunnelClientLatestTag;
            }
```

Step 6. In `StageLatestTunnelClientAsync`, directly after the `var latestTag = await GetLatestTunnelClientTagAsync(…).ConfigureAwait(false);` statement, insert:

```csharp
            // D35: no release known (GitHub unreachable): keep the running client.
            if (latestTag is null)
            {
                return config;
            }
```

**Don't:** change the 6-hour cache, the download path or the update journal.

**Acceptance:** Notification regression green (`D35_LATEST_BACKOFF_GREEN`); signed-in Full `Hepsi geçti.`; build and deploy.

### D36 — Atomic publication also retries a busy destination

**Why:** audit 40 finding 7.

**Files:** `src/Talvora.Shared/AtomicFile.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs` — "File.Replace UnauthorizedAccessException when the destination is in use or pending delete"; record the query.

#### Exact implementation

In `PublishDurable`, replace

```csharp
            catch (IOException) when (attempt < PublishAttempts && File.Exists(sourcePath))
```

with

```csharp
            // D36: a concurrent File.Replace or a delete-pending destination reports access denied, not IOException.
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) &&
                attempt < PublishAttempts && File.Exists(sourcePath))
```

**Don't:** change `PublishAttempts`, the delay or the replace/move order.

**Acceptance:** signed-in Full `Hepsi geçti.` (it runs the AtomicRace scenario); build and deploy.

### D37 — Event slips in plain Turkish

**Why:** audit 40 finding 8 (copy rule: no English, no "MCP", "tunnel-client", "runtime", "latest", "health" on main surfaces).

**Files:** `src/Talvora.Tray/ManagedMcpTunnelProvisioningService.ClientUpdate.cs`, `src/Talvora.Tray/ManagedMcpTunnelProvisioningService.cs`, `src/Talvora.Tray/TrayApplicationContext.cs`, `src/Talvora.Tray/ControlCenterSetupService.cs`, `tests/EventTextTurkishRegression.ps1` (new).

**Context7:** none needed (strings only); record the map's .NET entry with one relevant fact, per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Replace each old text with the new one, character for character (interpolation holes stay as they are). Each old text occurs exactly once in the four files, except `Gitea MCP zinciri sağlıklı ve kullanıma hazır.`, which occurs twice in `TrayApplicationContext.cs`: replace both. TrayLog lines, event categories and dedup keys stay unchanged.

| Old | New |
|---|---|
| `{registration.DisplayName} tunnel-client güncellendi` | `{registration.DisplayName} bağlantı istemcisi güncellendi` |
| `OpenAI tunnel-client {candidateConfig.TunnelClientVersion} sürümüne readiness doğrulamasından sonra atomik olarak geçirildi.` | `Güvenli bağlantı istemcisi {candidateConfig.TunnelClientVersion} sürümüne geçti; hazır olduğu doğrulandıktan sonra devreye alındı.` |
| `{registration.DisplayName} tunnel-client update recovered` | `{registration.DisplayName} yarım kalan güncelleme tamamlandı` |
| `Interrupted update recovered with {journal.CandidateConfig.TunnelClientVersion}; runtime readiness was verified before commit.` | `Yarıda kesilen güncelleme {journal.CandidateConfig.TunnelClientVersion} sürümüyle tamamlandı; sürüm hazır olduğu doğrulandıktan sonra devreye alındı.` |
| `{registration.DisplayName} tunnel-client update geri alındı` | `{registration.DisplayName} güncellemesi geri alındı` |
| `Interrupted candidate {journal.CandidateConfig.TunnelClientVersion} readiness doğrulamasını geçemedi; previous client {journal.PreviousConfig.TunnelClientVersion} yeniden çalıştırıldı.` | `Yeni sürüm {journal.CandidateConfig.TunnelClientVersion} hazırlık denetimini geçemedi; önceki sürüm {journal.PreviousConfig.TunnelClientVersion} yeniden çalıştırıldı.` |
| `{registration.DisplayName} tunnel-client update toparlandı` | `{registration.DisplayName} yarım güncelleme toparlandı` |
| `Incomplete update journal was rolled back to {journal.PreviousConfig.TunnelClientVersion}.` | `Yarım kalan güncelleme geri alındı; {journal.PreviousConfig.TunnelClientVersion} sürümü kullanılıyor.` |
| `Runtime profili oluşturuldu ve health/ready doğrulaması geçti.` | `Bağlantı profili oluşturuldu ve sağlık denetimi geçti.` |
| `Talvora MCP otomatik olarak düzeltildi` | `Talvora otomatik olarak düzeltildi` |
| `Yerel servis ile Dev ve Admin güvenli MCP tünelleri yeniden hazır.` | `Yerel servis ile Dev ve Admin güvenli bağlantıları yeniden hazır.` |
| `Talvora Dev ve Admin güvenli MCP tünelleri yeniden bağlandı.` | `Talvora Dev ve Admin güvenli bağlantıları yeniden kuruldu.` |
| `Gitea MCP otomatik kurtarma denemesi başarısız` | `Gitea otomatik kurtarma denemesi başarısız` |
| `Gitea MCP sağlık bakımı başarısız` | `Gitea sağlık bakımı yarıda kaldı` |
| `Gitea MCP yeniden başlatıldı` | `Gitea yeniden başlatıldı` |
| `Gitea MCP zinciri sağlıklı ve kullanıma hazır.` | `Gitea ve bağlantı zinciri sağlıklı, kullanıma hazır.` |
| `Gitea MCP yeniden başlatılamadı` | `Gitea yeniden başlatılamadı` |
| `{registration.DisplayName} tunnel-client güncellemesi ertelendi` | `{registration.DisplayName} bağlantı istemcisi güncellemesi ertelendi` |
| `Mevcut çalışan sürüm korunuyor; latest kontrolü daha sonra yeniden denenecek.` | `Çalışan sürüm korunuyor; yeni sürüm denetimi daha sonra yeniden yapılacak.` |
| `{registry.Mcps.Count} yönetilen MCP kaydı yüklendi.` | `{registry.Mcps.Count} yönetilen hizmet kaydı yüklendi.` |
| `Yönetilen MCP kayıtları veya otomatik kurtarma hazırlanamadı.` | `Yönetilen hizmet kayıtları ya da otomatik kurtarma hazırlanamadı.` |
| `Mevcut güvenli MCP tünelleri bağlandı.` | `Mevcut güvenli bağlantılar kuruldu.` |
| `Eksik güvenli MCP tüneli yapılandırması hazırlandı.` | `Eksik güvenli bağlantı ayarları hazırlandı.` |

Red first: create `tests/EventTextTurkishRegression.ps1` (UTF-8). This source-text check is prescribed on purpose, an exception to the general rule against new regex-over-source assertions: the texts are built at many call sites and only their wording changes. It reads the four source files above into one string; holds two arrays, `$old` with every text of the left column and `$new` with every text of the right column, as single-quoted PowerShell strings; throws `"D37: old event text still present: <text>"` for any old text it finds and `"D37: new event text missing: <text>"` for any new text it does not find; and ends with `Write-Output 'EVENT_TEXT_TURKISH_GREEN'`.

**Acceptance:** `EVENT_TEXT_TURKISH_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy.

### D38 — "Yalnızca hatalar ve ajan işleri" counts ChatGPT as an agent

**Why:** audit 40 finding 9; user decision 2026-11-07 (ChatGPT is the main agent).

**Files:** `src/Talvora.Tray/DesktopProgressNotificationPresenter.cs`, `tests/Talvora.Notification.Regression/Program.N8.cs`, `.context7/verification.json`.

**Context7:** record the map's .NET entry with the one fact used (`string.StartsWith(string, StringComparison.Ordinal)`), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). In `Program.N8.cs`:
- replace `presenter.Publish(Message("filtered", "ChatGPT", DesktopProgressKind.Completed, terminal: true));` with `presenter.Publish(Message("filtered", "Diğer", DesktopProgressKind.Completed, terminal: true));` (an unknown caller is the non-agent case; D41 names it "Diğer");
- directly below `claudeCard.Close();` insert:

```csharp
                // D38: ChatGPT is an agent too (user decision 2026-11-07).
                presenter.Publish(Message("chatgpt-dev", "ChatGPT · Dev", DesktopProgressKind.Started));
                if (Window("chatgpt-dev") is not { } chatgptCard)
                    throw new InvalidOperationException("D38: ChatGPT agent card filtered.");
                chatgptCard.Close();
```

Step 2. In `PublishCore`, replace `message.WorklogFrame?.Header.ActorName is not ("Codex" or "Claude")` with `!IsAgentActor(message.WorklogFrame?.Header.ActorName)` and add to the class:

```csharp
    // D38: agents are Codex, Claude and ChatGPT (ChatGPT · Dev, ChatGPT · Yönetim) — user decision 2026-11-07.
    private static bool IsAgentActor(string? actor) =>
        actor is "Codex" or "Claude" ||
        actor?.StartsWith("ChatGPT", StringComparison.Ordinal) == true;
```

**Don't:** change the setting's text, its default or the history behaviour.

**Acceptance:** Notification regression green; signed-in Full `Hepsi geçti.`; build and deploy.

### D39 — Penpot error text uses the shared secret masking

**Why:** audit 40 finding 10.

**Files:** `src/Talvora/Tools/PenpotMcpTools.cs`, `tests/Talvora.Notification.Regression/Program.D39.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** none needed beyond the map's .NET entry (one fact), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). Create `Program.D39.cs`:

```csharp
using Talvora.Tools;

internal static partial class Program
{
    /// <summary>D39: Penpot error text goes through the shared secret masking too.</summary>
    private static void AssertD39PenpotRedaction()
    {
        var text = PenpotMcpTools.RedactSensitiveText(
            "Penpot failed: userToken=abc123 Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl");
        if (text.Contains("abc123", StringComparison.Ordinal) ||
            text.Contains("c2lnbmF0dXJl", StringComparison.Ordinal))
            throw new InvalidOperationException("D39: Penpot error text still shows a secret.");
        Console.WriteLine("D39_PENPOT_REDACTION_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `AssertD39PenpotRedaction();` on its own line directly below `await AssertD25SessionRestartResetsTotalsAsync();`.

Step 2. In `PenpotMcpTools.cs` replace

```csharp
    private static string RedactSensitiveText(string value) =>
        UserTokenPattern.Replace(value, "$1<redacted>");
```

with

```csharp
    // D39: Penpot's own token mask plus the shared masking (keys, Authorization, JWT).
    internal static string RedactSensitiveText(string value) =>
        Talvora.Shared.FileLog.RedactSensitiveData(UserTokenPattern.Replace(value, "$1<redacted>"));
```

**Acceptance:** Notification regression green (`D39_PENPOT_REDACTION_GREEN`); signed-in Full `Hepsi geçti.`; build and deploy.
