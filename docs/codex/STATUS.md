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
| Audit baseline | Newest Claude audit tag `audit/2026-11-11-51` = the audit 51 commit. Claude creates and moves `audit/*` tags; the agent never does. |
| Executors | Codex **or** ChatGPT with the Talvora connector — identical rules (AGENTS.md → WHO MUST FOLLOW THESE RULES). Log entries name the executor. |
| Runtime | Windows service `Talvora` (LocalSystem), `127.0.0.1:7676`, installed under `C:\Program Files\Talvora\Versions\<commit>-<stamp>`; tray `Talvora.Tray.exe` in the user session. |
| Toolchain | .NET SDK 10.0.404 exact pin (SDK3, 2026-11-13; runtime 10.0.15) with matching locked NuGet graphs (K0 done). PowerShell 7 required by the interactive runner. |
| Install path | `scripts/Deploy-Windows-Installer.ps1` (SYSTEM task; when called from inside Talvora it does not wait — poll `/healthz.sourceCommit`). Never run `Reset-And-Install.ps1` unless a card says so. |
| Secrets | `C:\ProgramData\Talvora\Secrets\*` belong to the user's other projects. Never read, move, print or delete them. Listing names/ACLs is allowed. `C:\ProgramData\Talvora\Context7\api-key` is the user's Context7 key (CTX3): `talvora_context7_docs` sends it; never read, print, copy or commit it. |
| User preferences | Personal single-user workstation: no local MCP token (Q1). Cosmetics matter a lot: every visible change needs before/after PNGs. The worklog's vertical **code terminal** is the user's favourite feature — never break or shrink it. |

## In progress

In progress: (none)

## Agent queue

Legend: `[ ]` todo · `[x]` done · `[~]` done but waiting for user evidence · `[?]` blocked on an open question · `[-]` won't do.

> **Only open work lives in these documents** (user decision 2026-11-07). At every audit Claude removes finished queue lines, Log entries older than the previous audit, answered findings, finished cards and decided proposals that no open card names. Git keeps all of it: `git show audit/<tag>^:<path>` is a file as it was before that audit's cleanup (first cleanup: `audit/2026-11-07-40`). The agent may tick a line `[x]` and leave it there.

- [~] RL3 — installed 0b749e3: Apps entry values, `--what-if`, smokes and CI checked at audit 50; live pending (Claude): the real uninstall → reinstall → restart with the user at the next audit (user decision 2026-11-11), together with D66
- [~] RL4 — Tag-triggered release workflow built and tested; live pending: windows-ci and workflow_dispatch dry run (no real release)
- [ ] D67 — PowerShell 7 downloads to a private, locked file and shows its progress (audit 50; P-54 decided: SDK3 moved the pin to 10.0.404)
- [ ] DEP19 — Tomlyn 2.11.1 from Dependabot PR #19 (audit 51; P-55 decided: SDK3 moved the pin to 10.0.404)
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

- 2026-11-13 — [C] SDK3, executor Claude (answers P-54/P-55): Microsoft's November .NET 10 security servicing had replaced SDK 10.0.403/runtime 10.0.14 on the workstation, so every restore failed. `global.json` 10.0.404 (MSBuild still 18.9.15), the 17 platform packages 10.0.15 via `Update-DotnetPlatformPackages.ps1` with all six lock files regenerated, THIRD-PARTY-NOTICES, MODERNIZATION-POLICY, ARCHITECTURE, CONTEXT7 map, dependabot comment and the Toolchain fact aligned. PLATFORM_PACKAGE_LOCKSTEP_GREEN, THIRD_PARTY_NOTICES_SOURCE_GREEN, MODERNIZATION_POLICY_GREEN; signed-in Full 72 kontrol, 0 hata, Hepsi geçti. Context7 noLookup (servicing only; Microsoft release metadata opened). Installer, installed smokes and CI follow in audit 52.
- 2026-11-13 — P-55, executor ChatGPT: DEP19 Apply-DependabotPr FAILED at restore (-2147450725): the pinned SDK 10.0.403 is missing (only 10.0.404 installed). Partial changes reverted; no package update, deployment or PR close.
  Signed-in Changed: 11 checks, 0 errors, `Hepsi geçti.`; baseline windows-ci green: 43086450913. No Context7 lookup (proposal only). Waiting for Claude's toolchain decision; Gitea #9 comment 241.

- 2026-11-11 — P-54, executor ChatGPT: D67 RED CS1503 and NativeInstallerSourceRegression GREEN after temporary implementation; all code/test changes reverted.
  Signed-in Changed failed Restore: global.json requires SDK 10.0.403 (rollForward disabled), workstation has only 10.0.404; Full, installed checks and CI cannot be claimed. Waiting for Claude's separate toolchain decision; no SDK or pin change made. Gitea #9 comment 240.

- 2026-11-11 — Claude, audit 51 (tag `audit/2026-11-11-51`): range `audit/2026-11-09-50..29b7d5e` — D65 and D66 (ChatGPT). Rules: one Turkish commit per card with its `Card:`, files within each card, literal steps followed, self-review blocks present. Context7: D66's question is in the ledger (2026-11-09, status 200) word for word; D65's `noLookup` matches its card. Lines read: D65 reuses the shared scanner, removes the root table and its sub-tables only when the root carries the installer URL, keeps CRLF and multiline strings; `Upsert` output unchanged. D66 renames the running setup before the reboot deletion, falls back with a log line, order unchanged. Live: `/healthz` = 29b7d5e; the agent's metadata and full smokes GREEN (2026-11-09 and again 2026-11-11); windows-ci green on both commits. D66 closed here because a second `Card: D66` commit is refused after its own commit. Findings: Dependabot PR #19 (Tomlyn 2.11.1) is red with NU1004 → DEP19; D67's progress text used `…` while the engine uses three dots → step 4 now names both texts exactly. Screens: no UI change in the range; Tasarım geliştirmeleri: the setup window's progress texts mix `...`/`…` and "hizmet"/"servis" → added to Claude's [C] D47. Asked the user: the real RL3/D66 uninstall → reinstall → restart waits for the next audit ("Sonraki denetimde"). Release: none (0.0.1.0 still waits for RL4's dry run, RL5, D67, DEP19, D47–D49, WL3, the RL3 live check and README screenshots). Cleanup: finished D65/D66 cards, decided proposals and Log entries before 2026-11-09 removed.
- 2026-11-09 — D66, executor ChatGPT: uninstall renames the running setup to unique .kaldirildi before scheduling deletion, then schedules the directory; logs the original-path fallback on IO/access rename failure. Installation and uninstall order unchanged.
  RED CS0117 (RetireRunningSetup absent), NativeInstallerSourceRegression GREEN; signed-in Changed 16/0 and Full 72/0, Hepsi geçti.
  Context7 /websites/learn_microsoft_en-us_windows_win32_api (MoveFileEx path-based reboot deletion). Gitea #9 comment 239.
  Self-review — no UI or extra file changes; original setup name remains free for reinstall. Live pending clean-HEAD deploy, installed smokes, CI; real uninstall/reinstall/restart belongs to Claude with the user.

- 2026-11-09 — D65, executor ChatGPT: uninstall reuses the shared TOML scanner, removing the installed Codex root and sub-tables while preserving unrelated tables, CRLF and multiline strings; the Upsert writer remains unchanged.
  RED CS0117 (RemoveTalvoraLocal absent), then NativeInstallerSourceRegression and CodexCliIntegrationSourceRegression GREEN; signed-in Changed 19/0 and Full 72/0, Hepsi geçti.
  Context7 noLookup (own string handling). Installed --uninstall --what-if still lists the same six actions. Gitea #9 comment 238.
  Self-review — no UI changes and no other Codex registration touched. Installed 21848ca: healthz sourceCommit exact; metadata policy and full MCP smoke GREEN; windows-ci 42561332554 GREEN.

- 2026-11-09 — Claude, audit 50 (deep; tag `audit/2026-11-09-50`): range `audit/2026-11-08-49..2e7e6fd` — RL1–RL4 (ChatGPT), CTX3 and KUR1 (Claude). Rules: one Turkish commit per card with its `Card:`, files within each card, self-review blocks present. Context7: every question of RL1–RL4 is in the ledger with status 200 (RL3 and RL4 already through the user's key); the gate refused RL4's first push because its evidence named shorter questions than it had asked — the agent then asked exactly those and pushed. Lines read: RL3 uninstall order, Secrets and reparse guards are right; findings: the Codex cleanup orphans user sub-tables (D65), the reboot deletion is bound to the setup's fixed path so a reinstall before restarting loses its uninstaller (D66), the elevated PowerShell 7 download uses a predictable, unlocked file in the user's temp folder and shows no progress (D67). RL4: tag-only releases, env-passed inputs, ancestry, CI and notes checks, pins equal to windows-ci. Live: SYSTEM `--verify-powershell-download` green (32 s); installed 0b749e3 with metadata and full smokes green; Apps entry values as carded; CI green to 2e7e6fd; Control Center smoke PNGs show "geliştirme sürümü" in Hakkında and Genel bakış. Look-ahead: RL5 now names the rail dot (`NavRailItem.HasDot`), the state file, the smoke hook and a precise Context7 question. .NET 10.0.14 / SDK 10.0.403 still newest. Tasarım geliştirmeleri: PowerShell download progress (D67). Release: none (0.0.1.0 waits for RL4's dry run, RL5, D65–D67, D47–D49, WL3 and README screenshots). Asked the user nothing: every finding has one correct fix.
- 2026-11-09 — RL4, executor ChatGPT: new pinned GitHub Actions release workflow for v0.0.*.* and manual dry_run; checks version/tag, origin/main ancestry, same-commit successful windows-ci and release notes. Builds one versioned EXE and lowercase SHA256SUMS.txt; gh release create only on approved tag pushes, never on manual dispatch.
  RED missing workflow, then RELEASE_WORKFLOW_GREEN, ModernizationPolicyRegression and CiDocsOnlyRegression GREEN. Signed-in Changed 13/0 and Full 72/0, Hepsi geçti. Context7 /websites/github_en_actions (tag filters, boolean dispatch); GitHub/gh CLI official docs. Gitea #9 comment 236.
  Self-review — no UI changes; manual dry_run defaults true; no tag/release created, least-privilege GH token, checksum/notes/CI gates. Live pending committed-HEAD windows-ci and nonpublishing workflow_dispatch dry run.

- 2026-11-09 — RL3, executor ChatGPT: installer copies Talvora-Kurulum.exe, writes the HKLM Uninstall entry (version, publisher, icon, uninstall commands, DWORD flags/size), wires KUR1 uninstall UI, --silent and read-only --what-if. Removal stops tray then service; clears only Talvora Run/shortcut/Codex table and Program Files; optional ProgramData cleanup excludes Secrets.
  RED then GREEN NativeInstallerSourceRegression (temporary HKCU registry fields, 6-step plan, optional fixture data, Codex table isolation, no log recreation). Real setup --what-if: 6 steps, --remove-data: 33, Secrets excluded. Signed-in Changed 26/0 and Full 71/0, Hepsi geçti. Context7 /dotnet/docs + Microsoft Windows registry and MoveFileEx.
  Self-review — no KUR1 visual edits; ordered preview and cancellation-aware cleanup; exact Turkish progress/errors; reparse guards, protected Secrets, user TOML retained and no post-delete success log.
  Live pending: built committed installer, HKLM/Apps entry, installed smokes, GitHub CI; actual uninstall/reinstall and UI verification are Claude's next-audit check with user's go-ahead. Gitea #9 comment 235.

- 2026-11-09 — [C] KUR1, executor Claude (user request 2026-11-08: a stylish setup window that names the developer without overdoing it): `Talvora-Setup.exe` speaks Santral (`InstallerForm.cs`; new `SetupTheme.cs`, `SetupArt.cs`, `SetupControls.cs`; DESIGN-SANTRAL §10): a patch cord that the real install progress carries from KURULUM PAKETİ to BU BİLGİSAYAR, plug seat, one light and a green lamp on success, the cord pulling back and a red lamp with the log path and `Yeniden dene` on failure, no message boxes, no closing half way, Update and Uninstall modes (RL3 wires the latter), per-monitor DPI, Gündüz/Gece from the Control Center's preference then Windows, system colours and no motion in high contrast or reduced motion, the Windows 11 title bar in the panel colour, and the brass maker plate `GELİŞTİREN · TAYLAN SOYLU` ("Geliştiren: Taylan Soylu" for screen readers). Red first: `SetupWindowKeepsSantralLookAndDeveloperPlate` false on the old window, green after; every state rendered in both themes and high contrast through a design harness; Context7 `/dotnet/docs-desktop` and `/websites/learn_microsoft_en-us_windows_win32_api`, asked through the installed `talvora_context7_docs`.