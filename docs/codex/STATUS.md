# Talvora — Codex status

> Single live status for agent work (Codex or ChatGPT via Talvora). English. Newest Log entry on top.
> Cards: every open card is in `docs/codex/ROADMAP.md`. Design specs: `docs/codex/DESIGN-SANTRAL.md` (Control Center, current), `docs/codex/DESIGN-WORKLOG.md` (desktop worklog), `docs/codex/DESIGN-CONTROL-CENTER.md` (Gözlemevi: its information architecture, copy deck §9 and accessibility §10 stay valid).
> Protocol: AGENTS.md → **CONTINUE PROTOCOL**. Shortcut for the user: "Talvora'ya devam et".

## Fixed facts

| | |
|---|---|
| Local folder | `C:\Users\tayla\Talvora-MCP` |
| Remotes | `origin` = local Gitea `ssh://git@127.0.0.1:2222/taylan/Talvora-MCP.git` (primary tracker) · `github` = `https://github.com/bingoweb/Talvora-MCP.git` (**PUBLIC** — never commit secrets, machine-specific credentials or personal data) |
| Branch | `main` only. Do not create branches. Dependabot PRs: apply with scripts/Apply-DependabotPr.ps1 (DEP1), never merge on GitHub; when the queue is empty the agent takes them itself (AGENTS.md → MODERNIZATION FLOW). |
| Commit identity | `Taylan Soylu <6751394+bingoweb@users.noreply.github.com>` (do not change) |
| Push | **Authorized** (Q3, 2026-10-18): after each card `git push origin main` and `git push github main`; never force. |
| Gitea tracker | Mirror each card's Log entry as **one text comment** on Gitea issue **#9** (`taylan/Talvora-MCP`). No attachments. If the comment cannot be posted (connector approval, Gitea down), write `Gitea mirror skipped: <reason>` in the Log entry and continue — never stop or ask the user for it. |
| Audit baseline | Newest Claude audit tag `audit/2026-11-17-69` = the review commit of audit 69 (Claude's D112v follows it). Claude creates and moves `audit/*` tags; the agent never does. |
| Executors | Codex **or** ChatGPT with the Talvora connector — identical rules (AGENTS.md → WHO MUST FOLLOW THESE RULES). Log entries name the executor. |
| Runtime | Windows service `Talvora` (LocalSystem), `127.0.0.1:7676`, installed under `C:\Program Files\Talvora\Versions\<commit>-<stamp>`; tray `Talvora.Tray.exe` in the user session. |
| Toolchain | .NET SDK 10.0.404 exact pin (SDK3, 2026-11-13; runtime 10.0.15) with matching locked NuGet graphs (K0 done). PowerShell 7 required by the interactive runner. |
| Install path | `scripts/Deploy-Windows-Installer.ps1` (SYSTEM task; when called from inside Talvora it does not wait — poll `/healthz.sourceCommit`). Never run `Reset-And-Install.ps1` unless a card says so. |
| Secrets | `C:\ProgramData\Talvora\Secrets\*` belong to the user's other projects. Never read, move, print or delete them. Listing names/ACLs is allowed. `C:\ProgramData\Talvora\Context7\api-key` is the user's Context7 key (CTX3): `talvora_context7_docs` sends it; never read, print, copy or commit it. |
| User preferences | Personal single-user workstation: no local MCP token (Q1). Cosmetics matter a lot: every visible change needs before/after PNGs. The worklog's vertical **code terminal** is the user's favourite feature — never break or shrink it. The user follows Talvora on the **desktop worklog card** and almost never opens the Control Center (2026-11-16): the card comes first in every check and design change. |

## In progress

In progress: (none)

## Agent queue

Legend: `[ ]` todo · `[x]` done · `[~]` done but waiting for user evidence · `[?]` blocked on an open question · `[-]` won't do · `[C]` Claude's item: the agent stops there and reports `Sıra Claude'da: <item>`.

> **Only open work lives in these documents** (user decision 2026-11-07). At every audit Claude removes finished queue lines, Log entries older than the previous audit, answered findings, finished cards and decided proposals that no open card names. Git keeps all of it: `git show audit/<tag>^:<path>` is a file as it was before that audit's cleanup (first cleanup: `audit/2026-11-07-40`). The agent may tick a line `[x]` and leave it there.

- [ ] D111 — a failed Dependabot apply never leaves a half-applied tree or a RUNNING result (audit 69)

## User queue

- [ ] U16 — İsteğe bağlı: makine (sistem) PATH'inde aynı klasörü gösteren ikinci satırlar var: `C:\Python314\Scripts`, `C:\Python314`, `C:\Program Files\dotnet\`, `C:\Program Files\PowerShell\7\` ve `C:\Program Files\nodejs\`. Zararsızlar; isterseniz U14'teki gibi Sistem değişkenleri → Path → Düzenle'den bu beşini silebilirsiniz.

## Open questions

(none)

## Findings for Claude

(none)

(The agent adds one line per observation outside the current card: `file:line — what was seen`. Do not fix them.)

## Log

- 2026-11-17 — Claude, audit 69 (user: "Talvora'yı denetle"; tag `audit/2026-11-17-69`; range `audit/2026-11-17-68..6ed0a3d`: D110 by ChatGPT, Claude's D109v with its CI follow-up d5da542, the audit 68 close). D110 stays inside its Files, `Card:` last, Turkish human commit, `noLookup` matches the card's `Context7: gerekmiyor`; CI 44151762906 green → `[x]`. hakem·opus (11 mutations in a clone): accepted with findings — a conflicting `git apply --3way` leaves `UU global.json` unrestored, a failing restore leaves the result at `RUNNING`, and D110's `-DotnetRoot` forwarding and `-FromVersion` downgrade comparison are untested; both failures reproduced in a scratch repository (git 2.56.0.windows.4) → agent card D111 (prova: FINE after one wording fix; the card applied in a copy gives `DEPENDABOT_APPLY_GREEN cases=15`). OLASI, not carded: the restore also drops tracked edits someone else makes during the run, like `-DryRun`. d5da542 weakens no assertion (the motion controls run only where client-area animation plays).
  Checks: signed-in Full gate on 6ed0a3d `76 kontrol, 0 hata` / `Hepsi geçti.`; check_project 12 OK, 1 WARN (AGENTS sections 41,188 chars); installed 6376264 (D110 changes scripts only); no open proposal, queue addition, Dependabot PR or ready Obsidian idea; SDK 10.0.404 installed = pin.
  Screens: a live one-step card through the Talvora Chat bridge (639 DIP) and the 12 installed previews read correctly; the window list shows `0:Talvora-MCP·chatgpt 1:Talvora-MCP·claude*` as §7b says. Measured over 72 h of `worklog-sessions.jsonl`: 75 sessions, 617 steps, 40 one-step sessions whose card printed the step's name twice and its duration three times → the user chose "İkinci adımla gelsin" and "Bu oturumda": Claude's `[C]` D112v follows this audit. `Komut tamamlandı` (33 steps) comes almost only from Claude's own inline test commands and from ChatGPT calls without `purpose` before 2026-11-15: no card.
  Look-ahead: AGENTS.md MODERNIZATION FLOW 3 now says a Dependabot SDK PR whose SDK `dotnet --list-sdks` does not show yet is no item (no queue line, no STATUS change), so December's servicing cannot loop the agent; the obsolete pre-D107 hand steps were dropped.
  Release: none — 0.0.1.3 was published today (one release a day). The next audit cuts 0.0.2.0 (D109/D109v are a new capability) with fresh README screenshots of the card.
  Cleanup: the finished D109v and D110 lines and cards, the Log entries older than audit 68 (10); no artifacts older than 7 days.
- 2026-11-17 — ChatGPT, D110: SDK patch updater now checks major/feature-band and downgrade against the pre-PR -FromVersion (default current pin); an already-aligned normal run still invokes platform refresh while -SkipRestore returns early. Dependabot apply stores the old pin before git apply, forwards optional -DotnetRoot for fixtures, and rolls back only after a successful apply when later steps fail; pre-existing tracked edits remain untouched.
  Tests: RED downgrade attempted and missing -DotnetRoot; GREEN DotnetSdkPatchRegression 9 cases, DependabotApplyRegression 12 cases (including rejected SDK PR clean-tree and user-edit preservation), ThirdPartyNoticesUpdateRegression 5, ModernizationPolicyRegression, real 10.0.404 -SkipRestore no tracked version changes; signed-in Changed 15/0, Full 76/0 (Hepsi geçti.).
  Context7: noLookup (Talvora-owned scripts and tests, the version rule was documented and verified for D107). No SDK installed; no deploy (scripts/tests only); Windows CI pending after push.
  Self-review — cosmetics: no visual changes.
  Self-review — usability: rejected Dependabot SDK proposals no longer block the next work item with a dirty tree.
  Self-review — Turkish: no new user-facing UI text; scripts keep their documented failure receipts.
  Self-review — correctness and security: all refusal checks precede writes, pre-apply errors never trigger restore, and fixture tests verify all bytes remain intact after a refused SDK update.
- 2026-11-17 — Claude, `[C]` D109v: the status bar lists the open sessions tmux-style (new `WorklogWindowListPanel` fits the shown entry plus up to three others by priority and draws `+k`; flags `* # ~ !`; names cut at 16 text elements; spoken as `Açık oturumlar: …`); a click on another entry calls `ShowSession`, which now passes the time the tray received the frame (a silent session shows `Yanıt bekleniyor` at once, a terminal one keeps only the rest of its lifetime, no motion); PublishCore forgets left tabs before it observes; the window's lifetimes read `WorklogSessionTabs`. Also covers D109's review gaps (manual-dismissal lift, expired-tab refusal). Previews 11 and 12 added; eight reference copies and SHA256SUMS refreshed; ModernizationPolicyRegression pins the lifetimes in `WorklogSessionTabs.cs` and the received-time rule in its new form.
  Tests: red CS0246 (`IWorklogSessionSource` missing), then a failing assertion for each review fix; green `--d109v-only`, `--d109v-wpf-only`, D109, D99, full notification regression `DESKTOP_PROGRESS_BEHAVIOR_GREEN`; signed-in Changed 27/0 and Full 76/0 `Hepsi geçti.`
  Context7: `/dotnet/docs-desktop` (custom Panel measure/arrange; automation peer with Name and HelpText); FormattedText `pixelsPerDip` from the vendor page, Context7 had no answer.
  hakem·opus: fix first (a switch typed the title in; a freed number was not reused; two guards untested; clock-back clamp; cut by text elements) → fixed; re-review ship, its two untested motion paths now tested. Built by usta·sonnet to Claude's spec.
  Self-review — cosmetics: previews show `[talvora] 0:Talvora-MCP* 1:fatura-hesabi# +2` at 460 DIP; shown entry semibold, others at 70 %, high contrast in the system text colour; nothing moves.
  Self-review — usability: a click switches the same card at once; no entry click reaches the card's drag or Çalışma günlüğü path; hand cursor only on switchable entries.
  Self-review — Turkish: `Açık oturumlar`, `bu kart`, `sürüyor`, `sessiz`, `tamamlandı`, `hata verdi`, `iptal edildi`, `Kartı bu oturuma geçirir`, as DESIGN-WORKLOG §7b says.
  Self-review — correctness and security: no new IPC, setting or permission; the click path logs and never throws; the registry stays bounded by the lifetimes.
  Live (installed 6376264, clean-HEAD installer): `TALVORA MCP METADATA POLICY LIVE GREEN`, `TALVORA MCP SMOKE GREEN`. Two real Claude sessions (a running job in Talvora-MCP, a command in Kalem) showed `[talvora] 0:Talvora-MCP# 1:Kalem*` on the 639 DIP card, spoken `Açık oturumlar: Talvora-MCP, sürüyor; Kalem, bu kart` with the help text; invoking `Talvora-MCP, sürüyor` through UI Automation switched the same card at once (`0:Talvora-MCP* 1:Kalem`). The worklog has no light theme; high contrast checked on preview 12. CI 44120372335 red: the runner has no client-area animation, so the motion controls failed → follow-up d5da542 runs them only where motion can play (hakem·sonnet: ship), CI 44121840623 green.
- 2026-11-17 — Claude, audit 68 (range `audit/2026-11-17-67..4c9deb4`: PK5, D107, D108, D109 by ChatGPT). Every commit inside its card's Files with one `Card:` trailer and green CI; Context7 evidence matches Talvora's ledger (PK5 two `/dotnet/roslyn`, D107 two `/dotnet/docs` queries; D108 and D109 `noLookup` as their cards say); PR #22 closed with `Yerelde uygulandı: 34530f4`. Signed-in Full gate on 4c9deb4: 76/0 `Hepsi geçti.` D109 closed: clean-HEAD installer of 4c9deb4, both installed smokes GREEN (`artifacts/D109-meta.log`, `D109-smoke.log`), CI 44076076237.
  Reviews (hakem·opus ×2): PK5, D108 and D109 ship. D107: Apply checks the SDK band after applying the PR (a band or major PR passes), the updater can move the pin back without `-Version`, a refused SDK PR leaves `global.json` dirty, and the no-change return skips the lock refresh → agent card D110. D109: its WPF test stayed green with the manual-dismissal lift and the expired-tab refusal removed, and switching to a silent session showed it live for one stale budget → both covered by `[C]` D109v.
  OLASI: D107's `vendorSources` lists two learn.microsoft.com pages, but Talvora's call trace shows no page fetch in that window and Context7 answered both queries (CONTEXT7 PROTOCOL rule 6: none when Context7 answered).
  Release: none (0.0.1.3 was cut today, at most one a day); D109v is a new behaviour, so 0.0.2.0 is due at the next audit.
  Cleanup: finished queue lines and the cards PK5, D107, D108, D109 removed (`git show 4c9deb4:<path>`); no artifact older than 7 days. Vault: no ready idea, empty inbox. Skill scripts now read `[~] live pending: <id>` lines.
