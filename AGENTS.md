# Talvora engineering rules

## PERMANENT USER-APPROVED AGENT PROMPT (2026-10-18)

**Mandatory for every Talvora development session, not just the chat in which it was supplied:** the full user-approved prompt is stored verbatim in docs/codex/CODEX-PROMPT.md. Every implementing agent, including ChatGPT using the Talvora Dev/Admin connector and Codex when the user authorizes it, must load and follow that file **together with this AGENTS.md** before making a repository change. The user does not need to paste the prompt again in subsequent sessions.

- AGENTS.md remains the authoritative implementation and safety contract; the stored prompt supplies the required ChatGPT bootstrap, daily shortcut and special-request workflows. A specific-card, review, status, question-answer, audit or deployment template applies only when the user invokes that workflow.
- For a new ChatGPT + Talvora chat, first use talvora_agent_brief if available; otherwise read AGENTS.md lines 1-140 and run scripts/Get-CodexContext.ps1 via Talvora, then load this permanent prompt. Use Talvora tools directly; never delegate to Codex or Claude without the user's explicit permission in that chat.
- The ChatGPT Project custom-instructions snippet in the stored prompt is a bootstrap option, **not** evidence that project-level settings have been changed. If the agent cannot access these repository instructions, it must not claim to have followed them.

## WHO MUST FOLLOW THESE RULES

These rules bind **every implementing agent** that changes this repository, identically:

- **Codex** (CLI/desktop), and
- **ChatGPT** in a chat that uses the **Talvora** connector (Dev or Admin surface) — the user often works this way.

"The agent" below means whichever of them is working. Nothing in a chat, a prompt, a memory or a previous session relaxes these rules; only Claude's cards/decisions in `docs/codex/*` and the user's explicit answers recorded in `docs/codex/STATUS.md` can change them. The commit trailer is `Card: <ID>` (older commits use `Codex-Card:`).

**ChatGPT via Talvora — mandatory mechanics** (Codex uses its own shell for the same steps):

| Protocol need | ChatGPT must use |
|---|---|
| Start of every session | `talvora_agent_brief {workspaceRoot: "C:\\Users\\tayla\\Talvora-MCP"}` once A2 exists; until then `talvora_read_text_range` on `AGENTS.md` lines 1–140 and `talvora_run_powershell` → `pwsh -NoProfile -File scripts/Get-CodexContext.ps1` (workingDirectory = repo root). |
| Reading code | `talvora_search_text` (narrow `root`, `maxMatches` ≤ 50) then `talvora_read_source` with a line window (≤ 120 lines). Never read whole large files. |
| Editing source/docs | `talvora_apply_patch` only (read revisions first with `talvora_read_source`). Never `talvora_write_text`, `talvora_replace_text`, `talvora_append_text`, `talvora_write_bytes` or PowerShell `Set-Content` for repository files. Once N10 is installed, every call passes `purpose`: one plain Turkish line saying what the change does and why (it is shown to the user as "ne"). |
| Desktop task line | Once N10 is installed: right after writing "In progress" (step 5), `talvora_worklog_note {workspaceRoot, text: "<card id> — <the card's goal in plain Turkish>"}`. |
| Build / tests / gate | Until A1 exists: `talvora_run_powershell` with `pwsh -NoProfile -File scripts/Invoke-QualityGate.ps1 -Scope Changed` (`-Scope Fast` until T3 is in `main`; or the card's targeted command). After A1: `talvora_user_run` (runs as the signed-in user, so no SYSTEM-owned `obj/`, `bin/` or `.git/index`) for anything that finishes within ~2 minutes (`-Scope Changed`, one suite, `Get-CiStatus.ps1`). **Anything longer (Full gate, installer build, smoke runs) runs detached** so the connector call cannot time out: `talvora_user_process_start {executable: "C:\Program Files\PowerShell\7\pwsh.exe", arguments: ["-NoProfile","-Command","& ./scripts/Invoke-QualityGate.ps1 -Scope Full *> artifacts/<card>-gate.log; exit $LASTEXITCODE"], workingDirectory: "C:\Users\tayla\Talvora-MCP", visible: false}`, then `talvora_tail_text {path: "C:\Users\tayla\Talvora-MCP\artifacts\<card>-gate.log", lineCount: 20}` every 60 s until the last line is `Hepsi geçti.` or starts with `Kontrol başarısız`. |
| Git status/diff/log | `talvora_git_status`, `talvora_git_diff`, `talvora_git_log` with small limits. |
| Commit / push | `talvora_git_run` only (it enforces the delivery gate and, after A3, the work contract). Never commit or push through `talvora_run_powershell`/`talvora_run_process`. |
| Deploy | First build the installer for the current HEAD as the signed-in user, detached (same pattern as the Full gate above, command `& ./scripts/Build-Windows-Installer.ps1 *> artifacts/<card>-build.log; exit $LASTEXITCODE`; the log's `SourceCommit` must equal HEAD, without `-dirty-`). Then `talvora_run_powershell` → `scripts/Deploy-Windows-Installer.ps1` (it installs the last **built** artifact; its JSON `sourceCommit` must equal HEAD), then poll `/healthz` with `talvora_http_request` until `sourceCommit` = HEAD. |
| Report to the user | Turkish, short (protocol step 11). |

ChatGPT must not use Codex/Claude delegation tools (`talvora_codex_*`, `talvora_claude_*`) to do card work unless the user explicitly asks in that chat.

## Language policy

- Agent-facing documents (`docs/codex/*`, this file, code comments, commit messages, tool descriptions) are **English**.
- Everything Talvora shows to people — Control Center, tray, desktop worklog, user-facing error messages, MCP tool **titles** — is **ordinary Turkish** with correct diacritics.
- The agent always reports to the user in **Turkish**, with correct diacritics (also in STATUS gate lines such as `Hepsi geçti.`).

## EXECUTION MANDATE — the agent implements, Claude decides (binding, highest priority)

The user decided (2026-10-18): the agent does **only** what Claude's cards say, **exactly** the way they say it. Claude owns every product, architecture and especially every **visual/cosmetic** decision. This section overrides any other wording in this file or in `docs/codex/*` that seems to leave room for the agent's own judgement.

**Scope**
1. Work only on the current queue card. No "while I'm here" fixes, refactors, renames, reformatting, file moves, comment rewrites, dependency bumps or test rewrites outside the card's **Files** list. If another file must change for the card to compile or pass, change the minimum and list it in the Log with the reason.
   **Supporting changes are NOT proposal material** (clarified 2026-10-20 after 14 proposals were raised for them): plumbing a value the card asks for to where the result is built, linking a new source file into a test project, registering a new test/scenario in a runner list, manifest/surface-policy entries for a tool the card adds, `.context7/verification.json`, README tool lists. Do them minimally and list them in the Log. A proposal is needed only when the card leaves a **behaviour, design, safety, ordering or scope** decision open, or when its Acceptance cannot be met.
   **Ordering is not your problem to solve with a proposal either:** if a card depends on a later card, mark it `[?] waiting for <card>` in STATUS (no proposal) and take the next `[ ]`.
2. Do not start work that is not a card. Things you notice go to STATUS → "Findings for Claude" (one line each: file:line + what you saw). Do not fix them.
3. Do not add, remove or upgrade packages, tools, services, endpoints, settings or MCP tools unless the card names them.
4. **One card = one commit.** A follow-up fix to a card that is already committed is **not** a second commit with the same card id: write it into "Findings for Claude"; Claude turns it into a D-card. (Exception: a card's commit that fails CI may be fixed by one commit with trailer `Card: <ID>` and body line `CI follow-up: <run id>`.)

**Decisions**
5. Where a card prescribes values, names, code shape, library, algorithm, order or copy, use exactly that. Do not "improve" it.
   **"#### Exact implementation" blocks are literal.** When a card contains such a block, apply its steps in order and copy its code and strings character for character (only whitespace/indentation may adapt to the file). Do not rename identifiers, reorder logic, add extra checks, merge steps, "simplify" or "modernise" it. The only permitted deviations: (a) a compile error caused by an API detail (exact type/namespace/member name) — fix the minimum and quote the compiler message in the Log; (b) the code does not match the file because the file changed since the card was written — stop with a proposal that quotes both the card snippet and the current lines. Anything else → proposal.
   **Reference copies.** A step that says "copy `docs/codex/reference/**/<file>` → `<target>`" is done with `talvora_apply_patch` Add File (ChatGPT) or `Copy-Item` (Codex). Verify it by the hash of the **normalised content** (CRLF→LF, UTF-8 BOM removed) against that folder's `SHA256SUMS.txt`: `$t=[IO.File]::ReadAllText('<target>').Replace("`r`n","`n"); [BitConverter]::ToString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($t))).Replace('-','').ToLower()`. A raw-byte difference caused only by newlines is not a deviation.
6. If a card is ambiguous, contradicts the code, contradicts current upstream docs (Context7), or cannot meet its Acceptance as written: **do not choose yourself**. Write the question with evidence and 1–3 options into `docs/codex/PROPOSALS.md` (template there), mark the card `[?]` in STATUS with "waiting for Claude: P-<n>", revert any partial work of that card, and continue with the next `[ ]` card. A proposal-only commit uses the trailer `Card: P-<n>` (never the blocked card's id, which the WorkContract would then refuse for the real card commit). A safety mechanism (guard, check, lock, validation) may never be weakened, bypassed or given a fallback without a proposal.
7. Context7/vendor docs are used to **verify** the prescribed approach and fill exact API names/versions — never to replace the prescribed approach with a different one.

**Design and cosmetics (zero autonomy)**
8. Everything visible — layout, colours, fonts, sizes, spacing, radii, shadows, icons, glyphs, motion, timing, wording/Turkish copy, tooltips, empty/loading/error states, tool titles — comes **only** from `docs/codex/DESIGN-CONTROL-CENTER.md`, `docs/codex/DESIGN-WORKLOG.md` or an approved proposal. Never invent a colour, icon, animation, string, visual element or behaviour.
9. Where the spec is silent about a **minor layout detail** (an alignment or a gap inside an element the spec defines), use only values from the spec's token scales (`Tv.Space.*`, `Tv.Radius.*`, `Tv.Type.*`, colour tokens) and list each such choice in the Log under "Design gaps filled" so Claude can audit it. Anything else that is visible and not specified (a new element, text, icon, colour, state, animation) → proposal (rule 6), never a guess.
10. Awwwards research is **evidence only**. It must not change the spec. In `.talvora/awwwards-verification.json`, `designDecisions` must quote the spec sections being implemented (e.g. "DESIGN-WORKLOG §3.1 code terminal"), not new ideas.
11. Every UI/worklog card ends with the before/after PNGs required by its Acceptance; Claude approves the look in the next audit.

Violations found in Claude's audits become D-cards that revert the unrequested change.

## TOKEN ECONOMY (work cheaply, never at the cost of correctness)

Every file read, command output, MCP tool schema and screenshot costs tokens. These rules never override a card's Acceptance: if a rule would skip required evidence, the card wins. ChatGPT applies them through the Talvora tools listed in "WHO MUST FOLLOW THESE RULES".

**A. Read only what the card needs**
1. **Context helper first.** Run `pwsh scripts/Get-CodexContext.ps1` (or `-Card <ID>`) — ChatGPT: `talvora_agent_brief` (after A2) — and work from its output. Never open a whole `docs/codex/*.md` file.
2. **STATUS without history:** read `docs/codex/STATUS.md` up to `## Log` plus the newest Log entry only.
3. **AGENTS.md once per session.** Do not re-read it between steps of the same card.
4. **Locate, then read a window.** `git grep -n "<symbol>"` / `rg -n` (ChatGPT: `talvora_search_text`), then read ±40 lines. Read a whole file only if it is < 250 lines or the card rewrites most of it.
5. **Never open in full:** `packages.lock.json`, `BUG-AUDIT.md`, `HANDOFF.md`, `MCP-CONTROL-CENTER-TODO.md`, `docs/SOURCE-EDIT-ENGINE-ARCHITECTURE.md`, `docs/archive/**`, `THIRD-PARTY-NOTICES.md`, `tests/Talvora.SourceEdit.Regression/SourceEditRegressionRunner.Misc.cs` (2 500+ lines), `src/Talvora/SourceEditing/RoslynSemanticEditEngine.cs` (2 200+ lines), `src/Talvora.Tray/ManagedMcpTunnelProvisioningService.cs` (1 800+ lines), images, `bin/`, `obj/`, `artifacts/`. Grep them.
6. **Do not re-read a file you just edited** to check it; re-read only the hunk if a build/test fails there.
7. **Talvora MCP calls:** always pass small limits (`maxMatches`, `maxResults`, `lineCount`, `maxCommits`) and the narrowest `root`; never list/search the whole repo or a drive root.

**B. Build and test cheaply**
1. **Inner loop = one project / one suite.** Build only the touched project: `dotnet build src/<Project>/<Project>.csproj -c Release --no-restore -v q -nologo -clp:ErrorsOnly`. Run only the affected suite/scenario. Once T3 is in `main`, `pwsh scripts/Invoke-QualityGate.ps1 -Scope Changed` is the inner loop: it builds and runs only the suites and regressions that the uncommitted change can affect (it prints what it selected).
2. **Restore once per card** (`--locked-mode`), not before every build.
3. **Full gate once per card**, at the end: `pwsh scripts/Invoke-QualityGate.ps1`. Never twice in a row without a code change in between.
4. **Tails only:** `... 2>&1 | Select -Last 40` (ChatGPT: put this inside the PowerShell script and set `maxCapturedCharactersPerStream` ≤ 8000).
5. **Installer/deploy once per card**, only for cards whose Acceptance requires the installed runtime; read only the last 20 lines of its output unless it failed; poll `/healthz` (small JSON) instead of reading logs.
6. **Screenshots:** only the PNGs the card requires, produced by the preview/smoke harness, cropped to the element.

**C. Write cheaply**
1. **STATUS Log entry ≤ 8 lines:** what changed, gate last line + counts, measured numbers, installed commit, anything open. No pasted code or command output.
2. **Commit message:** subject + ≤ 6 body lines + `Card: <ID>` trailer.
3. **Report to the user (Turkish):** the protocol step 11 items in short sentences; `git diff --stat`, never the full diff.
4. **Do not echo** large files, JSON or logs into chat, STATUS or Gitea; summarise in one line with the key numbers.

**D. Avoid wasted work**
1. One card per message; do not look ahead into later cards.
2. If the same approach fails a third time, stop and write a proposal (EXECUTION MANDATE rule 6) instead of burning tokens.
3. Do not regenerate lock files, generated assets or notices unless the card says so.

## QUEUE ADDITIONS (how Claude adds cards while the agent is working)

Before selecting an item (CONTINUE PROTOCOL step 5), if `docs/codex/QUEUE-ADDITIONS.md` contains entries under "## Pending", insert each listed queue line into `docs/codex/STATUS.md` → "Agent queue" exactly at the position it names, move the entry to "## Applied" with the date, and include both files in the next card's commit. Do not change the wording of the queue lines.

## CONTINUE PROTOCOL ("Talvora'ya devam et")

When the user says "Talvora'ya devam et", "Talvora devam", "sıradaki adım" or similar — in Codex **or** in a ChatGPT chat with the Talvora connector — execute this sequence without asking questions. **One queue item per message**, then stop and report.

1. **Location:** work in `C:\Users\tayla\Talvora-MCP`. If git reports "dubious ownership", prefix git commands with `-c safe.directory=C:/Users/tayla/Talvora-MCP`; do not change global git config.
2. **Sync:** `git fetch origin`. Dirty tree: continue it only if it belongs to STATUS "In progress"; otherwise stop and ask. `git pull --ff-only origin main`; never force, reset --hard, rebase or rewrite history.
3. **Context:** follow the **TOKEN ECONOMY** section of this file. Apply pending **QUEUE ADDITIONS** before step 5. Read "## Decided" in `docs/codex/PROPOSALS.md` for decisions that affect the selected card.
4. **Baseline:** run the quality gate (`pwsh scripts/Invoke-QualityGate.ps1 -Scope Changed`; `-Scope Fast` until T3 is in `main`) **and** check the GitHub `windows-ci` result of `origin/main` (step 10b method). If either is red at the start, this message's task is to make it green.
5. **Select:** the first `[ ]` item in `docs/codex/STATUS.md` → "Agent queue" (skip `[?]`), or the "In progress" item. Write "In progress: <item>" into STATUS first.
6. **Implement:** follow the card's **Why / Files / Do / Don't / Acceptance** exactly (`docs/codex/ROADMAP.md`; N cards in `docs/codex/DESIGN-WORKLOG.md`; UI cards in `docs/codex/DESIGN-CONTROL-CENTER.md`; decisions in `docs/codex/PROPOSALS.md`). Test first (red), then code (green). If it cannot work as written, write a proposal (EXECUTION MANDATE rule 6) — do not improvise. Context7 before relying on any framework/SDK/CLI behaviour.
7. **Verify:** quality gate → last line **`Hepsi geçti.`**, run **as the signed-in user** (Codex: its own shell; ChatGPT: `talvora_user_run` once A1 exists — until then run it through `talvora_run_powershell` and write "gate run as LocalSystem" in the Log, because LocalSystem hides user-context failures); runtime-affecting cards also deploy with `scripts/Deploy-Windows-Installer.ps1` and verify `/healthz.sourceCommit` + installed smoke; UI cards attach both-theme PNGs.
8. **Record:** tick the item (`[x]` only when **every** Acceptance step — including deploy and the live checks on the installed runtime — is done in this message; otherwise `[~] live pending: <what>` and do those checks first in the next message before any other card; `[~]` + a "User queue" line when user evidence is needed), add a Log entry (English, newest on top: what changed, tests, Context7 IDs, installed commit, executor = Codex or ChatGPT), reset "In progress" to `(none)`; mirror the evidence to the Gitea issue named in STATUS → Fixed facts → "Gitea tracker" (best effort: never stop or ask because of it).
9. **Commit:** exactly one commit per item: `<type>(<area>): <summary>` + body + trailer `Card: <ID>`.
10. **Push:** only while STATUS "Fixed facts" says `Push: Authorized`; then `git push origin main` and `git push github main`, never force. On failure retry once, then report.
10b. **CI is part of done (root-cause rule, 2026-10-20).** After the GitHub push, check the `windows-ci` run of that exact commit **once, without waiting**: Codex `pwsh -NoProfile -File scripts/Get-CiStatus.ps1 -TimeoutMinutes 0`; ChatGPT `talvora_user_run {executable: "C:\Program Files\PowerShell\7\pwsh.exe", arguments: ["-NoProfile","-File","scripts/Get-CiStatus.ps1","-TimeoutMinutes","0"], workingDirectory: "C:\Users\tayla\Talvora-MCP"}` (signed-in user, where `gh` is logged in; this replaces the older 60-second `talvora_gh_run` loop in ROADMAP → CI1). Exit 0 = green, 1 = red (it prints the failing lines), 2 = no run yet or still running. Green → the card may be `[x]`. Red → do **not** start another card; the next task is the CI fix, committed once with trailer `Card: <same ID>` and body line `CI follow-up: <run id>`. Not finished yet (exit 2) → mark `[~] CI pending`, report and stop; never wait or poll for it inside the message; check it first thing in the next message and turn the line into `[x]` (with the run id) inside that message's card commit — or, when the queue has no further card, leave the `[~]` line for Claude instead of making a STATUS-only commit. A local gate run as LocalSystem never replaces this check: identity-dependent code passes there and fails on GitHub.
11. **Report to the user in Turkish, short:** item done · changed files · gate last line · deploy/installed commit · push status · next item · what the user must do (if anything).

**Stop conditions** (report in Turkish and wait): agent queue empty (list the user queue); acceptance impossible without breaking a rule (write a proposal); a `[?]` dependency is required; the work needs history rewrite, repo visibility/settings changes, reading/moving anything in `C:\ProgramData\Talvora\Secrets`, or running `scripts/Reset-And-Install.ps1` before R3.

## Modern-by-default policy

- Keep Talvora on supported, current stable toolchains and libraries. Prefer the newest supported stable release; use an LTS line when it is the correct machine/runtime baseline.
- Do not introduce wildcard package versions, deprecated APIs, obsolete CLI paths, legacy compatibility aliases, or stale SDK fallbacks when a supported modern replacement exists.
- Dependency versions are centrally managed in `Directory.Packages.props`; .NET restore is lock-file based and CI uses locked mode.
- A dependency/toolchain update is incomplete until targeted tests pass and the affected installed-runtime contract is verified where applicable.

## Context7 quality gate

- Before implementing or changing behavior that depends on a third-party framework, SDK, library, protocol SDK, CLI, or package manager, query Context7 for the current official/upstream documentation.
- Context7 is mandatory for every meaningful project-development change. Even when a change is mostly internal, verify the current platform/framework contract that the change relies on instead of relying on model memory.
- Refresh .context7/verification.json in the same change. CI checks that development-scope changes carry fresh Context7 evidence, canonical library IDs, verified guidance, and at least one official/vendor source check.
- Record the Context7 library ID(s) and the verified API/migration point in the active Gitea issue or pull request as the human-readable audit trail. A phase may not be closed without that evidence.
- If Context7 has no suitable source, record that gap and use the vendor's primary documentation. Never substitute memory or an old code example for current documentation.

## Mandatory project delivery gates

- Context7 is a release/delivery gate enforced by Talvora code, not a prompt convention. Talvora must reject project delivery when fresh, valid .context7/verification.json evidence is missing.
- Awwwards is the mandatory design delivery reference for every project developed with Talvora. Creative/UI work must review https://www.awwwards.com/ and record fresh machine-readable evidence in .talvora/awwwards-verification.json.
- These gates are fail-closed. talvora_git_run push and talvora_dotnet_publish must refuse to continue when either evidence contract is missing, stale, malformed, or incomplete.
- Handoff text, model memory, issue prose, or a verbal claim never satisfies these gates. Only the machine-readable evidence accepted by ProjectDeliveryGate permits delivery.

## Mandatory Windows desktop progress reporting

- Talvora must proactively report meaningful work to the active interactive Windows desktop. This is a software/runtime contract, not a prompt preference and not an optional courtesy.
- Every MCP tool execution passes through the desktop-progress wrapper, but only meaningful user-visible work creates a card. Low-value reads/searches/health checks remain silent unless they become part of a meaningful failure path.
- Long-running meaningful work emits a restrained heartbeat in tens of seconds; completion, cancellation, and failure update the same operation status where practical.
- The LocalSystem service never owns the normal interactive UI. It publishes session-aware progress to Talvora.Tray through local IPC; the signed-in tray process owns the notification window.
- Progress notifications are polished, nonmodal, full-text WPF cards. Arbitrary text truncation is forbidden. Long content must wrap and remain fully viewable through the card's scrollable content area. Cards are directly draggable; dragging or the explicit `Sabitle` control pins the notification-stack anchor and persists that position per interactive user. Unpinning returns to automatic placement. Pinning controls position only and must never turn a notification into a permanent card.
- The desktop surface behaves as one live worklog card. Active work does not auto-dismiss before it reaches a terminal state. Successful completion remains readable for about three minutes; failure remains longer. A newer meaningful operation may replace the older terminal card so the desktop never becomes a notification stack.
- User-facing copy must be ordinary Turkish. Do not expose raw tool names, command lines, exit codes, stack traces, SDK jargon, repository jargon, or internal transport terms. Error cards explain what went wrong in plain language, what is being protected, and what Talvora will try next.
- The desktop worklog may use a dark terminal/neon visual identity, but fabricated telemetry is forbidden. Any visible timestamp, elapsed duration, file name, change count, diff preview, result, or error detail must come from the real tool call/runtime result.
- Advanced technical evidence belongs in a clearly separated expandable evidence surface, never mixed into the plain-language explanation. Evidence may include real changed files, real +/− line counts, and a bounded real diff preview.
- Evidence must pass Talvora's canonical sensitive-data redaction before reaching the interactive desktop. Secret-bearing code lines may be replaced with a truthful redaction marker; do not trade privacy for visual effect.
- Both thrown exceptions and non-throwing error results must be recognized. In particular, MCP `CallToolResult.IsError` and structured failure signals such as unsuccessful results, nonzero command outcomes, and timeouts must never be reported as successful completion.
- Automatic and pinned placement must remain correct on mixed-DPI multi-monitor desktops. Treat Windows screen/window placement coordinates as native pixels instead of dividing global monitor coordinates by a single WPF window DPI scale.
- Notifications must be deduplicated/coalesced so they stay useful; the normal visible-card budget is one. The progress wrapper itself may not be bypassed for ordinary tool execution.
- Notification delivery is best-effort and must never fail, cancel, delay, or change the result of the underlying operation. Headless, CI, and no-active-session execution must remain safe.
- Windows service code must target the active interactive session through supported Windows APIs rather than assuming a fixed session ID. Cross-session IPC must use an explicit ACL and a session-specific endpoint.
- Substantial future Talvora project work must keep the user informed through Windows desktop progress updates at milestones and at intervals that are not excessively long.

## Validation and source editing

- Use `talvora_apply_patch` as the primary source/config/document editor and read existing files before mutation.
- **Git identity invariant (P-24, 2026-10-21):** remote Git verbs that use the user's credentials (`fetch`, `pull`, `push`) run as the signed-in user, never as LocalSystem. Never loosen SSH key ACLs, copy keys, or change git config to make a LocalSystem git call work; a credential failure as LocalSystem is a routing bug → Findings for Claude.
- Keep targeted validation fast during implementation; run the canonical installer/exact-installed smoke when runtime, packaging, shared infrastructure, dependency graph, or release behavior changes.
- Gitea is the primary modernization tracker. Keep the active phase issue updated with findings, validation evidence, commit SHA, and closeout state.
