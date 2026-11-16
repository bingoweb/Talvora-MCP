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
- **Security stays proportionate** (user, 2026-11-14): Talvora is a local MCP its owner runs on their own computer by their own choice. A card adds a security or privacy guard only against a concrete, likely accident and never where it makes an ordinary tool task harder; no extra refusals, confirmations or restrictions "just in case". A guard that breaks a normal task is a bug. The agent never adds a guard a card does not name.
- Push as STATUS → Fixed facts → "Push" says.

---

## Audit fixes (external audits by Claude)

D-cards go to the top of the agent queue. Newest audit first.

### Audit 2026-11-16 (65) · range `audit/2026-11-16-64..cfe584b` (D95 by ChatGPT) · user: the whole code, bugs and the desktop card, not the Control Center · new agent cards D98, D100, D99, D101, D102, D103, PK5; Claude's `[C]` D104v

### D98 — The card keeps each session's code terminal when another session's frame comes in between

**Why:** audit 65, reproduced twice. The service sends a session's code preview once; later frames of that session carry only its id (`CodeChangeId`, N3b). The tray's `DesktopProgressCodeState` (Talvora.Shared) remembers **one** session: the first frame of any other session resets it, so the next id-only frame of the first session finds nothing, and the card's code terminal (the user's favourite feature) disappears until the next code change. The session's history entry loses its code too, because the history store reads the restored message. Scratch test against `Talvora.Shared`: session A with a preview → A id-only (restored) → one frame of B → A id-only (**not** restored). Live on the installed cfe584b, Claude: `talvora_apply_patch` adding a file in this repository (the card showed the code), `talvora_run_powershell` without a folder (another session, 14:45:07), then a failing `talvora_user_run` in this repository: the card came back without its code terminal, and the stored history entry `9639eb64…` (11:46:46 UTC) has `codeChangeId: cff7a55f…` but no `codePreview`. 19 of the 99 stored sessions overlap another session in time (ChatGPT with Codex or Claude, an agent's own folder-less calls), so this happens in ordinary work.

**Files:** `src/Talvora.Shared/DesktopProgressCodeState.cs`, `src/Talvora.Tray/DesktopProgressNotificationPresenter.cs` (only if reading the code time has to change), `tests/Talvora.Notification.Regression/Program.D98.cs` (new), `tests/Talvora.Notification.Regression/Program.cs` (`--d98-only` and the whole run, registered like D95), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** `tests/Talvora.Notification.Regression/Program.N3bCode.cs` (calls `Restore` and reads `CodeChangedUtc`; it must stay green unchanged).

**Do:**
1. Red first: `Program.D98.cs` feeds `DesktopProgressCodeState.Restore` with worklog frames: (a) A with a preview and id `c1`, then B without code, then A with id `c1` only → A's restored message and frame carry A's preview, and `CodeChangedUtc` is A's change time; (b) A with `c1`, B with its own preview `c2`, A id-only → A's preview, then B id-only → B's preview; (c) A with a frame that carries no id → A's code is cleared (today's rule) and B's is kept.
2. Keep the latest code evidence and its change time **per session** (the frame's `SessionId`, otherwise `OperationId`), for at most 16 sessions; the least recently used session is dropped first. For one session, `Restore` keeps today's rules exactly. `CodeChangedUtc` keeps meaning "the code time of the session of the message last passed to `Restore`" (N3b reads it that way).

**Don't:** change the service (it sends a preview once on purpose), the protocol, the history store, or any of the eight N5 files pinned by `docs/codex/reference/worklog-phosphor/SHA256SUMS.txt`.

**Context7:** `Context7: gerekmiyor (in-memory tray state; no external API)` → `noLookup`.

**Acceptance:** red first; the whole Notification regression green with N3b unchanged; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI. Mark `[~] live check pending (Claude)`: Claude repeats the three calls above at the next audit and reads the stored `codePreview`.

### D100 — A call without a working folder joins the same agent's open card

**Why:** audit 65; user decision 2026-11-16 "Ajanın açık kartına katılsın" (DESIGN-WORKLOG §4.1). `ResolveProject` (`TalvoraDesktopWorklogState.cs`) puts every call that names no folder into the actor's `"global"` session, so one agent splits its own work into two cards: 21 of the 99 stored sessions have no project, most of them Claude's `talvora_run_powershell` calls and ChatGPT's `cmd` calls made while a project session was open. Each such call takes the card ("0:talvora*") from the project and, until D98, empties its code terminal.

**Files:** `src/Talvora/TalvoraDesktopWorklogState.cs` (`ResolveSessionIdentity`, `GetOrCreateSession`, `TryFindOpenSession`), `tests/Talvora.Notification.Regression/Program.D100.cs` (new) and `Program.cs` (registered), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** `tests/ModernizationPolicyRegression.ps1` (`GetOrCreateSession`, `_activeWorklog`, `session.Failure`, `session.Cancellation` and `SHA256.HashData` must stay in `TalvoraDesktopWorklogState.cs`, and its `Assert-NotContains` lines must stay true); the Notification regression (D25 session reset, D41 own clients, D50 one id per project and actor).

**Do:**
1. Red first through `RunToolCallAsync` with a delivery observer and a short `sessionIdleInterval`: (a) ChatGPT · Dev `talvora_apply_patch` with `workspaceRoot` = a temporary folder that has a `.git` folder, then `talvora_run_powershell` with no folder argument → both steps carry the same `SessionId` and project name, and the second step is in that session's timeline; (b) a folder-less call with no open session → the actor's `"global"` session, as today; (c) a folder-less Claude call while only ChatGPT's session is open → Claude's own `"global"` session; (d) ChatGPT's session idle longer than the interval → the folder-less call goes to `"global"`.
2. When a call's arguments name no folder (none of the keys `ResolveProject` reads) and no `projectOverride` is given, use the actor's open session: among the same actor's sessions that are not closed and either have an active item or a `LastActivityUtc` within the idle interval, the one with the newest `LastActivityUtc`. Otherwise keep today's `"global"` key. Quiet calls (`TryFindOpenSession`) follow the same rule, so a folder-less read keeps that session alive.

**Don't:** change how a call that names a folder finds its project, the session id formula, the idle interval or the actor names; never put a call into another actor's session.

**Context7:** `Context7: gerekmiyor (Talvora's own session bookkeeping)` → `noLookup`.

**Acceptance:** red first; the Notification regression and `ModernizationPolicyRegression.ps1` green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI. Mark `[~] live check pending (Claude)`.

### D99 — The card changes session only when a step starts or ends

**Why:** audit 65; user decision 2026-11-16 "Adım başı/sonunda geçsin" (DESIGN-WORKLOG §3 rules and §4.1). With one visible card (`MaximumVisibleCardsPerLane = 1`), `PublishCore` (`DesktopProgressNotificationPresenter.cs`) creates a window for every frame of a session that has none, and `TrimVisibleCards` closes the other window. With two active sessions (an agent's card while Claude audits; a followed gate's progress every 2 s next to another session's step) the card closes and reopens for every heartbeat of the other session, each time with the enter animation, a retyped title and its code terminal rebuilt. Seen live on cfe584b: Claude's folder-less command replaced the project card, and the next step replaced it back.

**Files:** `src/Talvora.Tray/DesktopProgressNotificationPresenter.cs`; one small new tray class for the decision if that keeps it testable without WPF (for example `src/Talvora.Tray/WorklogCardFocus.cs`); `tests/Talvora.Notification.Regression/Program.D99.cs` (new) and `Program.cs` (registered); `docs/codex/STATUS.md`; `.context7/verification.json`. **Tests that read these files:** `tests/ModernizationPolicyRegression.ps1` pins these texts in `DesktopProgressNotificationPresenter.cs`; keep every one of them true: `MaximumVisibleCardsPerLane = 1`, `OrderByDescending(static window => window.IsTerminal)` (keep `TrimVisibleCards` as the safety net), `_automaticScreenDeviceName`, `ApplyHeightConstraint`, `JsonFileStore.ReadBounded`, `desktop-progress-placement.json`, `PlacementDocumentVersion = 2`, `HasUserSize`, `WidthDip`, `HeightDip`, `ApplyPreferredSize`, and no `CaptureAutomaticPreferredSize`. The whole Notification regression.

**Do:**
1. Red first on the decision (a pure function or small class): card shows A, A running; a `Running` frame of B → the card stays on A; B `Started` → B takes the card; A `Running` → stays on B; A `Completed` → A takes it; a `SessionClosed` frame of B → stays; with no card on screen any accepted frame opens it; frames of the session on the card always update it.
2. Rule (DESIGN-WORKLOG §4.1): a frame of another session takes the card only when its `Kind` is `Started`, `Completed`, `Failed` or `Cancelled` and `SessionClosed` is false. A frame that does not take the card still goes through `_codeState.Restore` and the history store exactly as today (both run before the presentation decision) and leaves the window untouched.
3. Taking the card reuses the window on screen: key it to the new session, use that session's code-preview choice, and update it with the frame (the title types in, DESIGN-WORKLOG §7b). No close and no new window. The `Closed` handler uses the window's current `SessionId`, never the id it was created with; manual close still suppresses only the session that was on the card (§5.1). A reused window whose `WindowState` is `Minimized` is set back to `Normal` before the update (audit 65 live check: a minimized card is a 160×28 strip with no taskbar button to restore it).

**Don't:** change `DesktopProgressNotificationWindow.cs` or any other N5 file pinned by `SHA256SUMS.txt` (Claude's `[C]` D105v handles the window itself), the lifetimes, stale detection, placement, pinning, the N8 switches or the one-card budget.

**Context7:** `Context7: gerekmiyor (tray-only presentation rule)` → `noLookup`.

**Acceptance:** red first; the Notification regression green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI. Mark `[~] visual check pending (Claude)`: Claude watches the real card with two sessions at the next audit.

### D101 — A step Talvora still follows never shows "Yanıt bekleniyor"

**Why:** audit 65, code path. A followed GitHub check (`TryStartFollowedCi` → `WorklogFollower.FollowCiAsync`) polls every 30 s for up to 30 min but sends no frame after its first one, and that frame carries the actor's `ExpectedNextUpdateSeconds` (15 for ChatGPT). 30 s later the window's stale check (`2 × ExpectedNextUpdateSeconds`) shows "Yanıt bekleniyor · ChatGPT … yeni bilgi göndermedi" although Talvora itself is following GitHub; the card stays so until the check ends. A followed deploy has the same gap while the service is replaced: the measured hand-over took 33.7 s (audit 63, "Yeni sürüm çalışıyor · 20141a7"), longer than ChatGPT's 30 s budget. DESIGN-WORKLOG §5.2 now says a followed step never shows "Yanıt bekleniyor".

**Files:** `src/Talvora/WorklogFollower.cs`, `src/Talvora/TalvoraDesktopWorklogState.cs` (the frame's `ExpectedNextUpdateSeconds`), `tests/Talvora.Notification.Regression/Program.D101.cs` (new) and `Program.cs` (registered), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** the Notification regression's WL6, D46 (CI still running), D82 and deploy follow tests; `Program.WL6.cs` calls `WorklogFollower.FollowCiAsync` in five places, so keep them compiling and green (an optional progress parameter is enough); `tests/ModernizationPolicyRegression.ps1`.

**Do:**
1. Red first with scaled-down intervals: a followed CI check whose fake check answers "still running" twice and then success → one progress frame after each poll, title "GitHub denetimi sürüyor", the fact "GitHub denetimi sürüyor · {measured time}" (today's first fact, `WorklogSubject.FormatDuration`), and each such frame's `ExpectedNextUpdateSeconds` at least the poll interval plus the check's own time limit. The deploy hand-over frame from `TryPersistFollowedDeployAsync` carries at least 120.
2. Give `FollowCiAsync` a progress callback like `FollowProcessAsync`; `TryStartFollowedCi` reports each poll through `TryUpdateFollowedWorklog`. While a followed CI step or a deploy hand-over is the session's current step, its frames carry 120 (30 s poll + the check's 90 s limit; the measured hand-over was 34 s); every other frame keeps the actor's value.

**Don't:** change the poll interval, the limits, the titles or results of the follow, the stale rule in the window (an N5 file) or the actors' own values.

**Context7:** `Context7: gerekmiyor (Talvora's own follow loop)` → `noLookup`.

**Acceptance:** red first; the Notification regression green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI. Mark `[~] live check pending (Claude)`: Claude watches a real followed CI check on the card.

### D102 — The task note survives a session that reopens

**Why:** audit 65, code path (`TalvoraDesktopWorklogState.cs`). `TrySetTaskNote` (`talvora_worklog_note`) calls `GetOrCreateSession`, which resets a closed or idle session (timeline, steps, note) but leaves its `LastActivityUtc` old. The note is set, yet the next step finds the session idle again and resets it a second time, which wipes the note. The same call also publishes the reset session with the previous session's title and terminal state; the tray keeps that frame as the open session's last step and later stores it in the history as an unfinished session with 0 steps. It happens when the note is the first call after more than 90 s of quiet and the next call is a step.

**Files:** `src/Talvora/TalvoraDesktopWorklogState.cs` (`TrySetTaskNote`), `tests/Talvora.Notification.Regression/Program.D102.cs` (new) and `Program.cs` (registered), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** as D100.

**Do:**
1. Red first with a short idle interval: one step, wait past the interval until the session closes, `talvora_worklog_note`, then at once another step → that step's frames carry the note, and no frame with 0 steps and the old title is delivered.
2. A note counts as activity: it refreshes `LastActivityUtc` and, when no step is active, schedules the session's close as the end of a step does. When setting the note reopened the session, publish nothing until its first step (the note shows with that step); otherwise publish as today.

**Don't:** change the note's normalisation, its 140-character limit, the idle interval or the quiet-call rules.

**Context7:** `Context7: gerekmiyor (Talvora's own session bookkeeping)` → `noLookup`.

**Acceptance:** red first; the Notification regression and `ModernizationPolicyRegression.ps1` green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI.

### D103 — A long PowerShell error keeps its whole message

**Why:** audit 65, measured. D95 takes the thrown message from the one `|` line after PowerShell's `~~~` marker. PowerShell 7 wraps a long message over several `|` lines when stderr is redirected; measured with pwsh 7.6.10, a 230-character `throw` text came as three `|` lines, so the card's "sonuç" stops mid-sentence ("…olusturulamadi;") without any mark. D95 itself is verified live (audit 65: "Sonuç: Denetim 65 için bilinçli hata: beklenen sonuç bu satır").

**Files:** `src/Talvora/TalvoraDesktopProgressEvidence.cs` (`SelectFailedCommandErrorLine` only), `tests/Talvora.Notification.Regression/Program.D95.cs` (one more case), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** the Notification regression (D95's six cases keep their results), `tests/ModernizationPolicyRegression.ps1` (its four checks on `TalvoraDesktopProgressEvidence.cs`).

**Do:**
1. Red first: a seventh D95 case, the measured `standardError` of `throw "<230 characters>"` (C# literal, copy it exactly):
   ```
   "\u001B[31;1mException: \u001B[0mC:\\repo\\tests\\Long.ps1:1\u001B[0m\r\n\u001B[31;1m\u001B[0m\u001B[36;1mLine |\u001B[0m\r\n\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m   1 | \u001B[0m \u001B[36;1mthrow \"Bu uzun hata iletisi bilerek yazildi: derleme adiminda ikinci \u001B[0m \u0007\u001B[0m\r\n\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m     | \u001B[31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\u001B[0m\r\n\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[31;1m\u001B[31;1m\u001B[36;1m     | \u001B[31;1mBu uzun hata iletisi bilerek yazildi: derleme adiminda ikinci proje bulunamadi ve kurulum paketi olusturulamadi;\u001B[0m\r\n\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[31;1m\u001B[31;1m\u001B[36;1m\u001B[31;1m\u001B[36;1m     | \u001B[31;1mlutfen proje yolunu, surum numarasini ve calisma klasorunu yeniden kontrol edin, ardindan komutu tekrar\u001B[0m\r\n\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[31;1m\u001B[31;1m\u001B[36;1m\u001B[31;1m\u001B[36;1m\u001B[31;1m\u001B[36;1m     | \u001B[31;1mcalistirin.\u001B[0m\r\n"
   ```
   → `Evidence.Result` exactly "Sonuç: Bu uzun hata iletisi bilerek yazildi: derleme adiminda ikinci proje bulunamadi ve kurulum paketi olusturulamadi; lutfen proje yolunu, surum numarasini ve calisma klasorunu yeniden kontrol edin, ardindan komutu tekrar calistirin."
2. In D95's PowerShell rule, after the marker take every following line that starts with `|` (each without the `|` and the spaces after it) up to the first line that does not, joined with one space. Everything else stays as D95 wrote it, including the redaction and the 420-character limit.

**Don't:** change the order of D95's other rules, any title, the plain reason or the N5 files.

**Context7:** `Context7: gerekmiyor (Talvora's own worklog text; the measured output is in this card)` → `noLookup`.

**Acceptance:** red first; the Notification regression and `ModernizationPolicyRegression.ps1` green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI.

### PK5 — Roslyn 5.12.0 from Dependabot PR #22

**Why:** audit 65. Dependabot's open PR #22 ("Bump the nuget-stable group with 2 updates") moves `Microsoft.CodeAnalysis.CSharp.Workspaces` and `Microsoft.CodeAnalysis.Workspaces.MSBuild` from 5.9.0 to 5.12.0 but rewrites only `src/Talvora/packages.lock.json`, so its CI run 43796981937 failed in locked restore (NU1004 in both regression test projects). DEP1's script applies such a PR the Talvora way (modern-by-default).

**Files:** `Directory.Packages.props`, every `packages.lock.json` the script regenerates, `THIRD-PARTY-NOTICES.md` only if `tests/ThirdPartyNoticesSourceRegression.ps1` asks for it, `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** `tests/PlatformPackageLockstepRegression.ps1`, `tests/ThirdPartyNoticesSourceRegression.ps1`, `tests/InstallerDependencyProvenanceRegression.ps1`, the SourceEdit regression (semantic rename through `MSBuildWorkspace`), the installed smoke's semantic edit step.

**Do:** run `scripts/Apply-DependabotPr.ps1 -Pr 22` detached, as its header says, until `artifacts/dependabot/pr-22.result` reads `READY`. Context7 `/dotnet/roslyn` (map: `docs/codex/CONTEXT7.md`), one precise query each, about the two calls Talvora relies on in 5.12: creating an `MSBuildWorkspace` (`RoslynMsBuildBootstrap.cs`) and `Renamer.RenameSymbolAsync` (`RoslynSemanticEditEngine.cs`). Then commit and push as usual and close PR #22 with `talvora_gh_run` and the comment "Yerelde uygulandı: <kısa commit>". If the semantic edit tests or the installed smoke's semantic step fail, revert and stop with a proposal: do not pin other packages and do not edit the engine.

**Don't:** merge the PR on GitHub, take any other Dependabot change, or upgrade beyond 5.12.0.

**Acceptance:** the script's Full gate and a signed-in Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes (semantic step green); CI; PR #22 closed.

### D104v — Each "geçmiş" row shows its "ne"; numbers keep their units (`[C]`, Claude)

**Why:** audit 65 screen pass (renders of the real card XAML and screenshots of the live card on cfe584b); user decision 2026-11-16 "Her geçmiş satırında". (1) Since D92 every timeline entry carries its step's purpose, but the card's "geçmiş" rows show only the label: the second most common label, "Komut tamamlandı" (37 stored steps), says nothing on the only screen the user watches. (2) Titles and rows break between a number and its word ("Talvora.Tray.csproj · 3 / hata", "GitTools.cs ve 1 / dosya") and after the slash of "origin/main". (3) A one-step session prints "ara 0,0 sn".

**Plan (Claude, look only):** under each "geçmiş" label a quiet second line `ne  …` (DESIGN-WORKLOG §7b History row); no-break joins between a number and the word after it and inside short tokens such as `origin/main` in the title, the history labels and the fact line (display only; stored text unchanged); the ruler's "ara" only from 1 s up. `WorklogTimelineRow` carries the purpose; a preview fixture with purposes; the reference copies and `SHA256SUMS.txt` of the N5 files follow. Files: `src/Talvora.Tray/Worklog/WorklogCardView.xaml`, `WorklogCardViewModel.cs`, `WorklogCardMapper.cs`, `WorklogPreviewScenarios.cs`, `docs/codex/reference/worklog-phosphor/**`, DESIGN-WORKLOG §7b. The agent never takes this line.

### D105v — The card window can no longer be minimized into a strip (`[C]`, Claude)

**Why:** audit 65 live check on 2b6947b. The card window (`DesktopProgressNotificationWindow`, `ResizeMode.CanResize`) carries the minimize and maximize boxes (`WS_MINIMIZEBOX`, `WS_MAXIMIZEBOX`, measured style `0x360F0000`). When anything minimizes it (here Claude's screenshot tool hid the not-yet-allowed tray; for the user "minimize all" or show-desktop keys), Windows parks it as a 160×28 strip at the bottom-left; it has no taskbar button (`ShowInTaskbar = false`), and the presenter keeps updating the minimized window (it is still "visible") until its session's lifetime ends, so the card is lost for that whole session.

**Plan (Claude, window behaviour of an N5 file):** drop the minimize and maximize boxes from the card window's style once its handle exists (resizing by the border stays), and if it is ever minimized anyway, put it back to `Normal` at once; reference copy and `SHA256SUMS.txt` follow; DESIGN-WORKLOG §3 rules. Files: `src/Talvora.Tray/DesktopProgressNotificationWindow.cs`, `docs/codex/reference/worklog-phosphor/**`. Pinned texts to keep: `ModernizationPolicyRegression.ps1` checks `Math.Max(10, lastFrame.ExpectedNextUpdateSeconds) * 2`, `lastFrameReceivedUtc = DateTimeOffset.UtcNow`, `WmExitSizeMove`, `UserResizeCompleted`, `view.CodeMaxHeight = Math.Clamp`, `Math.Clamp(heightDip - 240, 160, 900)` and `SizeToContent = SizeToContent.Height` in that file. The agent never takes this line.
