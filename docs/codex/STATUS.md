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
| Audit baseline | Newest Claude audit tag `audit/2026-11-16-63` = the audit 63 commit. Claude creates and moves `audit/*` tags; the agent never does. |
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

- [~] D93 — Deleting a missing path really reads "Zaten yoktu" (live missing-path deletion check pending (Claude) at the next audit)
- [x] D92 — The history keeps every step's purpose (user decision, audit 61; narrowed by P-54: no N5 file)
- [ ] D94 — The brief's live smoke runs on its own fixture inside the full installed smoke; dead folders leave the recent list (audit 62)
- [ ] D95 — A failed command's result shows its real error line, without escape codes (audit 63)
- [ ] [C] D92v — Claude draws each step's purpose in the history view, `WorklogHistoryView.xaml` (after D92)

## User queue

- [ ] U16 — İsteğe bağlı: makine (sistem) PATH'inde aynı klasörü gösteren ikinci satırlar var: `C:\Python314\Scripts`, `C:\Python314`, `C:\Program Files\dotnet\`, `C:\Program Files\PowerShell\7\` ve `C:\Program Files\nodejs\`. Zararsızlar; isterseniz U14'teki gibi Sistem değişkenleri → Path → Düzenle'den bu beşini silebilirsiniz.

## Open questions

(none)

## Findings for Claude

(none)

(The agent adds one line per observation outside the current card: `file:line — what was seen`. Do not fix them.)

## Log

- 2026-11-16 — ChatGPT, D92: every completed step's normalized purpose is stored on its own timeline entry, persisted with redaction in JSONL and exposed on `TimelineRows`; legacy entries read with null. Added three-step notifier/history test, `--d92-only` and full registration.
  RED reproduced; focused D92, full Notification, ModernizationPolicy and ControlCenter smoke green. Signed-in Changed 29/0 and Full 73/0, both `Hepsi geçti.`; Context7 `noLookup` (own records). Clean-HEAD deployment, both installed smokes and CI follow this commit.
  Self-review — cosmetics: no visual files, layouts, motion or existing titles changed; D92v stays with Claude.
  Self-review — usability: separate purposes survive multiple steps, a saved session and `TimelineRows`; old records remain readable.
  Self-review — Turkish: existing copy is unchanged; the sample purposes use correct Turkish characters.
  Self-review — correctness and security: missing purposes remain null, serialized purposes are redacted and existing history retention is unchanged.
- 2026-11-16 — ChatGPT, D93: `ResultDeletedIsFalse` reads `deleted=false` from the first JSON-object text block when structured content is absent; other result paths remain. RED reproduced; D91 focused and full Notification regression green.
  Signed-in Changed 17/0 and Full 73/0, both `Hepsi geçti.`; Context7 `noLookup` (own worklog title logic). Clean-HEAD deploy, installed smokes and GitHub CI follow this single commit; Claude checks a real missing-path deletion at the next audit.
  Self-review — cosmetics: only the approved success title is selected; no layout or styling changes.
  Self-review — usability: missing-path results say `Zaten yoktu`; true deletions and unreadable text retain `Silindi`.
  Self-review — Turkish: prescribed titles and Turkish diacritics are unchanged.
  Self-review — correctness and security: only JSON boolean false changes the title; malformed text is ignored, and structured and ordinary objects remain supported.
- 2026-11-16 — Claude, audit 63 (user: "Talvora'yı denetle"; tag `audit/2026-11-16-63`; range `audit/2026-11-15-62..7d3a5c6` is empty). No agent work since audit 62: the call trace has no agent call after the audit 62 commit (12:18 UTC), no Codex session, stash or extra worktree, a clean tree, both remotes at 7d3a5c6. Signed-in Full gate on 7d3a5c6: 73/0 `Hepsi geçti.` (5 min); CI 43713155590 green; `/healthz` = 8ff0b07 (the two later commits change docs only); .NET SDK 10.0.404 and pwsh 7.6.10 present; no open PRs. Look-ahead: D93, D92 and D94 still match the code (every named file and symbol exists; D94's server root is the full run's endpoint without `/mcp`, as `--agent-brief-live` takes it); none needs a browser, a device or a wait. New finding, measured end to end on the installed 8ff0b07: Claude's `talvora_user_run` of a test script that throws stored "Sonuç: ESC[31;1mException: ESC[0m<path>\tests\Throw-Test.ps1:3ESC[0m" (the first stderr line: ANSI codes and the script location, never the thrown message), and the stored record of 2026-11-14 13:55 shows a failed Python test script with "Sonuç: test_adet_eklenir (…) ... ok" → D95 (Claude checked its line order on the measured samples and on the existing one-line test samples). Checked and closed: D91's root cause reaches no other title or failure path (only ten tools lack `UseStructuredContent`, and only `talvora_delete` has a result-dependent title: D93). Screen pass: desktop access was declined again, so the stored history was read (92 sessions): 6 of the 7 failed sessions show only the §5.3 fallback "Bu adım tamamlanamadı." (as specified; after D95 the real message stands under it in "Sonuç"), and "Komut tamamlandı" is the second most frequent label (37; D92 and D92v add each step's purpose), so no new look card. No proposals, findings or pending queue additions; no Talvora note in the Obsidian inbox. Release: none; since 0.0.1.2 only D88 and D90 are user-visible (D91 changes nothing in a real call), so 0.0.1.3 waits for D93, D92 with D92v, D94 and D95. Removed: the eight Log entries older than audit 62 (P-54, D91, D90, audit 61, D88, release 0.0.1.2, D89 and its installed check), the applied history in QUEUE-ADDITIONS.md and 284 `artifacts/` entries older than seven days (12.8 MB).
- 2026-11-15 — Claude, audit 62 (user: "denetle"; tag `audit/2026-11-15-62`; range `audit/2026-11-15-61..9c3537e`: ChatGPT's D90 9818014, D91 8ff0b07 and proposal P-54 9c3537e). One commit per card, `Card:` trailers, Turkish subjects, STATUS in each commit, Context7 `noLookup` matching both cards, Self-review blocks present; one ChatGPT session of 59 minutes (220 calls, one Changed and one Full gate per card, no loop). Signed-in Full gate on 9c3537e: 73/0 `Hepsi geçti.`; CI 43701702076, 43702870315 and 43705705838 green; `/healthz` = 8ff0b07 (9c3537e changes docs only); the agent's installed smokes for both cards green (`artifacts/d90-smoke.log`, `artifacts/d91-smoke.log`). D90 live on the installed 8ff0b07: a folder without AGENTS.md answers "No AGENTS.md in <folder>. workspaceRoot must be the repository folder that holds AGENTS.md. Workspaces briefed recently: C:\Users\tayla\Talvora-MCP." and the worklog reads "Proje talimatları alınamadı"; an AGENTS.md with only "## Setup" and "## Code style" comes back whole; `recent-workspaces.json` puts the newest root first → D90 `[x]`. Findings: (1) D91 does not work in a real call: `talvora_delete` of a missing path returned `deleted: false` and the stored worklog still read "Silindi · olmayan-dosya.txt"; the real `CallToolResult` carries the JSON in a `TextContentBlock` without `StructuredContent`, a shape D91's test never built → D91 `[-]`, replaced by D93; (2) P-54 is right and the conflict was Claude's card: D92 listed two of the eight N5 sources pinned by `docs/codex/reference/worklog-phosphor/SHA256SUMS.txt` → decided: D92 keeps to the protocol, the worklog state and the history store, the live card stays as it is, and Claude's `[C] D92v` draws the purpose in `WorklogHistoryView.xaml` (not pinned) → D92 `[ ]`; (3) `Talvora.Smoke --agent-brief-live` is in no procedure, so D90's change to it never ran; Claude ran it on 8ff0b07: red, "agent_brief context lost the character 'ç'.", because the scenario reads the live repository and an empty agent queue gives a 570-character context without 'ç' or '§', and with D93 open it went red at its explicit-card step, which asks for card `A2` (gone from ROADMAP since the cleanup of 2026-11-07); the recent-workspace list also never drops a folder whose AGENTS.md is gone → D94. Claude's card lessons went into the workflow skill: checksum-pinned files are Claude's; a test that reads a tool result uses the shape the server really builds; a card that changes a smoke names the run that executes it. No user decision was needed: P-54's options differ in who does the work, not in what the user gets. No open findings; no Talvora note in the Obsidian inbox. Release: none today (0.0.1.2 went out at 00:43 today, at most one a day); 0.0.1.3 with D88, D90, D93, D92 with D92v and D94 at an audit on 2026-11-16 or later. Look-ahead: D93, D92 and D94 name their files, the tests that pin them and a red test; none needs a browser, a device or a wait; D93's live check is Claude's at the next audit, because the agent's own session reaches the worklog file only after 90 s without calls. Screen pass: the stored worklog records (screen access was declined in audit 59). Design improvements: inline `pwsh -Command` steps still read "Komut tamamlandı" (4 today) and `git show <rev>:<path>` reads "Depo işlemi tamamlandı"; once D92 and D92v are in, the history shows each step's purpose under such titles, so no title card now. Removed: the finished queue lines D90 and D91, the D90 and D91 cards in ROADMAP, the three Log entries from before audit 61, and 5 `artifacts/` entries older than seven days (under 0.1 MB).
