# Talvora engineering rules

## Language policy

- Agent-facing documents (`docs/codex/*`, this file, code comments, commit messages, tool descriptions) are **English**.
- Everything Talvora shows to people — Control Center, tray, desktop worklog, user-facing error messages, MCP tool **titles** — is **ordinary Turkish** with correct diacritics.
- Codex always reports to the user in **Turkish**.

## EXECUTION MANDATE — Codex implements, Claude decides (binding, highest priority)

The user decided (2026-10-18): Codex does **only** what Claude's cards say, **exactly** the way they say it. Claude owns every product, architecture and especially every **visual/cosmetic** decision. This section overrides any other wording in this file or in `docs/codex/*` that seems to leave room for Codex's own judgement.

**Scope**
1. Work only on the current queue card. No "while I'm here" fixes, refactors, renames, reformatting, file moves, comment rewrites, dependency bumps or test rewrites outside the card's **Files** list. If another file must change for the card to compile or pass, change the minimum and list it in the Log with the reason.
2. Do not start work that is not a card. Things you notice go to STATUS → "Findings for Claude" (one line each: file:line + what you saw). Do not fix them.
3. Do not add, remove or upgrade packages, tools, services, endpoints, settings or MCP tools unless the card names them.

**Decisions**
4. Where a card prescribes values, names, code shape, library, algorithm, order or copy, use exactly that. Do not "improve" it.
5. If a card is ambiguous, contradicts the code, contradicts current upstream docs (Context7), or cannot meet its Acceptance as written: **do not choose yourself**. Write the question with evidence and 1–3 options into `docs/codex/PROPOSALS.md` (template there), mark the card `[?]` in STATUS with "waiting for Claude: P-<n>", revert any partial work of that card, and continue with the next `[ ]` card.
6. Context7/vendor docs are used to **verify** the prescribed approach and fill exact API names/versions — never to replace the prescribed approach with a different one.

**Design and cosmetics (zero autonomy)**
7. Everything visible — layout, colours, fonts, sizes, spacing, radii, shadows, icons, glyphs, motion, timing, wording/Turkish copy, tooltips, empty/loading/error states, tool titles — comes **only** from `docs/codex/DESIGN-CONTROL-CENTER.md`, `docs/codex/DESIGN-WORKLOG.md` or an approved proposal. Never invent a colour, icon, animation, string, visual element or behaviour.
8. Where the spec is silent about a **minor layout detail** (an alignment or a gap inside an element the spec defines), use only values from the spec's token scales (`Tv.Space.*`, `Tv.Radius.*`, `Tv.Type.*`, colour tokens) and list each such choice in the Log under "Design gaps filled" so Claude can audit it. Anything else that is visible and not specified (a new element, text, icon, colour, state, animation) → proposal (rule 5), never a guess.
9. Awwwards research is **evidence only**. It must not change the spec. In `.talvora/awwwards-verification.json`, `designDecisions` must quote the spec sections being implemented (e.g. "DESIGN-WORKLOG §3.1 code terminal"), not new ideas.
10. Every UI/worklog card ends with the before/after PNGs required by its Acceptance; Claude approves the look in the next audit.

Violations found in Claude's audits become D-cards that revert the unrequested change.

## CONTINUE PROTOCOL ("Talvora'ya devam et")

When the user says "Talvora'ya devam et", "Talvora devam", "sıradaki adım" or similar, execute this sequence without asking questions. **One queue item per message**, then stop and report.

1. **Location:** work in `C:\Users\tayla\Talvora-MCP`. If git reports "dubious ownership", prefix git commands with `-c safe.directory=C:/Users/tayla/Talvora-MCP`; do not change global git config.
2. **Sync:** `git fetch origin`. Dirty tree: continue it only if it belongs to STATUS "In progress"; otherwise stop and ask. `git pull --ff-only origin main`; never force, reset --hard, rebase or rewrite history.
3. **Context (token economy):** after K1 run `pwsh scripts/Get-CodexContext.ps1` and read only its output plus the files the card names. Do not read `docs/archive/*`, `BUG-AUDIT.md`, `HANDOFF.md` history or `MCP-CONTROL-CENTER-TODO.md` unless the card points to a section.
4. **Baseline:** run the quality gate (`pwsh scripts/Invoke-QualityGate.ps1 -Scope Fast`; before K1: locked restore + Release build). If it is red at the start, this message's task is to make it green (exception: the in-progress item is K0/K1).
5. **Select:** the first `[ ]` item in `docs/codex/STATUS.md` → "Agent queue" (skip `[?]`), or the "In progress" item. Write "In progress: <item>" into STATUS first.
6. **Implement:** follow the card's **Why / Files / Do / Don't / Acceptance** exactly (`docs/codex/ROADMAP.md`; N cards in `docs/codex/DESIGN-WORKLOG.md`; UI cards in `docs/codex/DESIGN-CONTROL-CENTER.md`). Test first (red), then code (green). When a card prescribes a technique, values or code shape, use it; if it cannot work as written, record an open question with evidence and stop — do not improvise. Context7 before relying on any framework/SDK/CLI behaviour.
7. **Verify:** quality gate → last line **`Hepsi geçti.`**; runtime-affecting cards also deploy with `scripts/Deploy-Windows-Installer.ps1` and verify `/healthz.sourceCommit` + installed smoke; UI cards attach both-theme PNGs.
8. **Record:** tick the item (`[x]`, or `[~]` + a "User queue" line when user evidence is needed), add a Log entry (English, newest on top: what changed, tests, Context7 IDs, installed commit), reset "In progress" to `(none)`; mirror the evidence to the active Gitea issue.
9. **Commit:** exactly one commit per item: `<type>(<area>): <summary>` + body + trailer `Codex-Card: <ID>`.
10. **Push:** only while STATUS "Fixed facts" says `Push: Authorized`; then `git push origin main` and `git push github main`, never force. On failure retry once, then report.
11. **Report to the user in Turkish, short:** item done · changed files · gate last line · deploy/installed commit · push status · next item · what the user must do (if anything).

**Stop conditions** (report in Turkish and wait): agent queue empty (list the user queue); acceptance impossible without breaking a rule (write an open question); a `[?]` dependency is required; the work needs history rewrite, repo visibility/settings changes, reading/moving anything in `C:\ProgramData\Talvora\Secrets`, or running `scripts/Reset-And-Install.ps1` before R3.

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
- Keep targeted validation fast during implementation; run the canonical installer/exact-installed smoke when runtime, packaging, shared infrastructure, dependency graph, or release behavior changes.
- Gitea is the primary modernization tracker. Keep the active phase issue updated with findings, validation evidence, commit SHA, and closeout state.
