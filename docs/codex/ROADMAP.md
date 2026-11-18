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
  5. `.context7/verification.json` as AGENTS.md → CONTEXT7 PROTOCOL says; when it changes, it holds this change only. There is no Awwwards evidence.
  6. `docs/codex/STATUS.md` updated (queue tick + one Log entry, English), one commit with trailer `Card: <ID>`; mirror the evidence to the active Gitea issue.
- User-facing strings: ordinary Turkish, no tool names / exit codes / stack traces / transport terms. Errors say what went wrong, what is protected, what happens next. Example: `"Bu istek Talvora'ya yerel bir uygulamadan gelmediği için reddedildi. Sistem güvende; bağlantınızı Talvora ayarlarından yeniden yapın."`
- Tool **descriptions** stay English (model-facing). Tool **titles** (human-facing in client approval dialogs) are short Turkish.
- Never print, copy or commit secrets. Never read files in `C:\ProgramData\Talvora\Secrets` content; listing names and ACLs is enough.
- **Security stays proportionate** (user, 2026-11-14): Talvora is a local MCP its owner runs on their own computer by their own choice. A card adds a security or privacy guard only against a concrete, likely accident and never where it makes an ordinary tool task harder; no extra refusals, confirmations or restrictions "just in case". A guard that breaks a normal task is a bug. The agent never adds a guard a card does not name.
- Push as STATUS → Fixed facts → "Push" says.

---

## Audit fixes (external audits by Claude)

D-cards go to the top of the agent queue. Newest audit first.

### Context7 gate review 2026-11-18 (user: "context7 kapısı gerçekten gerekli mi… en verimli şekilde kullanılıyor mu… gerekirse kartlar oluştur") · new agent cards CTX4, CTX5

Measured over Talvora, Classroom and TahtaPilot, 2026-11-01 to 11-18: of 48 evidence entries compared with their diffs, 11 covered a fact a wrong guess could break, 29 asked about an API the repository already called, 8 were defective; no lookup changed an implementation; since 2026-11-08 two commits in three carried only a `noLookup` record (46 here). The user chose "Hafiflet": the lookup tool, the map and the ledger stay, a card still says whether a lookup is needed, and evidence is written and checked only when a lookup was made.

### CTX4 — Context7 evidence only when a lookup was made

**Why:** user decision 2026-11-18. Today a development push must change `.context7/verification.json` even when the card says `Context7: gerekmiyor`, so 46 of the 134 evidence commits since 2026-11-01 exist only to record `noLookup`, and the same rule cost other projects three proposals and six refused pushes (a proposal-only push, a quoting difference). The rule lives in three places that must change together: the push and publish gate (`ProjectDeliveryGate.cs`), the CI and local Context7 check (`Validate-Context7Gate.ps1`) and the local Delivery check (`Validate-ProjectDeliveryGate.ps1`). After this card: a push that leaves the evidence file untouched passes whatever it changes; a push that changes the file is checked exactly as today.

**Files:** `src/Talvora/SourceEditing/ProjectDeliveryGate.cs`, `scripts/Validate-Context7Gate.ps1`, `scripts/Validate-ProjectDeliveryGate.ps1`, `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.QualityAudit.cs`, `tests/Context7ScopeRegression.ps1`, `tests/StrictEvidenceJsonRegression.ps1`, `AGENTS.md` (protected governance text; **this card overrides EXECUTION MANDATE rule 12 for exactly the seven AGENTS.md lines of step 5 and nothing else**, because the rule text and the gate must change in the same commit), `docs/MODERNIZATION-POLICY.md` and `.github/PULL_REQUEST_TEMPLATE.md` (exact replacements, step 6), `docs/codex/STATUS.md`, `.context7/verification.json`.
**Tests that read these files:** `tests/Context7ScopeRegression.ps1` and `tests/StrictEvidenceJsonRegression.ps1` run copies of the two scripts (and `StrictEvidenceJsonRegression.ps1:113` copies `ProjectDeliveryGate.cs`, `GitTools.cs` and `BuildRunnerTools.Dotnet.cs` for the source checks of `Validate-ProjectDeliveryGate.ps1:35-38`, which keep matching: both `EnsureSatisfied` call sites, `TALVORA_DELIVERY_GATE_BLOCKED` and the evidence path stay in the sources). `tests/ModernizationPolicyRegression.ps1` pins `Context7 is mandatory` in `AGENTS.md` (line 35), both script names in `scripts/Invoke-QualityGate.ps1` (lines 210–211) and `"schemaVersion": 1` in the evidence file (line 220): it stays green unchanged, do not edit it. `scripts/Apply-DependabotPr.ps1` names the evidence file in its allow-list and restore pathspecs: unchanged.

**Do:**
1. Red first, four places.
   - a. `SourceEditRegressionRunner.QualityAudit.cs`, `VerifyCtx2PushEvidenceAsync`: replace line 201 (`MustBlock("code with no committed evidence");`) with `ProjectDeliveryGate.EnsureSatisfied(clone, push, ledgerPath);` (a source commit without an evidence change is accepted). Replace the D69 part after the baseline push (today lines 256–267) with two rounds. Round 1: append the source line and commit it (`"CTX4 source change without evidence"`), append the documentation line and commit it (`"CTX4 documentation-only latest commit"`), then for each of the four refspecs (`"HEAD:main"`, `"+main"`, `"HEAD:refs/heads/main"`, `"HEAD:"`) set `push[2]` and call `ProjectDeliveryGate.EnsureSatisfied(clone, push, ledgerPath)`: it must return. Round 2 keeps D69's guarantee (the gate reads the whole pushed range in every refspec form): `await WriteEvidenceAsync(secondQuestion: "git push refspec forms and the remote-tracking branch each one updates");`, commit (`"D69 unproven evidence"`), append one more documentation line and commit it (`"D69 documentation-only latest commit"`), then for each refspec `MustBlock("D69: unproven evidence hidden behind documentation-only tip in " + refspec, id + " \"git push refspec forms and the remote-tracking branch each one updates\"")`. Nothing is pushed between the rounds.
   - b. Same file, `VerifyDeliveryEvidenceAsync`, before its last two statements (today lines 103–104): set `timestamp` to `DateTimeOffset.UtcNow.AddDays(-20).ToString("O")`, `await WriteContextAsync();`, `ProjectDeliveryGate.EnsureSatisfied(root);` must return; set `timestamp` back to now; `File.Delete(contextPath);`, `ProjectDeliveryGate.EnsureSatisfied(root);` must return.
   - c. `tests/Context7ScopeRegression.ps1`: all eight scenarios (lines 63–75) must pass without evidence. Remove the `$RequiresEvidence` parameter from `Assert-Context7GateScenario` and from all eight calls, and collapse its `if`/`elseif` into one check: a scenario whose gate run throws fails with `"A change without Context7 evidence must pass: $RelativePath"`.
   - d. `tests/StrictEvidenceJsonRegression.ps1`, delivery part, after the HTTPS case (today line 128) and before line 130: set `$delivery.vendorSources = @()` and `$delivery.verifiedAtUtc = [DateTime]::UtcNow.AddDays(-20).ToString('yyyy-MM-ddTHH:mm:ssZ')`, write the file, run `$deliveryGate` in the `try`/`catch` form of lines 123–125: any failure throws `'CTX4: the delivery check refused evidence older than 14 days.'`. Then `[IO.File]::Delete($deliveryPath)` and run it again the same way: any failure throws `'CTX4: the delivery check refused a repository without an evidence file.'`. The Context7 part of the file (lines 33–106, the 14-day case included) stays as it is: there the evidence file is part of the change, so it is still validated.
   Expected red on the current code: (a) `TALVORA_DELIVERY_GATE_BLOCKED: Context7 evidence must be committed with source changes`; (b) `Context7 evidence verifiedAtUtc must be today (UTC).`; (c) `A change without Context7 evidence must pass: src/Talvora.Tray/ControlCenter.xaml` (the test's own message; the gate's `Context7 evidence must be refreshed in the same change` is swallowed by the test); (d) `Context7 evidence is older than 14 days.`
2. `ProjectDeliveryGate.cs`, push path (`EnsureSatisfiedCore`): after the documentation-only return, a push whose changed paths do not contain `Context7Evidence` returns (this replaces the refusal at lines 44–45; its message disappears). A push that changes the file continues exactly as today: the committed file's form, today's UTC date, the limits, `(query: "…")`, `noLookup` of at least 20 characters when `libraries` is empty, `vendorSources`, and the 24-hour ledger match. Publish path (`pushArguments is null`): a missing working-tree file returns without a check; an existing file is checked as today, except that its date no longer has to be today. `ValidateContext7` already receives `committed`: when it is `false`, return an empty list before `ReadEvidence` if the file does not exist, and pass the flag on so that `RequireFreshTimestamp` applies its `must be today (UTC)` comparison only when `committed` is `true`; its valid-timestamp and future checks apply on both paths. `ReadEvidence` itself does not change.
3. `scripts/Validate-Context7Gate.ps1`: replace the refusal at lines 48–52 with `Write-Host 'CONTEXT7_QUALITY_GATE_GREEN (no lookup evidence in this change)'` and `return`. Everything after it (the file's validation when it is among the changed files, the 14-day rule included) stays.
4. `scripts/Validate-ProjectDeliveryGate.ps1`: when `$context7Path` does not exist, skip the evidence checks (lines 22–30) and run the source checks (lines 32–38) as today. When it exists, check it as today but delete the `older than 14 days` refusal (line 19); the strict JSON, `schemaVersion`, future-timestamp, `libraries`/`noLookup` and HTTPS checks stay.
5. `AGENTS.md`, exact replacements (line numbers as of `5954f5c`; replace each whole line with the one line inside the fence, without the fence indentation):
   - line 83 →
     ```text
     1. **What to look up:** what the card's `**Context7:**` line names, before coding. When the card says `Context7: gerekmiyor (<reason>)`, make no lookup and leave `.context7/verification.json` untouched. Documentation-, proposal- and test-only changes make none. One exception: when the code you are about to write calls a package, platform API, CLI flag or protocol that this repository has never used (`talvora_search_text` finds no earlier call) and the card names no lookup for it, make that one lookup, record it (rule 6) and name it in the Log entry. Never look up an API the repository already calls.
     ```
   - line 88 →
     ```text
     6. **Evidence, only when a lookup was made** (user decision 2026-11-18): replace `.context7/verification.json` with `schemaVersion` 1, `verifiedAtUtc` (now, UTC), `scope` (`<card>: <what>`), `libraries[]` and `vendorSources[]`. Each `libraries[]` entry has `libraryId` = the id actually queried (versioned if used) and `verified` = `<date> <card>: <the one fact the code relies on> (query: "<the question exactly as asked>")`; `vendorSources[]` = only the official pages actually opened (none when Context7 answered). Only what this card used, at most 12 each. A card without a lookup does not touch the file and writes no `noLookup` record. An id, question or URL not actually used is a critical audit finding, and so is a lookup the card named that was not made.
     ```
   - line 90 →
     ```text
     8. **Context7 unavailable:** when `talvora_context7_docs` answers `Context7 HTTP 429` or fails twice for a network reason, do not stop and write no proposal. Open the official page the map or the card names for that fact with `talvora_http_request`, rely only on what it says, leave `.context7/verification.json` untouched and write `Context7 unavailable (<reason>): <fact> checked on <URL>` into the Log entry. Claude checks the page at the audit. Never switch to another Context7 client instead.
     ```
   - line 194 →
     ```text
     - How to query, which ids to use and what to record: **CONTEXT7 PROTOCOL** (map: `docs/codex/CONTEXT7.md`). The card names the library and the exact point to verify: verify that point, not a generic topic.
     ```
   - line 195 →
     ```text
     - Context7 is mandatory where the card names a lookup and for the first use of an external API in this repository (CONTEXT7 PROTOCOL rule 1); every other change makes no lookup and needs no evidence (user decision 2026-11-18).
     ```
   - line 196 →
     ```text
     - `.context7/verification.json` changes only in a commit that made a lookup, and then holds **only that change**: replace `verifiedAtUtc`, `scope`, `libraries` (at most 12) and `vendorSources` (at most 12), never append; earlier evidence lives in git history. Record the library ids and the verified point in the card's STATUS Log entry.
     ```
   - line 201 →
     ```text
     - Context7 evidence is checked by Talvora code, not by a prompt convention. When a push changes `.context7/verification.json`, `talvora_git_run` push refuses it unless the file is well-formed, dated today (UTC) and every `(query: "…")` matches a question `talvora_context7_docs` recorded in the last 24 hours. A push that leaves the file untouched needs no evidence, whatever it changes. `talvora_dotnet_publish` refuses only a malformed file. Handoff text, model memory, issue prose or a verbal claim never counts as evidence of a lookup.
     ```
6. `docs/MODERNIZATION-POLICY.md`, exact replacements (replace each whole line with the one line inside the fence, without the fence indentation):
   - line 7 →
     ```text
     1. **Current documentation gate** — Where a card names a lookup, or a change makes the first use of an external API in this repository, the change starts with a Context7 lookup through `talvora_context7_docs` and records it in `.context7/verification.json` in the same change (library ID and the verified behavior or migration rule); official vendor documentation confirms release, support and breaking-change facts. A change without a lookup leaves the file untouched (user decision 2026-11-18).
     ```
   - line 40 →
     ```text
     **Implementation:** keep the durable rule in `AGENTS.md`; when `.context7/verification.json` changes, validate its form, age and library IDs with `scripts/Validate-Context7Gate.ps1` and its questions against Talvora's lookup ledger at push; retain the human-readable Context7/vendor evidence in the active Gitea issue/PR.
     ```
   - line 42 →
     ```text
     **Exit criteria:** CI and the push gate fail a change whose Context7 evidence is malformed, stale or names a question that was never asked.
     ```
   - line 117 →
     ```text
     Context7 evidence is recorded only for a change that made a lookup (AGENTS.md → CONTEXT7 PROTOCOL); a change without one leaves `.context7/verification.json` untouched.
     ```
   `.github/PULL_REQUEST_TEMPLATE.md`: line 7 → `- [ ] Where the card names a Context7 lookup, it was asked with talvora_context7_docs.`; line 8 → `- [ ] .context7/verification.json changed only if a lookup was made, and then holds this change only.`

**Don't:** change `talvora_context7_docs`, the ledger, the 24-hour match, the today rule for a changed file, `IsDocumentationPath`, `GetPushChangedPaths` or any refusal that applies to a file that changed; edit `tests/ModernizationPolicyRegression.ps1`, `scripts/Apply-DependabotPr.ps1`, `docs/codex/CONTEXT7.md` or `docs/codex/CODEX-PROMPT.md`; touch other AGENTS.md lines.

**Context7:** `Context7: gerekmiyor (Talvora's own gate code, scripts and tests; the git and System.Text.Json calls are already used in these files)`. For this one card still write the `noLookup` record as before: its push may run through the installed gate of the previous build, and the new gate accepts it too.

**Acceptance:** red first as listed in step 1; then `pwsh tests/Context7ScopeRegression.ps1` → `CONTEXT7_SCOPE_REGRESSION_GREEN`, `pwsh tests/StrictEvidenceJsonRegression.ps1` and `pwsh tests/ModernizationPolicyRegression.ps1` green, and both changed methods through `dotnet run --project tests/Talvora.SourceEdit.Regression/Talvora.SourceEdit.Regression.csproj -c Release -- --quality-audit-only` → last line `TALVORA QUALITY AUDIT REGRESSION GREEN` (it builds and runs git fixtures: start it detached with a log, as AGENTS.md → Session discipline describes for long commands); signed-in Changed and Full `Hepsi geçti.`; installer deploy and both installed smokes (the gate is service code); CI. `Claude, next audit:` CTX5's push (a test-only commit that leaves the evidence file untouched) went through the installed gate.

### CTX5 — the installed smoke asks Context7 live at most once a day

**Why:** Talvora's ledger, 2026-11-08 to 11-18: 220 questions, 154 of them from the full installed smoke itself (77 runs × 2: one `/dotnet/docs` lookup answered 200 and one `/talvora-smoke/does-not-exist` lookup answered 404; 467 KB downloaded), against 66 real questions from agents. On 2026-11-08 the smoke's own lookup was answered 429. The user's Context7 quota is spent mostly on self-tests; one live proof a day shows the same thing.

**Files:** `tests/Talvora.Smoke/SmokeScenarios.SharedInfrastructure.cs`, `docs/codex/STATUS.md`. No other file reads this block: `CTX1 live` appears only there, and `tests/Talvora.Notification.Regression/Program.CTX1.cs` uses its own fake HTTP client and does not change.

**Do:**
1. In `RunSharedInfrastructureAsync`, inside `if (byName is not null)` and before the live lookup (today line 29): read today's ledger once, with the path expression of lines 39–41 (move the `evidencePath` declaration up so both uses share it), through `SmokeSupport.ReadToolText(byName["talvora_read_text"], evidencePath)`. Any exception means "not asked today" (the file does not exist before the day's first question). "Asked today" = some line of that text contains all four of these ordinal substrings: `"caller":"Talvora.Smoke"`, `"libraryId":"/dotnet/docs"`, `"status":200` and the value of `context7Question`. A real ledger line, measured 2026-11-17:
   `{"ts":"2026-11-17T00:38:48.8550448Z","caller":"Talvora.Smoke","libraryId":"/dotnet/docs","query":"HttpClient Timeout and HttpMessageHandler for tests with a capped response read","status":200,"bytes":5684,"sha":"e645b76fba6f22db"}`
2. Asked today → skip the live lookup with its ledger wait and the 404 lookup (today lines 29–71, from `var answer = …` to the `CTX1 live Context7 404 did not name the library.` check). Not asked today → that part runs unchanged.
3. The tail-wait part that follows in the same `if` block (from `var tailTool = byName["talvora_tail_text"];`, today line 73) always runs.

**Don't:** change `talvora_context7_docs`, the ledger, the question text or the `--shared-infrastructure-only` path; add a cache file, a setting or a command-line switch; skip the lookups by any rule other than today's ledger.

**Context7:** `Context7: gerekmiyor (string checks on Talvora's own ledger lines)`. This is the first card under CTX4: do not touch `.context7/verification.json`.

**Acceptance:** the smoke has no unit seam, so red is a measurement. Red, before the change: count the lines containing `Talvora.Smoke` in today's `C:\ProgramData\Talvora\Context7\lookups-<UTC date>.jsonl` (`talvora_read_text`), run the full installed smoke (AGENTS.md → Session discipline → Deploy row, the full run, detached, log `artifacts/CTX5-smoke.log`), count again: +2. Green, with the changed smoke on the same UTC day: the run ends `TALVORA MCP SMOKE GREEN` and the count is unchanged. Write the three counts into the Log entry. The +2 on a day's first run is `Claude, next audit: the first full smoke of a UTC day adds two Talvora.Smoke ledger lines`. Signed-in Changed and Full `Hepsi geçti.`; no deploy (test project only); CI. The push leaves the evidence file untouched and must pass (CTX4's live proof); if it is refused with `Context7 evidence must be committed`, the installed build predates CTX4: deploy HEAD's installer as the Deploy row says and push again.

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
