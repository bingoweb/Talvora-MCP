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

### Audit 2026-11-17 (69) · range `audit/2026-11-17-68..6ed0a3d` (D110 by ChatGPT; Claude's D109v) · new agent card D111

### D111 — a failed Dependabot apply never leaves a half-applied tree or a RUNNING result

**Why:** audit 69 (review of D110; both failures reproduced with this machine's git 2.56.0.windows.4). (1) `scripts/Apply-DependabotPr.ps1` sets `$appliedByScript` only after `git apply --3way` succeeded (line 138). When the 3-way apply conflicts (a PR whose base is older than `main`: its `global.json` moves 10.0.404 → 10.0.406 while `main` already says 10.0.405), git prints `Applied patch to 'global.json' with conflicts.`, exits 1 and leaves `UU global.json` with conflict markers; the `catch` restores nothing, so the next item stops on a dirty tree. Measured: the existing restore command (`git restore --staged --worktree -- . …`, line 191) exits 0 on that state and leaves the committed file and an empty `git status --porcelain`. (2) The `catch` (lines 188–196) runs the restore before it writes the failure. When the restore itself fails, `Invoke-Checked` throws out of the `catch`, `pr-<n>.result` keeps `RUNNING pid <n>` and the original failure is lost, so a detached run never reaches a final line. Measured: with an empty `.git/index.lock` present, `git status --porcelain` still exits 0 with no output, while `git apply --3way` and `git restore` both exit 128 with `fatal: Unable to create '…/.git/index.lock': File exists.` (3) Two D110 behaviours have no test (mutations survived): passing `-DotnetRoot` on to the updater (line 155) and comparing a downgrade with `-FromVersion` instead of the patched `global.json`.

**Files:** `scripts/Apply-DependabotPr.ps1`, `tests/DependabotApplyRegression.ps1`, `docs/codex/STATUS.md`, `.context7/verification.json`. `scripts/Update-DotnetSdkPatch.ps1` does not change. No other test reads `Apply-DependabotPr.ps1`; keep `tests/DotnetSdkPatchRegression.ps1`, `tests/ThirdPartyNoticesUpdateRegression.ps1` and `tests/ModernizationPolicyRegression.ps1` green unchanged.

**Do:**
1. Red first, in `DependabotApplyRegression.ps1`, through `Invoke-SdkFixtureCase` (line 123). Give it two optional parameters after `$ExpectedFailure`: `[string] $MainSdk = ''` and `[bool] $IndexLocked = $false`.
   - `$MainSdk`: right after the existing `checkout -- .` (line 169), when it is not empty, write `global.json` as `$originalGlobal.Replace('10.0.404', $MainSdk)`, stage it (`add -- global.json`) and commit it with the same `-c user.name=… -c user.email=…` form as line 148, and set `$originalGlobal` to that new content, so the existing assertions compare with the committed `main`.
   - `$IndexLocked`: when set, create an empty file `.git/index.lock` in the fixture right before the Apply call (line 175) and delete it right after the call returns, before the `git status` check.
   - Add three calls next to the existing SDK cases (lines 213–215): (h) `Invoke-SdkFixtureCase 'conflict' '10.0.406' $false $false 'git apply --3way' '10.0.405'`; (i) `Invoke-SdkFixtureCase 'index-locked' '10.0.405' $false $false 'restore failed: ' '' $true`; (j) `Invoke-SdkFixtureCase 'downgrade' '10.0.403' $true $false 'SDK 10.0.403 is older than the pinned 10.0.404; the updater never moves the pin back.'`. (h) and (i) fail on the current script (dirty `UU global.json`; no `restore failed: ` in the result). (j) already passes and guards D110: this machine has no SDK 10.0.403, so it fails if `-DotnetRoot` is not passed on, and it fails if the downgrade is compared with the patched `global.json`.
2. `Apply-DependabotPr.ps1`: move `$appliedByScript = $true` to the line right before `Invoke-Checked git @('apply', '--3way', $patchToApply)`, so every failure from the apply on is restored; the clean-tree check and the selection before it stay as they are. In the existing `finally`, delete `$patchToApply` when it was set (`-ErrorAction SilentlyContinue`), and drop the `Remove-Item` after the apply.
3. In the `catch`: build `$failure` as today and write it to the log and the result file first. Then, only when `$appliedByScript` is set, run the existing restore command inside its own `try`/`catch`; when it throws, append `; restore failed: <its exception message>` to `$failure` and write the log line and the result file again with that text. `exit 1` stays the last statement.

**Don't:** change `Update-DotnetSdkPatch.ps1`, the selection rules, the clean-tree check or the restore command and its pathspecs; restore anything when the failure came before the apply line; weaken or remove an existing refusal.

**Context7:** `Context7: gerekmiyor (Talvora's own script; the git behaviour it relies on was measured on this machine's git 2.56.0.windows.4 and is quoted in the card)` → `noLookup`.

**Acceptance:** red first ((h) and (i) as described); `DependabotApplyRegression.ps1` green with the three new cases; the three tests named in Files green unchanged; signed-in Changed and Full `Hepsi geçti.`; no deploy (scripts and tests only); CI.
