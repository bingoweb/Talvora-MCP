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

### Audit 2026-11-15 (61) · range `audit/2026-11-14-60..cc54ef5` ([C] D89, release 0.0.1.2, D88 by ChatGPT) · D90, D91, D92 for the agent

### D90 — The agent brief is never silently empty

**Why:** `talvora_agent_brief` (`src/Talvora/Tools/AgentBriefTools.cs`) answers a successful, empty brief in two cases, and the agent then works without rules or a card: (1) the folder has no AGENTS.md — Codex started in ChatGPT's empty project folder `C:\Users\tayla\Documents\ChatGPT\Talvora` on 2026-11-10 and 2026-11-15, got `{"agentsSections":"","context":""}` and stopped after one failed read (call trace); (2) AGENTS.md exists but has none of the seven Talvora headings, which is every other project and every public user's repository (live on cc54ef5: an AGENTS.md with only "## Setup" and "## Code style" gave `agentsSections: ""`).

**Files:** `src/Talvora/Tools/AgentBriefTools.cs`; `tests/Talvora.SourceEdit.Regression/` (new `SourceEditRegressionRunner.D90.cs`, registered in `Program.cs` with `--d90-only` and in the list in `SourceEditRegressionRunner.cs`, the way D89 is); `tests/Talvora.Smoke/SmokeScenarios.AgentBrief.cs`; `docs/codex/STATUS.md`; `.context7/verification.json`. **Tests that read these files:** the installed smoke's agent-brief scenario (its empty-root case pins today's empty answer and changes here), `SmokeScenarios.MetadataPolicy.cs` and `SmokeScenarios.SurfacePolicy.cs` (the tool's metadata, unchanged), the whole SourceEdit regression, `ModernizationPolicyRegression.ps1`.

**Do:**
1. Red first: the D90 regression calls `AgentBriefTools.Get` on fixture folders under `%TEMP%` (none has `scripts\Get-CodexContext.ps1`) and fails on today's code: an AGENTS.md with two of the seven headings and one other heading returns only those two sections (as today); an AGENTS.md with only other headings returns the whole file; an empty folder fails with the message below, naming the first fixture; after the first fixture's AGENTS.md is deleted, the message no longer names it; after briefs of twelve fixtures the message names at most five, newest first, and the stored list holds at most ten.
2. AGENTS.md with none of the seven headings → `agentsSections` is the whole file; the existing 60,000-character cap and `truncated` apply unchanged.
3. No AGENTS.md in the folder → the call throws `FileNotFoundException` (a missing folder already throws `DirectoryNotFoundException`) with exactly this English text: `No AGENTS.md in <root>. workspaceRoot must be the repository folder that holds AGENTS.md.` and, when Talvora knows other workspaces, ` Workspaces briefed recently: <root>; <root>.` (at most five, newest first, never the requested folder, only folders whose AGENTS.md still exists).
4. Every brief whose AGENTS.md exists records its root in `%ProgramData%\Talvora\AgentBrief\recent-workspaces.json`: a JSON array of `{"root", "lastUsedUtc"}`, newest first, at most ten, roots compared case-insensitively, written atomically (a temp file, then a replace); a failed write never fails the brief. Add a test override for the path, the way `ProtectedSecretPaths.EntriesOverrideForTests` exists.
5. Installed smoke: replace the empty-root assertion. The smoke has briefed the repository first, so the call must now be an error whose text contains `No AGENTS.md in <emptyRoot>` and `Workspaces briefed recently: ` followed by the repository root; read that error without the throwing `CallAsync` helper. The oversized case stays.

**Don't:** change the tool's name, title, description, annotations or output schema, the seven headings, the cap, the `Get-CodexContext.ps1` handling, or any other tool.

**Context7:** `Context7: gerekmiyor (Talvora's own brief logic and its own state file; no external API)` → `noLookup`.

**Acceptance:** red first; `--d90-only` prints `D90_AGENT_BRIEF_GREEN`; the whole SourceEdit regression and `ModernizationPolicyRegression.ps1` green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes (the full smoke proves step 5 live); CI.

### D91 — Deleting a missing path says it was already gone

**Why:** live on cc54ef5 (audit 61): `talvora_delete` on a missing file returned `{"deleted":false,"kind":"missing"}` (`FileTools.cs`), yet Çalışma günlüğü said "Silindi · olmayan-dosya.txt". The worklog claims a deletion that never happened, and a mistyped path looks like success. The worklog must say exactly what was done (user rule 2026-11-13).

**Files:** `src/Talvora/TalvoraDesktopProgressNarrative.cs` (`Wl5SucceededTitle`), `src/Talvora/WorklogCopy.cs` (the text), `tests/Talvora.Notification.Regression/` (new `Program.D91.cs` and its registration in `Program.cs`, the way D88 registers), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** the whole Notification regression (WL5, WL6, N9Subjects, D79, D82, D83, D88), `ModernizationPolicyRegression.ps1`.

**Do:**
1. Red first: a D91 test runs `talvora_delete` through the notifier with three results: `{ path, deleted = false, kind = "missing" }` → the completed title and the worklog frame read `Zaten yoktu · <name>` (`Zaten yoktu` when there is no name); `deleted = true` → `Silindi · <name>`; a result without `deleted` (D88's `{ success = true }`) → `Silindi · <name>`.
2. Extend `Wl5SucceededTitle` for the delete tool: read the result's `deleted` the way `ResultContainsText` reads results (structured content, else the serialized result); only a boolean `false` changes the title.

**Don't:** change the running or failed titles, the category, the subject, `talvora_delete`'s behaviour or result, or any other tool.

**Context7:** `Context7: gerekmiyor (Talvora's own title logic)` → `noLookup`.

**Acceptance:** red first; the complete Notification regression and `ModernizationPolicyRegression.ps1` green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI. Claude checks a real missing-path delete at the next audit (`[~] visual check pending (Claude)`).

### D92 — The history keeps every step's purpose

**Why:** user decision, audit 61 ("Her adımda saklansın"): Çalışma günlüğü's history keeps only the last step's purpose (`current.purpose`); every earlier step loses its "ne". In three days 32 steps read only "Komut tamamlandı" in the history, although ChatGPT passes purposes (11 of its 14 sessions since 2026-11-14 kept one on their last step). This card stores and carries the purpose; Claude draws it in `[C] D92v`.

**Files:** `src/Talvora.Shared/DesktopProgressProtocol.cs` (`WorklogTimelineEntry` gains `string? Purpose = null` as its last parameter), `src/Talvora/TalvoraDesktopWorklogState.cs` (the entry inserted when a step ends gets that step's `ended.Narrative.Purpose`), `src/Talvora.Tray/WorklogHistoryStore.cs` (`WorklogHistoryTimelineRow` gains `Purpose`; `TimelineRows` fills it), `src/Talvora.Tray/Worklog/WorklogCardViewModel.cs` and `WorklogCardMapper.cs` (`WorklogTimelineRow` gains an optional `Purpose`, filled by the mapper), `tests/Talvora.Notification.Regression/` (new `Program.D92.cs` and its registration), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** the whole Notification regression (WL1, D40, D45, D50, D61, N7, N8, UI12c and the rest), the ControlCenter smoke (`ControlCenterSmoke.cs`, `ControlCenterSmoke.WL3.cs`), `ModernizationPolicyRegression.ps1`.

**Do:**
1. Red first: a D92 test runs three steps through the notifier, two with different purposes and one without, and checks that each timeline entry carries its own step's purpose (null for the one without), that a session written to and read back from the history store keeps them, that an old stored line whose timeline entries have no `purpose` reads with null, and that `TimelineRows` and the mapper's rows carry them.
2. Add the field and fill it where the entry is created. The Claude and Codex engine timelines (`TalvoraDesktopClaudeWorklog.cs`, `TalvoraDesktopCodexWorklog.cs`) keep null.

**Don't:** change any XAML or visible text (D92v), any title, `current.purpose`, `WorklogNote.Normalize` (140 characters), or the history file's name or other fields.

**Context7:** `Context7: gerekmiyor (Talvora's own records)` → `noLookup`.

**Acceptance:** red first; the Notification regression, the ControlCenter smoke and `ModernizationPolicyRegression.ps1` green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI. Nothing on screen changes in this card.
