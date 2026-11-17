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
| Audit baseline | Newest Claude audit tag `audit/2026-11-17-67` = the last commit of audit 67. Claude creates and moves `audit/*` tags; the agent never does. |
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

- [ ] PK5 — Roslyn 5.12.0 from Dependabot PR #22 (P-56 answered by D106: run the script unchanged; Claude's dry run reached DRY RUN OK)
- [ ] D107 — .NET SDK patches go through one script (audit 66; MODERNIZATION FLOW 3)
- [ ] D108 — Dependabot's GitHub Actions PRs go through the apply script (audit 66 look-ahead; MODERNIZATION FLOW 1)
- [ ] D109 — the tray keeps the open sessions for the card's bottom bar (audit 67; data for D109v)
- [C] D109v — the card's bottom bar lists the open sessions, tmux-style (Claude ships it after D109)

## User queue

- [ ] U16 — İsteğe bağlı: makine (sistem) PATH'inde aynı klasörü gösteren ikinci satırlar var: `C:\Python314\Scripts`, `C:\Python314`, `C:\Program Files\dotnet\`, `C:\Program Files\PowerShell\7\` ve `C:\Program Files\nodejs\`. Zararsızlar; isterseniz U14'teki gibi Sistem değişkenleri → Path → Düzenle'den bu beşini silebilirsiniz.

## Open questions

(none)

## Findings for Claude

(none)

(The agent adds one line per observation outside the current card: `file:line — what was seen`. Do not fix them.)

## Log

- 2026-11-17 — Claude, cards D109 and `[C]` D109v (audit 67 follow-up; user: "Evet, kart yaz" for the vault idea "Kart alt çubuğunda süren öteki oturumlar", now `durum: karta döndü`): the card's bottom bar will list every open session the way tmux lists windows (`DESIGN-WORKLOG.md` §7b "Window list": `*` this card, `#` running, `~` silent, `!` failed; entries leave 3/8/5 min after success/failure/cancel, a silent one 8 min after its newest frame). D109 (agent) builds `WorklogSessionTabs` in the tray and feeds it from `PublishCore`; `[C]` D109v (Claude) draws it in the eight checksum-pinned worklog files. Clicking an entry switches the card to that session (user chose "Kart o oturuma geçsin"; D109 `ShowSession`, D109v the click); afterwards D99 decides again. prova·sonnet: two rounds, six stops fixed, then a third round after the click decision.
- 2026-11-17 — Claude, audit 67 (user: "Talvora'yı denetle"; tag `audit/2026-11-17-67`; range `audit/2026-11-17-66..fadc8d8`: no agent commit and no worklog session since audit 66): health check 13 OK, signed-in Full gate 74/0 on fadc8d8, CI 43965322623 green, installed dda859f (= v0.0.1.3; fadc8d8 is docs-only). No release: nothing user-visible since v0.0.1.3.
  AGENTS.md brought to the current project template (user decision 2026-11-17, "Sıradaki denetimlerde"): an open `[C]` line stops the agent (`Sıra Claude'da: <item>`), REPORTING TO CLAUDE, ownership rules 12–14, Sync/GitHub/Revert/Infrastructure/tool-refused rows, commit and push mechanics, `B<n>` baseline items, non-fast-forward push, "next open item" instead of "next `[ ]`"; CODEX-PROMPT line 13 and the open-question prompt follow. Kept on purpose: delegation only with the user's explicit permission, a queued `[C]` card locks no files, the ChatGPT custom-instructions block (AGENTS.md wins where they differ), no numbered sections 1–9. A plain `pull --ff-only` needs no `explicitAdmin` (W8). Brief sections 41,249 chars (check WARN above 40,000; `talvora_agent_brief` cuts at 60,000).
  Cards rehearsed in two rounds: PK5 fine (stale `artifacts/dependabot/pr-22.result` of 2026-11-16 deleted; Context7 = the two Roslyn queries); D107 test copies the three scripts with lock-file fixtures, `-Version` from `global.json` after `git apply`; D108 rewritten: `-PatchFile`/`-SelectOnly` seam in a fresh repository per case, pin rule `# vX.Y.Z` as `ModernizationPolicyRegression.ps1` requires, a major check, and `tests/ReleaseWorkflowRegression.ps1` (it pinned both action SHAs literally, so the first Actions PR would have failed) now checks equal full-SHA pins in both workflows.
  Screen pass: worklog preview renders and the live card of a real call (window-guard capture) are correct, Turkish and measured; no visual defect. Vault idea "Kart alt çubuğunda süren öteki oturumlar" is still `durum: ham`. Cleanup: 12 Log entries older than audit 66 and one decided proposal removed (STATUS 28,341 → 9,231 chars). Gates: hakem·sonnet on the AGENTS.md alignment (gap list, then 13 findings, all fixed or kept with a reason); prova·sonnet on PK5/D107/D108 (5 stops fixed, then FINE).
- 2026-11-17 — Claude, release 0.0.1.3 (tag `v0.0.1.3` on dda859f): signed-in Full gate 74/0 on dda859f; its own installer (SourceCommit dda859f, SHA-256 1ec351f1…) installed 21:48 UTC and `/healthz` served dda859f after ~55 s; metadata and full installed smokes GREEN; CI 43962049828; release run 43964793077 green; the page is Latest with `Talvora-Kurulum-0.0.1.3.exe` (SHA-256 e1f99f4b…, built by the workflow with the release version) and `SHA256SUMS.txt`. The installed runtime is now dda859f.
- 2026-11-17 — Claude, release 0.0.1.3 prepared (user decision audit 65: cut at the audit after the card fixes): `docs/releases/0.0.1.3.md` (fix release: D88, D90, D91, D92, D92v, D93, D95, D98–D103, D104v, D105v), README version and a new `docs/media/kod-karti.png` taken from the live card (each history row with its "ne"); the Control Center history image predates D92v and stays until a Control Center pass. Verification follows in the next entry.
- 2026-11-17 — Claude, audit 66 (user: "talvora modernizasyonunun önünü kesen durumlara izin verme. talvora yı denetle"; tag `audit/2026-11-17-66`; range `audit/2026-11-16-65..44b4e66`): D98, D100, D99, D101, D102, D103 and P-56 checked. Each commit stayed inside its Files, `noLookup` matches each card's "Context7: gerekmiyor", self-reviews present; no defect found (note for later cards: D100's folder-key list repeats `ResolveProject`'s, keep the two equal). CI green for every commit. Live on the installed 8389b20, card captured with window-guard (PrintWindow, nothing minimised): D102 the task note showed with the first step of a fresh session; D100 a folder-less command joined the open card (same session, 3 steps); D98 the code terminal came back after another project's step; D103 all three wrapped error lines in "sonuç"; D99 a step start or end took the card, and the followed CI's progress frames left another project's finished card alone for 85 s; D101 the followed CI read "GitHub denetimi sürüyor · 1 dk 47 sn", a new fact every ~34 s, never "Yanıt bekleniyor". All six `[x]`.
  P-56 answered by Claude's `[C]` D106 (de7d6b0, CI 43959569931); PK5 runs unchanged. User decisions (AGENTS.md → MODERNIZATION FLOW, CONTINUE PROTOCOL 4, 5 and stop conditions): the agent fixes defects of Talvora's own tooling inside a modernization item, applies open Dependabot PRs itself when the queue is empty (`DEP<n>`), and moves the SDK pin when Microsoft's servicing replaces the installed patch (`SDK<version>`; new card D107 makes that one script). Claude also corrected THIRD-PARTY-NOTICES' runtime packs (10.0.12 → 10.0.15, stale since SDK3), the CONTEXT7 map's Sqlite row (10.0.15) and added the NuGet row. Cleanup: compact_docs removed 9 finished cards and queue lines, Log entries before audit 65 removed; Obsidian inbox empty. Design ideas (not queued): the followed CI's "durum" repeats its title; while another session still runs, the card's tmux footer could list it as a second window.
- 2026-11-17 — Claude, `[C]` D106 (audit 66; P-56; user: "talvora modernizasyonunun önünü kesen durumlara izin verme"): the notice step of `Apply-DependabotPr.ps1` moved to `scripts/Update-ThirdPartyNotices.ps1`. It updates list, table and heading entries (the `### DiffPlex 1.9.0` heading had stopped every Dependabot update since N3, 2026-10-22), adds a new package to the Microsoft list only when it is Microsoft's own MIT package (reserved prefix, nuspec author, license expression) and otherwise stops naming every package that needs a license review. RED: the new `tests/ThirdPartyNoticesUpdateRegression.ps1` failed on the old logic with the repository's own lock files ("no line for DiffPlex"); GREEN 5/5. Dry run of PR #22 in a scratch clone: `DRY RUN OK`, five Roslyn lines to 5.12.0. Context7 `/nuget/docs.microsoft.com-nuget` (license expression, folder layout, prefix reservation; new map row). `docs/MODERNIZATION-POLICY.md` describes the new rule. Executor: Claude.
  Self-review — cosmetics: nothing visible changed; the card and the Control Center are untouched.
  Self-review — usability: a Dependabot update now runs through to READY unless a genuinely new third-party package needs a license review, and then one stop names all of them.
  Self-review — Turkish: no user-facing text changed.
  Self-review — correctness and security: the inventory guard is as strict as before for unknown packages and stricter for Microsoft-prefixed ones (author and license are checked); the notice is written only when every package is accounted for.