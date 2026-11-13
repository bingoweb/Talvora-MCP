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
| Audit baseline | Newest Claude audit tag `audit/2026-11-13-54` = the audit 54 commit. Claude creates and moves `audit/*` tags; the agent never does. |
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

- [~] RL3 — installed 0b749e3: Apps entry values, `--what-if`, smokes and CI checked at audit 50; live pending (Claude): the real uninstall → reinstall → restart with the user, together with D66 (postponed by the user 2026-11-13; before 0.0.1.0)
- [~] RL4 — Tag-triggered release workflow built and tested; live pending: windows-ci and workflow_dispatch dry run (no real release)
- [~] WL5 — Measured Turkish titles/results installed at 3ce8e66; metadata and full MCP smokes GREEN; CI 43510238063 GREEN. P-57 and Claude visual check pending
- [~] WL6 — Real-time process/log/CI/deploy followers built; signed-in Changed 30/0 and Full 72/0 GREEN; live install/CI and Claude visual review pending
- [ ] RL5 — The Control Center says quietly when a new version is out (user decision 2026-11-08; after RL1)
- [ ] [C] D48 — A vault note belongs to the vault's owner (Claude since audit 46: built at audit 42; after the agent's queue)
- [ ] [C] D49 — Plain Turkish service names, also for older registries (Claude since audit 46; after D46)
- [ ] [C] D47 — Plain Turkish for the remaining Control Center texts (Claude since audit 46; setup texts per the user's decision; table and test in docs/codex/reference/d47)
- [ ] [C] WL3 — Çalışma günlüğü and Olaylar polish (Claude; audit 42, user: later; after D45 and D50; audit 43 adds items 7–8)

## User queue

- [ ] U17 — Yalnızca gerekirse: bir sonraki ChatGPT oturumunda masaüstü kartında «ne» satırı hiç görünmezse, ChatGPT Talvora'nın araç listesini 13 Kasım öncesinden tutuyordur. ChatGPT'deki Talvora bağlayıcısının ayarlarından araç listesini yenileyin; yenileme seçeneği yoksa bağlayıcıyı bir kez kaldırıp yeniden ekleyin.
- [ ] U16 — İsteğe bağlı: makine (sistem) PATH'inde aynı klasörü gösteren ikinci satırlar var: `C:\Python314\Scripts`, `C:\Python314`, `C:\Program Files\dotnet\`, `C:\Program Files\PowerShell\7\` ve `C:\Program Files\nodejs\`. Zararsızlar; isterseniz U14'teki gibi Sistem değişkenleri → Path → Düzenle'den bu beşini silebilirsiniz.

## Open questions

(none)

## Findings for Claude

(The agent adds one line per observation outside the current card: `file:line — what was seen`. Do not fix them.)

## Log

- 2026-11-14 — WL6, executor ChatGPT: gate START lines, Turkish stages and 64 KiB live log/process follower; signed-in CI checks every 30 s, persisted deploy replay on new service startup. TDD RED for START, follower and timeout; WL6_FOLLOWER_GREEN, QualityGateRegression, QualityGateChanged, QualityGateParallel and Notification GREEN; Changed 30/0 and Full 72/0, Hepsi geçti. Context7 /dotnet/docs (FileShare.ReadWrite); Gitea #9 comment 252. Clean-HEAD install/smokes, CI and Claude's real desktop visual review pending; first install upgrades an older service without the pending-deploy writer.
  Self-review — Cosmetics: existing card layout and terminal unchanged; real gate stages use only printed START/PASS/FAIL records.
  Self-review — Usability: followed sessions stay active and finish only at observed exit; missing and timed-out results are not called success.
  Self-review — Turkish: duration, stage, success, failure and timeout labels reviewed against DESIGN-WORKLOG 6.4.
  Self-review — Correctness: shared 64 KiB reads, 2-hour/30-minute limits, idempotent 30-minute deploy replay and signed-in CI proven by regression.

- 2026-11-13 — WL5, executor ChatGPT: accurate Turkish titles and measured outputs for Git, PowerShell, test scripts and .NET tests; command names only in technical evidence; one real staged diff after git add. RED inline title observed, WL5_MEASURED_RESULTS_GREEN, Notification GREEN; signed-in Changed 29/0 and Full 72/0, Hepsi geçti.; Context7 noLookup, Gitea #9 comment 251. P-57: N5 SHA-256 locks the required preview string edits; original approved fixture preserved. Clean-HEAD deployment, installed smokes and CI pending; Claude visual check outstanding.
  Self-review — Cosmetics: no layout or visual primitives changed; frozen N5 reference kept intact pending P-57.
  Self-review — Usability: all recorded titles show actions; measurable facts or elapsed duration replace bare success, no unsupported totals.
  Self-review — Turkish: all new labels and duration units checked against DESIGN-WORKLOG 6.4; fallback and failures remain clear.
  Self-review — Correctness/security: captured-output parsing is best effort; commands omit flags/secret arguments, redaction unchanged, Git staged count measured.

- 2026-11-13 — Claude, audit 54 (tag `audit/2026-11-13-54`): range `audit/2026-11-13-53..c6ea631` — D68, DEP21, D69, WL4 (ChatGPT) and Claude's card commit. Rules: one Turkish commit per card with its `Card:`, files within each card, self-review blocks present; WL4's session has no task note (`talvora_worklog_note` missing — the other three have one). Lines read: D68 stages under `Versions/.staging-<guid>` behind reparse checks, only the setup flow and the cleanup touch `Versions` (a leftover staging folder is removed by the next cleanup); DEP21 changes only Tomlyn entries, the 2.11.1 package equals nuget.org's catalog hash (the lock's contentHash differs from the raw .nupkg hash the same way for 2.11.0: NuGet hashes the unsigned content), PR #21 closed; D69 normalises `+`, `src:dst` and `refs/heads/`, four refspec forms blocked in the test. WL4: its schema/validator read-only source is the same; but the Turkish per-tool description pushed /mcp/dev tools/list to 232 407 B (Q7 budget 203 776; CI 43499460932 red, reproduced with `--surface-policy-live`), and its validator newly rejected a purpose over 140 characters, also on `talvora_apply_patch` → Claude's D70 (shipped). Live: a direct MCP call with `purpose` shows the 'ne' row on the real desktop card; Claude's own bridge did not send it because MCP clients keep the tool list from connect time → U17 (only if ChatGPT's cards stay without 'ne'). Context7: WL4's question is in the ledger (200) but used the unversioned id (card: `/v2.3.0`), and its vendor page was not fetched through Talvora (OLASI: ChatGPT's own browsing); D68/DEP21/D69 noLookup match their cards. Look-ahead: WL5 now says where command names go (`Komut: …` in the evidence Summary; they were not in the technical evidence for processes); WL6's gate lines are `START` only (the gate knows its total only at the end, so no fraction), the deploy step finishes from `pending-deploy.json` in the new service (no polling). Also: the three `labels: dependencies` blocks removed from dependabot.yml (the label does not exist; Dependabot warned in every PR). Admin tools/list margin is 1.6 KB: watch it on the next tool addition. Release: none. Asked the user nothing: every finding had one correct fix.
- 2026-11-13 — [C] D70, executor Claude (audit 54): WL4 pushed /mcp/dev tools/list to 232 407 bytes (budget 203 776: CI run 43499460932 red, reproduced locally with `--surface-policy-live`) because its Turkish per-tool description cost 389 bytes on ~100 tools, and its validator rejected a purpose over 140 characters, also on `talvora_apply_patch`, where a long purpose used to be shortened. Now the property is `{"type":"string","maxLength":140}` (no per-tool description), the server instructions explain `purpose` once, and no length check blocks a call (the worklog shortens it with "…"); a wrong type and unknown arguments are still rejected. Red first: the updated WL4 regression failed on the old schema, green after. Signed-in Full 72/0, `Hepsi geçti.`; Context7 noLookup (own schema text and validator).
- 2026-11-13 — WL4, executor ChatGPT: optional Turkish purpose schema for tools without readOnlyHint=true, validated string <=140 chars, other unknown arguments blocked; tool signatures unchanged. RED first, WL4_PURPOSE_METADATA_GREEN, ModernizationPolicy and ToolManifestDocumentation GREEN; signed-in Changed 28/0 and Full 72/0, Hepsi geçti. Context7 /modelcontextprotocol/csharp-sdk (unmatched arguments); Gitea #9 comment 250. Queue additions WL4–WL6 applied. Clean-HEAD installer, installed smoke and CI pending.
  Self-review — Cosmetics: existing desktop worklog uses the purpose as its 'ne' line; no new layout, empty/loading/error displays.
  Self-review — Usability: optional purpose appears only on writable tools; existing calls remain valid.
  Self-review — Turkish: description and 140-character error reviewed; no titles altered.
  Self-review — Security: SDK binding to be live-smoked; read-only and unknown arguments stay restricted, existing redaction retained.

- 2026-11-13 — D69, executor ChatGPT: normalize +src:dst and refs/heads/ destination before remote-branch diff; preserve missing-remote fallback. Two-commit RED test observed gate accepting HEAD:main despite older source change; CTX2_PUSH_EVIDENCE_GREEN for HEAD:main, +main, HEAD:refs/heads/main and empty target. Signed-in Changed 18/0 and Full 72/0, Hepsi geçti.; Context7 noLookup (string parsing only); Gitea #9 comment 249. Deployed b345f13, installed metadata and MCP smokes GREEN, CI 43496330575 GREEN.
  Self-review — Cosmetics: no visible UI change; existing error strings retained.
  Self-review — Usability: all tested push forms inspect the full outgoing range, not only the tip.
  Self-review — Turkish: no user-facing text changed.
  Self-review — Security: Context7 ledger matching and evidence format untouched; fallback preserved.

- 2026-11-13 — DEP21, executor ChatGPT: Tomlyn 2.11.1 pin, three Tomlyn-only lock file updates, Dependabot ignore for AI.Abstractions 11; Microsoft.Extensions.AI.Abstractions remains 10.11.0. RED locked-mode NU1004; both forced restores; Notices, ModernizationPolicy, PlatformLockstep GREEN; signed-in Changed 68/0 and Full 72/0, Hepsi geçti.; Context7 noLookup (Tomlyn patch API unchanged); Gitea #9 comment 247. Deployed a57ec07; installed metadata and MCP smokes GREEN; PR #21 closed; CI 43493491284 GREEN.
  Self-review — Cosmetics: no UI change; no loading, empty, or error copy altered.
  Self-review — Usability: TOML tools keep their existing commands and output.
  Self-review — Turkish: no user-facing text changed; planned PR comment uses the card's exact Turkish.
  Self-review — Security: diff is Tomlyn-only in NuGet locks; AI.Abstractions stays 10.x for MCP 2.4.0.

- 2026-11-13 — D68, executor ChatGPT: setup staging moved to Versions/.staging-GUID under Program Files; both installRoot and versionsRoot reject reparse points. RED source regression first, NativeInstaller and QualityGateChanged GREEN; signed-in Changed 16/0, Full 72/0, Hepsi geçti.; Context7 noLookup (existing Directory/File.GetAttributes API); Gitea #9 comment 246. Deployed f693395, installed metadata and full MCP smokes GREEN, CI 43488968971 GREEN.
  Self-review — Cosmetics: no visual change; only the card's Turkish error text added.
  Self-review — Usability: staging now inherits Program Files directory protections, existing setup flow retained.
  Self-review — Turkish: error text follows the card exactly; diacritics checked.
  Self-review — Security: reparse directory guards and protected staging path verified, cleanup/rollback untouched.

- 2026-11-13 — Claude, audit 53 (deep, whole project; tag `audit/2026-11-13-53`): range `audit/2026-11-13-52..579bc10` — D67 and P-56 (ChatGPT) — plus the subsystems changed since the last whole-project audit 42 (installer RL1–RL3/KUR1/D65–D67, release workflow, CTX1–CTX3 ledger, key and push gate). D67 follows its card (random MSI name, CreateNew, hash on the open FileShare.Read handle kept through msiexec, percent progress, UTF-8 output); its Context7 question is in the ledger (2026-11-13, 200). Live probes: Host header filter (foreign host 400), Origin guard (foreign and null 403), MCP with a foreign host 400; pipe ACLs (SYSTEM + session user, server identity checked); ProgramData\Talvora closed to the unelevated user; search regex timeout 2 s; archive extraction contained; key never logged; no culture-sensitive casing; every tray `async void` handler reaches the dispatcher handler. Tray log: the October tunnel/update error waves are gone; the rare 'frame payload was truncated' is a write timeout whose frame is re-sent (harmless). Findings: the elevated setup stages its payload in the user's %TEMP% → D68 (security); `push origin HEAD:main` checks only the last commit → D69; P-56 → DEP21 (Tomlyn only; Microsoft.Extensions.AI.Abstractions stays 10.x for ModelContextProtocol 2.4.0). Known limit, no card: Talvora's own SYSTEM tools could write the Context7 ledger, so audits keep comparing evidence with the ledger. Screens (Genel bakış, Çalışma günlüğü, Olaylar, Gece vardiyası): clean; texts for Claude's [C] D47 (audit 53 addition). Asked the user nothing: every finding has one correct fix. Release: none.
- 2026-11-13 — P-56, executor ChatGPT: DEP19 Apply-DependabotPr -Pr 19 FAILED, PR #19 CLOSED on 2026-11-13T18:11:06Z without merge. No package lock files changed; documentation changes reverted; awaiting Claude's PR workflow decision. Signed-in Changed 11/0 `Hepsi geçti.`, baseline windows-ci 43483191656 GREEN; Gitea #9 comment 244.

- 2026-11-13 — D67, executor ChatGPT: random MSI name and exclusive CreateNew; FileShare.Read remains open through hash and msiexec; whole-percent download progress and UTF-8 verifier. RED contract observed, NativeInstaller/COM and QualityGateChanged GREEN; signed-in Changed 26/0, Full 72/0, `Hepsi geçti.`. Context7 `/dotnet/docs`; Gitea #9 comment 243. Installed 09ba8c6; SYSTEM verifier and no MSI residue; metadata and full smoke GREEN; GitHub CI 43483191656 GREEN.
  Self-review — Cosmetics: prescribed ellipsis and percent format only; other display states unchanged.
  Self-review — Usability: 81920-byte download chunks report whole percentages; existing preflight kept.
  Self-review — Turkish: specified text and UTF-8 console output preserved.
  Self-review — Security: CreateNew/None, hash and install under read lock; unchanged pin, timeout, URL check and msiexec arguments.

- 2026-11-13 — Claude, audit 52 (tag `audit/2026-11-13-52`): range `audit/2026-11-11-51..3cd0443` — P-54 (D67) and P-55 (DEP19), ChatGPT. Both stopped correctly: Microsoft's November .NET 10 security servicing (10.0.15, 16 CVEs) had replaced SDK 10.0.403 on the workstation, and `rollForward: disable` made every restore fail. Partial work reverted, no SDK installed, proposals in the right form. Decided: option 2 for both, shipped by Claude as [C] SDK3 (9f9cb14): signed-in Full 72/0, installer built from 9f9cb14, deployed, `/healthz` = 9f9cb14, metadata and full smokes GREEN, windows-ci 43479686754 GREEN. D67 and DEP19 back to `[ ]` unchanged (DEP19's script takes only the PR's version files, so the new locks do not conflict). Dependabot opened PR #20 (.NET 11.0.100): closed; `.github/dependabot.yml` now ignores dotnet-sdk 11.x (stay on .NET 10 LTS, user decision 2026-11-08). Asked the user: the RL3/D66 uninstall test was postponed again ("Başka zaman"). Screens: no UI change in the range. Release: none (0.0.1.0 waits for D67, DEP19, RL5, RL4's dry run, D47–D49, WL3, the RL3 live check and README screenshots).
- 2026-11-13 — [C] SDK3, executor Claude (answers P-54/P-55): Microsoft's November .NET 10 security servicing had replaced SDK 10.0.403/runtime 10.0.14 on the workstation, so every restore failed. `global.json` 10.0.404 (MSBuild still 18.9.15), the 17 platform packages 10.0.15 via `Update-DotnetPlatformPackages.ps1` with all six lock files regenerated, THIRD-PARTY-NOTICES, MODERNIZATION-POLICY, ARCHITECTURE, CONTEXT7 map, dependabot comment and the Toolchain fact aligned. PLATFORM_PACKAGE_LOCKSTEP_GREEN, THIRD_PARTY_NOTICES_SOURCE_GREEN, MODERNIZATION_POLICY_GREEN; signed-in Full 72 kontrol, 0 hata, Hepsi geçti. Context7 noLookup (servicing only; Microsoft release metadata opened). Installer, installed smokes and CI follow in audit 52.
- 2026-11-13 — P-55, executor ChatGPT: DEP19 Apply-DependabotPr FAILED at restore (-2147450725): the pinned SDK 10.0.403 is missing (only 10.0.404 installed). Partial changes reverted; no package update, deployment or PR close.
  Signed-in Changed: 11 checks, 0 errors, `Hepsi geçti.`; baseline windows-ci green: 43086450913. No Context7 lookup (proposal only). Waiting for Claude's toolchain decision; Gitea #9 comment 241.
