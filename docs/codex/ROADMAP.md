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

### Audit 2026-11-17 (68) · range `audit/2026-11-17-67..4c9deb4` (PK5, D107, D108, D109 by ChatGPT) · new agent card D110

### D110 — the SDK updater never leaves the band or moves back, and a refused SDK PR leaves the tree clean

**Why:** audit 68 (review of D107, reproduced on fixtures). (1) `scripts/Apply-DependabotPr.ps1` applies the PR's diff (`git apply`, line 131) before it calls `Update-DotnetSdkPatch.ps1 -Version <the PR's version>` (lines 144–148), and the updater reads the pinned version from that already-patched `global.json` (lines 25–29). Its band and major check therefore compares the PR's version with itself: a Dependabot PR to 10.0.500 or 12.0.100 passes once that SDK is installed, although new feature bands and majors are Claude's decision (AGENTS.md → MODERNIZATION FLOW 1). (2) Without `-Version` the updater takes the newest installed patch of the band even when it is older than the pin: 10.0.405 pinned and only 10.0.404 installed printed `SDK 10.0.405 -> 10.0.404` and downgraded the platform packages. (3) When the PR's SDK is not installed or is refused, Apply throws after `git apply` and leaves `global.json` changed, so the next item stops on a dirty tree, while FLOW 3 says to leave that PR open and take the next item. (4) The "already the newest patch" return (lines 100–103) also skips the lock refresh, so a `-SkipRestore` run followed by a normal run leaves the lock files stale until the gate's locked restore fails.

**Files:** `scripts/Update-DotnetSdkPatch.ps1`, `scripts/Apply-DependabotPr.ps1`, `tests/DotnetSdkPatchRegression.ps1`, `tests/DependabotApplyRegression.ps1`, `docs/codex/STATUS.md`, `.context7/verification.json`. No other test reads the two scripts; keep `tests/ThirdPartyNoticesUpdateRegression.ps1` and `tests/ModernizationPolicyRegression.ps1` green.

**Do:**
1. Red first, on throwaway fixtures:
   - In `DotnetSdkPatchRegression.ps1` (it copies the three updater scripts into a fixture repository and passes a fake `-DotnetRoot`; its existing fake root also holds 10.0.405, so cases (a) and (b) build a second fake root with only `sdk\10.0.404`, each SDK folder with its `Microsoft.NETCoreSdk.BundledVersions.props`): (a) pin 10.0.405, only 10.0.404 installed, no `-Version` → throws the downgrade message of step 2 and changes no file; (b) the same with `-Version 10.0.404` → the same; (c) `global.json` already at 10.0.500 with 10.0.500 installed and `-FromVersion 10.0.404 -Version 10.0.500` → throws the existing band message (`Claude decides feature bands and majors. …`) and changes no file; (d) everything aligned and no `-SkipRestore` → the platform updater still runs (for this case replace the fixture's copy of `Update-DotnetPlatformPackages.ps1` with a stub that writes a marker file), while with `-SkipRestore` the script returns early as today.
   - In `DependabotApplyRegression.ps1`, a new helper beside `Invoke-FixtureCase` (that one is specific to workflow cases): its fixture commits `global.json` at 10.0.404 and the three updater scripts, and gets its own fake dotnet root passed through `-DotnetRoot`. (e) a `-PatchFile` moving `global.json` to 10.0.500, with `sdk\10.0.404` and `sdk\10.0.500` installed, is refused; (f) a patch to 10.0.405 with only `sdk\10.0.404` installed is refused. In both, the result file starts with the script's existing failure text and the fixture's `git status --porcelain` is empty afterwards. (g) a fixture with an uncommitted change to a tracked file is refused by the clean-tree check and that change is still there afterwards.
2. `Update-DotnetSdkPatch.ps1`: a new optional `-FromVersion` (`x.y.z`), the version pinned before the change; default: `sdk.version` of `global.json`. It feeds only the major, minor and band values of the existing check and the new lower-than check; `$oldSdk` keeps reading `global.json`, because it also decides whether `global.json` is written, the no-change return and the receipt line. A chosen patch lower than `-FromVersion` is refused, with or without `-Version`, with exactly `SDK <chosen> is older than the pinned <from>; the updater never moves the pin back.` Every check stays before the first write. When nothing needs to change, return early only with `-SkipRestore`; without it, run `Update-DotnetPlatformPackages.ps1 -Version <runtime>` first, then print the existing "already the newest patch" line.
3. `Apply-DependabotPr.ps1`: only when the selected paths contain `global.json` (known after the selection loop, before `git apply`), read its `sdk.version` before `git apply` and pass it as `-FromVersion`; otherwise pass no `-FromVersion`. Accept an optional `-DotnetRoot` and pass it to the updater (fixtures need it). Set a flag right after a successful `git apply`. In the existing `catch`, when that flag is set, perform the same revert the `-DryRun` branch performs (line 168) before writing the failure, so the tree is as before the run and the PR stays open. Without the flag (the clean-tree check or anything before `git apply` failed) the `catch` reverts nothing: it must never touch changes the script did not make.

**Don't:** change `.github/dependabot.yml`, the workflows, `global.json`, `Directory.Packages.props`, the lock files or the notices; weaken or remove an existing refusal; install an SDK.

**Context7:** `Context7: gerekmiyor (Talvora's own scripts and tests; the version rules are in docs/MODERNIZATION-POLICY.md and were verified for D107)` → `noLookup`.

**Acceptance:** red first; both tests green with the new cases; the two tests named above green unchanged; on the repository `pwsh -NoProfile -File scripts/Update-DotnetSdkPatch.ps1 -SkipRestore` still prints `SDK 10.0.404 is already the newest patch of its band` and changes nothing; signed-in Changed and Full `Hepsi geçti.`; no deploy (scripts only); CI.

### D109v — the bottom bar lists the open sessions (`[C]`: Claude ships it)

**Why:** the look half of D109 (user decision 2026-11-17). The bar lives in the eight checksum-pinned worklog files, so Claude builds it; after D109 the agent stops at this line with `Sıra Claude'da: D109v`.

**What Claude ships:** DESIGN-WORKLOG §7b "Window list". The presenter gives each card window a provider that calls `GetSessionTabs`; the window applies the tabs on every `Update` and on its 5 s stale tick; a click on an entry calls `ShowSession` (never the body click that opens Çalışma günlüğü); `WorklogCardMapper` builds the entries (flags, duplicate names, `…`, the choice of up to three others, `+<k>`, the automation name) and the view fits as many as the bar allows before the right side; the window's three lifetimes read `WorklogSessionTabs`; a preview scenario `11-two-sessions`; the eight reference copies and `SHA256SUMS.txt` refreshed in the same commit.

**Acceptance (Claude):** red first in `Talvora.Notification.Regression`; signed-in Full gate; clean-HEAD deploy and both installed smokes; the live card captured with two real sessions in both themes and in high contrast; `hakem` before the commit; CI.
