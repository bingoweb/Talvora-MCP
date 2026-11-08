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
| Audit baseline | Newest Claude audit tag `audit/2026-11-08-49` = the audit 49 commit. Claude creates and moves `audit/*` tags; the agent never does. |
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
- [~] RL1 — b83b2f1 deployed with healthz and both MCP smokes green; CI 42517091038 successful; visual check pending (Claude)
- [~] RL2 — PowerShell 7 preflight and MSI integrity gate implemented; live pending: verified MSI download, deployed HEAD, smokes, CI
- [ ] [C] KUR1 — The setup window speaks Santral and carries the developer's maker plate (Claude; user request 2026-11-08; before RL3)
- [?] RL3 — Talvora is in Apps & features and can be uninstalled (user request 2026-11-08; waits for Claude's [C] KUR1, which rebuilds `InstallerForm.cs`; Claude reopens this line and updates the card)
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

- 2026-11-08 — RL2, executor ChatGPT: installer preflights existing PowerShell 7 before Talvora mutations, otherwise downloads pinned 7.6.9 x64 MSI and verifies SHA-256 50d33c7494aaf82b987cfd4de3f232d04f91fdd4c0deeca8a669cced3e7cf8f9 (official hashes.sha256); silent msiexec, safe failures and hidden --verify-powershell-download added.
  RED missing pin/preflight, then native installer C# fixture GREEN (injected availability avoids download; temporary wrong-hash MSI rejected). Pinned source/rollback/CI suites passed; signed-in Changed 26/0 and Full 71/0, Hepsi geçti. Context7: /dotnet/docs (stream and SHA256.HashData); vendor PowerShell release and MS install guide.
  Self-review — exact Turkish progress/errors and no screen layout changes; MSI is checked before execution, failures precede Talvora mutation, and existing PowerShell is not changed. Live download/deploy/smokes and CI pending committed HEAD. Gitea #9 comment 234.

- 2026-11-08 — RL1, executor ChatGPT: versions now derive from TalvoraReleaseVersion; development builds advertise 0.0.0.0-dev+commit, while About and Overview show "geliştirme sürümü". Installer accepts only 0.0.x.x, names release EXEs accordingly and records the version in its manifest; normal setup name unchanged.
  RED initial Version test (3.0.0-dev), GREEN RELEASE_VERSION_GREEN and UI7; four pinned installer/modernization regressions green. Signed-in Changed 67/0 and Full 71/0, Hepsi geçti. Release EXE and manifest had FileVersion/version 0.0.0.1 and the test EXE was deleted. Context7: /dotnet/docs (two queries); official MSBuild property sources checked.
  Installed service, overview/About live state and CI await this card's committed HEAD; no unrelated files changed. Gitea #9 comment 233.

- 2026-11-08 — Claude, audit 49 (update check; tag `audit/2026-11-08-49`): range `audit/2026-11-08-48..154b616` (the P-55 stop by ChatGPT) and the user's request to re-check updates and modernization. P-55 decided: Claude shipped [C] PK1 (853d78a: MCP 2.4.0, Tomlyn 2.11.0, lock files, notices), [C] PK2 (7b20fd8: Microsoft.Build.Framework 18.9.6 plus a compile-only Microsoft.NET.StringTools after MSBL001), [C] PK3 (3df3070: setup-dotnet 6.1.0) and [C] PK4 (Dependabot ignored every Microsoft.Build.Framework 18.x as "incompatible until .NET 11" since 2026-10-07; the cause was MSBL001, so it now ignores only >=18.10.0 for both MSBuild packages, pinned by `ModernizationPolicyRegression.ps1`). RL4 now names the pinned action commits. Checks: every NuGet pin against nuget.org (the rest are the newest stable), `dotnet list package --vulnerable --include-transitive` and `--deprecated` (none), .NET release index (10.0.403/10.0.14 newest LTS; .NET 11 RC3), CI actions, PowerShell 7.6.9, Git 2.56.0, uv 0.13.7 (all current; map row Git 2.56). The installer project is outside Talvora.slnx and uses none of the moved packages (its locked restore passes). Signed-in Full gate in a clean clone 70/0 `Hepsi geçti.`; installer built and deployed at 3df3070; installed smokes green (metadata policy, semantic source edit, full). User decision: Talvora stays on .NET 10 LTS when .NET 11 (STS) comes out. Noted: a scratch clone lost its pack indexes during the work (cause not found); the main repository checked clean with `git fsck` and the three commits were moved over intact. Removed: finished queue lines and cards (PK1, PK2, PK3, PK4), the audit 48 section, Log entries before audit 48.

- 2026-11-08 — [C] PK4, executor Claude: `.github/dependabot.yml` ignored every Microsoft.Build.Framework 18.x as "not compatible with net10.0-windows until .NET 11" (9713527, 2026-10-07). PK2 showed the cause was MSBL001 and fixed it with a compile-only Microsoft.NET.StringTools; the installed semantic source edit smoke is green on 18.9.6. The guard now ignores only >=18.10.0 (newer than the SDK's MSBuild 18.9.15) for both packages, and `ModernizationPolicyRegression.ps1` pins that. Context7: noLookup.

- 2026-11-08 — [C] PK3, executor Claude: `actions/setup-dotnet` v6.0.0 → v6.1.0 (pinned to its release commit 0f4a354d; new optional inputs and security fixes only); `actions/checkout` is already the newest v7.0.1. Context7: noLookup.

- 2026-11-08 — [C] PK2, executor Claude (P-55): `dotnet msbuild -version` 18.9.15, so `Microsoft.Build.Framework` 17.14.28 → 18.9.6. It brings `Microsoft.NET.StringTools` 18.9.6, and the build stopped with MSBL001 (Microsoft.Build.Locator: a MSBuild package without ExcludeAssets="runtime"); StringTools is now pinned 18.9.6 and referenced compile-only next to Microsoft.Build.Framework in src/Talvora and both regression projects. One `--force-evaluate` restore, three lock files moved (only these two packages); the service output still holds only Microsoft.Build.Locator.dll. Notices name both (packages=46). Context7 `/microsoft/msbuildlocator`.

- 2026-11-08 — [C] PK1, executor Claude (P-55): `ModelContextProtocol` and `.AspNetCore` 2.3.1 → 2.4.0, `Tomlyn` 2.10.1 → 2.11.0 (the newest stable on 2026-11-08; 2.11.0 appeared after the card was written). One `dotnet restore Talvora.slnx --force-evaluate`: five lock files moved, only the MCP packages, Tomlyn and the project nodes that reference them; `--locked-mode` passes. THIRD-PARTY-NOTICES.md names the new versions (THIRD_PARTY_NOTICES_SOURCE_GREEN packages=45). Changed 66/0 `Hepsi geçti.` Context7 (asked directly): `/modelcontextprotocol/csharp-sdk/v2.3.0` UseStructuredContent/OutputSchemaType, `/xoofx/tomlyn/2.10.1` TomlSerializer; map rows show the index gaps. Full gate, deploy and installed smokes run with PK2 (one install).

- 2026-11-08 — PK1, executor ChatGPT: NuGet confirmed SDK 2.4.0 and Tomlyn 2.10.2; Context7 verified SDK tools/filters/errors and Tomlyn serializer, release notes reviewed. Signed-in `--force-evaluate` once and `--locked-mode` restore passed. Changed gate 62 checks / 1 error: `PS:ThirdPartyNoticesSourceRegression` misses four new versions in `THIRD-PARTY-NOTICES.md` (`tests/ThirdPartyNoticesSourceRegression.ps1:61`); lock graph also changes `talvora` and `talvora.tray` project-reference nodes. Submitted P-55; all source, map, evidence and generated lock changes reverted. Baseline Changed 11/0 `Hepsi geçti.`; baseline CI run 42512176939 succeeded. No Full, deploy or new CI for PK1.
  Self-review — no UI changes; original packages and locks preserved; required license-attribution safety check was not bypassed. Gitea #9 comment 232.

- 2026-11-08 — Claude, audit 48 (deep; tag `audit/2026-11-08-48`): range `audit/2026-11-08-47..2f86766` (D64 and the P-54 stop by ChatGPT). D64 follows its card line by line: the gate skips ledger records of Talvora's own clients, checks every `(query: …)` of an entry, `talvora_context7_docs` is open-world (84), an empty detached subject becomes program plus first argument. P-54 decided (option 1, Claude's miss): PK1 names the seven lock files and one `--force-evaluate` restore; Claude checked MCP 2.4.0's nuspec dependencies on nuget.org (inside the current pins) and its release notes (no breaking change; #1934 and #1928 are checked by the installed smokes). Look-ahead: PK2 gets the same lock step and an MSBuildLocator check, because `Microsoft.Build.Framework` 18.9.6 brings `Microsoft.NET.StringTools` 18.9.6 and MSBuildLocator's documentation (Context7 `/microsoft/msbuildlocator`) asks for `ExcludeAssets="runtime"` on every MSBuild package; the map's MSBuild row names `/microsoft/msbuildlocator`. RL1–RL5 read clean. CTX2's live proof still waits for a push with real questions (D64 used `noLookup`). Signed-in Full gate at 2f86766 in a clean clone 70/0 `Hepsi geçti.`; CI success on every commit. No screen changed. Removed: finished queue lines and cards (D64), the audit 47 section, Log entries before audit 47.
