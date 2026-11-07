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
| Audit baseline | Newest Claude audit tag `audit/2026-11-07-41` = the audit 41 commit. Claude creates and moves `audit/*` tags; the agent never does. |
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

- [~] D34 — Tunnel health without launching tunnel-client every round (audit 40; RED missing methods then D34_TUNNEL_HEALTH_FILE_GREEN; Changed 31/0, signed-in Full 69/0 Hepsi geçti.; before 92 status launches/hour; GitHub CI 42371088200 success; Gitea #9 comment 204; live pending (Claude): one-hour count since 1302b75 15:09)
- [ ] D40 — Tests never write into the user's Talvora files (audit 41)
- [ ] D41 — One caller name: Talvora's own calls leave no trace, Claude is named (audit 41, user decisions)
- [ ] D42 — "Zaten hazır" is not an automatic repair (audit 41)
- [ ] D35 — The GitHub release lookup waits after a failure (audit 40)
- [ ] D36 — Atomic publication also retries a busy destination (audit 40)
- [ ] D37 — Event slips in plain Turkish (audit 40)
- [ ] D38 — "Yalnızca hatalar ve ajan işleri" counts ChatGPT as an agent (audit 40, user decision)
- [ ] D39 — Penpot error text uses the shared secret masking (audit 40)
- [ ] OB1 — talvora_note_write: write Obsidian vault notes, refuse secrets and the linked repo docs (user request 2026-11-01)

## User queue

(none)

## Open questions

(none)

## Findings for Claude

(The agent adds one line per observation outside the current card: `file:line — what was seen`. Do not fix them.)

## Log

- 2026-11-07 — Claude, audit 41 (full check; tag `audit/2026-11-07-41`): range `audit/2026-11-07-40..1302b75` (D30, D31, D29, D32, D33, D34) matches its cards; Claude's signed-in Full 69/0 `Hepsi geçti.`; CI success on every commit; installed 1302b75. Why ChatGPT stalled: D34's "after one hour of normal running" step plus CONTINUE PROTOCOL step 8 (live-pending checks first) made every "devam et" in that hour re-check an unfinished hour; AGENTS.md steps 5 and 8 now give waiting checks to Claude (`[~] live pending (Claude)`, skipped by the agent). D34 stays `[~] live pending (Claude)`: its hour runs to 16:09:50 (so far 17 `tunnel-client runtimes status` lines in 39 minutes, about half of them from smoke runs; limit 30, before 92); Claude closes it from tray.log.
  New cards, first in the queue: D40 (tests write into the user's tray.log and worklog history), D41 (Talvora's own calls leave no worklog session or desktop card; the bridge's `X-Talvora-Client` header names Claude; user decisions), D42 ("zaten hazır" counted as an automatic repair). Their literal blocks were verified in a scratch clone (Release build 0 warnings, Notification regression GREEN). Card texts fixed: OB1 Step 8 (a four-line statement) and its live-check date, D38's non-agent actor "Diğer". Claude added `--header X-Talvora-Client:Claude` to the Claude Desktop "Talvora Chat" bridge (installed and source copies; backups in `%LOCALAPPDATA%\Talvora\ClaudeDesktopChatExtension\backups`).
  Removed: finished queue lines and cards (D29–D33, SN9, WL2), Log entries older than audit 40, the regression-kept K1, N1–N8/N3b and UI1–UI8 sections (D30 removed their last test dependency). No artifacts older than 7 days.

- 2026-11-07 — D34, executor ChatGPT via Talvora: added published loopback health URL file validation, 15 s non-ready/10 min ready runtime cache, 20 s status timeout and invalidation on non-live/non-ready/degraded or failed health HTTP response. RED missing members, then D34_TUNNEL_HEALTH_FILE_GREEN; Notification GREEN, Changed 31/0, signed-in Full 69/0 Hepsi geçti. Context7 /dotnet/docs and Microsoft Learn confirmed Uri.IsDefaultPort/GetLeftPart. Pre-deploy 60 min: 92 tunnel-client runtimes status lines; post-install 60 min count/ready state, exact-commit install and CI pending. Gitea #9 comment 204.
  Self-review — cosmetics: no appearance or copy changes.
  Self-review — usability: published health URL avoids repeated process launches when ready.
  Self-review — Turkish: existing UI wording untouched.
  Self-review — correctness/security: RED/GREEN, Notification and Full 69/0; failure invalidates ready cache; maintenance, repair, RV1 log and WaitForReady unchanged.
- 2026-11-07 — D33, executor ChatGPT via Talvora: added literal delivery-budget and PID/start-time cache regression; initial RED CS0103 (cache not implemented), then D33_DELIVERY_BUDGETS_GREEN and Notification GREEN. Set read/write budgets 10/8 s, kept connect 2 s and three retries, cached trusted tray by PID+StartTime, logged dropped frames at Warning at most once per 10 min. Changed gate 28/0 and signed-in Full 69/0, Hepsi geçti. Context7 /dotnet/docs and Microsoft Learn Process.StartTime and ILogger.Log overloads verified; exact-commit install, desktop-card check, GitHub CI pending. Gitea issue #9 comment 203.
  Self-review — cosmetics: no layout, colour or motion change.
  Self-review — usability: busy-machine frames gain headroom without weakening tray-path verification.
  Self-review — Turkish: no user-facing text changed.
  Self-review — correctness/security: RED/GREEN, Notification and Full gate; process-session check, protocol, frame limit, connect deadline and retries unchanged.
- 2026-11-07 — D32, executor ChatGPT via Talvora: SN1 test first RED "D32: Talvora's own clients must never be counted", then SN1_LINE_TRAFFIC_GREEN after the exact prefix guard in Record. Notification/Changed gate 17 checks, 0 errors; signed-in Full gate 69 checks, 0 errors, Hepsi geçti. The line, JSON and ring remain unchanged. Context7 /dotnet/docs and Microsoft Learn confirmed String.StartsWith(value, StringComparison.OrdinalIgnoreCase). Exact-commit installation and installed smoke follow the commit. Gitea issue #9 comment 202.
  Self-review — cosmetics: no new layout or visual decoration; only measured traffic data changes.
  Self-review — usability: own-client smoke and tray activity is excluded; external calls remain counted.
  Self-review — Turkish: no user-facing copy changed.
  Self-review — correctness/security: RED/GREEN plus Notification, Changed and Full gates; endpoint and ResolveClient unchanged.
- 2026-11-07 — D29, executor ChatGPT via Talvora: added the exact SN3 fixture cleanup before final Settle; source assertion RED (missing cleanup), then D29_GREEN; Changed gate 16 checks/0 failures and signed-in Full 69 checks/0 failures/Hepsi geçti. ControlCenter smoke PASS; artifacts/d29/control-center-loaded.png visually verified with 'veri yok' for Dev/Admin/core, never fixture 13/1/2. Context7 /dotnet/docs plus Microsoft Learn verified a null argument to TrafficFeed?. Exact-commit installer, installed smoke and GitHub CI follow the commit. Gitea issue #9 comment 201.
  Self-review — cosmetics: no production layout, palette or motion changed; loaded overview preview verified.
  Self-review — usability: the live-data capture no longer retains fixture counters.
  Self-review — Turkish: no product wording changed; existing 'veri yok' visible.
  Self-review — correctness/security: RED/GREEN, source syntax and JSON validation, Changed 16/0 and Full 69/0; cleanup touches only the smoke fixture.
- 2026-11-07 — D31, executor ChatGPT via Talvora: moved exactly the existing QualityGateParallelRegression.ps1 to the final position in $serialRegressions, preserving the preceding three entries and 7-second regression threshold. Source assertion RED (missing fourth serial entry), then D31_SERIAL_GREEN; standalone QUALITY_GATE_PARALLEL_GREEN. Two consecutive signed-in Full gates: first 69 checks/0 failures/Hepsi geçti., second 69 checks/0 failures/Hepsi geçti. No timing guard, parallel phase, or test was changed. Context7 /microsoftdocs/powershell-docs and Microsoft Learn verified PowerShell array literals. Exact-commit installer, installed MCP smoke and GitHub CI follow the commit. Gitea issue #9 mirror skipped: no authenticated comment tool exposed through Talvora Dev.
  Self-review — cosmetics: no UI, colour, animation or layout change.
  Self-review — usability: timing-sensitive regression no longer competes with the parallel gate phase.
  Self-review — Turkish: no product strings changed; technical test records remain in English.
  Self-review — correctness/security: RED/GREEN, focused regression and two consecutive Full 69/0; original 7 s threshold and every unrelated gate step unchanged.
- 2026-11-07 — D30, executor ChatGPT via Talvora: added exactly the prescribed AgentContextOrderRegression fixture (RED "the card must come first, got: === STATUS...", then AGENT_CONTEXT_ORDER_GREEN); reordered the helper with ROADMAP fallback for UI/N, trimmed finished queue lines at the end; the 60,000-character brief keeps the card and marks trimmed STATUS. QualityGateRegression GREEN; C# build 0 warnings/errors; signed-in Full gate 69 checks, 0 errors, Hepsi geçti. Context7 /dotnet/docs and Microsoft Learn verified string[..^n] (C# from-end slicing). Exact-commit install, live agent-brief and GitHub CI follow commit. Gitea issue #9 mirror skipped: no authenticated comment tool via Talvora Dev.
  Self-review — cosmetics: no UI elements, layouts or style values changed.
  Self-review — usability: current card is shown first; finished queue items are omitted; UI/N cards can come from ROADMAP.
  Self-review — Turkish: no user-facing copy changed; technical marker matches the card.
  Self-review — correctness/security: RED then GREEN, target regression plus Full 69/0; summary size limit unchanged and no secrets emitted.
- 2026-11-07 — Claude: [C] SN9 and [C] WL2 installed 8222d4f (`/healthz` sourceCommit after ~30 s; the tray restarted from the new version folder); the installed tray's Control Center smoke is GREEN (hotspots and keyboard focus kept across five polls; 39 captures checked in both themes: reconnect timer clear of the neighbour's text, "Proje belirsiz" and the empty-state hint in place); GitHub CI 42359284287 success. Both closed.

- 2026-11-07 — [C] WL2, executor Claude: smoke first (`AssertWorklogHistorySelection` gains a session without a project) RED "WL2: the detail column has no empty-state hint", then GREEN. `WorklogHistoryView`: a session without a project reads "Proje belirsiz" in `Tv.TextFaint` (list row and detail title); until a session is chosen the detail column shows "Soldan bir oturum seç; adımları, kanıtları ve sonucu burada görürsün." centred in `Tv.TextMuted` (hidden while the list is empty). XAML style triggers only; the project filter already skips empty names.
  Design gaps filled: the card named `Sn.InkFaint`/`Sn.InkMuted`; this page uses the Gözlemevi palette, so its `Tv.TextFaint`/`Tv.TextMuted` were used. Context7 `/dotnet/docs-desktop` (`{x:Null}`; MultiDataTrigger gap → vendor page). Signed-in Full 68/0 `Hepsi geçti.` (ControlCenter smoke included; one gate run covered SN9 and WL2 together).
  Self-review — cosmetics: wl2-empty and worklog-session captures in both themes; usability: the empty column now says what to do; Turkish: two new strings reread; correctness: no view-model change.

- 2026-11-07 — [C] SN9, executor Claude: smoke first (`ControlCenterSmoke.SN9.cs`: the hotspots must survive five traffic polls) RED "SN9: a traffic poll must update the socket hotspots in place, not rebuild them", then GREEN (keyboard focus granted and kept). `Switchboard` updates its hotspots in place and rebuilds them only when the socket ids change; the reconnect timer moved to `outside.Y + 70/+88`, clear of the neighbour's "son …" line; brushes, pens, text layouts and one static layer (grain, divider, labels, jacks) are cached per theme and DPI.
  Measured in the smoke fixture, 300 `OnRender` frames: 3.94–4.70 ms → 0.34–0.48 ms per frame. The window's idle CPU sample did not move (6.00 % → 6.71 %, single 3 s samples): at idle the board draws nothing, that number comes from the window's polls. Context7 `/dotnet/docs-desktop` (Freezable; DrawingGroup gap → vendor pages). Signed-in Full 68/0 `Hepsi geçti.` (ControlCenter smoke included; one gate run covered SN9 and WL2 together).
  Self-review — cosmetics: same look in both themes (sn4/sn8 captures), timer no longer overlaps; usability: focus and hover survive the 1 s poll; Turkish: no new strings; correctness: shared theme brushes are cloned before Freeze, every cache resets on a theme or DPI change.

- 2026-11-07 — Claude, audit 40 (deep, whole project; tag `audit/2026-11-07-40`): range `audit/2026-11-07-39..47b43a8` matches its cards; D26, D27, D28 and LB1 passed the visual check in both themes and SN7 is done (idle CPU goes to SN9): closed. User decisions: `talvora_delete` stays unguarded; ChatGPT counts as an agent (D38); Talvora's own test calls are never counted (D32); E1, E2, S5, TI1, UI2 and the user lines U2–U11 closed (the user reports problems himself); local `artifacts/` older than 7 days pruned at every audit; the 2.5 GB `C:\ProgramData\Talvora\Diagnostics` leftovers deleted.
  Token economy: finished work removed from the agent documents (STATUS 203 k → 18.5 k characters, ROADMAP 445 k → 64 k, PROPOSALS 105 k → 5.6 k, DESIGN-WORKLOG §1/§9, DESIGN-CONTROL-CENTER §1/§7/§11, `docs/archive/`, `APPLICATION-DEVELOPMENT-ROADMAP.md`, finished cards' reference folders, the stale HANDOFF text); AGENTS.md: rule 8 names DESIGN-SANTRAL.md, CONTINUE PROTOCOL step 3 reads only the proposals a card names, conditions for finished cards (A1, A2, A3, T3, N10, R3) removed; then AGENTS.md compacted without any rule change (file 38.4 k → 35.2 k characters, its brief part 30.6 k → 28.1 k). The brief is 43.6 k characters with the card first (before: cut at 60 k without the card). K1, UI1–UI8 and N1–N8 stay under "Kept for a regression test" until D30 moves `tests/QualityGateRegression.ps1` off finished cards. P-49 decided (option 2 → D31). New cards: D30–D39, [C] SN9, [C] WL2.
