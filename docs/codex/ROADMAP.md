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

### Audit 2026-11-14 (55, deep) · range `audit/2026-11-13-54..87628f8` (WL5, WL6, RL5) · D71–D74, D76 and D78 for the agent; Claude shipped `[C]` D75 and D77

Evidence for D71–D73 comes from the real worklog history (`%LOCALAPPDATA%\Talvora\ControlCenter\worklog-sessions.jsonl`, sessions of 2026-11-13 21:59–22:26 UTC) and from Claude's live run on 2026-11-14 (a followed Full gate with a failed `git fetch` and a `git status` during it). The rules these cards implement are in `docs/codex/DESIGN-WORKLOG.md` §6.4 rules 5–8 and its tables (Claude updated them in this audit). None of them changes the tray or the frame protocol.

### D71 — A deploy continues the agent's card instead of splitting it (WL6 follow-up)

**Why:** after RL5's deploy the new service replayed the deploy step into a second in-memory session that has the agent's session id: `ReplayPendingDeploymentAsync` stores it as `_sessions["deploy:" + id]`, and the agent's next call creates its normal session (key = project root + actor, same id, because the id is the hash of that key). `CreateWorklogFrame`, `PrepareCodeEvidence` and `IsWorklogSessionClosed` find a session by id with `FirstOrDefault`, so from then on every frame of the agent was built from the stale replay session: timeline frozen at "Yeni sürüm çalışıyor · 87628f8", `Steps` 1, no `ne`, no live line, no task note. 90 s after the replay ended that session counted as closed, so every later frame carried `SessionClosed: true`: the tray retired the card after each step and stored one history entry per step. Real history: the CI step after the deploy ended as "GitHub denetimi geçti" with the deploy row as its only timeline row and `steps 1`. It lasts until the service restarts, so the whole next card is affected.

**Files:** `src/Talvora/WorklogFollower.cs`, `src/Talvora/TalvoraDesktopWorklogState.cs`, `tests/Talvora.Notification.Regression/Program.WL6.cs`, `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** the Notification regression; `tests/ModernizationPolicyRegression.ps1` requires `GetOrCreateSession`, `_activeWorklog`, `session.Failure`, `session.Cancellation` and `SHA256.HashData` to stay in `TalvoraDesktopWorklogState.cs` (keep those names).

**Do:**
1. Red first in `Program.WL6.cs`, with the notifier's delivery observer: a session (one actor, one project) with three finished steps, two changed files and a task note runs the deploy step; a second notifier instance (the "new service") replays the pending file. Assert the replay frame continues that session: same id, `Totals.Steps` 4, `FilesChanged` 2, the task note, the timeline newest first = the deploy row followed by the three earlier rows. Then a new step from the same actor and project: its frames show its purpose, `Steps` 5, five timeline rows and `SessionClosed` false; a followed step started afterwards puts its live fact into the frame. Also assert that the notifier never holds two sessions with the same id. Tests run as the signed-in user and cannot write `C:\ProgramData\Talvora\Worklog`: give the writer side the same path parameter the replay already takes (an internal property or constructor argument whose default is `WorklogFollower.PendingDeployPath`) and use a temporary file in the test.
2. `PendingDeploy` carries what the next frame needs to continue the session: the session key exactly as `ResolveSessionIdentity` builds it, `BeganUtc`, `Steps`, the timeline entries (at most `DesktopProgressProtocol.MaximumTimelineEntries`), the task note, the changed-file paths (at most 500) and the last build and test states. `TakePendingDeployAsync` accepts files up to 256 KB (today 16 KB); every other check stays.
3. `ReplayPendingDeploymentAsync` restores that session under its key (never under `"deploy:" + id`) and finishes the deploy step in it, so the agent's next calls find it through `GetOrCreateSession`. If a session with that key already exists (a call arrived first), the deploy step is finished in that session instead. A pending file without a key (written by any build before D71, so also by the deploy of this card itself) is replayed into a session that is removed from `_sessions` right after its terminal frame is queued.
4. Finding a session by id must return the one live session: keep sessions unique by id (for example a guard that never adds a second session with an existing id, or one dictionary by id kept together with `_sessions`). No `FirstOrDefault` over possible duplicates.

**Don't:** change the tray, the frame protocol or `Deploy-Windows-Installer.ps1`; poll anything; persist anything except the fields in step 2.

**Context7:** gerekmiyor (Talvora's own session bookkeeping and its own JSON file).

**Acceptance:** red first; Notification regression and `ModernizationPolicyRegression.ps1` green; signed-in Full `Hepsi geçti.`; deploy (this deploy itself replays an old keyless file: its card must still end as "Yeni sürüm çalışıyor · {kısa sürüm}") and both installed smokes; CI; `[~] visual check pending (Claude)` (Claude reads the deploy of the next card in Çalışma günlüğü: one session, its earlier steps kept).

### D72 — Followed jobs end with plain, measured results and never hide the agent's other steps (WL6 follow-up)

**Why:** four defects in WL6's follower (§6.4 rules 6 and 7 and the live-line table now say what is right):
(a) A step that ends while a followed job is still running never becomes the card's current step: `EndWorklog` sees the followed job as "still running" and publishes its running title instead, so the finished step's result, `sonuç` and code preview never reach the card (the code terminal misses every diff applied during a followed gate, or during a CI follow of up to 30 minutes), and a failure in that time stays pending and later replaces the followed job's own result. Live, audit 55: a failed `git fetch` and a `git status` during a followed gate reached only the timeline.
(b) `FinishProcess`'s fallback writes "çıkış kodu N · {süre}" into the visible fact (AGENTS.md: no exit codes in user-facing copy); a failing smoke or metadata smoke ends with the generic title "İşlem hata ile bitti"; a passing smoke repeats its title in the fact ("Kurulu sürüm testi geçti" + "Kurulu sürüm testi geçti · …").
(c) CI: the fact is the time Talvora followed the check (real history: "GitHub denetimi geçti · 35,1 sn" for a run of several minutes); a `Get-CiStatus.ps1` that throws (gh signed out, network) exits 1 and is reported as "GitHub denetimi başarısız"; the failure reason is the raw first output line ("CI başarısız (failure): https://…").
(d) Gate live line: after a stage's own `PASS` the live line keeps that stage while the following quiet checks run (live: "şu an: Yükleyici çalışma testi" during the remaining PowerShell tests).

**Files:** `src/Talvora/WorklogFollower.cs`, `src/Talvora/TalvoraDesktopWorklogState.cs`, `src/Talvora/TalvoraDesktopProgressNotifier.cs`, `scripts/Get-CiStatus.ps1`, `tests/Talvora.Notification.Regression/Program.WL6.cs`, `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** the Notification regression (`Program.D46.cs` runs `Get-CiStatus.ps1` arguments; `Program.WL6.cs` fakes its output "CI başarılı"); `ModernizationPolicyRegression.ps1` (names listed in D71); `QualityGateRegression.ps1` (gate lines, unchanged here).

**Do:**
1. Red first in `Program.WL6.cs`, one case each for (a)–(d), including: a source edit that ends while a followed gate runs is published with its own title, fact and code preview; a failed step during a followed job does not replace the followed job's final result; a 30-minute CI follow does not stop a later step from becoming current.
2. (a) Followed steps are background work. In `EndWorklog`, the "another step is still running" check and the pending failure and cancellation ignore followed steps: a step that ends while only followed steps remain is published exactly like a normal end (title, fact, evidence and code preview, changes), and the session is not scheduled for closure while a followed step remains. A followed step refreshes its live line only while no newer step has started in its session; when it ends, its final result is published (it is then the newest event) and no earlier foreground failure replaces it. Mark followed operations explicitly (for example a flag on the active item set by the follow start), never by tool name.
3. (b) `FinishProcess`: the fact is `{süre}`; the exit code goes only into the evidence `Result` in today's technical form (`Sonuç: tamamlanamadı  //  kod {n}`). Installed smoke: success title "Kurulu sürüm testi geçti", fact `{süre}`; `TALVORA MCP SMOKE RED` → title "Kurulu sürüm testi başarısız", reason "Kurulu sürüm testi bir adımda durdu; ayrıntı günlükte.", the first non-empty line after the RED line (redacted, at most 160 characters) in the evidence `Result`. Metadata smoke: "Araç tanımları denetimi geçti" / "Araç tanımları denetimi başarısız", reason "Araç tanımları denetimi bir adımda durdu; ayrıntı günlükte." (the smoke prints the same RED line in both modes).
4. (c) `Get-CiStatus.ps1`: `gh run list` also asks `startedAt,updatedAt`; the verdict lines become `CI başarılı ({süre}): <url>` and `CI başarısız ({conclusion}, {süre}): <url>`, where `{süre}` is `updatedAt − startedAt` in §6.4 rule 3 form (`0,8 sn`, `12,4 sn`, `4 dk 12 sn`, `1 sa 3 dk`; Turkish decimal comma). Nothing else in the script changes (exit codes, the other lines, the failing-line filter). The follower takes the fact from that parenthesis. Exit 1 is a CI failure only when the output's first line starts with `CI başarısız`: reason "GitHub denetimi başarısız oldu; ayrıntılar CI günlüğünde.", the first failing line (redacted, at most 160 characters) in the evidence `Result`. Any other non-zero, non-2 result is retried at the next 30-second check; three such results in a row end the step as cancelled, title "GitHub denetiminin sonucu alınamadı", reason "GitHub'a ulaşılamadı; sonuç bir sonraki denetimde okunacak."
5. (d) `LiveFact`: `{aşama}` is the newest `START` that has no `PASS`/`FAIL` line of its own after it; when that stage has already finished, the newest enclosing `START` that is still open (e.g. `PowerShellRegressions`) is shown; with none open, the newest `START`. Counting stays as today.

**Don't:** change the tray, the frame protocol, the gate's existing lines or `Get-CiStatus.ps1`'s exit codes; add polling beyond today's 2 s log read and 30 s CI check; show any number the job did not print or Talvora did not measure.

**Context7:** `gh` is not in the map: check the `startedAt` and `updatedAt` JSON fields of `gh run list` on `https://cli.github.com/manual/gh_run_list` (CONTEXT7 PROTOCOL rule 2: record the page in `vendorSources` and add `Context7 map candidate: gh <version>` to the Log entry).

**Acceptance:** red first; Notification regression green; signed-in Full `Hepsi geçti.`; deploy and both installed smokes; CI; `[~] visual check pending (Claude)` (Claude edits a file during a followed gate and watches the card).

### D73 — WL5's facts come only from a successful answer; gate scope labels; P-57 (WL5 follow-up)

**Why:**
(a) `DescribeWl5ResultFact` ignores the exit result, so a failed step gets a success-shaped fact: a failed `git fetch` reads "yeni commit yok", a failed `git status --short` "değişiklik yok", a failed `git add <paths>` "{n} dosya". `git fetch -q` prints nothing and still reads "yeni commit yok". `git status --short --branch` counts its `## main...origin/main` line as a changed file (live, audit 55: a clean tree gave `## main...origin/main`). The WL5 tests cover only exit 0.
(b) `BuildTerminalEvidence` now appends every initial evidence summary to the fact, so a `dotnet_build`/`dotnet_test` step's card reads "0 hata · 2 uyarı  //  Hedef: …  //  Yapı: Release" in its `sonuç` row (`VisibleFactOnly` removes only `Komut: …`); before WL5 it read "0 hata · 2 uyarı". §6.4 rules 2 and 5.
(c) `ScriptMode` labels a gate without `-Scope` "hızlı", but the gate's default scope is `Full` (real history: RL5's 164-second full gate read "Kalite kontrolü geçti · hızlı"); `-Scope Changed` gets no label. §6.1 now: none or `Full` → "tam", `Fast` → "hızlı", `Changed` → "değişenler".
(d) P-57 is decided: option 1 (below).
(e) `cmd.exe` is not treated as a shell: an inline `cmd.exe /d /c …` step reads "cmd.exe hata ile bitti" (live, audit 55), against §6.4 rule 2.

**Files:** `src/Talvora/TalvoraDesktopProgressFacts.cs`, `src/Talvora/TalvoraDesktopProgressEvidence.cs`, `src/Talvora/WorklogSubject.cs`, `src/Talvora.Tray/Worklog/WorklogPreviewScenarios.cs`, `docs/codex/reference/worklog-phosphor/src/WorklogPreviewScenarios.cs`, `docs/codex/reference/worklog-phosphor/SHA256SUMS.txt` (that file's line only), `tests/Talvora.Notification.Regression/Program.WL5.cs`, `tests/Talvora.Notification.Regression/Program.N9Subjects.cs` (gate scope rows), `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** the Notification regression (N9Subjects pins the gate titles: update exactly the rows (c) changes); `ModernizationPolicyRegression.ps1` compares `WorklogPreviewScenarios.cs` and its reference copy with `SHA256SUMS.txt`; the Control Center smoke renders the previews.

**Do:**
1. Red first in `Program.WL5.cs`: failed `git fetch`, `git status --short` and `git add a.cs` (exit 128 with real stderr such as `fatal: 'x' does not appear to be a git repository`) → no parsed fact; `git fetch -q` with exit 0 and empty output → `Sonuç: tamamlandı · {süre}` and no fact; `git status --short --branch` with `## main...origin/main` and ` M a.cs` → "1 değişmiş dosya", and with only the `##` line → "değişiklik yok"; a successful `dotnet_build` keeps exactly the fact it had before WL5 (no `Hedef:`); gate titles for no `-Scope`, `Full`, `Fast` and `Changed`.
2. Parse facts only when the call succeeded (exit code 0, not timed out, not an error result). `fetch` with `-q` or `--quiet` → no fact. `status` counts only entry lines (skip lines that start with `## `).
3. In `BuildTerminalEvidence` only the `Komut: …` summary is appended to the terminal summary; any other initial summary (`Hedef: … // Yapı: …` of `dotnet_build`/`dotnet_test`) is left out as before WL5.
4. `ScriptMode`: none or `Full` → "tam", `Fast` → "hızlı", `Changed` → "değişenler". `cmd`/`cmd.exe` with `/c` or `/k` and no `*.ps1` or program file in its command text is an inline shell command like `pwsh` (titles "Komut çalışıyor / Komut tamamlandı / Komut hata ile bitti"; `Komut: cmd.exe` only in the technical evidence); add a red case for `cmd.exe /d /c exit 3`.
5. P-57, option 1: in `src/Talvora.Tray/Worklog/WorklogPreviewScenarios.cs` and in its reference copy `docs/codex/reference/worklog-phosphor/src/WorklogPreviewScenarios.cs`, replace each of the four "Git işlemi tamamlandı · pull origin/main" with "Uzak değişiklikler alındı · origin/main" and change nothing else; then replace that file's line in `docs/codex/reference/worklog-phosphor/SHA256SUMS.txt` with the normalised hash of the new content (AGENTS.md rule 5 method). The two files must stay identical.

**Don't:** change any other title, the card, redaction or any other preview string; add git calls.

**Context7:** gerekmiyor (parsing of Talvora's own captured outputs and its own title table).

**Acceptance:** red first; Notification regression and `ModernizationPolicyRegression.ps1` green; signed-in Full `Hepsi geçti.`; deploy and both installed smokes; CI; `[~] visual check pending (Claude)`.

### D74 — The release check reads GitHub's whole answer (RL5 follow-up)

**Why:** `ReleaseCheckService.CheckOnceAsync` ignores every answer over 16 384 bytes. GitHub's latest-release JSON carries the release notes and every asset with its uploader: measured 2026-11-14, `cli/cli`'s latest release is 43 460 bytes (22 assets, 4 503-character notes), about 1.6 KB per asset. Talvora's release has two assets and Turkish notes with an English summary, so a longer note passes 16 KB, and then the check fails silently on every run: the new-version notice never appears.

**Files:** `src/Talvora.Tray/ReleaseCheckService.cs`, `tests/Talvora.Notification.Regression/Program.RL5.cs`, `.context7/verification.json`, `docs/codex/STATUS.md`.

**Do:** red first in `Program.RL5.cs`: a fake answer of about 40 KB (a long `body`, a newer `tag_name`, an approved `html_url`) sets the new-version state; an answer over 262 144 bytes is ignored. Then raise the bound to 262 144 bytes; keep reading incrementally into a buffer of that size plus one byte and keep ignoring anything larger.

**Don't:** change the address, headers, timeout, schedule, cache or texts.

**Context7:** gerekmiyor (a size constant in Talvora's own reader).

**Acceptance:** red first; Notification regression green; signed-in Full `Hepsi geçti.`; deploy and both installed smokes; CI.

### D76 — The Control Center smoke always shows its own window

**Why:** the gate's ControlCenter check (`Start-Process … --control-center-smoke -WindowStyle Hidden`) failed twice with `UI5 raw log timer must run when the panel is visible.` (tray log 2026-11-08 14:52 and 2026-11-14 01:51). Both runs logged "keyboard focus not granted to the hidden smoke window"; the 116 runs that logged "keyboard focus kept" all passed. So in those two runs the shell window was not visible when the UI5 assertions ran (the raw-log timer only runs while its view `IsVisible`). The likely cause is the hidden start-up state Windows hands to a process's first shown window; whatever the cause, the smoke must make its own window visible before it asserts anything about visibility. A flaky ControlCenter check stops agent cards that changed nothing there.

**Files:** `src/Talvora.Tray/ControlCenterSmoke.cs`, `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** the gate's ControlCenter check runs this smoke.

**Do:**
1. Red first: a test-only switch for the smoke, the environment variable `TALVORA_CONTROL_CENTER_SMOKE_START_HIDDEN=1`, hides the shell window right after the smoke has shown it (`window.Hide()`). Run the smoke with it: today it fails at the UI5 assertion (red).
2. Add one visibility guard that runs after the shell window is shown and before the first assertion: when the window is not visible or is minimized, set `WindowState` to `Normal`, call `Show()` without activating it, wait for one render pass (`Dispatcher.Invoke` at `DispatcherPriority.Render` or `ContentRendered`), then write one tray log line: "Control Center smoke window made visible again." Nothing else changes: the window keeps the position the smoke gives it today.
3. Run the smoke with the switch again: green. Run it three times without the switch, started with `Start-Process -WindowStyle Hidden` exactly as the gate does: green each time.

**Don't:** change the gate script, other smoke assertions or the window position; weaken the UI5 assertion; leave the switch active outside the smoke.

**Context7:** `/dotnet/docs-desktop`, query "WPF Window.Show ShowActivated false and WindowState Normal to make a hidden window visible without activation .NET 10".

**Acceptance:** red first with the switch; green with the switch and for three hidden starts; signed-in Full `Hepsi geçti.`; CI.

### D78 — A documentation push never hides an unverified code push from CI

**Why:** `windows-ci.yml` runs in one concurrency group per branch with `cancel-in-progress: true`, and its "Classify the change" step decides documentation-only from the push's own range (`event.before..event.after`). Audit 55, live: Claude pushed 18cae76 (a test change) and then f8165d3 (STATUS only). The 18cae76 run was cancelled; the f8165d3 run classified its range as documentation-only and skipped the build and every test (30 s). `Get-CiStatus.ps1` accepts "the newest run that contains this commit" for a cancelled run, so 18cae76 would have read green although CI never built it (Claude re-ran the cancelled run by hand). An agent that pushes a card commit and then a STATUS-only commit gets the same false green.

**Files:** `.github/workflows/windows-ci.yml`, `scripts/Get-CiStatus.ps1`, `tests/CiDocsOnlyRegression.ps1`, `.context7/verification.json`, `docs/codex/STATUS.md`. **Tests that read these files:** `CiDocsOnlyRegression.ps1` and `ModernizationPolicyRegression.ps1` read `windows-ci.yml`; the Notification regression runs `Get-CiStatus.ps1` arguments (`Program.D46.cs`); D72 also changes `Get-CiStatus.ps1` (its verdict lines): if D72 is already in, keep its lines.

**Do:**
1. Red first in `CiDocsOnlyRegression.ps1`: assert that `windows-ci.yml` does not cancel a running pipeline (`cancel-in-progress: false`), and that `Get-CiStatus.ps1`, on its cancelled-run path, reads the deciding run's jobs (`gh run view <id> --json jobs`) and requires the step "Validate build and all test suites (including SourceEdit)" to have concluded `success`.
2. `windows-ci.yml`: `cancel-in-progress: false`, so every push runs to its end (a documentation-only run takes about 30 s; the repository is public, so the minutes cost nothing). Keep the concurrency group, so runs of one branch queue instead of overlapping.
3. `Get-CiStatus.ps1`: when the run that decides a commit is not that commit's own run (the cancelled-run path), accept it only if that step concluded `success`; a skipped step proves nothing for this commit → print "CI: {kısa hash} çalışması iptal edildi; onu kapsayan yeni çalışma kodu derlemedi." and exit 2. Everything else in the script stays.

**Don't:** change the classification rules, the build and test steps or the other workflows.

**Context7:** `/websites/github_en_actions` (mapped by RL4), query "concurrency cancel-in-progress false queues runs of the same group"; the `jobs[].steps[].conclusion` fields of `gh run view --json jobs` on `https://cli.github.com/manual/gh_run_view` (`gh` is not in the map: CONTEXT7 PROTOCOL rule 2, with a `Context7 map candidate` line).

**Acceptance:** red first; `CiDocsOnlyRegression.ps1` and the Notification regression green; signed-in Full `Hepsi geçti.`; CI.

### D48 — A vault note belongs to the vault's owner (Claude, `[C]`)

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

### D49 — Plain Turkish service names, also for older registries (Claude, `[C]`)

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

### D47 — Plain Turkish for the remaining Control Center texts (Claude, `[C]`)

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

**Audit 51 addition (setup window texts):** the installer's progress texts mix three dots with `…` and "hizmet" with "servis" ("Talvora hizmeti durduruluyor..." next to "Talvora servisi başlatılıyor..."). When Claude ships D47, every progress text in `src/Talvora.Installer/*.cs` ends with `…` (U+2026) and says "servis", as the Control Center does; D67 already changes the PowerShell download text. Re-check `NativeInstallerSourceRegression.ps1` for pinned texts first.

**Audit 53 addition (seen on the real screens 2026-11-13):** Olaylar shows "Yönetim Merkezi hazır" (`TrayApplicationContext.cs`) while the product is "Kontrol Merkezi" → "Kontrol Merkezi hazır"; "Kayıtlı servis/görev zinciri ve güvenli tünel birlikte işlendi." (`ControlCenterLifecycleService.cs`) → "Servis, görev ve güvenli tünel birlikte başlatıldı."; Çalışma günlüğü lists a finished session with the headline "GitHub denetimi sürüyor" under "Tamamlandı" (`TalvoraDesktopProgressNotifier.cs`, the terminal title when CI was still running) → "GitHub denetimi henüz bitmemişti" for that terminal title only (the live card keeps "sürüyor"); Genel bakış repeats "4/5 bileşen hazır…" as "4 hazır · 1 kapalı" right below it → drop the second line (Claude's look work).

### WL3 — Çalışma günlüğü and Olaylar polish (Claude)

**Why:** audit 42 screen pass; user decisions 2026-11-08 ("Sıraya koy, sonra yap" for the look fixes and the list mark; setup names in parentheses). Build after D45 and D50, which change the same page's data.

**Do (Claude, under the agent rules: smoke first, Full gate, deploy, captures in both themes, CI):**
1. `WorklogHistoryView.xaml`, detail column: the task note, the fact line, the evidence summary and the evidence result collapse when their text is empty (one `HistoryOptionalText` style on `Tv.Type.Body` with triggers `Text` = "" and `{x:Null}` → `Visibility` `Collapsed`), so no empty line leaves a 40 px hole.
2. `WorklogHistoryView.xaml`, list card: the bold outcome line and the time line become one Caption line: an 8 px dot (`Tv.Ready` completed, `Tv.Offline` failed, `Tv.TextMuted` cancelled, `Tv.Attention` running), the outcome word, " · ", `ListTimeText`. The card loses one line.
3. `EventsView.xaml`: when the filtered list holds no event, a centred `Tv.Type.Body` line in `Tv.TextMuted`: "Bu süzgeçte olay yok." (with search text: "Aramayla eşleşen olay yok.").
4. `EventsView.xaml`: while the raw log is open the toggle reads "Ham günlüğü gizle" (content and `AutomationProperties.Name`).
5. `StepperCard.xaml` setup labels: "Talvora Dev tünel kimliği (tunnel ID)", "Talvora Admin tünel kimliği (tunnel ID)", "OpenAI Admin API anahtarı (Admin API key)" (label and automation name; the placeholder "Admin API key" stays).
6. `ControlCenterSmoke.D44.cs`: after the click, assert that the Olaylar list holds the recovery event the smoke applied (feed it through the snapshot the Olaylar page reads); the light-theme capture of audit 42 showed an empty list.
7. `WorklogHistoryView.xaml`, detail column, right under the `OutcomeLabel` line (audit 43, user decision 2026-11-08 "Evet, nedeni göster"; the data is already in `WorklogOutcome`): `{Binding Current.Outcome.PlainReason, TargetNullValue='', FallbackValue=''}` in `Tv.Type.Body` with `Tv.TextMuted`, then `{Binding Current.Outcome.NextStep, StringFormat='Sonraki adım: {0}', TargetNullValue='', FallbackValue=''}` in `Tv.Type.Caption`; both use the `HistoryOptionalText` collapse of item 1, so a completed session shows neither line.
8. `WorklogHistoryView.xaml`: the "Kanıtlar" heading collapses when the session has no evidence (summary, result, files and code preview all empty), with the same trigger style; check the bound values on a real session without evidence first (audit 43 screen pass: the "scratchpad" session showed the heading alone).
9. `WorklogHistoryView.xaml`: with search text and no matching session, the empty line reads "Aramayla eşleşen çalışma yok." instead of "Henüz kayıtlı çalışma yok." (README screen pass 2026-11-08). After WL3, Claude takes the README screenshots again before the first release.
10. `WorklogHistoryView.xaml`, list card (audit 46 screen pass): the files part (" dosya değişti" after the bound count) collapses when `Totals.FilesChanged` is 0, the way `WorklogCardMapper.cs` leaves zero parts out of the desktop card, so a session without file changes reads "4 adım" instead of "4 adım · 0 dosya değişti".
11. `WorklogHistoryView.xaml`, list card (audit 47, user decision 2026-11-08 "Görev notu"): when `Header.TaskNote` is set it is the card's headline (`Tv.Type.Body`, one line, ellipsis) and `Current.Title` moves under it as a muted caption "Son adım: …"; a session without a task note looks as today. Since D61 most agent sessions end with the deploy step, so three of the last five read "Kurulum devredildi · doğrulama bekleniyor" in the list.
