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
| Audit baseline | Newest Claude audit tag `audit/2026-11-08-42` = the audit 42 commit. Claude creates and moves `audit/*` tags; the agent never does. |
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

- [ ] D45 — Çalışma günlüğü measures a session up to its last step (audit 42, data truth)
- [ ] D46 — A CI run that is still going is not a failed step (audit 42)
- [ ] D48 — A vault note belongs to the vault's owner (audit 42)
- [ ] D49 — Plain Turkish service names, also for older registries (audit 42; after D46)
- [ ] D47 — Plain Turkish for the remaining Control Center texts (audit 42; table and test in docs/codex/reference/d47)

## User queue

(none)

## Open questions

(none)

## Findings for Claude

(The agent adds one line per observation outside the current card: `file:line — what was seen`. Do not fix them.)

## Log

- 2026-11-08 — Claude, audit 42 (deep, whole project; tag `audit/2026-11-08-42`): range `audit/2026-11-07-41..97344f7` (D40, D41, D42, D35–D39, D43, D44, OB1) matches its cards (D35 and D40–D44 byte for byte with the verified blocks); Claude's signed-in Full 70/0 `Hepsi geçti.`; CI success on all 11 commits; installed 97344f7. Live checks closed by Claude: D40 (Claude's gate left the worklog history unchanged), D41 (bridge script call = `core · Claude`, worklog actor Claude), D42, D37, D43 and D44 (both themes; the real link opens Olaylar on Onarımlar with the repairs listed), OB1 (live note, secret refused, CI 42410824919). Accepted: D35's extra marker change in TunnelClientUpdateRollbackRegression.ps1 (Claude's card gap).
  New cards, first in the queue: D45 (a worklog session's duration ends with its last step, not with the 90 s close), D46 (Get-CiStatus exit 2 is "GitHub denetimi sürüyor", not a failed step), D48 (vault notes and new folders get the vault owner), D49 (plain service names, also for older registries), D47 (75 remaining English or jargon texts; table and test in `docs/codex/reference/d47`). Their literal blocks were applied together in a scratch clone: Notification and SourceEdit regressions green, Full gate 71/0 `Hepsi geçti.`. Claude deleted the fake "Project A" worklog session left by the old tests.
  Removed: finished queue lines and cards (D35–D44, OB1), the audit 41 section, Log entries older than audit 41, `docs/codex/reference/ob1`. No artifacts older than 7 days.

- 2026-11-07 — OB1, executor ChatGPT via Talvora: added Dev-only talvora_note_write to create/append Obsidian Markdown notes with fail-closed secret, Templater code, traversal, hidden path, repo-document and junction checks. Both reference copies match normalized SHA-256. RED CS0103, then ob1-vault-note-writer and SourceEdit core green; signed-in Changed 23/0 and Full 70/0 Hepsi geçti, Q7 payload unchanged. Context7 /modelcontextprotocol/csharp-sdk and /dotnet/docs; install, live create/refusal and CI pending. Gitea #9 comment 215.
  Self-review — cosmetics: no user interface changes.
  Self-review — usability: caller supplies vault root; create and append are explicit.
  Self-review — Turkish: title Kasaya not yaz and all refusal messages use approved Turkish.
  Self-review — correctness/security: whole secret-containing notes refused, never partially redacted; hidden folders, <%, reparse-point links and repo-linked docs blocked; user settings untouched.
- 2026-11-07 — D44, executor ChatGPT via Talvora: the 24-hour repair count is a dedicated overview link with the prescribed dotted brass underline and keyboard focus ring; click selects Olaylar → Onarımlar and clears search/service filters. RED CS1061 then UI3_NARRATOR_GREEN; Changed 17/0, signed-in Full 70/0 Hepsi geçti. Context7 /dotnet/docs-desktop WPF template triggers and namescopes. Exact-commit install, smoke, CI pending. Gitea #9 comment 214.
  Visual evidence — before: artifacts/d44-before/control-center-ui3-overview-{initial,alternate}.png; after: artifacts/d44-after/control-center-d44-{link,repairs}-{initial,alternate}.png; Claude visual check pending.
  Self-review — cosmetics: quiet monospace link, text-width dotted underline, hover and focus styling use prescribed tokens only.
  Self-review — usability: click enters events with only Onarımlar category, search/service cleared; keyboard and accessibility names preserved.
  Self-review — Turkish: current status copy reused unchanged; no unrelated strings modified.
  Self-review — correctness/security: D42 count and event filtering unchanged; UI/Notification regressions and both-theme smoke green.
- 2026-11-07 — D43, executor ChatGPT via Talvora: added LocalTimeText for day-once Turkish worklog session labels and actual measured duration; list and detail XAML bindings updated without touching timeline, storage or filters. RED CS0103 then D43_SESSION_TIME_TEXT_GREEN; Changed 27/0 and signed-in Full 70/0 Hepsi geçti. Context7 /dotnet/docs tr-TR custom date-time formats; installed runtime/CI pending. Gitea issue #9 comment 213.
  Visual evidence — before: artifacts/d43-before; after: artifacts/d43-after, worklog-session and wl2-empty, both themes; Claude visual check pending.
  Self-review — cosmetics: only list/detail timestamp rows, no styling or layout tokens changed.
  Self-review — usability: day shown once and real duration is legible.
  Self-review — Turkish: Bugün, Dün, month and weekday names, singular short duration formats tested.
  Self-review — correctness/security: span uses EndedAtUtc minus StartedAtUtc; crossing midnight and year handled; JSONL and timeline unchanged.
- 2026-11-07 — D39, executor ChatGPT via Talvora: Penpot error text now passes through both its existing userToken regex and shared FileLog.RedactSensitiveData for Authorization and JWT masking. RED CS0117 then D39_PENPOT_REDACTION_GREEN; Changed 28/0 and signed-in Full 70/0 Hepsi geçti. Context7 /dotnet/docs regex $1 capture substitution; exact-commit install, smoke and CI pending. Gitea issue #9 comment 212.
  Self-review — cosmetics: no interface or layout changes.
  Self-review — usability: Penpot status errors no longer expose covered secret patterns.
  Self-review — Turkish: no product copy changed.
  Self-review — correctness/security: existing Penpot token mask, endpoint resolution and credential configuration unchanged; shared masking added afterward.
- 2026-11-07 — D38, executor ChatGPT via Talvora: presenter agent-only filter recognizes Codex, Claude, and ordinal ChatGPT prefixes; unknown caller Diğer is filtered but terminal history remains. RED N8 "D38: ChatGPT agent card filtered", then N8_NOTIFICATIONS_GREEN. Signed-in Changed 16/0, Full 70/0 Hepsi geçti. Context7 /dotnet/docs StartsWith(StringComparison.Ordinal); exact-commit install, smoke and CI pending. Gitea issue #9 comment 211.
  Self-review — cosmetics: no layout or styling change; visual behavior is now visible ChatGPT cards.
  Self-review — usability: ChatGPT · Dev and ChatGPT · Yönetim are agent labels.
  Self-review — Turkish: no product copy or setting text changed.
  Self-review — correctness/security: failure events, history persistence, filter defaults and already-open cards retain their behavior.
- 2026-11-07 — D37, executor ChatGPT via Talvora: replaced 23 specified event text pairs in four tray sources; both Gitea readiness details updated. RED old text then EVENT_TEXT_TURKISH_GREEN; Changed 26/0 and signed-in Full 70/0 Hepsi geçti. Context7 /dotnet/docs C# interpolation; exact-commit install, smoke and CI pending. Gitea issue #9 comment 210.
  Self-review — cosmetics: no layout, styling or motion change; prescribed event wording only.
  Self-review — usability: event cards use the exact Turkish titles and details specified.
  Self-review — Turkish: all 23 pairs and the duplicate Gitea detail pass regression.
  Self-review — correctness/security: interpolation holes, TrayLog, event categories and dedup keys are unchanged; visual check pending (Claude).
- 2026-11-07 — D36, executor ChatGPT via Talvora: PublishDurable retries IOException or UnauthorizedAccessException with its existing bounded filter and source-exists guard. RED source assertion then D36_RETRY_FILTER_GREEN; Changed 18/0 and signed-in Full 69/0 Hepsi geçti (AtomicRace). Context7 /dotnet/docs File.Replace and I/O errors; exact-commit install, smoke and CI pending. Gitea issue #9 comment 209.
  Self-review — cosmetics: no layout, styling or visible elements changed.
  Self-review — usability: busy file destinations use the existing retry window.
  Self-review — Turkish: all user-facing text unchanged.
  Self-review — correctness/security: only the exception filter changed; attempts, delay, replace/move order and source-path guard are preserved.
- 2026-11-07 — D35, executor ChatGPT via Talvora: failed release lookups back off for 30 minutes; no latest tag keeps the running client. HTTP handler regression RED CS8604/CS8602 then D35_LATEST_BACKOFF_GREEN; supporting PS rollback source-range marker updated for the nullable internal signature. Changed 31/0, signed-in Full 69/0 Hepsi geçti. Context7 /dotnet/docs HttpMessageHandler.SendAsync; exact-commit install, smoke and CI pending. Gitea issue #9 comment 208.
  Self-review — cosmetics: no UI or visual changes.
  Self-review — usability: a GitHub outage preserves the running tunnel client and suppresses repeated requests.
  Self-review — Turkish: existing product copy unchanged.
  Self-review — correctness/security: 30-minute backoff before and inside the release gate; six-hour cache, download path, and update journal unchanged.
- 2026-11-07 — D42, executor ChatGPT via Talvora: RepairApplied distinguishes a non-ready initial state; Gitea and managed-MCP "already ready" events use distinct :already-ready keys, filtered from RecoveryCount. RED CS1061 then UI3_NARRATOR_GREEN; Changed 22/0 and signed-in Full 69/0 Hepsi geçti. Context7 /dotnet/docs EndsWith(StringComparison.Ordinal); exact-commit install, smoke and CI pending. Gitea issue #9 comment 207.
  Self-review — cosmetics: no layout or styling changed.
  Self-review — usability: only applied repairs count.
  Self-review — Turkish: existing product copy unchanged.
  Self-review — correctness/security: repair logic, dedup windows, event titles and core recovery untouched.
- 2026-11-07 — D41, executor ChatGPT via Talvora: caller identity now X-Talvora-Client, MCP name, then HTTP User-Agent; self Talvora.* calls bypass desktop cards/worklog and traffic; unknown actor Diğer. RED CS0117, then D41_CALLER_IDENTITY_GREEN and SN1_LINE_TRAFFIC_GREEN; Changed 28/0, signed-in Full 69/0 Hepsi geçti. Context7 /dotnet/aspnetcore.docs; exact-commit install/CI pending; live bridge and worklog checks owned by Claude next audit. Gitea #9 comment 206.
  Self-review — cosmetics: no layout changed.
  Self-review — usability: smoke has no cards, Claude is named, unknowns Diğer.
  Self-review — Turkish: UI text unchanged except mandated Diğer fallback.
  Self-review — correctness/security: caller precedence and self-filter tested; ResolveClient, D32 and surface routes unchanged.
- 2026-11-07 — D40, executor ChatGPT via Talvora: isolated Notification tests' tray.log and default worklog-sessions.jsonl under unique user temp root, restoring previous TrayLog override in recovery regression. RED CS1061 then D40_TEST_ISOLATION_GREEN, Changed 27/0 and signed-in Full 69/0 Hepsi geçti. Before/after Full: last timeout 2026-11-07T15:24:05.2225314+03:00 / identical; Project A endedAtUtc 11/07/2026 15:05:25 / identical. Context7 /dotnet/docs Path.GetTempPath; installed smoke and CI pending. Gitea #9 comment 205.
  Self-review — cosmetics: no UI or layout changed.
  Self-review — usability: test entries never pollute the real desktop history.
  Self-review — Turkish: user-visible text unchanged.
  Self-review — correctness/security: both real-file timestamps unchanged after Full; default test file paths isolated; existing Control Center smoke untouched.
- 2026-11-07 — Claude, audit 41 follow-up: D34 closed (22 status lines in the hour after the install, limit 30). Design improvements written as cards (user: "Evet, yaz"): D43 (Çalışma günlüğü names the day once: "Bugün 15:44 · 3 sn", detail "Bugün 15:44:23 → 15:44:26 · 3 sn") and D44 (the overview's repair count is a quiet link that opens Olaylar on "Onarımlar"), queued after D39. Their literal blocks were built in a scratch clone together with D35 and D40–D42: Release 0 warnings, Notification regression GREEN (D43_SESSION_TIME_TEXT_GREEN, UI3_NARRATOR_GREEN), Control Center smoke GREEN; the D44 captures were checked in both themes (link on the status line, dotted brass underline, Olaylar opens on "Onarımlar"); the first draft's stretched underline was fixed before the card was written.

- 2026-11-07 — Claude, audit 41 (full check; tag `audit/2026-11-07-41`): range `audit/2026-11-07-40..1302b75` (D30, D31, D29, D32, D33, D34) matches its cards; Claude's signed-in Full 69/0 `Hepsi geçti.`; CI success on every commit; installed 1302b75. Why ChatGPT stalled: D34's "after one hour of normal running" step plus CONTINUE PROTOCOL step 8 (live-pending checks first) made every "devam et" in that hour re-check an unfinished hour; AGENTS.md steps 5 and 8 now give waiting checks to Claude (`[~] live pending (Claude)`, skipped by the agent). D34 closed by Claude's count: 22 `tunnel-client runtimes status` lines from 15:09:50 to 16:09:50 (limit 30; before 92), 9 of them from three smoke runs, otherwise one every 5 minutes; slowest call 176 ms; tunnels ready.
  New cards, first in the queue: D40 (tests write into the user's tray.log and worklog history), D41 (Talvora's own calls leave no worklog session or desktop card; the bridge's `X-Talvora-Client` header names Claude; user decisions), D42 ("zaten hazır" counted as an automatic repair). Their literal blocks were verified in a scratch clone (Release build 0 warnings, Notification regression GREEN). Card texts fixed: OB1 Step 8 (a four-line statement) and its live-check date, D38's non-agent actor "Diğer". Claude added `--header X-Talvora-Client:Claude` to the Claude Desktop "Talvora Chat" bridge (installed and source copies; backups in `%LOCALAPPDATA%\Talvora\ClaudeDesktopChatExtension\backups`).
  Removed: finished queue lines and cards (D29–D33, SN9, WL2), Log entries older than audit 40, the regression-kept K1, N1–N8/N3b and UI1–UI8 sections (D30 removed their last test dependency). No artifacts older than 7 days.
