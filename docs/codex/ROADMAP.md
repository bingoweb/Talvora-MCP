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

### Audit 2026-11-16 (64) · range `audit/2026-11-16-63..8790f05` (D93, D92, D94, P-55 by ChatGPT) · no new agent card; Claude shipped `[C]` D96 (P-55: Defender blocked the smoke's encoded probe) and `[C]` D92v; D95 is next

### D97v — The history's timeline marks each step's state (`[C]`, Claude)

**Why:** audit 64 screen pass. The history's "Zaman çizelgesi" draws failed, cancelled and running steps exactly like finished ones; only the words differ, so one failure in a long session is hard to find. The live card marks each row with its state (✓ ✗ ▸ ■, DESIGN-WORKLOG §7b), and the history list already shows an 8 px outcome dot (`HistoryOutcomeDot`: `Tv.Ready`, `Tv.Attention`).

**Plan (Claude, look only):** a state mark before each row's time in the outcome dot's tokens, the step's words unchanged; `TimelineRows` carries the state; `ControlCenterSmoke` checks one mark per state in both themes. Files: `src/Talvora.Tray/Views/WorklogHistoryView.xaml`, `src/Talvora.Tray/WorklogHistoryStore.cs`, `src/Talvora.Tray/ControlCenterSmoke.D92v.cs`; no N5 file. The agent never takes this line.

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
