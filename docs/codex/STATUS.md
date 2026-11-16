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
| Audit baseline | Newest Claude audit tag `audit/2026-11-16-65` = the audit 65 commit. Claude creates and moves `audit/*` tags; the agent never does. |
| Executors | Codex **or** ChatGPT with the Talvora connector — identical rules (AGENTS.md → WHO MUST FOLLOW THESE RULES). Log entries name the executor. |
| Runtime | Windows service `Talvora` (LocalSystem), `127.0.0.1:7676`, installed under `C:\Program Files\Talvora\Versions\<commit>-<stamp>`; tray `Talvora.Tray.exe` in the user session. |
| Toolchain | .NET SDK 10.0.404 exact pin (SDK3, 2026-11-13; runtime 10.0.15) with matching locked NuGet graphs (K0 done). PowerShell 7 required by the interactive runner. |
| Install path | `scripts/Deploy-Windows-Installer.ps1` (SYSTEM task; when called from inside Talvora it does not wait — poll `/healthz.sourceCommit`). Never run `Reset-And-Install.ps1` unless a card says so. |
| Secrets | `C:\ProgramData\Talvora\Secrets\*` belong to the user's other projects. Never read, move, print or delete them. Listing names/ACLs is allowed. `C:\ProgramData\Talvora\Context7\api-key` is the user's Context7 key (CTX3): `talvora_context7_docs` sends it; never read, print, copy or commit it. |
| User preferences | Personal single-user workstation: no local MCP token (Q1). Cosmetics matter a lot: every visible change needs before/after PNGs. The worklog's vertical **code terminal** is the user's favourite feature — never break or shrink it. The user follows Talvora on the **desktop worklog card** and almost never opens the Control Center (2026-11-16): the card comes first in every check and design change. |

## In progress

In progress: (none)

## Agent queue

Legend: `[ ]` todo · `[x]` done · `[~]` done but waiting for user evidence · `[?]` blocked on an open question · `[-]` won't do.

> **Only open work lives in these documents** (user decision 2026-11-07). At every audit Claude removes finished queue lines, Log entries older than the previous audit, answered findings, finished cards and decided proposals that no open card names. Git keeps all of it: `git show audit/<tag>^:<path>` is a file as it was before that audit's cleanup (first cleanup: `audit/2026-11-07-40`). The agent may tick a line `[x]` and leave it there.

- [ ] D98 — The card keeps each session's code terminal when another session's frame comes in between
- [ ] D100 — A call without a working folder joins the same agent's open card
- [ ] D99 — The card changes session only when a step starts or ends
- [ ] D101 — A step Talvora still follows never shows "Yanıt bekleniyor"
- [ ] D102 — The task note survives a session that reopens
- [ ] D103 — A long PowerShell error keeps its whole message
- [ ] PK5 — Roslyn 5.12.0 from Dependabot PR #22
- [ ] [C] D104v — Claude: each "geçmiş" row of the card shows its "ne", numbers keep their units, no "ara 0,0 sn" (audit 65; user decision "Her geçmiş satırında")

## User queue

- [ ] U16 — İsteğe bağlı: makine (sistem) PATH'inde aynı klasörü gösteren ikinci satırlar var: `C:\Python314\Scripts`, `C:\Python314`, `C:\Program Files\dotnet\`, `C:\Program Files\PowerShell\7\` ve `C:\Program Files\nodejs\`. Zararsızlar; isterseniz U14'teki gibi Sistem değişkenleri → Path → Düzenle'den bu beşini silebilirsiniz.

## Open questions

(none)

## Findings for Claude

(none)

(The agent adds one line per observation outside the current card: `file:line — what was seen`. Do not fix them.)

## Log

- 2026-11-16 — Claude, audit 65 (user: "Talvora'yı tüm kod yapısı, bug ve talvora kontrol merkezi değil masaüstü bildirim görsellerini incele.. masaüstündeki bildirimden takip ediyorum her şeyi"; tag `audit/2026-11-16-65`; range `audit/2026-11-16-64..cfe584b`: ChatGPT's D95). D95: one commit, `Card:` trailer, Turkish subject, STATUS in the commit, `noLookup` matching its `gerekmiyor`, honest Self-review, code as the card says; signed-in Full gate on cfe584b 73/0 `Hepsi geçti.`; CI 43836818430 green; `/healthz` = cfe584b. D95 live → `[x]`: Claude's failing `talvora_user_run` (14:45 local) stored "Sonuç: Denetim 65 için bilinçli hata: beklenen sonuç bu satır".
  Whole-code pass (about 104,000 lines; the desktop pipeline line by line: protocol, notifier state, follower, pipe, presenter, window, mapper, view, history store; the rest by pattern): culture-sensitive string calls, `async void`, empty catches and sync-over-async are clean or bounded. Findings → cards: D98 (the tray's code cache holds one session, so one frame of another session empties the card's code terminal; scratch test and live on cfe584b: stored entry `9639eb64` has `codeChangeId` without `codePreview`), D100 and D99 (folder-less calls split an agent's card, and the one card closes and reopens for every frame of another session; 19 of 99 stored sessions overlap; user chose "Ajanın açık kartına katılsın" and "Adım başı/sonunda geçsin"), D101 (a followed GitHub check sends no frame after its first, so the card says "Yanıt bekleniyor" after 30 s; the deploy hand-over took 34 s), D102 (a task note set after 90 s of quiet is wiped by the next step), D103 (pwsh 7.6.10 wraps a long thrown message over several `|` lines; D95 keeps the first), PK5 (Dependabot PR #22, Roslyn 5.12.0, red in locked restore).
  Screen pass on the desktop card (user granted screenshots of Talvora.Tray, no clicks; preview renders of the real XAML): number/word line breaks in titles and rows, "ara 0,0 sn" on a one-step session, history rows without their "ne" → `[C]` D104v (user chose "Her geçmiş satırında"). New user rule: the desktop card is the user's window onto Talvora and the Control Center is almost never opened → `[C]` D97v dropped; DESIGN-WORKLOG §3, §4.1, §5.2 and §7b carry the new rules. Release: 0.0.1.3 waits for these card fixes (user chose "Kart düzeltmelerinden sonra"). No proposals, findings or queue additions pending; no Talvora note in the Obsidian inbox. Removed: the D95 and D97v queue lines and cards, the audit 64 heading, the Log entries older than audit 64 (D92v, D96, D94, D92, D93, audit 63), one decided proposal no open card names, and 8 `artifacts/` entries older than seven days.
- 2026-11-16 — ChatGPT, D95: failed run_powershell and user_run evidence now removes ANSI CSI codes and selects the PowerShell thrown message after the last underline, otherwise the first error/failure line, otherwise the last nonblank line. Six measured PowerShell, unittest, compiler and fallback cases run through the notifier in the focused and full Notification suites.
  RED reproduced the colored PowerShell script location instead of its thrown message; focused D95, full Notification and ModernizationPolicy green. Signed-in Changed 29/0 and Full 73/0, both `Hepsi geçti.`; Context7 `noLookup` (own worklog text). Clean-HEAD deployment, installed smokes and CI follow this commit; Claude checks a real failing script in stored history after 90 seconds of inactivity.
  Self-review — cosmetics: no layout, live line, preview, success result or title changes; only failed command evidence selection changed.
  Self-review — usability: error evidence points to the thrown message, failing unittest or compiler error rather than colored locations or progress.
  Self-review — Turkish: current wording and visible titles stay unchanged; Turkish error terms are recognized.
  Self-review — correctness and security: existing LimitEvidenceText redaction and length bound apply; plain failure reasons and pinned N5 reference files are unchanged.
- 2026-11-16 — Claude, audit 64 (user: "denetle"; tag `audit/2026-11-16-64`; range `audit/2026-11-16-63..8790f05`: ChatGPT's D93 6394d02, D92 e169a4e, D94 5c441bf and proposal P-55 8790f05). One commit per card, `Card:` trailers, Turkish subjects, STATUS in each commit, Context7 `noLookup` matching each card's `gerekmiyor`, honest Self-review blocks; the code matches each card. One ChatGPT session (51 min, 231 calls, one Changed and one Full gate per card, the red-first runs as the only errors, no loop), but its 50 log waits used 18 s each (earlier sessions: median 50–62 s), so the AGENTS.md gate row now carries `waitForPattern` and `waitSeconds: 100` literally. CI green on all four (43773435262, 43775199418, 43776853155, 43777243633); the agent's installed smokes green for D93 and D92. Live on the installed 5c441bf: Claude's `talvora_delete` of a missing file stored "Zaten yoktu · olmayan-dosya-d93.txt" with its purpose → D93 `[x]`; every ChatGPT step after the D92 deploy kept its own purpose in the history file. P-55 decided: Microsoft Defender blocked the smoke's own encoded probe, not the session → `[C]` D96 (d15aee9, CI 43780504358 green) → D94 `[x]` (full smoke with the brief scenario inside, 03:07–03:10). Screen pass through the Control Center smoke renders (desktop access stays declined): long timeline labels were cut off at the detail card's edge and purposes were not drawn; asked, the user chose "Eskiden yeniye" for the history's order → `[C]` D92v (20141a7): Full gate 73/0 `Hepsi geçti.`, installed 00:29 UTC, `/healthz` = 20141a7, metadata and full smokes green 03:30–03:33, no Defender detection since 03:07, CI 43780543708 green. Design improvement queued: `[C]` D97v (state marks on the history's timeline). No new agent card: D95's files are unchanged since audit 63. No Talvora note in the Obsidian inbox. Release: none yet; 0.0.1.3 after D95 (D88, D90, D93, D92 with D92v, D94, D95). Removed: the finished queue lines D93, D92, D94, D96 and D92v, the D93, D94 and D92 cards and the audit 62 and 63 headings in ROADMAP, the audit 62 Log entry; no `artifacts/` entry was older than seven days.
