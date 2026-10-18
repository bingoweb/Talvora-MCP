# Talvora — Codex status

> Single live status for agent work (Codex or ChatGPT via Talvora). English. Newest Log entry on top.
> Cards: `docs/codex/ROADMAP.md` (fixes, K/S/R/Q), `docs/codex/DESIGN-WORKLOG.md` (desktop worklog N1–N8), `docs/codex/DESIGN-CONTROL-CENTER.md` (Control Center UI1–UI8).
> Protocol: AGENTS.md → **CONTINUE PROTOCOL**. Shortcut for the user: "Talvora'ya devam et".

## Fixed facts

| | |
|---|---|
| Local folder | `C:\Users\tayla\Talvora-MCP` |
| Remotes | `origin` = local Gitea `ssh://git@127.0.0.1:2222/taylan/Talvora-MCP.git` (primary tracker) · `github` = `https://github.com/bingoweb/Talvora-MCP.git` (**PUBLIC** — never commit secrets, machine-specific credentials or personal data) |
| Branch | `main` only. Do not create branches. Do not touch `dependabot/*`. |
| Commit identity | `Taylan Soylu <6751394+bingoweb@users.noreply.github.com>` (do not change) |
| Push | **Authorized** (Q3, 2026-10-18): after each card `git push origin main` and `git push github main`; never force. |
| Audit baseline | Newest Claude audit tag `audit/2026-10-18-02` = `dd4ee5b`. Claude creates and moves `audit/*` tags; the agent never does. |
| Executors | Codex **or** ChatGPT with the Talvora connector — identical rules (AGENTS.md → WHO MUST FOLLOW THESE RULES). Log entries name the executor. |
| Runtime | Windows service `Talvora` (LocalSystem), `127.0.0.1:7676`, installed under `C:\Program Files\Talvora\Versions\<commit>-<stamp>`; tray `Talvora.Tray.exe` in the user session. |
| Toolchain | .NET SDK 10.0.402 exact pin with matching locked NuGet graphs (K0 done). PowerShell 7 required by the interactive runner. |
| Install path | `scripts/Deploy-Windows-Installer.ps1` (SYSTEM task; when called from inside Talvora it does not wait — poll `/healthz.sourceCommit`). Never run `Reset-And-Install.ps1` (destructive until R3). |
| Secrets | `C:\ProgramData\Talvora\Secrets\*` belong to the user's other projects. Never read, move, print or delete them. Listing names/ACLs is allowed. |
| User preferences | Personal single-user workstation: no local MCP token (Q1). Cosmetics matter a lot: every visible change needs before/after PNGs. The worklog's vertical **code terminal** is the user's favourite feature — never break or shrink it. |

## In progress

(none)

## Agent queue

Legend: `[ ]` todo · `[x]` done · `[~]` done but waiting for user evidence · `[?]` blocked on an open question · `[-]` won't do.

- [x] K0 — Locked restore green, SDK drift policy
- [x] K1 — `Invoke-QualityGate.ps1`, all suites in CI, `Get-CodexContext.ps1`
- [x] K2 — `talvora_apply_patch` works on this repository (Win32 32)
- [x] E5 — Context helper and gate output minimal (extends K1)
- [~] E1 — Codex sees only the Talvora tools it uses (live Codex session check deferred by user)
- [~] E2 — Stop sending every result twice (Codex live acceptance deferred by user)
- [?] D1 — Make the rename-protection fallback visible (waiting for Claude: P-5)
- [?] D3 — Keep the text of structured failures (waiting for Claude: P-6)
- [x] D4 — Run service-dependent gate checks locally when the service is up (audit 2026-10-18 (2))
- [ ] D2 — Compare legacy directory identities with the legacy serial (audit 2026-10-18 (2))
- [ ] A2 — `talvora_agent_brief` (rules + current card for any agent)
- [ ] A1 — `talvora_user_run` + keep file ownership + one-time owner repair
- [ ] A3 — Work-contract gate in `talvora_git_run`
- [ ] E3 — Lean defaults for high-frequency tools (P-1 decided: no `talvora_list` row)
- [ ] S1 — Reject foreign Host/Origin (P-2 decided: Penpot static routes exempt from Origin guard)
- [-] S2 — Local MCP caller authentication (user decision Q1: no)
- [ ] Q6 — Schema validation of tool arguments before invocation (P-3 decided, re-scoped)
- [ ] Q6b — Research: supported exception-translation mechanism (proposal only, no product code)
- [ ] N1 — Worklog: correct states, titles, reasons; no duplication (P-4 decided: message fallbacks)
- [ ] N2 — Worklog: protocol v4, sessions, project/actor context
- [ ] N3 — Worklog: real facts from results
- [ ] N3b — Worklog: code terminal real, complete, persistent
- [ ] N4 — Worklog: agent jobs tell their story (+ Codex code terminal)
- [ ] S3 — ProgramData ACL + interactive runs out of user-controlled paths
- [ ] S4 — Pin ast-grep toolchain
- [ ] S5 — Do not keep the OpenAI Admin key
- [ ] S6 — Knowledge tools: scoped fetch, no cloud hydration
- [ ] S7 — Desktop IPC server verification + real interactive session
- [ ] S8 — Git push detection parser
- [ ] R1 — Agent job registry, persistence, orphan recovery, limits
- [ ] R2 — Prompts via stdin
- [ ] R3 — Safe Reset-And-Install
- [ ] R4 — Upstream-degradation aware recovery, bounded logs
- [ ] R5 — One version everywhere
- [ ] Q1 — Tool titles, shorter descriptions, payload budget
- [ ] Q2 — Tool surface consolidation Q2a/Q2b/Q2c (zero capability loss)
- [ ] Q3 — Documentation diet
- [ ] Q4 — Context7 gate covers XAML/tests
- [ ] Q5 — Control Center smoke works for light and dark
- [ ] UI1 — Tokens, theme manager (Sistem default, Gece fallback), typography
- [ ] N5 — Worklog card visual redesign + preview harness
- [ ] N6 — Worklog evidence surface ("Ayrıntılar")
- [ ] UI2 — Shell, rail, title bar, command palette
- [ ] N7 — "Çalışma günlüğü" history page
- [ ] UI3 — Overview: Nabız ring + chain strip
- [ ] UI4 — Services list + detail
- [ ] UI5 — Events + raw log
- [ ] UI6 — Settings + setup stepper
- [ ] N8 — Notification settings
- [ ] UI7 — Tray icon/menu, worklog colour alignment, About
- [ ] UI8 — Polish, accessibility, cleanup, screenshots

## User queue

- U8 — E2: live Codex MCP round-trip intentionally deferred under "do not use Codex". Unit, installed wire smoke and ChatGPT Dev connector will be checked without running Codex; nothing to do unless Codex use is reauthorized.
- U7 — E1: optional live Codex MCP get and scratch read/patch verification is deferred by the user's "do not use Codex" instruction; no action is needed unless Codex use is reauthorized. Installer registration and ChatGPT connectors are verified independently.
- U2 — Claude Code is installed but **not logged in** (HANDOFF 2026-10-17). If you want the optional Claude engine: run `& "$env:USERPROFILE\.local\bin\claude.exe" auth login` in your own terminal.
- U3 — After S5 ships: if the Control Center asks for the OpenAI Admin key again during setup, enter it once (it will no longer be stored).
- U4 — After Q2 ships: refresh the Talvora Dev/Admin connectors in ChatGPT Business so the new tool list is picked up.
- U5 — After UI2: check window placement on your multi-monitor setup (drag between monitors, restart tray).
- U6 — After N5: look at the worklog preview PNGs on the Gitea issue and say "beğendim" or what to change.

## Open questions

P-5/P-6 — D1 and D3 need scope decisions (docs/codex/PROPOSALS.md).

## Findings for Claude

(The agent adds one line per observation outside the current card: `file:line — what was seen`. Do not fix them.)

## Log

- 2026-10-18 — D4 (executor: ChatGPT via Talvora): the local Full gate probes `/healthz` without proxy and with 2-second timeouts; runs HealthHandle for a Windows service, and NativeInstallerRuntime only with an interactive desktop. CI still skips both when no service exists.
  Fixture cases: service+desktop, headless, non-service and unreachable all green; installed HealthHandle 200 requests with +3 handles (max +25), and NativeInstallerRuntime green.
  Context7 `/microsoftdocs/powershell-docs`, official PowerShell/CIM evidence in `.context7/verification.json`. Local Fast 13/0; Full 51/0, last line `Hepsi geçti.`; installed runtime `b79ac3e` unchanged (scripts/tests only).
  Carry-over: P-5/P-6 proposal records document why D1/D3 remain blocked. Gitea SSH sync failed: private-key permissions too open; push delivery to be checked after commit. Next D2.
- 2026-10-18 — Claude audit (2) of `8b1a611..dd4ee5b` (executor: ChatGPT via Talvora): local Full gate 49/0 `Hepsi geçti.`, CI green since `fca44fe`, installed `b79ac3e`, K2 verified live (apply_patch add/delete in this repo committed). Findings → D1–D4 + A1; proposals P-1…P-4 decided (E3, S1, Q6 re-scoped + Q6b, N1 unblocked). New Phase A (A1–A3) makes ChatGPT+Talvora follow the same contract as Codex; AGENTS.md generalised to "the agent", trailer is now `Card: <ID>`. Next audit baseline: tag `audit/2026-10-18-02` = `dd4ee5b`.
- 2026-10-18 — Blocked E3/S1/Q6/N1 without runtime modifications. P-1: E3 listed DeveloperTools.Files.cs but talvora_list is in FileTools.cs; P-2: S1 Penpot browser origin unavailable; P-3: Q6 SDK handles tool exceptions before outer filters; P-4: N1 no-copy filler conflicts with IPC's nonempty Message validation. N1 test proved RED; attempted fix compiled 0 warnings but hit existing IPC validation; partial source/test changes fully reverted. Context7: /modelcontextprotocol/csharp-sdk, /dotnet/aspnetcore.docs, /dotnet/wpf. Installed runtime remains b79ac3e; baseline Fast gate 13/13, Hepsi geçti. Next N2 depends on N1 decisions.
- 2026-10-18 — E2: replace duplicate text with one short marker only for successful non-null structured results; structured JSON and non-text blocks unchanged; isError/only-text/null-JSON preserved. Fixture and installed-live smoke RED before code/deploy; in-process targeted smoke GREEN. Source: Context7 /openai/codex and /modelcontextprotocol/csharp-sdk; vendor sources in .context7/verification.json.
  | Client | Model receives when both fields exist | Evidence |
  |---|---|---|
  | Codex CLI | structuredContent preferred; ordinary text ignored | github.com/openai/codex/blob/main/protocol/src/models.rs |
  | ChatGPT custom MCP | structuredContent and text both model-visible | developers.openai.com/plugins/reference |
  | Claude Code | structuredContent alone in reported versions | github.com/anthropics/claude-code/issues/54737 |
  Full gate: 49 controls, 0 failures; Hepsi geçti. Installer sourceCommit and wire/ChatGPT smoke pending; Codex session forbidden by user (U8).
  Compatibility note: MCP 2026-07-28 spec SHOULD mirror JSON in text for older text-only clients; the E2 card restricts this decision to the three researched clients.
- 2026-10-18 — E1: registered only eight selected Talvora Dev tools for Codex at /mcp/dev, preserving unrelated TOML comments, tables and CRLF bytes; installer writes Codex-only configuration. Current live /mcp/dev exposes 212 tools and /mcp/admin 88 unchanged; the eight selected schemas are 18,691 versus 265,499 characters (full Dev). Fixture test RED then GREEN; targeted policy/source tests passed. Context7 /openai/codex confirmed enabled_tools client-side filtering. Full gate: 49 controls, 0 errors; Hepsi geçti. Canonical installer/deploy after commit; Codex CLI/session not used per user instruction (U7).
- 2026-10-18 — E5: context helper now selects only the card, cited N/UI spec sections, pending queue additions and open proposal titles; fixture docs and all 17 real N/UI cards asserted at ≤250 lines. Quality gate times steps, groups 39 successful PowerShell regressions, and shows only the failing step's last 60 diagnostic lines; the fixture was RED before the fix and GREEN after. Context7 /microsoftdocs/powershell-docs, official Microsoft Learn about_Redirection and Get-Content sources; .context7/verification.json refreshed. Local Full gate: 49 controls, 0 errors, 15 lines; Hepsi geçti. Installed runtime stays 457565f (no runtime changes). Commit/remote sync to be verified.
- 2026-10-18 — K2: verified Win32 32 on installed patch tool and in a RED test with a separate DELETE-capable directory holder; the targeted test is now GREEN. Metadata identity probes use shared backup-semantic non-reparse handles with no data/delete access; FILE_ID_INFO checks 128-bit identity + volume, sharing retries 50/100/200 ms, with rename-protection handles retained when compatible. Minimal SourceEditModels.cs addition preserves legacy journal FileId and adds optional full ID (required outside Files list). Context7 /websites/learn_microsoft_en-us_windows_win32_api. Applied E5/E1/E2/E3 queue additions verbatim. Local Full gate: 49 kontrol, 0 hata; Hepsi geçti. Install, live patch smoke, CI and push remain to be verified; baseline installed runtime f0b5f9d.
- 2026-10-18 — K1 CI follow-up: GitHub Actions 39124553887 proved locked restore/build, SourceEdit, Notification and 14 additional checks green, then failed because HealthHandleRegression requires a running Talvora service but Full gate ran before installer. The gate now explicitly defers only this service-dependent test; CI executes its same 225 health requests and process-handle stability assertion after installer setup. QualityGateRegression enforces workflow step ordering. Context7 /microsoftdocs/powershell-docs evidence refreshed; new CI verification pending.
- 2026-10-18 — K1: using Talvora tools only (no Codex agent), added scripts/Invoke-QualityGate.ps1 (Fast/Full, quiet named steps, failure details only on failure, final “Hepsi geçti.” or “Kontrol başarısız: <step>”), scripts/Get-CodexContext.ps1 (STATUS header + newest log + selected card + file line counts), and QualityGateRegression.ps1 behavioral fixture for SourceEdit failure attribution/context selection. Replaced duplicate early CI steps with the same Full gate; SourceEdit, Notification, integration-policy behavior and every applicable tests/*.ps1 now run in CI. NativeInstallerRuntime is deferred until installation and an interactive Windows logon; locally it passed separately. All 41 PowerShell regressions passed; Full gate: 50 controls, 0 failures, final “Hepsi geçti.”; Release build and locked restores green. Updated stale InstallerRollbackState snapshot assertion to validate bounded ReadExactlyAsync rather than obsolete File.ReadAllBytesAsync; updated modernization/Codex integration source regressions to validate shared CI wiring without relaxing any behavior. Context7: /microsoftdocs/powershell-docs (native LASTEXITCODE, pwsh -File exit semantics, Parser.ParseFile) with Microsoft vendor evidence in .context7/verification.json. Talvora installed runtime remains f0b5f9d (K1 has no runtime source changes). Source-edit service still rejects its own repository with Win32 32; K2 is next. Post-commit CI and two-remote push pending.
- 2026-10-18 — K0: pinned .NET SDK 10.0.401/latestFeature to 10.0.402/disable per Context7 /dotnet/docs and current Microsoft docs; regenerated the Tray and Installer ILLink lock entries to 10.0.13 without dependency overrides. Added SDK-change lock evidence requirement to GitHub CI for PR and push, with fixture-backed modernization policy regressions. Verified solution and installer --locked-mode restores, Release solution build (0 warnings / 0 errors), ModernizationPolicyRegression.ps1, Context7 gate, git diff --check; installer/deployment and GitHub CI must be checked against the post-commit SHA.
- 2026-10-18 — Claude: user answers recorded. Q1 = no local token (S2 → won't do; S1 stays, it needs no client change). Q2 = consolidate only with zero capability loss (Q2 unblocked, hard rule added). Q3 = push after each card to `origin` and `github`. Q4 = delegated to design → Control Center follows the Windows theme, Gece fallback, worklog always Gece. New: deep review of the desktop worklog (live screenshot `docs/codex/evidence/worklog-card-2026-10-18.png`) → `docs/codex/DESIGN-WORKLOG.md` (defects W1–W19, code-terminal defects C1–C7, cards N1–N8 + N3b); new card Q6 (opaque tool errors, verified live). Queue reordered: worklog correctness (N1–N4) right after the security/build basics.
- 2026-10-18 — Claude: deep review at `8b1a611`; created `docs/codex/ROADMAP.md`, `docs/codex/DESIGN-CONTROL-CENTER.md`, this STATUS and `docs/codex/CODEX-PROMPT.md`; added CONTINUE PROTOCOL to AGENTS.md. No source code changed. Key evidence: locked restore NU1004 (CI run 39028784145 red), foreign `Host` accepted by `/mcp` (HTTP 200), `C:\ProgramData\Talvora` inherits `BUILTIN\Users:(CI)(WD,AD)`, Control Center smoke fails with `WhiteRatio=68.2%`, live tool counts 249/212/88 and `tools/list` 302 KB on `/mcp`.
