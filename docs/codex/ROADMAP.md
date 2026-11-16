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

### Audit 2026-11-17 (66) · range `audit/2026-11-16-65..44b4e66` (D98, D100, D99, D101, D102, D103 and P-56 by ChatGPT) · user: "talvora modernizasyonunun önünü kesen durumlara izin verme. talvora yı denetle" · new agent cards D107, D108; Claude's `[C]` D106 (P-56); PK5 (audit 65) unblocked

### D107 — .NET SDK patches go through one script

**Why:** audit 66; user decisions 2026-11-17 (AGENTS.md → MODERNIZATION FLOW 3). Microsoft's monthly servicing replaces the installed SDK patch, while `global.json` pins it exactly (`rollForward: disable`). On 2026-11-13 every restore failed until Claude moved the pin by hand (SDK3: `global.json`, 17 platform packages, six lock files, THIRD-PARTY-NOTICES, docs). Nothing ties the notice's three runtime pack lines to the runtime: they said 10.0.12 until Claude corrected them to 10.0.15 in audit 66. December's patch would stop every card again; one script must do the whole move, for a Dependabot SDK PR and for a machine whose SDK was replaced.

**Files:** `scripts/Update-DotnetSdkPatch.ps1` (new), `scripts/Apply-DependabotPr.ps1`, `scripts/Update-ThirdPartyNotices.ps1`, `tests/ThirdPartyNoticesSourceRegression.ps1`, `tests/DotnetSdkPatchRegression.ps1` (new), `docs/MODERNIZATION-POLICY.md` (the "SDK updates" paragraph), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** `tests/PlatformPackageLockstepRegression.ps1`, `tests/ThirdPartyNoticesSourceRegression.ps1`, `tests/ThirdPartyNoticesUpdateRegression.ps1` (green unchanged), `tests/ModernizationPolicyRegression.ps1`, `tests/QualityGateRegression.ps1`; CI's SDK check in `.github/workflows/windows-ci.yml` (an SDK change must come with changed lock files) stays as it is.

**Do:**
1. Red first: `tests/DotnetSdkPatchRegression.ps1` copies `Update-DotnetSdkPatch.ps1`, `Update-DotnetPlatformPackages.ps1` and `Update-ThirdPartyNotices.ps1` into `<temp>\scripts` (all three keep taking the repository root from `$PSScriptRoot`; add no root parameter) and builds the fixture repository around them: `global.json`, `Directory.Packages.props` with the platform family, `THIRD-PARTY-NOTICES.md` with the three runtime pack lines, and the two lock files the notice step reads by default (`src\Talvora\packages.lock.json`, `src\Talvora.Tray\packages.lock.json`), written the way `tests/ThirdPartyNoticesUpdateRegression.ps1` writes its fixtures. It runs the copied script with `-SkipRestore` and a fake dotnet root (`-DotnetRoot`) holding `sdk/<version>/Microsoft.NETCoreSdk.BundledVersions.props`, never against the real repository files: (a) without `-Version` the newest installed patch of `global.json`'s feature band is chosen (10.0.404 pinned, 10.0.405 and 10.0.500 installed → 10.0.405); (b) `global.json`'s version, every platform package and the three runtime pack lines take that SDK's `BundledNETCoreAppPackageVersion`; (c) another feature band or major (10.0.500, 11.0.100) stops with a message saying Claude decides feature bands and majors; (d) a requested version that is not installed stops and names https://dotnet.microsoft.com/download/dotnet/10.0; (e) the script is idempotent: it brings `global.json`, the platform family and the runtime pack lines to the chosen SDK even when `global.json` already names it (after `Apply-DependabotPr.ps1` applied the PR), and when all three already match it changes nothing and prints "SDK <version> is already the newest patch of its band".
2. `Update-DotnetSdkPatch.ps1 [-Version <x.y.z>] [-SkipRestore] [-DotnetRoot <path>]` (default root: the folder of the `dotnet` on PATH): choose the version as above (same major.minor, same hundreds digit of the patch), read `BundledNETCoreAppPackageVersion` from that SDK's `Microsoft.NETCoreSdk.BundledVersions.props`, write `global.json` keeping `rollForward: disable`, its BOM and newlines, run `Update-DotnetPlatformPackages.ps1 -Version <runtime>` (with `-SkipRestore` when given), then `Update-ThirdPartyNotices.ps1 -RuntimeVersion <runtime>`, and print `SDK <old> -> <new>, runtime <old> -> <new>`. It never installs anything.
3. `Update-ThirdPartyNotices.ps1` gets `-RuntimeVersion`: the three runtime pack list entries (`Microsoft.NETCore.App.Runtime.win-x64`, `Microsoft.WindowsDesktop.App.Runtime.win-x64`, `Microsoft.AspNetCore.App.Runtime.win-x64`) take it. `ThirdPartyNoticesSourceRegression.ps1` fails when those three versions differ from the platform family's version in `Directory.Packages.props` (the family moves with the runtime patch).
4. `Apply-DependabotPr.ps1`: when the PR's diff changes `global.json`, call `Update-DotnetSdkPatch.ps1 -Version <sdk.version read from global.json after git apply>` right after `git apply` (it brings the platform packages, notice and restores along); the rest of the script stays as it is.
5. `docs/MODERNIZATION-POLICY.md` → "SDK updates": one sentence naming the script and the same-feature-band rule.

**Don't:** install an SDK, change `rollForward`, the dependabot ignore list, the gate or CI.

**Context7:** `/dotnet/docs`, one precise query each: `global.json` `sdk.version` with `rollForward: disable`, and where an SDK records its bundled runtime version (`Microsoft.NETCoreSdk.BundledVersions.props`, `BundledNETCoreAppPackageVersion`). If Context7 has no answer for the props file, read it in `C:\Program Files\dotnet\sdk\10.0.404\` and say so in the Log.

**Acceptance:** red first; the new test, both notice tests and the lockstep test green; on the real repository `Update-DotnetSdkPatch.ps1 -SkipRestore` with no newer patch installed changes nothing and says so; signed-in Changed and Full `Hepsi geçti.`; CI. No deploy (scripts only).

### D108 — Dependabot's GitHub Actions PRs go through the apply script

**Why:** audit 66 look-ahead; MODERNIZATION FLOW 1 lets the agent apply open Dependabot PRs itself, GitHub Actions included (minor and patch versions). `.github/dependabot.yml` also updates GitHub Actions weekly, but `Apply-DependabotPr.ps1` takes only `Directory.Packages.props` and `global.json` from a PR and stops with "PR #<n> does not change …" for an Actions PR. Audit 67's rehearsal found a second stop: `tests/ReleaseWorkflowRegression.ps1` pins the two action SHAs literally, so the first Actions update would turn the gate red.

**Files:** `scripts/Apply-DependabotPr.ps1`, `tests/DependabotApplyRegression.ps1` (new), `tests/ReleaseWorkflowRegression.ps1`, `docs/MODERNIZATION-POLICY.md` (the "Dependabot PRs" paragraph), `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** `tests/QualityGateRegression.ps1`, `tests/ModernizationPolicyRegression.ps1` (keep both green unchanged).

**Do:**
1. Red first: `tests/DependabotApplyRegression.ps1` builds a fresh temporary git repository for every case (`git init -b main`; one commit holding a copy of `scripts/Apply-DependabotPr.ps1` under `scripts\`, a `.gitignore` with `artifacts/` as the real repository has, a `README.md`, and fixture `.github/workflows/windows-ci.yml` and `release.yml` whose `uses:` lines are pinned like the real ones, `@<40 hex> # vX.Y.Z`). In that throwaway repository it edits the fixture files, writes the patch with `git diff` to a file outside the repository folder, puts the files back with `git checkout -- .` (the script refuses a dirty tree), then runs the copied script with `-Pr 1 -PatchFile <patch> -SelectOnly` and checks the exit code, `artifacts\dependabot\pr-1.result` and the files:
   (a) the same action moved in both workflows to another full 40-hex SHA with its `# vX.Y.Z` comment, same major → exit 0, both files changed, result `SELECTED .github/workflows/release.yml, .github/workflows/windows-ci.yml`;
   (b) the new pin is a tag (`@v7`), a short SHA, has no `# v…` comment, or a comment with fewer than three numbers (`# v7`, `# v7.1`) → exit 1, a `FAILED: …` result naming the line, files unchanged;
   (c) a workflow line that is not a `uses:` line changes → the same refusal;
   (d) the comment's major changes (`# v7.0.1` → `# v8.0.0`) → exit 1, result `FAILED: PR #1 moves actions/checkout to a new major version; Claude decides majors.`;
   (e) a patch that changes only `README.md` → exit 1, the existing "PR #1 does not change …" failure.
   Today the script has neither parameter, so (a) fails.
2. `Apply-DependabotPr.ps1`:
   - `-PatchFile <path>` (for tests): this unified diff replaces `gh pr view`, the fetch and `git diff`.
   - Path selection works on the patch text, the same for both sources: keep the file sections (`diff --git a/<path> b/<path>`) whose path is `Directory.Packages.props`, `global.json` or `.github/workflows/<name>.yml`; nothing left → the existing "does not change" failure, its list now naming the workflows too.
   - Workflow sections are checked before `git apply`: every removed or added line is a `uses:` line; every added one is `uses: <owner>/<repo>[/<path>]@<40 lowercase hex> # v<major>.<minor>.<patch>` (the same `# v\d+\.\d+\.\d+` form `tests/ModernizationPolicyRegression.ps1` requires for `windows-ci.yml`); the removed and the added line of the same action have the same major (the first number after `# v`). Anything else throws with the messages of step 1.
   - `-SelectOnly`: after `git apply` of the selected sections, write `SELECTED <paths, sorted, ", "-separated>` to the result file and exit 0; nothing else runs.
   - Only workflow sections selected → skip the restores, the lockstep test, the build and the notice step, run the Full gate and write `READY …` as today. Package files selected → everything runs as today, with the workflow sections applied alongside.
3. `tests/ReleaseWorkflowRegression.ps1`: replace the two literal pins with a rule that survives Dependabot: every `uses:` line in `windows-ci.yml` and `release.yml` is pinned to a full 40-hex SHA with a `# vX.Y.Z` comment, and an action used in both files has the identical `@<sha> # vX.Y.Z` text in both (keep the message `RL4: release and CI must use the same pinned action: <line>`). It still fails when the two files differ for an action, or a pin is a tag or a short SHA.
4. `docs/MODERNIZATION-POLICY.md` → "Dependabot PRs": one sentence: the script also takes the pinned `uses:` lines of `.github/workflows/*.yml` (same major only), and a workflow-only PR skips the restores, lock files and notices but still runs the Full gate.

**Don't:** edit a workflow by hand, change CI, take any other path from a PR, or accept a new major version.

**Context7:** `Context7: gerekmiyor (Talvora's own script and test; the pinning rule is already in docs/MODERNIZATION-POLICY.md)` → `noLookup`.

**Acceptance:** red first; the new test and `tests/ReleaseWorkflowRegression.ps1` green; signed-in Changed and Full `Hepsi geçti.`; CI. No deploy (scripts only).

### PK5 — Roslyn 5.12.0 from Dependabot PR #22

**Why:** audit 65. Dependabot's open PR #22 ("Bump the nuget-stable group with 2 updates") moves `Microsoft.CodeAnalysis.CSharp.Workspaces` and `Microsoft.CodeAnalysis.Workspaces.MSBuild` from 5.9.0 to 5.12.0 but rewrites only `src/Talvora/packages.lock.json`, so its CI run 43796981937 failed in locked restore (NU1004 in both regression test projects). DEP1's script applies such a PR the Talvora way (modern-by-default).

**Files:** `Directory.Packages.props`, every `packages.lock.json` the script regenerates, `THIRD-PARTY-NOTICES.md` only if `tests/ThirdPartyNoticesSourceRegression.ps1` asks for it, `docs/codex/STATUS.md`, `.context7/verification.json`. **Tests that read these files:** `tests/PlatformPackageLockstepRegression.ps1`, `tests/ThirdPartyNoticesSourceRegression.ps1`, `tests/InstallerDependencyProvenanceRegression.ps1`, the SourceEdit regression (semantic rename through `MSBuildWorkspace`), the installed smoke's semantic edit step.

**Do:** run `scripts/Apply-DependabotPr.ps1 -Pr 22` detached, as its header says, until `artifacts/dependabot/pr-22.result` reads `READY …` (it reads `RUNNING pid …` while the script works, and `FAILED: …` when it stops; audit 67 removed the stale result of 2026-11-16). Context7 `/dotnet/roslyn` (map: `docs/codex/CONTEXT7.md`), one precise query each, about the two calls Talvora relies on in 5.12: creating an `MSBuildWorkspace` (`RoslynMsBuildBootstrap.cs`) and `Renamer.RenameSymbolAsync` (`RoslynSemanticEditEngine.cs`). These two queries are the whole lookup: this card wins over the NuGet lookup that `docs/MODERNIZATION-POLICY.md` → "Dependabot PRs" mentions. Then commit and push as usual and close PR #22 with `talvora_gh_run` and the comment "Yerelde uygulandı: <kısa commit>". If the semantic edit tests or the installed smoke's semantic step fail, revert and stop with a proposal: do not pin other packages and do not edit the engine.

**Don't:** merge the PR on GitHub, take any other Dependabot change, or upgrade beyond 5.12.0.

**Acceptance:** the script's Full gate and a signed-in Full `Hepsi geçti.`; clean-HEAD deploy and both installed smokes (semantic step green); CI; PR #22 closed.

**P-56 (answered 2026-11-17):** Claude's `[C]` D106 fixed the notice step; Claude's dry run of PR #22 reached `DRY RUN OK` (five Roslyn lines to 5.12.0, no new package). Run the script unchanged and commit with `Card: PK5` (its READY line suggests DEP22; the queue id wins).
