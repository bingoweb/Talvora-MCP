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

## Audit fixes (external audits by Claude)

D-cards go to the top of the agent queue. Newest audit first.

### Audit 2026-11-08 (42, deep, whole project) · range `audit/2026-11-07-41..97344f7` plus every subsystem · result: D45–D50, live checks of D37, D40–D44 and OB1 closed

User request: "Talvora'yı derinlemesine denetle". Method: the range line by line against the cards (the literal-block cards compared file by file with Claude's audit 41 scratch clone), Claude's signed-in Full gate, CI, the runtime records since audit 41 (tray.log, Windows Application log, `/healthz/traffic`, the worklog history and the event store), the real Control Center read through UI Automation, then the subsystems audit 40 did not read line by line (source-edit commit and rollback core, interactive user runner, delivery and work-contract gates, HTTP download, registry store) and bug-class searches over `src` (culture-dependent casing, `async void`, sync-over-async).

Range: D40, D41, D42, D35, D36, D37, D38, D39, D43, D44 and OB1 match their cards (D35 and D40–D44 byte for byte with the verified blocks, OB1's reference hashes match, D37's 23 pairs exact); one commit per card, Turkish subjects, red-first evidence real (the Application log holds D38's red run at 22:23). Claude's signed-in Full 70/0 `Hepsi geçti.`; CI success on all 11 commits; installed 97344f7 at 23:24. Accepted deviation: D35 also moved the source-range marker in `tests/TunnelClientUpdateRollbackRegression.ps1`, because the card changed a signature that test pins (Claude's card gap, logged by the agent).

Live checks closed by Claude: D40 (Claude's Full gate left `worklog-sessions.jsonl` byte-identical and added only the Control Center smoke's own stage lines to tray.log; no IPC timeout since 15:24); D41 (a call through the installed bridge script is `core · Claude` on `/healthz/traffic` and a "Claude" worklog session; no smoke session since 17:33 across nine installs; the running Claude Desktop picks the header up at its next start); D42 (later installs ran the smoke green); D43 and D44 (captures in both themes; on the real Control Center the link opens Olaylar on "Onarımlar" and lists the repairs with "· 9 KEZ", "· 2 KEZ", "· 3 KEZ"); D37 (new texts live, "5 yönetilen hizmet kaydı yüklendi."); OB1 (the live note `40 Oturum Notları/2026-11-07 Talvora OB1.md` exists with the template properties, the secret note was refused and wrote nothing, CI 42410824919 success).

| # | Severity | Where | Finding (verified) | Card |
|---|---|---|---|---|
| 1 | HIGH (data truth, Claude's D43) | Çalışma günlüğü | A worklog session closes 90 s after its last step and `EndedAtUtc` is that close; D43 measures to it, so every real session shows at least "1 dk 30 sn" (one ping: 23:36:48 → 23:38:18; every session in the history has a 90–127 s tail). | D45 |
| 2 | MEDIUM (false failure) | `WorklogSubject`, desktop notifier | `Get-CiStatus.ps1` exits 2 while CI is still running (AGENTS.md 10b), and the agent checks right after every push: the session ends red with "GitHub denetimi okunamadı" (ChatGPT 23:26 after OB1; CI was green minutes later). Exit 1 (CI red) reads "okunamadı" too. | D46 |
| 3 | MEDIUM (Turkish) | Tray texts | D37 changed 23 event texts; 75 more on main surfaces (repair results that become events, tray balloons and menu, service states, chain rows) still say MCP, readiness, health, runtime, browser smoke, Secure MCP Tunnel, Control plane. Seen live: "Gitea MCP zaten hazır / Sağlık ve readiness kontrolleri …". The setup wizard's own texts follow the user's decision (OpenAI names in parentheses; 38 more rows). | D47 |
| 4 | LOW (consistency, Claude's OB1) | `NoteTools` | Notes and new folders are owned by `NT AUTHORITY\SYSTEM` (the live OB1 note); source edits keep the workspace owner. | D48 |
| 5 | MEDIUM (Turkish) | Registry names | The real registry (written 2026-09-30) says "Talvora Core Service" and "Gitea MCP", components "Secure MCP Tunnel", "Penpot local MCP"; the smoke fixtures use "Talvora Çekirdeği" and "Gitea", so the captures hid it. | D49 |
| 6 | PROCESS (Claude) | Card writing | D35 did not list a test that pins the signature it changed. Rule added to Claude's card checklist: grep `tests/` and the smokes for every signature and text a card changes and list those files (D47 lists two such tests). | — |
| 7 | TEST GAP | D44 smoke | The light-theme capture of the repairs page is empty: the smoke feeds the count, not the Olaylar list, and asserts only the page and the chip. The real page lists the repairs (UI Automation). | [C] with the Olaylar empty state |
| 8 | HIGH (data truth) | `WorklogHistoryStore`, session ids | One project and one actor keep one `SessionId` for all their sessions, and the history deduplicates by it: each new session replaces the previous one, so Çalışma günlüğü holds only the newest session per project and actor instead of every session of the last 7 days (a week of ChatGPT work on Talvora-MCP left one entry; Claude's 00:16 probe replaced the 23:36 one). | D50 |

Screen pass, design improvements: the Çalışma günlüğü detail leaves 40 px holes for an empty task note and an empty result line; Olaylar shows a blank page when a filter or a search has no events; "Ham günlüğü göster" keeps its text while the raw log is open; each Çalışma günlüğü card reads its outcome twice ("Düzenleme tamamlandı." and "Tamamlandı").

Checked and sound (no card): the source-edit commit core (revision checks before and after, `File.Replace` with a backup, flush, owner restore), the interactive user runner (protected run folder, reparse check, identity check, bounded output, whole-tree kill on timeout), the HTTP download tool (staging file, length check, hash, atomic replace), the delivery and work-contract gates (fail closed; they guard `talvora_git_run` only, by design: the agent's git goes through it), tray timers and `async void` handlers (all caught and logged), no culture-dependent `ToLower`/`ToUpper` in `src` (the culture-aware comparisons are the Turkish search and sort boxes).

Notes: "son 24 saatte 14 otomatik onarım" holds 3 pre-D42 "zaten hazır" events (old key) that leave the window at 03:59 on 11-08; every automatic repair happened between 20:07 (11-06) and 08:42 (11-07) during the heavy install period, none since. The 1 November smoke sessions still in the worklog history ("devws ğüş …", "fx", "kopya", "yeni alt", "uzun_klasör_adı_5_…", "Türkçe klasör ğüşİı") expire with the 7-day retention on 11-08; Claude deleted the fake "Project A" session from audit 41. Elevated Task Manager kept the foreground, so Claude drove the Control Center through UI Automation instead of clicks.

### D45 — Çalışma günlüğü measures a session up to its last step

**Why:** audit 42 finding 1 (data truth; Claude's D43). A worklog session closes 90 s after its last step and `EndedAtUtc` is that close, so every real session carries a 90–127 s quiet tail and D43 shows "1 dk 30 sn" for a ping that took a tenth of a second. The duration ends with the newest step (timeline times are step ends). The stored format does not change. The literal blocks below were built and run by Claude in a scratch clone (Release 0 warnings, Notification regression green, Full gate green).

**Files:** `src/Talvora.Tray/WorklogHistoryStore.cs`, `tests/Talvora.Notification.Regression/Program.D45.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** record the map's .NET entry with the one fact used (`Enumerable.Max` over `DateTimeOffset`), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). Create `Program.D45.cs`:

```csharp
using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D45: a session's duration ends with its last step, not with the card's quiet close 90 s later.</summary>
    private static void AssertD45WorkEndsWithLastStep()
    {
        var offset = TimeSpan.FromHours(3);
        var now = new DateTimeOffset(2026, 11, 7, 18, 0, 0, offset);
        var start = new DateTimeOffset(2026, 11, 7, 15, 44, 23, offset);
        var closed = start.AddSeconds(93);
        var directory = Path.Combine(Path.GetTempPath(), "talvora-d45-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new WorklogHistoryStore(Path.Combine(directory, "worklog-sessions.jsonl"), () => now);
            var frame = new DesktopWorklogFrame(
                "d45", 2, closed,
                new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"),
                new WorklogStep(WorklogStepCategory.SourceEdit, WorklogStepState.Succeeded,
                    "Düzenleme tamamlandı.", "1 dosya değişti", null, 3),
                [new WorklogTimelineEntry(start.AddSeconds(3), WorklogStepState.Succeeded, "Düzenleme tamamlandı.", 3)],
                new WorklogTotals(1, 93, 1, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                null, 15);
            store.AppendTerminal(new DesktopProgressMessage(
                "d45", "talvora_apply_patch", "Düzenleme tamamlandı.", "",
                DesktopProgressKind.Completed, closed, 3, null,
                WorklogFrame: frame, SessionClosed: true));
            var session = store.LoadRecent().Single(item => item.SessionId == "d45");
            if (session.WorkEndedAtUtc != start.AddSeconds(3) ||
                LocalTimeText.SessionShort(session.StartedAtUtc, session.WorkEndedAtUtc, now) != "Bugün 15:44 · 3 sn" ||
                LocalTimeText.SessionLong(session.StartedAtUtc, session.WorkEndedAtUtc, now) !=
                    "Bugün 15:44:23 → 15:44:26 · 3 sn")
                throw new InvalidOperationException(
                    $"D45: the work must end with the last step (15:44:26), got {session.WorkEndedAtUtc.ToOffset(offset):HH:mm:ss}.");
            if ((session with { Timeline = [] }).WorkEndedAtUtc != session.EndedAtUtc)
                throw new InvalidOperationException("D45: a session without steps keeps its stored end.");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        Console.WriteLine("D45_WORK_ENDS_WITH_LAST_STEP_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `AssertD45WorkEndsWithLastStep();` on its own line directly below `AssertD43SessionTimeText();`. Red: CS1061 (`WorkEndedAtUtc` does not exist yet).

Step 2. `WorklogHistoryStore.cs`: replace

```csharp
    // D43: the day once, then the time and the measured duration (the Olaylar page's day words).
    [JsonIgnore]
    public string ListTimeText => LocalTimeText.SessionShort(StartedAtUtc, EndedAtUtc, DateTimeOffset.Now);

    [JsonIgnore]
    public string DetailTimeText => LocalTimeText.SessionLong(StartedAtUtc, EndedAtUtc, DateTimeOffset.Now);
```

with

```csharp
    // D43: the day once, then the time and the measured duration (the Olaylar page's day words).
    // D45: the duration ends with the last step; the card closes 90 s later and that quiet tail is not work.
    [JsonIgnore]
    public string ListTimeText => LocalTimeText.SessionShort(StartedAtUtc, WorkEndedAtUtc, DateTimeOffset.Now);

    [JsonIgnore]
    public string DetailTimeText => LocalTimeText.SessionLong(StartedAtUtc, WorkEndedAtUtc, DateTimeOffset.Now);

    /// <summary>D45: the end of the newest step (timeline times are step ends), kept inside the session.</summary>
    [JsonIgnore]
    public DateTimeOffset WorkEndedAtUtc
    {
        get
        {
            if (Timeline is not { Count: > 0 })
            {
                return EndedAtUtc;
            }

            var last = Timeline.Max(entry => entry.TimestampUtc);
            return last < StartedAtUtc ? StartedAtUtc : last > EndedAtUtc ? EndedAtUtc : last;
        }
    }
```

**Don't:** change `EndedAtUtc`, the stored JSON, the 90-second close, the timeline rows or `LocalTimeText`.

**Acceptance:** `D45_WORK_ENDS_WITH_LAST_STEP_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy; installed Control Center smoke green; record `[~] visual check pending (Claude)` (Claude reads the real Çalışma günlüğü).

### D50 — Çalışma günlüğü keeps every session

**Why:** audit 42 finding 8 (data truth). The service gives one project and one actor the same `SessionId` for all their sessions (`SHA256(projectRoot + actor)` in `TalvoraDesktopWorklogState.GetOrCreateSession`, so the desktop card follows the project), and `WorklogHistoryStore` deduplicates by `SessionId` when it appends, loads and prunes: each new session of that project and actor replaces the previous one. DESIGN-WORKLOG §8 asks for every session of the last 7 days (up to 2,000). Live: a week of ChatGPT work on Talvora-MCP left exactly one "ChatGPT · Dev / Talvora-MCP" entry, and Claude's 00:16 probe replaced Claude's 23:36 one. A history entry becomes the `SessionId` plus its start; a repeated close of the same session stays one entry; a card click still opens the newest session of its id (the list is newest first). The literal blocks below were built and run by Claude in a scratch clone (Notification regression green, Full gate green).

**Files:** `src/Talvora.Tray/WorklogHistoryStore.cs`, `src/Talvora.Tray/ViewModels/WorklogHistoryViewModel.cs`, `tests/Talvora.Notification.Regression/Program.D50.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** record the map's .NET entry with the one fact used (`Enumerable.DistinctBy` keeps the first element of each key), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

Step 1 (red first). Create `Program.D50.cs`:

```csharp
using Talvora.Shared;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>D50: two sessions of the same project and actor are two history entries; a repeated close is one.</summary>
    private static void AssertD50HistoryKeepsEverySession()
    {
        var offset = TimeSpan.FromHours(3);
        var now = new DateTimeOffset(2026, 11, 7, 18, 0, 0, offset);
        var directory = Path.Combine(Path.GetTempPath(), "talvora-d50-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "worklog-sessions.jsonl");
            var store = new WorklogHistoryStore(path, () => now);
            void Close(DateTimeOffset start, string title)
            {
                var closed = start.AddSeconds(93);
                var frame = new DesktopWorklogFrame(
                    "d50-same-project-and-actor", 2, closed,
                    new WorklogSessionHeader("Talvora-MCP", "ChatGPT · Dev"),
                    new WorklogStep(WorklogStepCategory.SourceEdit, WorklogStepState.Succeeded, title, null, null, 3),
                    [new WorklogTimelineEntry(start.AddSeconds(3), WorklogStepState.Succeeded, title, 3)],
                    new WorklogTotals(1, 93, 1, WorklogCheckState.Unknown, WorklogCheckState.Unknown),
                    null, 15);
                store.AppendTerminal(new DesktopProgressMessage(
                    "d50", "talvora_apply_patch", title, "", DesktopProgressKind.Completed, closed, 3, null,
                    WorklogFrame: frame, SessionClosed: true));
            }

            Close(new DateTimeOffset(2026, 11, 7, 15, 0, 0, offset), "İlk oturum");
            Close(new DateTimeOffset(2026, 11, 7, 15, 10, 0, offset), "İkinci oturum");
            Close(new DateTimeOffset(2026, 11, 7, 15, 10, 0, offset), "İkinci oturum");
            foreach (var reader in new[] { store, new WorklogHistoryStore(path, () => now) })
            {
                var titles = reader.LoadRecent()
                    .Where(session => session.SessionId == "d50-same-project-and-actor")
                    .Select(session => session.Current.Title)
                    .ToArray();
                if (titles is not ["İkinci oturum", "İlk oturum"])
                    throw new InvalidOperationException(
                        $"D50: expected both sessions newest first, got [{string.Join(", ", titles)}].");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        Console.WriteLine("D50_HISTORY_KEEPS_EVERY_SESSION_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `AssertD50HistoryKeepsEverySession();` on its own line directly below `AssertD45WorkEndsWithLastStep();` (D45 adds that line). Red: `D50: expected both sessions newest first, got [İkinci oturum].`

Step 2. `WorklogHistoryStore.cs`: replace

```csharp
    [JsonIgnore]
    public bool HasCodePreview => !string.IsNullOrWhiteSpace(Evidence?.CodePreview);
```

with

```csharp
    [JsonIgnore]
    public bool HasCodePreview => !string.IsNullOrWhiteSpace(Evidence?.CodePreview);

    // D50: a project and an actor keep one SessionId across all their sessions (the desktop card follows it);
    // one history entry is that id plus its start, so every session of the last 7 days stays in the history.
    [JsonIgnore]
    public string HistoryKey =>
        SessionId + "@" + StartedAtUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
```

then replace the line `_sessions!.RemoveAll(item => item.SessionId == session.SessionId);` with `_sessions!.RemoveAll(item => item.HistoryKey == session.HistoryKey);`, in `EnsureLoaded` the line `.DistinctBy(row => row.Session.SessionId)` with `.DistinctBy(row => row.Session.HistoryKey)`, and in `Prune` the line `.DistinctBy(item => item.SessionId)` with `.DistinctBy(item => item.HistoryKey)`.

Step 3. `WorklogHistoryViewModel.cs` (a reload or a filter keeps the session the user chose): replace `var previousSessionId = SelectedSession?.SessionId;` with `var previousKey = SelectedSession?.HistoryKey;`; replace `if (previousSessionId is not null) SelectSession(previousSessionId);` with `if (previousKey is not null) SelectedSession = Sessions.FirstOrDefault(s => s.HistoryKey == previousKey);`; replace `var selectedId = SelectedSession?.SessionId;` with `var selectedKey = SelectedSession?.HistoryKey;`; replace

```csharp
        SelectedSession = selectedId is null ? null :
            Sessions.FirstOrDefault(s => s.SessionId == selectedId);
```

with

```csharp
        SelectedSession = selectedKey is null ? null :
            Sessions.FirstOrDefault(s => s.HistoryKey == selectedKey);
```

**Don't:** change the service's session id, the desktop card, `SelectSession(string)`, the retention, the bounds or the stored format. Sessions that were already replaced cannot come back; say so in the Log.

**Acceptance:** `D50_HISTORY_KEEPS_EVERY_SESSION_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy; installed Control Center smoke green; record `[~] live check pending (Claude): two sessions of the same project and actor both stay in Çalışma günlüğü`.

### D46 — A CI run that is still going is not a failed step

**Why:** audit 42 finding 2. `scripts/Get-CiStatus.ps1` exits 2 while the windows-ci run is still going (AGENTS.md step 10b: `[~] CI pending`), and the agent checks CI right after every push, so almost every card ends its worklog session with a red "GitHub denetimi okunamadı" step and a failed session; exit 1 (CI red) reads "okunamadı" too. Exit 2 becomes a finished step "GitHub denetimi sürüyor", exit 0 "GitHub denetimi geçti", exit 1 "GitHub denetimi başarısız". The literal blocks below were built and run by Claude in a scratch clone (Notification regression green, Full gate green).

**Files:** `src/Talvora/WorklogSubject.cs`, `src/Talvora/TalvoraDesktopProgressNarrative.cs`, `src/Talvora/TalvoraDesktopProgressNotifier.cs`, `tests/Talvora.Notification.Regression/Program.D46.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** `/modelcontextprotocol/csharp-sdk` — "CallToolResult StructuredContent JsonElement"; `/dotnet/docs` — "JsonElement.TryGetInt32"; record both queries.

#### Exact implementation

Step 1 (red first). Create `Program.D46.cs`:

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora;
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>D46: Get-CiStatus.ps1 exit 2 (CI still running) is a finished check; exit 1 is a failed one.</summary>
    private static async Task AssertD46CiStillRunningAsync()
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            ["executable"] = JsonSerializer.SerializeToElement(@"C:\Program Files\PowerShell\7\pwsh.exe"),
            ["arguments"] = JsonSerializer.SerializeToElement(
                new[] { "-NoProfile", "-File", "scripts/Get-CiStatus.ps1", "-TimeoutMinutes", "0" }),
        };
        foreach (var (exitCode, kind, title) in new[]
        {
            (2, DesktopProgressKind.Completed, "GitHub denetimi sürüyor"),
            (1, DesktopProgressKind.Failed, "GitHub denetimi başarısız"),
            (0, DesktopProgressKind.Completed, "GitHub denetimi geçti"),
        })
        {
            var observed = new ConcurrentQueue<DesktopProgressMessage>();
            var notifier = CreateNotifier(observed);
            try
            {
                var result = new CallToolResult
                {
                    Content = [],
                    StructuredContent = JsonSerializer.SerializeToElement(
                        new { exitCode, timedOut = false, standardOutput = "CI" }),
                };
                _ = await notifier.RunToolCallAsync("talvora_user_run", arguments,
                    _ => ValueTask.FromResult(result), CancellationToken.None, "/mcp/dev", "ChatGPT");
                var terminal = observed.LastOrDefault(message =>
                    message.Kind is DesktopProgressKind.Completed or DesktopProgressKind.Failed);
                if (terminal is null || terminal.Kind != kind || !terminal.Title.StartsWith(title, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"D46: exit {exitCode} must end as {kind} \"{title}\", got {terminal?.Kind} \"{terminal?.Title}\".");
            }
            finally
            {
                await notifier.DisposeAsync();
            }
        }
        Console.WriteLine("D46_CI_STILL_RUNNING_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `await AssertD46CiStillRunningAsync();` on its own line directly below `await AssertD41CallerIdentityAsync();`. Red: `D46: exit 2 must end as Completed "GitHub denetimi sürüyor", got Failed …`.

Step 2. `WorklogSubject.cs`: replace the line

```csharp
        ["Get-CiStatus.ps1"] = new("GitHub denetimi okunuyor", "GitHub denetimi okundu", "GitHub denetimi okunamadı"),
```

with

```csharp
        ["Get-CiStatus.ps1"] = new("GitHub denetimi okunuyor", "GitHub denetimi geçti", "GitHub denetimi başarısız"),
```

Step 3. `TalvoraDesktopProgressNarrative.cs`: directly above `    private static bool ResultContainsText<T>(` add

```csharp
    // D46: Get-CiStatus.ps1 exits 2 while the windows-ci run is still going (AGENTS.md step 10b).
    private static bool IsCiStillRunning<T>(OperationNarrative narrative, T result) =>
        string.Equals(narrative.Subject, "Get-CiStatus.ps1", StringComparison.OrdinalIgnoreCase) &&
        ResultExitCode(result) == 2;

    private static int? ResultExitCode<T>(T result)
    {
        try
        {
            var json = result is CallToolResult { StructuredContent: JsonElement structured }
                ? structured
                : JsonSerializer.SerializeToElement(result);
            return json.ValueKind == JsonValueKind.Object &&
                TryGetPropertyIgnoreCase(json, "exitCode", out var exitCode) &&
                exitCode.ValueKind == JsonValueKind.Number &&
                exitCode.TryGetInt32(out var code)
                    ? code
                    : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
```

Step 4. `TalvoraDesktopProgressNotifier.cs`, method `RunToolCallAsync`: replace

```csharp
            var failed = TryDescribeFailure(result);
            try
            {
```

with

```csharp
            var failed = TryDescribeFailure(result);
            // D46: a CI run that is still going is a finished check, never a failed step.
            var ciStillRunning = failed && IsCiStillRunning(narrative, result);
            if (ciStillRunning)
            {
                failed = false;
            }

            try
            {
```

then replace

```csharp
                else
                {
                    if (TryBuildDeferredCompletion(
                            narrative,
                            result,
                            out var deferredTitle,
                            out var deferredMessage))
```

with

```csharp
                else
                {
                    if (ciStillRunning)
                    {
                        TryEndWorklog(
                            operationId,
                            DesktopProgressKind.Completed,
                            "GitHub denetimi sürüyor",
                            "GitHub denetimi henüz bitmedi; sonucu bir sonraki adımda okunacak.",
                            terminalEvidence is null
                                ? null
                                : terminalEvidence with { Result = "Sonuç: GitHub denetimi sürüyor" },
                            stopwatch.Elapsed);
                    }
                    else if (TryBuildDeferredCompletion(
                            narrative,
                            result,
                            out var deferredTitle,
                            out var deferredMessage))
```

and in the `catch (Exception ex)` block right below replace the line

```csharp
                    failed ? narrative.Titles.Failed : narrative.Titles.Succeeded,
```

with

```csharp
                    failed ? narrative.Titles.Failed : ciStillRunning ? "GitHub denetimi sürüyor" : narrative.Titles.Succeeded,
```

**Don't:** change `Get-CiStatus.ps1` or its exit codes, AGENTS.md step 10b, the other scripts' titles or the failure classifier.

**Acceptance:** `D46_CI_STILL_RUNNING_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy; push and run the step-10b CI check once as usual; record `[~] live check pending (Claude): the CI check right after the push reads "GitHub denetimi sürüyor"` (Claude reads the worklog history).

### D48 — A vault note belongs to the vault's owner

**Why:** audit 42 finding 4 (consistency; Claude's OB1 reference file). The service runs as LocalSystem, so `talvora_note_write` leaves notes and the folders it creates owned by `NT AUTHORITY\SYSTEM` (live: `40 Oturum Notları/2026-11-07 Talvora OB1.md`), while source edits keep their workspace owner through `FileOwnership`. The literal blocks below were built and run by Claude in a scratch clone (SourceEdit regression green, Full gate green).

**Files:** `src/Talvora/Tools/NoteTools.cs`, `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.Notes.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs` — "FileSystemSecurity.SetOwner SecurityIdentifier"; record the query.

#### Exact implementation

Step 1. `SourceEditRegressionRunner.Notes.cs`: replace

```csharp
            await AssertNoteRefusedAsync(root, "a.md", "api_key=abcdef123456", "create", "OB1: secrets are refused.");
```

with

```csharp
            // D48: a note in a new folder; the folder and the note carry the vault's owner.
            await NoteTools.WriteAsync(root, "Yeni klasör/Alt/not.md", "# Not\n", "create");
            var vaultOwner = Talvora.SourceEditing.FileOwnership.TryGetOwner(root);
            Assert(vaultOwner is not null &&
                vaultOwner == Talvora.SourceEditing.FileOwnership.TryGetOwner(Path.Combine(root, "Yeni klasör")) &&
                vaultOwner == Talvora.SourceEditing.FileOwnership.TryGetOwner(Path.Combine(root, "Yeni klasör", "Alt", "not.md")),
                "D48: a new note and its new folders belong to the vault owner.");

            await AssertNoteRefusedAsync(root, "a.md", "api_key=abcdef123456", "create", "OB1: secrets are refused.");
```

Run as the signed-in user, a new note already belongs to that user, so this check cannot be red; the red evidence is the live OB1 note owned by `NT AUTHORITY\SYSTEM` (Claude, audit 42). Quote it in the Log.

Step 2. `NoteTools.cs`: replace

```csharp
        await AtomicFile.WriteAllTextAsync(
            fullPath,
            text,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken: cancellationToken);

        return new TalvoraNoteWriteResponse(Path.GetRelativePath(root, fullPath), mode, content.Length);
```

with

```csharp
        // D48: folders this note creates get the vault owner too (the service writes as LocalSystem).
        var createdDirectories = new List<string>();
        for (var directory = Path.GetDirectoryName(fullPath);
             directory is not null && directory.Length > root.Length && !Directory.Exists(directory);
             directory = Path.GetDirectoryName(directory))
        {
            createdDirectories.Add(directory);
        }

        await AtomicFile.WriteAllTextAsync(
            fullPath,
            text,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken: cancellationToken);

        // D48: the note belongs to the vault's owner, as source edits keep their workspace owner.
        if (Talvora.SourceEditing.FileOwnership.TryGetOwner(root) is { } owner)
        {
            foreach (var directory in createdDirectories)
            {
                Talvora.SourceEditing.FileOwnership.TrySetOwner(directory, owner);
            }

            Talvora.SourceEditing.FileOwnership.TrySetOwner(fullPath, owner);
        }

        return new TalvoraNoteWriteResponse(Path.GetRelativePath(root, fullPath), mode, content.Length);
```

**Don't:** change the refusals, the 64,000-character limit, `AtomicFile` or `FileOwnership`.

**Acceptance:** SourceEdit regression green (`ob1-vault-note-writer`); signed-in Full `Hepsi geçti.`; build and deploy; live on the installed runtime: `talvora_note_write {vaultRoot: "C:\\Users\\tayla\\Obsidian\\Taylan", path: "40 Oturum Notları/2026-11-07 Talvora OB1.md", content: "D48: not artık kasanın sahibine ait.\n", mode: "append"}` succeeds, and `(Get-Acl -LiteralPath 'C:\Users\tayla\Obsidian\Taylan\40 Oturum Notları\2026-11-07 Talvora OB1.md').Owner` run with `talvora_user_run` names the signed-in user, not `NT AUTHORITY\SYSTEM`; record both lines.

### D49 — Plain Turkish service names, also for older registries

**Why:** audit 42 finding 5. The real registry (`managed-mcps.json`, written 2026-09-30) names the core "Talvora Core Service" and Gitea "Gitea MCP", components "Secure MCP Tunnel", "Gitea MCP sunucusu", "Penpot local MCP", "Penpot MCP": the Hizmetler titles, the detail pages and every event built from `registration.DisplayName` ("Gitea MCP zaten hazır") show them, while the smoke fixtures already say "Talvora Çekirdeği" and "Gitea". New registries get the plain names from discovery; a registry written earlier is read with them (the file changes only when the tray saves it itself). The literal blocks below were built and run by Claude in a scratch clone (Notification regression green, Full gate green).

**Files:** `src/Talvora.Shared/ManagedMcpDisplayNames.cs` (new), `src/Talvora.Shared/ManagedMcpRegistryStore.cs`, `src/Talvora.Tray/ManagedMcpRecoveryDiscovery.cs`, `src/Talvora.Tray/ControlCenterSmoke.UI4.cs`, `tests/Talvora.Notification.Regression/Program.D49.cs` (new), `tests/Talvora.Notification.Regression/Program.cs`, `.context7/verification.json`.

**Context7:** `/dotnet/docs` — "C# record with expression nondestructive mutation"; record the query.

#### Exact implementation

Step 1 (red first). Create `Program.D49.cs`:

```csharp
using Talvora.Shared;

internal static partial class Program
{
    /// <summary>D49: registry names read in plain Turkish, also from a registry written with the old names.</summary>
    private static async Task AssertD49PlainServiceNamesAsync()
    {
        var registration = new ManagedMcpRegistration
        {
            Id = "gitea",
            DisplayName = "Gitea MCP",
            Description = "Yerel Gitea, Caddy, resmi Gitea MCP sunucusu ve güvenli tünel zinciri.",
            Endpoint = "http://127.0.0.1:8081/mcp",
            Components =
            [
                new ManagedMcpComponentRegistration { Id = "tunnel", DisplayName = "Secure MCP Tunnel", Kind = "scheduled-task" },
                new ManagedMcpComponentRegistration { Id = "caddy", DisplayName = "Caddy", Kind = "windows-service" },
            ],
        };
        var directory = Path.Combine(Path.GetTempPath(), "talvora-d49-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "managed-mcps.json");
            await ManagedMcpRegistryStore.WriteAsync(path, new ManagedMcpRegistryDocument { Mcps = [registration] });
            var read = (await ManagedMcpRegistryStore.TryReadAsync(path))?.Mcps.Single();
            if (read is null ||
                read.DisplayName != "Gitea" ||
                read.Description != "Yerel Gitea, Caddy, resmi Gitea araç sunucusu ve güvenli tünel zinciri." ||
                read.Components[0].DisplayName != "Güvenli tünel" ||
                read.Components[1].DisplayName != "Caddy" ||
                ManagedMcpDisplayNames.For("Talvora Core Service") != "Talvora Çekirdeği" ||
                ManagedMcpDisplayNames.For("Talvora Dev") != "Talvora Dev")
                throw new InvalidOperationException("D49: registry names must read in plain Turkish.");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        Console.WriteLine("D49_PLAIN_SERVICE_NAMES_GREEN");
    }
}
```

In `Program.cs` (`Main`, full run) add `await AssertD49PlainServiceNamesAsync();` on its own line directly below `await AssertD46CiStillRunningAsync();` (D46 adds that line). Red: CS0103 (`ManagedMcpDisplayNames` does not exist yet).

Step 2. Create `src/Talvora.Shared/ManagedMcpDisplayNames.cs`:

```csharp
namespace Talvora.Shared;

/// <summary>
/// D49: older registry names in plain Turkish (copy deck §9: no "MCP" or English on the main surfaces).
/// Applied whenever the registry is read, so a registry written before D49 shows the same names everywhere.
/// </summary>
public static class ManagedMcpDisplayNames
{
    private static readonly Dictionary<string, string> Plain = new(StringComparer.Ordinal)
    {
        ["Talvora Core Service"] = "Talvora Çekirdeği",
        ["Gitea MCP"] = "Gitea",
        ["Secure MCP Tunnel"] = "Güvenli tünel",
        ["Gitea MCP sunucusu"] = "Gitea araç sunucusu",
        ["Penpot local MCP"] = "Penpot yerel araç sunucusu",
        ["Penpot MCP"] = "Penpot araç sunucusu",
        ["Yerel Gitea, Caddy, resmi Gitea MCP sunucusu ve güvenli tünel zinciri."] =
            "Yerel Gitea, Caddy, resmi Gitea araç sunucusu ve güvenli tünel zinciri.",
        ["İsteğe bağlı yerel Penpot tasarım platformu ve resmi Penpot MCP sunucusu."] =
            "İsteğe bağlı yerel Penpot tasarım platformu ve resmi Penpot araç sunucusu.",
    };

    public static string For(string text) =>
        Plain.TryGetValue(text, out var plain) ? plain : text;

    public static ManagedMcpRegistration Normalize(ManagedMcpRegistration registration) =>
        registration with
        {
            DisplayName = For(registration.DisplayName),
            Description = For(registration.Description),
            Components = registration.Components
                .Select(component => component with { DisplayName = For(component.DisplayName) })
                .ToList(),
        };
}
```

Step 3. `ManagedMcpRegistryStore.cs`, method `TryReadAsync`: replace

```csharp
        ValidateDocument(document);
        return document;
```

with

```csharp
        ValidateDocument(document);
        // D49: a registry written before D49 keeps its old names on disk; every reader gets the plain ones.
        return document with { Mcps = document.Mcps.Select(ManagedMcpDisplayNames.Normalize).ToList() };
```

and in `TryReadRecoveryManifestsAsync` replace

```csharp
                ValidateRegistration(registration);
                registrations.Add(registration);
```

with

```csharp
                ValidateRegistration(registration);
                registrations.Add(ManagedMcpDisplayNames.Normalize(registration));
```

Step 4. `ManagedMcpRecoveryDiscovery.cs`, exact replacements:

- `DisplayName = "Talvora Core Service",` → `DisplayName = "Talvora Çekirdeği",` (1×)
- `DisplayName = "Secure MCP Tunnel",` → `DisplayName = "Güvenli tünel",` (2×)
- `DisplayName = "Gitea MCP",` → `DisplayName = "Gitea",` (1×)
- `Description = "Yerel Gitea, Caddy, resmi Gitea MCP sunucusu ve güvenli tünel zinciri.",` → `Description = "Yerel Gitea, Caddy, resmi Gitea araç sunucusu ve güvenli tünel zinciri.",` (1×)
- `DisplayName = "Gitea MCP sunucusu",` → `DisplayName = "Gitea araç sunucusu",` (1×)
- `"İsteğe bağlı yerel Penpot tasarım platformu ve resmi Penpot MCP sunucusu."` → `"İsteğe bağlı yerel Penpot tasarım platformu ve resmi Penpot araç sunucusu."` (1×)
- `DisplayName = "Penpot local MCP",` → `DisplayName = "Penpot yerel araç sunucusu",` (1×)
- `DisplayName = "Penpot MCP",` → `DisplayName = "Penpot araç sunucusu",` (1×)

The folder in `Path.Combine(programFiles, "Gitea MCP", "gitea-mcp.exe")` and the task names (`"Gitea MCP Server"`, `"Gitea MCP Tunnel"`, `"Talvora Penpot MCP"`) are real Windows names: never change them.

Step 5. `ControlCenterSmoke.UI4.cs`: replace `State(Registration("gitea", "Gitea MCP"), ControlCenterHealthState.Ready, "Hazır")` with `State(Registration("gitea", "Gitea"), ControlCenterHealthState.Ready, "Hazır")`.

**Don't:** rename ids, folders, scheduled tasks or services; change `ValidateRegistration` or the stored file.

**Acceptance:** `D49_PLAIN_SERVICE_NAMES_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy; installed Control Center smoke green; record `[~] visual check pending (Claude)`.

### D47 — Plain Turkish for the remaining Control Center texts

**Why:** audit 42 finding 3 (copy deck §9: no "MCP", "tunnel-client", "runtime", "control plane" on main surfaces). D37 changed 23 event texts; 75 more still reach Olaylar, the event slips, the tray balloons and menu, the Hizmetler states and the chain rows with MCP, readiness, health, runtime, browser smoke, Secure MCP Tunnel, Control plane or OAuth discovery (seen live: "Gitea MCP zaten hazır / Sağlık ve readiness kontrolleri onarım gerektiren bir arıza göstermedi."). The setup wizard and its errors add 38 rows under the user's decision of 2026-11-08 ("Adlar kalsın, cümleler Türkçe"): OpenAI's own names stay in parentheses inside Turkish sentences, "OpenAI Admin API anahtarı (Admin API key)", so the user finds the same name on OpenAI's pages. Claude applied the whole table in a scratch clone: the new test is red on the current source and green after it, D37's test stays green, Full gate green.

**Files:** the sixteen files in the table, all under `src/Talvora.Tray/` (`ControlCenterRepairService.cs`, `GiteaTrayClient.cs`, `ControlCenterDashboardService.cs`, `ControlCenterLifecycleService.cs`, `ControlCenterLifecycleService.Penpot.cs`, `ControlCenterWindow.Actions.cs`, `ControlCenterComponentHealthService.cs`, `ManagedMcpProtocolProbeService.cs`, `TrayApplicationContext.cs`, `ControlCenterSetupService.cs`, `ControlCenterWindow.Setup.cs`, `ControlCenterAdminCredentialStore.cs`, `ManagedMcpTunnelProvisioningService.cs`, `ManagedMcpTunnelProvisioningService.Pending.cs`, `BusinessTunnelClient.cs`, `Program.cs`), `tests/MainSurfaceTurkishRegression.ps1` (new, reference copy), `tests/GiteaProtocolReadinessRegression.ps1`, `tests/TunnelClientV016IntegrationSourceRegression.ps1`, `.context7/verification.json`.

**Context7:** none needed (strings only); record the map's .NET entry with one relevant fact (interpolation holes stay as they are), per CONTEXT7 PROTOCOL rule 1.

#### Exact implementation

The table is `docs/codex/reference/d47/texts.tsv` (read it with `talvora_read_source`): one row per change with the columns `file`, `count`, `match`, `old`, `new`. Replace `old` with `new`, character for character, exactly `count` times in that file. `match` = `literal` means the whole C# string literal, quotes included (`"Gitea MCP"` → `"Gitea"`), because the same words also stand inside longer strings and English `TrayLog.Write` lines that stay; `match` = `text` means the text inside a literal (holes such as `{registration.DisplayName}` stay as they are). A few texts end with a space because the literal goes on in the next line with `+`: keep that space. `GetFriendlySetupError` in `ControlCenterWindow.Setup.cs` picks its message by the substrings `tunnel ID`, `Admin API key`, `Runtime API key` and `temel kurulum`; every new text keeps the substring its old text had, and those four match strings do not change.

Step 1 (red first). Copy `docs/codex/reference/d47/MainSurfaceTurkishRegression.ps1` → `tests/MainSurfaceTurkishRegression.ps1` (reference copy, AGENTS.md rule 5; normalised SHA-256 in that folder's `SHA256SUMS.txt`). Run it: red `D47: old text still present: Sağlık ve readiness kontrolleri onarım gerektiren bir arıza göstermedi.`

Step 2. Apply the 113 rows of `texts.tsv`, one `talvora_apply_patch` per file.

Step 3. Two existing tests pin texts this card changes; update them in the same commit:
- `tests/GiteaProtocolReadinessRegression.ps1`: `$statusBody.Contains('Gitea MCP health açık, protokol hazır değil')` → `$statusBody.Contains('Gitea araç sunucusu açık, protokolü hazır değil')`;
- `tests/TunnelClientV016IntegrationSourceRegression.ps1`, in the three `Assert-Contains $componentHealth` lines: `'"Tunnel runtime"'` → `'"Tünel çalışma katmanı"'`, `'"Control plane"'` → `'"Yönetim katmanı"'`, `'"Tünel MCP gözlemi"'` → `'"Tünel gözlemi"'` (the assertion messages stay).

**Don't:** change `TrayLog.Write` or `ILogger` lines, exception types, event categories or dedup keys; `BuildTunnelRuntimeDetail` (technical evidence under "Teknik kanıt"); the four match strings in `GetFriendlySetupError`; the setup labels in `StepperCard.xaml` (Claude's [C] WL3); the other probe details in `ManagedMcpProtocolProbeService.cs`; `ManagedMcpRecoveryDiscovery.cs` (D49 owns its names).

**Acceptance:** `MAIN_SURFACE_TURKISH_GREEN` and `EVENT_TEXT_TURKISH_GREEN`; signed-in Full `Hepsi geçti.`; build and deploy; installed Control Center smoke green; record `[~] visual check pending (Claude)`.

### WL3 — Çalışma günlüğü and Olaylar polish (Claude)

**Why:** audit 42 screen pass; user decisions 2026-11-08 ("Sıraya koy, sonra yap" for the look fixes and the list mark; setup names in parentheses). Build after D45 and D50, which change the same page's data.

**Do (Claude, under the agent rules: smoke first, Full gate, deploy, captures in both themes, CI):**
1. `WorklogHistoryView.xaml`, detail column: the task note, the fact line, the evidence summary and the evidence result collapse when their text is empty (one `HistoryOptionalText` style on `Tv.Type.Body` with triggers `Text` = "" and `{x:Null}` → `Visibility` `Collapsed`), so no empty line leaves a 40 px hole.
2. `WorklogHistoryView.xaml`, list card: the bold outcome line and the time line become one Caption line: an 8 px dot (`Tv.Ready` completed, `Tv.Offline` failed, `Tv.TextMuted` cancelled, `Tv.Attention` running), the outcome word, " · ", `ListTimeText`. The card loses one line.
3. `EventsView.xaml`: when the filtered list holds no event, a centred `Tv.Type.Body` line in `Tv.TextMuted`: "Bu süzgeçte olay yok." (with search text: "Aramayla eşleşen olay yok.").
4. `EventsView.xaml`: while the raw log is open the toggle reads "Ham günlüğü gizle" (content and `AutomationProperties.Name`).
5. `StepperCard.xaml` setup labels: "Talvora Dev tünel kimliği (tunnel ID)", "Talvora Admin tünel kimliği (tunnel ID)", "OpenAI Admin API anahtarı (Admin API key)" (label and automation name; the placeholder "Admin API key" stays).
6. `ControlCenterSmoke.D44.cs`: after the click, assert that the Olaylar list holds the recovery event the smoke applied (feed it through the snapshot the Olaylar page reads); the light-theme capture of audit 42 showed an empty list.
