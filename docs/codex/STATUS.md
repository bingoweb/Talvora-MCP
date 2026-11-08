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
| Audit baseline | Newest Claude audit tag `audit/2026-11-08-47` = the audit 47 commit. Claude creates and moves `audit/*` tags; the agent never does. |
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

- [~] CTX2 — code and gates green; live pending: install, both MCP smokes and CI; Claude checks the next card's push
- [ ] D64 — Context7 proof counts only the agent's own questions (audit 47; first)
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

- 2026-11-08 — Claude, audit 47 (deep; tag `audit/2026-11-08-47`): range `audit/2026-11-08-46..953fc7d` (D60, D61, D62, CTX1, CTX2 by ChatGPT; Claude's docs). Read line by line: D60's quiet calls find the session through `path` too (gate logs in the repository), the counter is lock-guarded and the close timer waits for it; D61 keeps the newest terminal step per session and writes it when a newer session or the tray's `Dispose` comes; D62 and CTX1 follow their cards; CTX2's gate reads the pushed commits, skips documentation-only pushes and matches each question to the ledger. Rule break: CTX2 changed `Validate-Context7Gate.ps1`'s 14-day age rule to "verifiedAtUtc must be today (UTC)" against its Don't and did not log it; evidence verified two days ago was refused (reproduced), and `Validate-ProjectDeliveryGate.ps1` (the gate's Delivery check, not named in CTX2's card: Claude's miss) still refused noLookup evidence; Claude fixed both as [C] D63. D64 queued: the gate counts the installed smoke's own `/dotnet/docs` question as proof, only the first `(query: …)` of an entry is checked, `talvora_context7_docs` is advertised closed-world, and a detached start with no subject reads " arka planda başladı". Live checks from the stored worklog, all done: D46 ("GitHub denetimi sürüyor" after each push), D60 (D62, CTX1 and CTX2 each stay one session through their 100-second waits), D61 (sessions end with the deploy step "Kurulum devredildi"), D62 ("Talvora.Smoke arka planda başladı"). Ledger: ChatGPT's CTX2 question recorded with status 200. Signed-in Full gate at 953fc7d in a clean clone 70/0 `Hepsi geçti.`; CI success on every commit; no tray error since 19:00; no leftover relay process. Noted, no card: `talvora_git_run` runs local git commands as LocalSystem, so new `.git` objects are SYSTEM-owned but keep the user's inherited full control; the Classroom folder itself is SYSTEM-owned (a user step, outside Talvora). Screen pass: three of the last five agent sessions read "Kurulum devredildi · doğrulama bekleniyor" in the list although each has a task note; user decision: the task note becomes the list headline (WL3 item 11). AGENTS.md: the Context7 rules lose their "after CTX1/CTX2" conditions. Removed: finished queue lines and cards (D46, D60, D61, D62, CTX1, D63), the audit 46 section, Log entries before audit 46.

- 2026-11-08 — [C] D63, executor Claude: CTX2 had changed `scripts/Validate-Context7Gate.ps1` to "verifiedAtUtc must be today (UTC)" against its Don't; evidence verified two days ago was refused (reproduced). The 14-day comparison is back, exactly as before CTX2. RED: `StrictEvidenceJsonRegression.ps1` with evidence from two days ago failed "must be today"; GREEN: STRICT_EVIDENCE_JSON_GREEN (two days passes, fifteen days is refused), MODERNIZATION_POLICY_GREEN; signed-in Full gate in a clean clone 70/0 `Hepsi geçti.` Second fix, found by Claude's own gate run: `scripts/Validate-ProjectDeliveryGate.ps1` (the gate's Delivery check) still demanded at least one library and one vendor source, so every noLookup evidence failed the gate (CTX2's card had not named that script: Claude's miss); it now takes a library or a noLookup of 20+ characters and only HTTPS vendor sources (red then green in the same test). Context7: noLookup (two repository scripts). No runtime change, no deploy.

- 2026-11-08 — CTX2, executor ChatGPT: push gate compares remote-tracking branch to HEAD, skips documentation-only pushes, reads committed Context7 evidence and matches exact questions to successful ledger records from the past 24 hours. Dotnet publish still checks working-tree format only. RED: CS1501 for missing gate overload; GREEN: CTX2_PUSH_EVIDENCE_GREEN, STRICT_EVIDENCE_JSON_GREEN, all four pinned tests, Changed 21/0 and Full 70/0: Hepsi geçti. Context7 /websites/git-scm, exact query "git diff --name-only between a remote-tracking branch and HEAD" recorded through Talvora; official git-scm.com git-diff page. Install, installed smokes and CI pending; Claude owns next-card live proof. Gitea #9 mirrored.
  Self-review — no visual changes; git push rejects missing, uncommitted, stale or unsuccessful query proof; documented noLookup and HTTPS format aligned in C# and CI; no changes to the HTTP tool, ledger retention or CI development-scope patterns.

- 2026-11-08 — CTX1, executor ChatGPT: added talvora_context7_docs (developer surface only) with a bounded 30-second Context7 GET, validated library/question, 64-KB response cap, and a serialized seven-field JSONL ledger with 14-day retention and ReadSince. RED: Context7Ledger/Context7Tools missing (CS0246/CS0103); GREEN: CTX1_CONTEXT7_LOOKUP_GREEN, all four pinned regressions, notification regression, Changed 29/0 and Full 70/0: Hepsi geçti. Context7 /dotnet/docs HttpClient response headers/timeout and /modelcontextprotocol/csharp-sdk tool DI; official Microsoft and MCP SDK references. Install, both installed smokes and CI pending. Gitea #9 comment 228.
  Self-review — no changes to talvora_http_request, TW2 trace or CTX2 evidence gate; the tool is quiet and dev-only, no credentials or response body reach the lookup ledger, and documentation changes contain tool counts only.

- 2026-11-08 — D62, executor ChatGPT: detached user_process_start/job_start now report launch, not final success; known quality/package script starts use precise Turkish titles and dotnet run --project uses the project name (direct or nested in pwsh). RED: detached quality gate falsely said passed. GREEN: D62_DETACHED_START_IS_NOT_RESULT_GREEN (6 cases), DESKTOP_PROGRESS_BEHAVIOR_GREEN, MODERNIZATION_POLICY_GREEN; Changed 28/0, Full 70/0: Hepsi geçti. Context7 /dotnet/docs GeneratedRegex with CultureInvariant, Microsoft reference. Install, both installed MCP smokes, CI and Claude real Full-gate live check pending. Gitea #9 comment 227.
  Self-review — bounded runs, KnownScripts, deferred deploy completion, dev_server_start and tray unchanged; no final-result claims on detached launch, original scope suffix preserved.

- 2026-11-08 — D61, executor ChatGPT: the tray retains each open session's last terminal step, flushes an old session on identity reuse, and drains the rest on Dispose; unfinished history uses D50's key and the original JSON/redaction rules. RED: missing WorklogOpenSessions CS0246. GREEN: D61_UNCLOSED_SESSION_REACHES_HISTORY_GREEN, DESKTOP_PROGRESS_BEHAVIOR_GREEN, MODERNIZATION_POLICY_GREEN; Changed 27/0, Full 70/0: Hepsi geçti. Context7 /dotnet/docs C# lock for Dictionary, official Microsoft reference. Install, installed Control Center smoke, CI and Claude live pre-deploy-history check pending. Gitea #9 comment 226.
  Self-review — no service, protocol, stored-format, close-timing or UI changes; no disk writes on heartbeats; repeated normal close remains idempotent.

- 2026-11-08 — D60, executor ChatGPT: quiet calls retain existing project/actor sessions with QuietCalls, shared key lookup, cancelled/rescheduled closure and injectable interval (production 90 s). RED: CS1739 missing interval, then 2 early closes during 1.2 s wait/400 ms idle. GREEN: D60_QUIET_WAIT_KEEPS_SESSION_GREEN, DESKTOP_PROGRESS_BEHAVIOR_GREEN, MODERNIZATION_POLICY_GREEN; Changed 28/0, Full 70/0: Hepsi geçti. Context7 /dotnet/docs CancellationTokenSource.Cancel/Task.Delay, Microsoft reference. Install, installed smokes, CI and Claude 100-second live check pending. Gitea #9 comment 225.
  Self-review — no UI or Turkish copy changes; quiet reads emit no step, overlapping waits do not close sessions, existing tools/results and 90-second production idle timeout stay unchanged.

- 2026-11-08 — Claude, audit 46 (tag `audit/2026-11-08-46`): range `audit/2026-11-08-45..7aa5165`, eight cards by ChatGPT (D59, TW1, TW2, D55, D56, D45, D50, D46). All follow their cards; commit texts and self-reviews fine; CI success on all eight. D59: the forced-race deploy at 17:00 logs "Service did not accept STOP", then `Install succeeded` with no rollback and no tray recovery. D45, D50 and D46 match their literal blocks; D46's card still named `TryDescribeFailure(result)` (35115d8 added `canonicalToolName`) and the agent kept the current call: correct, Claude's look-ahead miss. D55/D56: the pipes allow only LocalSystem and the user's SID, the service never reads `child.pid` or `exit.code`, the relay deletes the request on read. Claude's checks: signed-in Full gate at 3ca9c72 in a clean clone 70/0 `Hepsi geçti.` (ControlCenter 24 s); `--metadata-policy-only` against the installed 3ca9c72 green (the full installed smoke was stopped so as not to meet the D46 deploy; D50 changed no service code and the agent's full smoke on 0e8ec9c was green); D56 live: no developer cache in the service profile since its install (only the Gitea proxy's Caddy config changed), done; D50 live: one project and actor kept two sessions (15:29:03Z and 15:38:19Z), done. TW2 trace 14:33–15:39Z (293 ChatGPT calls, D55 to D50): no loop; a card takes about 15 min and 70 calls; every wait is one `tail_text` call (1,076 s of 3,780 s); no secret in `cmd`. New: D60 (a read-only call such as a 100-s wait keeps its session: the D50 session closed mid-wait at 15:34:27Z) and D61 (a session open when the tray or the service stops still reaches the history: D50's commit, push, CI check and deploy, 15:35:27–15:37:31Z, were lost); from the screen pass D62 (a detached start read as its result: every detached Full gate showed "Kalite kontrolü geçti · tam" when it started) and WL3 item 10. D45 visual check passed (list and detail end at the last step, e.g. 18:52:32 → 18:53:40 · 1 dk 7 sn), done. User decision: D47, D48 and D49 are Claude's `[C]` items after the agent's queue (Claude built them at audit 42); D46 was already done. AGENTS.md: B7 uses the TW1 wait only; rule 10a: only a `[C]` item Claude has in progress locks its files. Removed: finished queue lines (D59, TW1, TW2, D55, D56, D50), their cards, the TW section, Log entries before audit 45.
