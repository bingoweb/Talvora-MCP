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

### Audit 2026-11-16 (63) · range `audit/2026-11-15-62..7d3a5c6` (empty: no agent work since audit 62) · D95 for the agent

### D95 — A failed command's result shows its real error line

**Why:** audit 63, measured on the installed 8ff0b07. For a failed `talvora_run_powershell` or `talvora_user_run` step, `BuildTerminalEvidence` (`TalvoraDesktopProgressEvidence.cs`, the `run_powershell`/`user_run` branch added by WL5) puts the first non-empty line of `standardError` into "Sonuç" and into the evidence summary. Real outputs put something else there. A real `talvora_user_run` of `pwsh.exe -NoProfile -File tests/Throw-Test.ps1` (a script ending in `throw 'D93 RED: …'`) stored "Sonuç: `ESC[31;1m`Exception: `ESC[0m`C:\…\tests\Throw-Test.ps1:3`ESC[0m`" in the history: PowerShell 7 writes its error view to the redirected stderr with ANSI colour sequences, its first line is the script location, and the thrown message is its last line. Python's unittest writes every test line to stderr, so the stored record of 2026-11-14 13:55 reads "Test betiği başarısız · Test-Stok.ps1" with "Sonuç: test_adet_eklenir (…) ... ok", a passing test. cargo, uv and git also print progress lines to stderr before the error. DESIGN-WORKLOG §6.4 asks for "the first line of the thrown message" (test script) and the "first error line" (inline command). WL5's test used a made-up stderr (`"Betiğin ilk hata satırı\n   at line 2\n"`), never the shape PowerShell really writes, so every red-first run of a PowerShell test shows escape codes and a path on the live card.

**Files:** `src/Talvora/TalvoraDesktopProgressEvidence.cs` (the failed `run_powershell`/`user_run` branch of `BuildTerminalEvidence` and one new private helper), `tests/Talvora.Notification.Regression/Program.D95.cs` (new), `tests/Talvora.Notification.Regression/Program.cs` (`--d95-only` and the whole run, registered like D91), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** the whole Notification regression (WL5 "PowerShell failure" and "D73 cmd inline failure", N9, D79 and D83 build one-line stderr samples; they keep their results), `tests/ModernizationPolicyRegression.ps1` (four text checks on `TalvoraDesktopProgressEvidence.cs`: `RedactSensitivePreviewLine`, no `MaximumPreviewLines`, `MaximumPreviewCharacters = DesktopProgressProtocol.MaximumCodeCharacters`, `BuildStructuredEditEvidence`; keep them true).

**Do:**
1. Red first: `Program.D95.cs` runs failed steps through the notifier the way WL5's `Check` does, each as `talvora_user_run` with executable `pwsh.exe`, arguments `-NoProfile -File tests/Throw-Test.ps1` and `exitCode = 1`, with these `standardError` texts (C# literals, copy them exactly):
   - a. PowerShell's error view as measured:
     ```
     "\u001B[31;1mException: \u001B[0mC:\\repo\\tests\\Throw-Test.ps1:3\u001B[0m\r\n\u001B[31;1m\u001B[0m\u001B[36;1mLine |\u001B[0m\r\n\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m   3 | \u001B[0m \u001B[36;1mthrow 'D95 RED: örnek hata'\u001B[0m\r\n\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m     | \u001B[31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~\u001B[0m\r\n\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[31;1m\u001B[31;1m\u001B[36;1m     | \u001B[31;1mD95 RED: örnek hata\u001B[0m\r\n"
     ```
     → title "Test betiği başarısız · Throw-Test.ps1", `Evidence.Result` exactly "Sonuç: D95 RED: örnek hata", and no U+001B in `Evidence.Result` or `Evidence.Summary`.
   - b. Text a with every `\u001B[…m` sequence removed → the same result.
   - c. Python unittest as measured:
     ```
     "test_a_ok (__main__.T.test_a_ok) ... ok\ntest_b_stock (__main__.T.test_b_stock) ... FAIL\n\n======================================================================\nFAIL: test_b_stock (__main__.T.test_b_stock)\n----------------------------------------------------------------------\nTraceback (most recent call last):\n  File \"<string>\", line 4, in test_b_stock\nAssertionError: -1 != 0\n\n----------------------------------------------------------------------\nRan 2 tests in 0.001s\n\nFAILED (failures=1)\n"
     ```
     → "Sonuç: test_b_stock (__main__.T.test_b_stock) ... FAIL".
   - d. Text c followed by text a (unittest, then the script throws) → "Sonuç: D95 RED: örnek hata".
   - e. Progress before the error:
     ```
     "   Compiling demo v0.1.0 (C:\\repo)\nerror[E0425]: cannot find value `x` in this scope\nerror: could not compile `demo` (bin \"demo\") due to 1 previous error\n"
     ```
     → "Sonuç: error[E0425]: cannot find value `x` in this scope".
   - f. `"step one\nstep two\n"` → "Sonuç: step two".
   All six fail today, because the first line is taken.
2. In that branch, choose the line in this order, then redact and limit it exactly as today:
   1. Remove ANSI CSI sequences from the whole `standardError` with the pattern `\x1B\[[0-?]*[ -/]*[@-~]`, split it into lines, trim each line and drop empty ones.
   2. PowerShell error view: when one line equals `Line |`, take the line right after the last line matching `^\|\s*~+$`, without its leading `|` and the spaces after it (the first line of the thrown message). When there is no such line, or it is empty, go on with 3.
   3. Otherwise the first line that matches `\b(?:error|errors|fail|failed|failure|failures|fatal|exception)\b` (ignore case, culture invariant) or contains `hata`, `Hata`, `HATA`, `başarısız`, `Başarısız` or `BAŞARISIZ`, and does not end with `... ok`.
   4. Otherwise the last line.
   Claude checked this order on the six texts above and on the existing one-line samples ("Betiğin ilk hata satırı", "command failed", "x", "failure", "fixture failure"): each gives the expected line.

**Don't:** strip escape codes or change text anywhere else (live lines, code previews, successful results, other tools), change any title, the plain reason (`WorklogFailureClassifier`, DESIGN-WORKLOG §5.3), `LimitEvidenceText`'s limit or the redaction, or any of the eight N5 files pinned by `docs/codex/reference/worklog-phosphor/SHA256SUMS.txt`.

**Context7:** `Context7: gerekmiyor (Talvora's own worklog text; the real output samples are in this card)` → `noLookup`.

**Acceptance:** red first; the Notification regression and `ModernizationPolicyRegression.ps1` green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI. Mark `[~] live check pending (Claude)`: your own session reaches the history file only after 90 s without calls, so Claude runs a failing test script through the installed service at the next audit and reads the stored "Sonuç".

### Audit 2026-11-15 (62) · range `audit/2026-11-15-61..9c3537e` (D90, D91, P-54 by ChatGPT) · D93, D94 for the agent; D92 narrowed by P-54

### D93 — Deleting a missing path really reads "Zaten yoktu"

**Why:** audit 62, live on the installed 8ff0b07 (it contains D91): `talvora_delete` of a missing path returned `{"deleted":false,"kind":"missing"}`, and the stored worklog still read "Silindi · olmayan-dosya.txt". The notifier receives the `CallToolResult` the server built (`Program.cs` → `AddCallToolFilter` → `RunToolCallAsync`). `talvora_delete` has no `UseStructuredContent`, so its JSON sits as text in one `TextContentBlock` and `StructuredContent` is null. `ResultDeletedIsFalse` then serializes the whole `CallToolResult`, finds no top-level `deleted` and keeps "Silindi". D91's test built a plain object and a `CallToolResult` with `StructuredContent`, never the shape a real call has.

**Files:** `src/Talvora/TalvoraDesktopProgressNarrative.cs` (`ResultDeletedIsFalse`), `tests/Talvora.Notification.Regression/Program.D91.cs` (new cases in `AssertD91MissingDeleteNarrativeAsync`; it already runs with `--d91-only` and in the whole regression), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** the whole Notification regression (D88, D91, WL5, WL6 and the rest).

**Do:**
1. Red first: add the real shape to D91's test, a `CallToolResult` whose `Content` is one `TextContentBlock` holding `JsonSerializer.Serialize(new { path, deleted = false, kind = "missing" })` with no `StructuredContent`, which must read "Zaten yoktu · <name>"; the same with `deleted = true` must read "Silindi · <name>"; a text block that is not JSON must read "Silindi · <name>". The first case fails today.
2. In `ResultDeletedIsFalse`, when the result is a `CallToolResult` without `StructuredContent`, parse the text of its first `TextContentBlock` whose text is a JSON object and read `deleted` there. Only a JSON `false` counts, as before. Keep the structured-content path and the serialized-object path.

**Don't:** change `talvora_delete` itself (no `UseStructuredContent`, no output schema: the tool list and the metadata policy stay as they are), `ResultContainsText`, any other title or text.

**Context7:** `Context7: gerekmiyor (Talvora's own worklog title logic)` → `noLookup`.

**Acceptance:** red first; the Notification regression green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI. Mark `[~] live check pending (Claude)`: your own session is written to the worklog file only after 90 s without calls, so Claude checks a real missing-path delete at the next audit.

### D94 — The brief's live smoke runs on its own fixture, inside the full installed smoke

**Why:** audit 62. D90 changed `tests/Talvora.Smoke/SmokeScenarios.AgentBrief.cs`, but `--agent-brief-live` is in no procedure (AGENTS.md → Deploy row runs `--metadata-policy-only` and the full run), so the change never ran. Claude ran it on the installed 8ff0b07: `TALVORA MCP SMOKE RED`, "agent_brief context lost the character 'ç'.". The scenario reads the live repository. With no open agent card the context is only "AGENT QUEUE EMPTY - STOP" and the user queue (570 characters, no 'ç' and no '§'), and the active-card step cannot pass either, so the smoke is red exactly when Claude audits. Its explicit-card step also asks for card `A2`, which left ROADMAP with the cleanup of 2026-11-07: Claude's second run, with D93 open, went red there, so on the live repository it cannot pass at all. Every agent session starts with this tool. Same audit: the recent-workspace list (D90) never drops a folder whose `AGENTS.md` is gone. Each run of this smoke records two temporary folders, so after a few deploys the ten places hold dead entries and real projects fall out of the hint.

**Files:** `tests/Talvora.Smoke/SmokeScenarios.AgentBrief.cs`, `tests/Talvora.Smoke/Program.cs` (the full run also runs the brief scenario), `src/Talvora/Tools/AgentBriefTools.cs` (`RecordRecentWorkspaceAsync` only), `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.D90.cs` (new case), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** the SourceEdit regression (`--d90-only` and the whole run), both installed smokes.

**Do:**
1. Red first: in the D90 runner, a recorded root whose `AGENTS.md` was deleted must be gone from the stored list after the next successful brief of another root. Today it stays.
2. `RecordRecentWorkspaceAsync` keeps only entries whose `AGENTS.md` still exists, plus the root it records. The other list rules stay: ten entries, newest first, case-insensitive, atomic write, a failed write never fails the brief.
3. The scenario builds its own repository in a temp folder: the real `AGENTS.md` and `scripts/Get-CodexContext.ps1` copied from the repository path, plus fixture `docs/codex/STATUS.md` (an `## Agent queue` with one open card), `docs/codex/ROADMAP.md` (that card and a second card for the explicit-card step, their text containing ç, ğ, — and §) and the other files the script reads (`QUEUE-ADDITIONS.md`, `PROPOSALS.md`). Every existing check runs against this fixture; the empty-folder step (with its "Workspaces briefed recently" check, now naming the fixture) and the oversized step stay. Everything is removed in `finally`.
4. The full installed run (`Talvora.Smoke <endpoint> <repository>`) also runs the brief scenario (its server root is the endpoint without `/mcp`), so the Deploy row's run covers it. `--agent-brief-live` keeps working on its own.

**Don't:** change the brief's output, its error text, the seven headings, the 60,000 cap or the tool's metadata; write into the real repository's `docs/` from the smoke.

**Context7:** `Context7: gerekmiyor (own test and state file)` → `noLookup`.

**Acceptance:** red first; the SourceEdit regression green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy; both installed smokes green with the brief scenario inside the full run (it no longer depends on what the live queue holds); CI.

### D92 — The history keeps every step's purpose

**Why:** user decision, audit 61 ("Her adımda saklansın"): Çalışma günlüğü's history keeps only the last step's purpose (`current.purpose`); every earlier step loses its "ne". In three days 32 steps read only "Komut tamamlandı" in the history, although ChatGPT passes purposes (11 of its 14 sessions since 2026-11-14 kept one on their last step). This card stores and carries the purpose; Claude draws it in `[C] D92v`.

**Decision P-54 (Claude, 2026-11-15):** the live card's files stay out of this card. The purpose is stored per step and reaches the history view through `TimelineRows`; Claude draws it in `WorklogHistoryView.xaml` (not pinned) in `[C] D92v`.

**Files:** `src/Talvora.Shared/DesktopProgressProtocol.cs` (`WorklogTimelineEntry` gains `string? Purpose = null` as its last parameter), `src/Talvora/TalvoraDesktopWorklogState.cs` (the entry inserted when a step ends gets that step's `ended.Narrative.Purpose`), `src/Talvora.Tray/WorklogHistoryStore.cs` (`WorklogHistoryTimelineRow` gains `Purpose`; `TimelineRows` fills it), `tests/Talvora.Notification.Regression/` (new `Program.D92.cs` and its registration), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** the whole Notification regression (WL1, D40, D45, D50, D61, N7, N8, UI12c and the rest), the ControlCenter smoke (`ControlCenterSmoke.cs`, `ControlCenterSmoke.WL3.cs`), `ModernizationPolicyRegression.ps1` (it pins the eight N5 worklog sources by checksum; this card touches none of them).

**Do:**
1. Red first: a D92 test runs three steps through the notifier, two with different purposes and one without, and checks that each timeline entry carries its own step's purpose (null for the one without), that a session written to and read back from the history store keeps them, that an old stored line whose timeline entries have no `purpose` reads with null, and that `TimelineRows` carries them.
2. Add the field and fill it where the entry is created. The Claude and Codex engine timelines (`TalvoraDesktopClaudeWorklog.cs`, `TalvoraDesktopCodexWorklog.cs`) keep null.

**Don't:** touch the eight N5 sources pinned by `docs/codex/reference/worklog-phosphor/SHA256SUMS.txt` (`WorklogCardViewModel.cs` and `WorklogCardMapper.cs` among them, P-54): the live card stays as it is. Don't change any XAML or visible text (D92v), any title, `current.purpose`, `WorklogNote.Normalize` (140 characters), or the history file's name or other fields.

**Context7:** `Context7: gerekmiyor (Talvora's own records)` → `noLookup`.

**Acceptance:** red first; the Notification regression, the ControlCenter smoke and `ModernizationPolicyRegression.ps1` green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes; CI. Nothing on screen changes in this card.
