# Talvora — Codex status

> Single live status for agent work (Codex or ChatGPT via Talvora). English. Newest Log entry on top.
> Cards: every open card is in `docs/codex/ROADMAP.md`. Design specs: `docs/codex/DESIGN-SANTRAL.md` (Control Center, current), `docs/codex/DESIGN-WORKLOG.md` (desktop worklog), `docs/codex/DESIGN-CONTROL-CENTER.md` (Gözlemevi: its information architecture, copy deck §9 and accessibility §10 stay valid).
> Protocol: AGENTS.md → **CONTINUE PROTOCOL**. Shortcut for the user: "Talvora'ya devam et".

## Fixed facts

| | |
|---|---|
| Local folder | `C:\Users\tayla\Talvora-MCP` |
| Remotes | `origin` = local Gitea `ssh://git@127.0.0.1:2222/taylan/Talvora-MCP.git` (primary tracker) · `github` = `https://github.com/bingoweb/Talvora-MCP.git` (**PUBLIC** — never commit secrets, machine-specific credentials or personal data) |
| Branch | `main` only. Do not create branches. Dependabot PRs: apply with scripts/Apply-DependabotPr.ps1 (DEP1), never merge on GitHub. |
| Commit identity | `Taylan Soylu <6751394+bingoweb@users.noreply.github.com>` (do not change) |
| Push | **Authorized** (Q3, 2026-10-18): after each card `git push origin main` and `git push github main`; never force. |
| Gitea tracker | Mirror each card's Log entry as **one text comment** on Gitea issue **#9** (`taylan/Talvora-MCP`). No attachments. If the comment cannot be posted (connector approval, Gitea down), write `Gitea mirror skipped: <reason>` in the Log entry and continue — never stop or ask the user for it. |
| Audit baseline | Newest Claude audit tag `audit/2026-11-08-46` = the audit 46 commit. Claude creates and moves `audit/*` tags; the agent never does. |
| Executors | Codex **or** ChatGPT with the Talvora connector — identical rules (AGENTS.md → WHO MUST FOLLOW THESE RULES). Log entries name the executor. |
| Runtime | Windows service `Talvora` (LocalSystem), `127.0.0.1:7676`, installed under `C:\Program Files\Talvora\Versions\<commit>-<stamp>`; tray `Talvora.Tray.exe` in the user session. |
| Toolchain | .NET SDK 10.0.403 exact pin (SDK2; runtime 10.0.14) with matching locked NuGet graphs (K0 done). PowerShell 7 required by the interactive runner. |
| Install path | `scripts/Deploy-Windows-Installer.ps1` (SYSTEM task; when called from inside Talvora it does not wait — poll `/healthz.sourceCommit`). Never run `Reset-And-Install.ps1` unless a card says so. |
| Secrets | `C:\ProgramData\Talvora\Secrets\*` belong to the user's other projects. Never read, move, print or delete them. Listing names/ACLs is allowed. |
| User preferences | Personal single-user workstation: no local MCP token (Q1). Cosmetics matter a lot: every visible change needs before/after PNGs. The worklog's vertical **code terminal** is the user's favourite feature — never break or shrink it. |

## In progress

In progress: (none)

## Agent queue

Legend: `[ ]` todo · `[x]` done · `[~]` done but waiting for user evidence · `[?]` blocked on an open question · `[-]` won't do.

> **Only open work lives in these documents** (user decision 2026-11-07). At every audit Claude removes finished queue lines, Log entries older than the previous audit, answered findings, finished cards and decided proposals that no open card names. Git keeps all of it: `git show audit/<tag>^:<path>` is a file as it was before that audit's cleanup (first cleanup: `audit/2026-11-07-40`). The agent may tick a line `[x]` and leave it there.

- [~] D46 — installed 7aa5165; Control Center and both installed MCP smokes green; CI success (audit 46); Claude checks real post-push pending-CI worklog wording
- [~] D60 — installed a899938; both installed MCP smokes green; CI 42500049574 success; Claude verifies one session across a 100-second quiet wait
- [~] D61 — code and gates green; live pending: install, Control Center smoke and CI; Claude checks pre-deploy steps remain in history
- [ ] D62 — A detached start is not a result (audit 46; after D61)
- [ ] PK1 — MCP C# SDK 2.4.x and Tomlyn 2.10.2 (after UR4)
- [ ] PK2 — Microsoft.Build.Framework follows the SDK's MSBuild, 18.9.6 (after PK1)
- [ ] RL1 — Talvora's version is 0.0.x.x and comes from the release tag (user request 2026-11-08)
- [ ] RL2 — A fresh Windows gets PowerShell 7 from the installer (user decision 2026-11-08)
- [ ] RL3 — Talvora is in Apps & features and can be uninstalled (user request 2026-11-08)
- [ ] RL4 — A v0.0.x.x tag publishes the release (user decision 2026-11-08)
- [ ] RL5 — The Control Center says quietly when a new version is out (user decision 2026-11-08; after RL1)
- [ ] [C] D48 — A vault note belongs to the vault's owner (Claude since audit 46: built at audit 42; after the agent's queue)
- [ ] [C] D49 — Plain Turkish service names, also for older registries (Claude since audit 46; after D46)
- [ ] [C] D47 — Plain Turkish for the remaining Control Center texts (Claude since audit 46; setup texts per the user's decision; table and test in docs/codex/reference/d47)
- [ ] [C] WL3 — Çalışma günlüğü and Olaylar polish (Claude; audit 42, user: later; after D45 and D50; audit 43 adds items 7–8)

## User queue

- [ ] U16 — İsteğe bağlı: makine (sistem) PATH'inde aynı klasörü gösteren ikinci satırlar var: `C:\Python314\Scripts`, `C:\Python314`, `C:\Program Files\dotnet\`, `C:\Program Files\PowerShell\7\` ve `C:\Program Files\nodejs\`. Zararsızlar; isterseniz U14'teki gibi Sistem değişkenleri → Path → Düzenle'den bu beşini silebilirsiniz.

## Open questions

(none)

## Findings for Claude

(The agent adds one line per observation outside the current card: `file:line — what was seen`. Do not fix them.)

## Log

- 2026-11-08 — D61, executor ChatGPT: the tray retains each open session's last terminal step, flushes an old session on identity reuse, and drains the rest on Dispose; unfinished history uses D50's key and the original JSON/redaction rules. RED: missing WorklogOpenSessions CS0246. GREEN: D61_UNCLOSED_SESSION_REACHES_HISTORY_GREEN, DESKTOP_PROGRESS_BEHAVIOR_GREEN, MODERNIZATION_POLICY_GREEN; Changed 27/0, Full 70/0: Hepsi geçti. Context7 /dotnet/docs C# lock for Dictionary, official Microsoft reference. Install, installed Control Center smoke, CI and Claude live pre-deploy-history check pending. Gitea #9 comment 226.
  Self-review — no service, protocol, stored-format, close-timing or UI changes; no disk writes on heartbeats; repeated normal close remains idempotent.

- 2026-11-08 — D60, executor ChatGPT: quiet calls retain existing project/actor sessions with QuietCalls, shared key lookup, cancelled/rescheduled closure and injectable interval (production 90 s). RED: CS1739 missing interval, then 2 early closes during 1.2 s wait/400 ms idle. GREEN: D60_QUIET_WAIT_KEEPS_SESSION_GREEN, DESKTOP_PROGRESS_BEHAVIOR_GREEN, MODERNIZATION_POLICY_GREEN; Changed 28/0, Full 70/0: Hepsi geçti. Context7 /dotnet/docs CancellationTokenSource.Cancel/Task.Delay, Microsoft reference. Install, installed smokes, CI and Claude 100-second live check pending. Gitea #9 comment 225.
  Self-review — no UI or Turkish copy changes; quiet reads emit no step, overlapping waits do not close sessions, existing tools/results and 90-second production idle timeout stay unchanged.

- 2026-11-08 — Claude, audit 46 (tag `audit/2026-11-08-46`): range `audit/2026-11-08-45..7aa5165`, eight cards by ChatGPT (D59, TW1, TW2, D55, D56, D45, D50, D46). All follow their cards; commit texts and self-reviews fine; CI success on all eight. D59: the forced-race deploy at 17:00 logs "Service did not accept STOP", then `Install succeeded` with no rollback and no tray recovery. D45, D50 and D46 match their literal blocks; D46's card still named `TryDescribeFailure(result)` (35115d8 added `canonicalToolName`) and the agent kept the current call: correct, Claude's look-ahead miss. D55/D56: the pipes allow only LocalSystem and the user's SID, the service never reads `child.pid` or `exit.code`, the relay deletes the request on read. Claude's checks: signed-in Full gate at 3ca9c72 in a clean clone 70/0 `Hepsi geçti.` (ControlCenter 24 s); `--metadata-policy-only` against the installed 3ca9c72 green (the full installed smoke was stopped so as not to meet the D46 deploy; D50 changed no service code and the agent's full smoke on 0e8ec9c was green); D56 live: no developer cache in the service profile since its install (only the Gitea proxy's Caddy config changed), done; D50 live: one project and actor kept two sessions (15:29:03Z and 15:38:19Z), done. TW2 trace 14:33–15:39Z (293 ChatGPT calls, D55 to D50): no loop; a card takes about 15 min and 70 calls; every wait is one `tail_text` call (1,076 s of 3,780 s); no secret in `cmd`. New: D60 (a read-only call such as a 100-s wait keeps its session: the D50 session closed mid-wait at 15:34:27Z) and D61 (a session open when the tray or the service stops still reaches the history: D50's commit, push, CI check and deploy, 15:35:27–15:37:31Z, were lost); from the screen pass D62 (a detached start read as its result: every detached Full gate showed "Kalite kontrolü geçti · tam" when it started) and WL3 item 10. D45 visual check passed (list and detail end at the last step, e.g. 18:52:32 → 18:53:40 · 1 dk 7 sn), done. User decision: D47, D48 and D49 are Claude's `[C]` items after the agent's queue (Claude built them at audit 42); D46 was already done. AGENTS.md: B7 uses the TW1 wait only; rule 10a: only a `[C]` item Claude has in progress locks its files. Removed: finished queue lines (D59, TW1, TW2, D55, D56, D50), their cards, the TW section, Log entries before audit 45.

- 2026-11-08 — D46, executor ChatGPT: Get-CiStatus.ps1 exit 2 now closes as Completed "GitHub denetimi sürüyor"; 0 succeeds, 1 fails with accurate titles. RED: exit 2 wrongly Failed; GREEN: D46_CI_STILL_RUNNING_GREEN and DESKTOP_PROGRESS_BEHAVIOR_GREEN. Changed 28/0, Full 70/0: Hepsi geçti. Context7 /modelcontextprotocol/csharp-sdk CallToolResult.StructuredContent and /dotnet/docs JsonElement.TryGetInt32; official API references. Install, installed smoke, CI and Claude real post-push check pending. Gitea #9 comment 224.
  Self-review — no layout or unrelated Turkish copy; CI pending never paints a failure, real failure remains Failed, existing script exit codes and classifier unchanged.

- 2026-11-08 — D50, executor ChatGPT: HistoryKey (SessionId + start) preserves same-project/actor sessions across append, reload and prune, with duplicate close deduplication and stable selection on reload/filter. RED: only latest session remained; GREEN: D50_HISTORY_KEEPS_EVERY_SESSION_GREEN, DESKTOP_PROGRESS_BEHAVIOR_GREEN; Changed 27/0, Full 70/0: Hepsi geçti. Context7 /dotnet/docs Enumerable.DistinctBy, official Microsoft reference. Install, installed Control Center smoke, CI and Claude two-session live check pending. Gitea #9 comment 223. Previously overwritten sessions cannot be recovered.
  Self-review — no layout or Turkish copy change; selection keeps the chosen entry; retention limits, stored JSON, session ID and desktop card unchanged.

- 2026-11-08 — D45, executor ChatGPT: WorkEndedAtUtc uses bounded Enumerable.Max of timeline step-end timestamps; list/detail duration no longer includes the 90-second quiet tail. Stored EndedAtUtc, JSON and close remain unchanged. RED: 5 CS1061 errors; GREEN: D45_WORK_ENDS_WITH_LAST_STEP_GREEN (93 s close displays 3 s) and DESKTOP_PROGRESS_BEHAVIOR_GREEN; Changed 27/0, Full 70/0: Hepsi geçti. Context7 /dotnet/docs Enumerable.Max, Microsoft API reference. Install, installed Control Center smoke, CI and Claude visual check pending. Gitea #9 comment 222.
  Self-review — no layout or Turkish copy changes; history list/detail show the correct duration, no-step fallback preserves the stored end, JSON schema and close behaviour remain unchanged.

- 2026-11-08 — D56, executor ChatGPT: jobs and dev servers default to signed-in user with SID-scoped three-pipe relay, optional runAs=service LiteralProcessStart, protected folder, persisted RunAs (legacy=service), and owned pipe/script cleanup. RED: installed whoami job printed SYSTEM instead of signed-in user; six pinned tests green; Changed 20/0; Full 70/0: Hepsi geçti. Context7 /dotnet/docs pipe ACL and Process.Kill, Microsoft NamedPipeServerStreamAcl, named-pipe security and CreateProcessAsUserW docs. Install, installed smoke, CI and Claude post-use cache check pending. Gitea #9 comment 221.
  Self-review — no UI or tool/schema additions, existing output/rotation preserved; user/service selection, stop/delete, legacy records and SID isolation covered; Turkish-visible text unchanged.

- 2026-11-08 — D55, executor ChatGPT: UserJobRelay.Run connects three pipes and forwards live stdin/stdout/stderr with child PID/exit files; the exact RunnerScript and request contract are preserved. RED: relay NotImplementedException; GREEN: pwsh ping/exit 3, separate stderr, and args.cmd special-character argument fidelity; TALVORA SHARED INFRASTRUCTURE GREEN. Changed 17/0; Full 70/0: Hepsi geçti. Context7 /dotnet/docs pipe client and Process.StandardInput; Microsoft NamedPipeClientStream/CreateProcessAsUser pages. Install, installed smokes and CI pending. Gitea #9 comment 220.
  Self-review — no tools wired until D56, no handle inheritance across sessions, no UI changes; disconnected outputs keep draining and temporary command scripts are cleaned.

- 2026-11-08 — TW2, executor ChatGPT: async serialized call trace records nine redacted/hash-only fields, skips D41 own clients, and deletes files older than 14 days daily. RED: CS0246 missing ToolCallTrace; TW2_CALL_TRACE_GREEN and DESKTOP_PROGRESS_BEHAVIOR_GREEN. Changed 28/0; Full 70/0: Hepsi geçti. Context7 /dotnet/docs Utf8JsonWriter and SHA256.HashData, official JSON writer docs. Commit, deploy, whoami trace, installed smokes and CI pending. Gitea #9 comment 219.
  Self-review — no UI, output, environment values or file contents written to trace; no reader or setting; the notification-test runner only invokes the new isolated fixture.

- 2026-11-08 — TW1, executor ChatGPT: tail_text optionally waits for a regex-matching line with two-second cancellable polls, a one-second culture-invariant regex timeout and 0–100-second bounds; default output is unchanged. RED: the installed tool refused both new parameters. Pinned append encoding/newline regressions green; Changed 31/0; Full 70/0: Hepsi geçti. Context7 /dotnet/docs Regex and Task.Delay; official regex best practices. Install, installed smokes and CI pending. Gitea #9 comment 218.
  Supporting changes: the shared-infrastructure smoke receives live MCP tools through Program.cs; SourceEditRegressionRunner.Misc.cs awaits three calls (initial Changed compiler CS1061). Self-review: no visual, copy or unrelated tool changes.

- 2026-11-08 — D59, executor ChatGPT: InstallAsync stops the tray before stopping the service; both wait loops detect a new PID without altering the 8/30-second windows or rollback. RED: InstallerStopsTrayBeforeService and InstallerNamesRestartedService false; both pinned regression suites green. Changed 16/0; Full 70/0: Hepsi geçti. Context7 /dotnet/docs and official Windows service state/Process.Kill pages; forced-race deploy, installed smokes and CI pending.
  Self-review — no visual or Turkish copy changes; service ACL, forced kill, recovery policy and rollback kept as written.

- 2026-11-08 — Claude, audit 45 (tag `audit/2026-11-08-45`): range `audit/2026-11-08-44..5afa33a` (D53 and D54 by Codex, D58 by ChatGPT, the P-52 stop, Claude's README and loop-check docs) plus P-53. D53 follows the card (the article fix runs after "arbitrary" → "caller-supplied"; descriptions are normalized once per tool name, like the schema cache). D54 follows it step by step (`RunCliAsync` runs as the signed-in user, `scope` defaults to the user, JAVA_HOME and ANDROID_HOME come from the user's environment, the policy test takes every file by pattern). D58 follows it (fixtures replace `_events`, no live refresh in smoke mode, normal use unchanged). Commit texts and self-reviews fine. P-53 decided: a wait call through Talvora kept the old service from stopping within 8 s, the installer killed it and the tray's automatic recovery started it again (B7 was Claude's miss). Claude installed 5afa33a with no call running (stopped in under 1 s, `Install succeeded` 16:24:57); `--metadata-policy-only` and the full installed smoke green; CI 42481964947 and 42482990847 success: D54 and D58 done. Full gate as the user with D57 70/0 `Hepsi geçti.` (ControlCenter 16 s). D57 shipped by Claude (6c8c977: README, both tests red first and green after, normalized SHA-256 matches the reference, old `control-center*.png` removed; CI 42485400239 success). New: D59 first. AGENTS.md: B7 says how to wait for an install, the Deploy row too, D4 removed (D58 installed). RL3 stops the tray before the service. Removed: finished queue lines (D53, D54, D57, D58), cards D53, D54, D57, D58, Log entries before audit 44, `docs/codex/reference/d57`. The agent's uncommitted STATUS lines are kept.
