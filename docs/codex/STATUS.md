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
| Audit baseline | Newest Claude audit tag `audit/2026-11-08-45` = the audit 45 commit. Claude creates and moves `audit/*` tags; the agent never does. |
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

- [ ] D59 — The tray's automatic recovery can no longer undo an install (P-53, audit 45; first)
- [ ] TW1 — talvora_tail_text can wait for a line (loop check 2026-11-08)
- [ ] TW2 — Talvora keeps a short trace of agent tool calls (user decision 2026-11-08)
- [ ] D55 — A relay runs one job inside the signed-in user's session (audit 43; after D52)
- [ ] D56 — Background jobs and dev servers run as the signed-in user (audit 43, user decision; after D55)
- [ ] D45 — Çalışma günlüğü measures a session up to its last step (audit 42, data truth)
- [ ] D50 — Çalışma günlüğü keeps every session (audit 42, data truth; after D45)
- [ ] D46 — A CI run that is still going is not a failed step (audit 42)
- [ ] D48 — A vault note belongs to the vault's owner (audit 42)
- [ ] D49 — Plain Turkish service names, also for older registries (audit 42; after D46)
- [ ] D47 — Plain Turkish for the remaining Control Center texts (audit 42; setup texts per the user's decision; table and test in docs/codex/reference/d47)
- [ ] PK1 — MCP C# SDK 2.4.x and Tomlyn 2.10.2 (after UR4)
- [ ] PK2 — Microsoft.Build.Framework follows the SDK's MSBuild, 18.9.6 (after PK1)
- [ ] RL1 — Talvora's version is 0.0.x.x and comes from the release tag (user request 2026-11-08)
- [ ] RL2 — A fresh Windows gets PowerShell 7 from the installer (user decision 2026-11-08)
- [ ] RL3 — Talvora is in Apps & features and can be uninstalled (user request 2026-11-08)
- [ ] RL4 — A v0.0.x.x tag publishes the release (user decision 2026-11-08)
- [ ] RL5 — The Control Center says quietly when a new version is out (user decision 2026-11-08; after RL1)
- [ ] [C] WL3 — Çalışma günlüğü and Olaylar polish (Claude; audit 42, user: later; after D45 and D50; audit 43 adds items 7–8)

## User queue

- [ ] U16 — İsteğe bağlı: makine (sistem) PATH'inde aynı klasörü gösteren ikinci satırlar var: `C:\Python314\Scripts`, `C:\Python314`, `C:\Program Files\dotnet\`, `C:\Program Files\PowerShell\7\` ve `C:\Program Files\nodejs\`. Zararsızlar; isterseniz U14'teki gibi Sistem değişkenleri → Path → Düzenle'den bu beşini silebilirsiniz.

## Open questions

(none)

## Findings for Claude

(The agent adds one line per observation outside the current card: `file:line — what was seen`. Do not fix them.)

## Log

- 2026-11-08 — Claude, audit 45 (tag `audit/2026-11-08-45`): range `audit/2026-11-08-44..5afa33a` (D53 and D54 by Codex, D58 by ChatGPT, the P-52 stop, Claude's README and loop-check docs) plus P-53. D53 follows the card (the article fix runs after "arbitrary" → "caller-supplied"; descriptions are normalized once per tool name, like the schema cache). D54 follows it step by step (`RunCliAsync` runs as the signed-in user, `scope` defaults to the user, JAVA_HOME and ANDROID_HOME come from the user's environment, the policy test takes every file by pattern). D58 follows it (fixtures replace `_events`, no live refresh in smoke mode, normal use unchanged). Commit texts and self-reviews fine. P-53 decided: a wait call through Talvora kept the old service from stopping within 8 s, the installer killed it and the tray's automatic recovery started it again (B7 was Claude's miss). Claude installed 5afa33a with no call running (stopped in under 1 s, `Install succeeded` 16:24:57); `--metadata-policy-only` and the full installed smoke green; CI 42481964947 and 42482990847 success: D54 and D58 done. Full gate as the user with D57 70/0 `Hepsi geçti.` (ControlCenter 16 s). D57 shipped by Claude (6c8c977: README, both tests red first and green after, normalized SHA-256 matches the reference, old `control-center*.png` removed; CI 42485400239 success). New: D59 first. AGENTS.md: B7 says how to wait for an install, the Deploy row too, D4 removed (D58 installed). RL3 stops the tray before the service. Removed: finished queue lines (D53, D54, D57, D58), cards D53, D54, D57, D58, Log entries before audit 44, `docs/codex/reference/d57`. The agent's uncommitted STATUS lines are kept.

- 2026-11-08 — D58 live follow-up: canonical installer for 5afa33a launched but the scheduled task returned 1; healthz still serves 4d8bdfe, so the new runtime was not installed. Installed smokes were not run against the stale service. Existing P-53 remains the service-install blocker; CI 42482990847 was in_progress at its single check.

- 2026-11-08 — D58, executor ChatGPT: UI5 smoke fixtures now replace EventsView's stored events, live refresh is skipped only in smoke mode, and the existing snapshot is awaited for deterministic testing. RED: UI5 seven-day empty state was not rendered. Built tray smoke 3/3; Changed 17/0; Full 70/0: Hepsi geçti. Context7 /dotnet/docs-desktop, Microsoft IsVisible. Install, installed smokes and CI pending. D54 P-53 service-stop blocker remains; prior D54 proposal/STATUS changes preserved.
  Self-review — cosmetics and raw-log timer unchanged; normal events refresh unchanged; no new user-facing text. Mirrored to Gitea issue #9 comment 216.

- 2026-11-08 — D54 live follow-up: installer for c671abb could not stop the old service and rolled back to 4d8bdfe, verified by healthz; P-53 records the failure. CI 42481964947 was in progress at the single check; installed smokes pending.

- 2026-11-08 — D54, executor Codex: command lookup defaults to the signed-in user, Java/Android prefer that environment, Python guidance uses uv and Rust shares the runner; dynamic source and uv smoke checks added. RED: installed cargo lookup and JAVA_HOME source check; modernization green, Changed 17/0, Full 70/0: Hepsi geçti. Context7 /dotnet/docs environment target; install and CI pending. Queue additions applied. Gitea mirror skipped: sending a comment was not explicitly authorized.
  Self-review — cosmetics: no screen change; usability: user tools and settings are found consistently; Turkish: no Turkish copy changed; correctness/security: service scope retained explicitly and checks cover both toolchain paths.

- 2026-11-08 — P-52, executor Codex: D57's two tests red before the reference copy and green after; normalized hash matched. Changed 13/0; Full stopped at unrelated UI5 empty-state smoke (69 checks, one failure). D57 partial work reverted; decision pending. D53 installed 4d8bdfe, first metadata and full installed smoke green, CI 42479997608 success. Gitea mirror skipped: sending a comment was not explicitly authorized.

- 2026-11-08 — D53, executor Codex: normalize tool descriptions once, fixing articles and semicolon endings; red on talvora_dotnet_run, Notification green. Changed 28/0 and Full 70/0: Hepsi geçti. Context7 /dotnet/docs GetOrAdd and RegexOptions.Compiled; installed verification and CI pending. P-51 documents the interrupted initial Full gate. Queue additions applied. Gitea mirror skipped: sending a comment was not explicitly authorized.
  Self-review — cosmetics: no screen change; usability: descriptions are correct on the first list; Turkish: no user-facing Turkish changed; correctness/security: idempotence tested across tools and metadata/schema behavior preserved.

- 2026-11-08 — Claude, audit 44 (tag `audit/2026-11-08-44`): range `audit/2026-11-08-43..877662c` (D52 by Codex, U15 closed by Claude). D52 follows the card: `LiteralProcessStart` with the specified order (start info, clear, caller variables, wrapper variables last), `ProcessRunner` and the runner script use it, the literal block is exact, both red tests exist, `CodexCliIntegrationSourceRegression.ps1` follows the moved encoding lines (logged as a supporting change), Context7 records the gap and the three vendor pages. P-50 decided: the metadata stop was D53's first-list defect, not D52's; --metadata-policy-only against the installed 877662c green (TALVORA MCP METADATA POLICY LIVE GREEN); the full installed smoke green (TALVORA MCP SMOKE GREEN); signed-in Full gate 70/0 Hepsi geçti.; CI 42476029267 success; live talvora_npm_run ["--version","a&echo","TALVORA_BATCH_PROBE"] → stdout `12.2.1` only. D53's acceptance now names the first metadata run after its install. No screen changed in the range; the design improvements stay in WL3 (eight items). Next: D53.
