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
| Audit baseline | Newest Claude audit tag `audit/2026-11-08-48` = the audit 48 commit. Claude creates and moves `audit/*` tags; the agent never does. |
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

- [~] CTX2 — installed 953fc7d; both installed MCP smokes green, CI success (audit 47); Claude checks the next card's push
- [ ] PK1 — MCP C# SDK 2.4.x and Tomlyn 2.10.2 (after UR4; P-54 decided: the lock files are part of it)
- [ ] PK2 — Microsoft.Build.Framework follows the SDK's MSBuild, 18.9.6 (after PK1; audit 48 added the lock step and the MSBuildLocator check)
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

- 2026-11-08 — Claude, audit 48 (deep; tag `audit/2026-11-08-48`): range `audit/2026-11-08-47..2f86766` (D64 and the P-54 stop by ChatGPT). D64 follows its card line by line: the gate skips ledger records of Talvora's own clients, checks every `(query: …)` of an entry, `talvora_context7_docs` is open-world (84), an empty detached subject becomes program plus first argument. P-54 decided (option 1, Claude's miss): PK1 names the seven lock files and one `--force-evaluate` restore; Claude checked MCP 2.4.0's nuspec dependencies on nuget.org (inside the current pins) and its release notes (no breaking change; #1934 and #1928 are checked by the installed smokes). Look-ahead: PK2 gets the same lock step and an MSBuildLocator check, because `Microsoft.Build.Framework` 18.9.6 brings `Microsoft.NET.StringTools` 18.9.6 and MSBuildLocator's documentation (Context7 `/microsoft/msbuildlocator`) asks for `ExcludeAssets="runtime"` on every MSBuild package; the map's MSBuild row names `/microsoft/msbuildlocator`. RL1–RL5 read clean. CTX2's live proof still waits for a push with real questions (D64 used `noLookup`). Signed-in Full gate at 2f86766 in a clean clone 70/0 `Hepsi geçti.`; CI success on every commit. No screen changed. Removed: finished queue lines and cards (D64), the audit 47 section, Log entries before audit 47.

- 2026-11-08 — PK1, executor ChatGPT: NuGet confirms MCP 2.4.0 and Tomlyn 2.10.2; Context7 `/modelcontextprotocol/csharp-sdk/v2.3.0` returned tool schemas, filters and McpException docs; `/xoofx/tomlyn/2.10.2` reported no indexed version. Official SDK v2.4.0 and Tomlyn 2.10.2 release notes checked. RED pin test: MCP 2.3.1 and Tomlyn 2.10.1; pinned updates led to NU1004 from `dotnet restore Talvora.slnx --locked-mode` because seven tracked package lock graphs still pin old versions. Changes to the pins reverted, no lock files touched. P-54 submitted for Claude decision; Full gate, deploy and CI for PK1 not run. Baseline Changed 8/0, Hepsi geçti. D64 CI 42510491771 success.
  Self-review — no visual or Turkish UI changes; existing runtime and locks preserved; the requested upgrade cannot pass the mandatory locked restore without explicitly authorized lockfile updates.

- 2026-11-08 — D64, executor ChatGPT: the push gate excludes Talvora-owned Context7 ledger records and validates every (query: "...") in each library entry; Context7 docs is open-world (84 reviewed tools), and empty detached-start subjects become program plus first non-option argument. RED: Talvora.Smoke-only evidence accepted and dotnet build reported an empty subject; GREEN: D64 source-edit and notification regressions, four pinned PS tests, Changed 18/0 and signed-in Full 70/0, Hepsi geçti. Context7 noLookup: Talvora-only implementation; no external API reliance. Install, installed smokes and CI pending. Gitea #9 comment 230.
  Self-review — no UI layout changes; detached tasks have correct Turkish names, all claimed questions require non-own success records, and the ledger format, CI scripts and 24-hour window were untouched.

- 2026-11-08 — Claude, audit 47 (deep; tag `audit/2026-11-08-47`): range `audit/2026-11-08-46..953fc7d` (D60, D61, D62, CTX1, CTX2 by ChatGPT; Claude's docs). Read line by line: D60's quiet calls find the session through `path` too (gate logs in the repository), the counter is lock-guarded and the close timer waits for it; D61 keeps the newest terminal step per session and writes it when a newer session or the tray's `Dispose` comes; D62 and CTX1 follow their cards; CTX2's gate reads the pushed commits, skips documentation-only pushes and matches each question to the ledger. Rule break: CTX2 changed `Validate-Context7Gate.ps1`'s 14-day age rule to "verifiedAtUtc must be today (UTC)" against its Don't and did not log it; evidence verified two days ago was refused (reproduced), and `Validate-ProjectDeliveryGate.ps1` (the gate's Delivery check, not named in CTX2's card: Claude's miss) still refused noLookup evidence; Claude fixed both as [C] D63. D64 queued: the gate counts the installed smoke's own `/dotnet/docs` question as proof, only the first `(query: …)` of an entry is checked, `talvora_context7_docs` is advertised closed-world, and a detached start with no subject reads " arka planda başladı". Live checks from the stored worklog, all done: D46 ("GitHub denetimi sürüyor" after each push), D60 (D62, CTX1 and CTX2 each stay one session through their 100-second waits), D61 (sessions end with the deploy step "Kurulum devredildi"), D62 ("Talvora.Smoke arka planda başladı"). Ledger: ChatGPT's CTX2 question recorded with status 200. Signed-in Full gate at 953fc7d in a clean clone 70/0 `Hepsi geçti.`; CI success on every commit; no tray error since 19:00; no leftover relay process. Noted, no card: `talvora_git_run` runs local git commands as LocalSystem, so new `.git` objects are SYSTEM-owned but keep the user's inherited full control; the Classroom folder itself is SYSTEM-owned (a user step, outside Talvora). Screen pass: three of the last five agent sessions read "Kurulum devredildi · doğrulama bekleniyor" in the list although each has a task note; user decision: the task note becomes the list headline (WL3 item 11). AGENTS.md: the Context7 rules lose their "after CTX1/CTX2" conditions. Removed: finished queue lines and cards (D46, D60, D61, D62, CTX1, D63), the audit 46 section, Log entries before audit 46.
