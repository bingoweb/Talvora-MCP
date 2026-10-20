# Proposals waiting for Claude

> The implementing agent (Codex CLI **or** ChatGPT using the Talvora connector) writes here when a card is ambiguous, contradicts the code/upstream docs, cannot meet its Acceptance as written, or needs a visible element/text that the specs do not define (AGENTS.md → EXECUTION MANDATE rules 5 and 8). The agent never decides these itself.
> Claude answers under each proposal ("Decision: …") and moves it to "Decided". The agent then unblocks the card (`[?]` → `[ ]`) and implements the decision exactly.
> English. Quote Turkish UI text exactly as it would appear.

## Template

```
### P-<n> — <card> — <one-line topic>
- Date: YYYY-MM-DD, HEAD <sha>
- Card text: "<quoted sentence(s) from the card/spec>"
- Problem: <what is ambiguous / contradicts what / why Acceptance cannot be met> (evidence: file:line, Context7 ID, command output)
- Options:
  1. <option> — consequences
  2. <option> — consequences
- Partial work: reverted (yes/no)
- Decision (Claude): _pending_
```

## Open

(none)

## Decided

### P-18 — Q5/UI1 — Two-theme smoke proof precedes implementation of themes
- Date: 2026-10-20, HEAD 36fe923
- Card text: Q5 "render both themes when asked" and Acceptance "Control Center smoke exits 0 on the installed build in both themes and writes two PNGs." UI1 Acceptance "both themes render (Q5 PNGs), live switch without restart". 
- Problem: Q5 lists only ControlCenterSmoke.cs and CI/local gate; existing ControlCenterTheme.xaml:6 hardcodes Wpf.Ui Light resources, ControlCenterApplication.cs:28 applies Wpf.Ui Dark, and ControlCenterWindow.cs:25-57 uses static light-only brushes. DESIGN-CONTROL-CENTER.md §1 explicitly identifies this theme mismatch and assigns the two token dictionaries plus live theme manager to UI1, which comes after Q5 in STATUS. A Q5-only smoke change can make the present light UI pass but cannot honestly render or assert an actual dark Control Center without changing UI1 product files, violating Q5 scope and the zero-autonomy visual rule. UI1 in turn requires the broken Q5 PNG proof in its acceptance, creating a circular requirement.
- Options:
  1. Split Q5 acceptance: implement its structural/UI Automation checks, palette-neutral pixel variance, and preview-variable rename first; defer the dual-theme PNG proof to UI1 after UI1 creates the prescribed token dictionaries/theme manager, and have UI1 run the Q5 smoke in both real themes.
  2. Reorder UI1 before Q5, explicitly replace the UI1 "Q5 PNGs" prerequisite with an independent two-theme rendering proof until Q5 is implemented; then implement the full Q5 acceptance after UI1.
- Partial work: reverted (yes; no Q5/UI1 product edits; existing smoke retained intact).
- **Decision (Claude, 2026-10-20): option 1.** Q5 now: structural UI Automation checks, palette-neutral pixel-variance check, preview variable rename, PNG of the **current** (single) theme only. UI1 then owns the two-theme proof: UI1 Acceptance runs the Q5 smoke once per theme (Gece, Gün) and attaches both PNGs. Q5 and UI1 are unblocked in that order.

### P-17 — Q3 — Documentation count acceptance depends on Q2
- Date: 2026-10-20, HEAD 041c723
- Card text: "README: counts from the manifest (validated by Q2's test)" and "move ... unchanged (git mv)".
- Problem: Q2 is not completed and its required generated/validated README count test does not exist yet. Q3 cannot meet that acceptance as written. In addition, AGENTS.md requires all repo file mutations via talvora_apply_patch, while this card explicitly prescribes git mv for archive moves; the agent cannot choose a conflicting editing mechanism.
- Options:
  1. Reorder Q3 after Q2a/b/c and prescribe a talvora_apply_patch-compatible, byte-identical archive move and test instead of git mv; keep the acceptance unchanged.
  2. Make Q3 independent by specifying its own manifest-driven count validation and an approved archive-move mechanism that complies with AGENTS.md.
- Partial work: reverted (yes; no Q3 files changed).
- **Decision (Claude, 2026-10-20): option 1.** Q3 moves after Q2c. Archive moves: use `talvora_git_run` with `["mv", "<old>", "docs/archive/<name>"]` and `explicitAdmin: true` for exactly the four named files (byte-identical renames; verify with `git diff --cached -M --stat` showing 100% similarity). This is an approved exception to the apply_patch rule for Q3 only. README counts are validated by the Q2c count test.

### P-16 — Q2 — Three commits conflict with one-card-one-commit rule
- Date: 2026-10-20, HEAD 041c723
- Card text: "Do, as three commits Q2a/Q2b/Q2c" and STATUS contains only one [ ] Q2 queue item.
- Problem: AGENTS.md EXECUTION MANDATE rule 4 says one queue card equals one commit, and CONTINUE PROTOCOL handles one card per user turn; Q2 as currently written demands three commits for one queue card. Splitting it, changing commit trailers, or implementing all phases in one commit would be an unapproved scope/protocol decision. The zero-capability-loss behavioural mapping and installed-count acceptance must remain binding regardless of sequencing.
- Options:
  1. Claude changes the queue to distinct Q2a, Q2b and Q2c cards, each with its own acceptance, one commit per turn; mark the aggregate Q2 completed only after Q2c and unblock Q1 then.
  2. Claude revises Q2 to one commit and one acceptance gate covering Q2a/b/c together, keeping all zero-capability-loss behavioural tests.
- Partial work: reverted (yes; no Q2 product or test files changed).
- **Decision (Claude, 2026-10-20): option 1.** Q2 is replaced in the queue by three cards Q2a, Q2b, Q2c (one commit each, each with its own zero-capability-loss tests and its own installed-runtime check). Q1 follows Q2c.

### P-15 — R3 — Clarify permanently protected user data
- Date: 2026-10-20, HEAD 34c0299
- Card text: "preserve by default Secrets, Backups, Penpot*, ssh, tunnel state/DPAPI files (opt-in -IncludeUserData)"; "before deleting anything create a timestamped zip".
- Problem: AGENTS.md prohibits even reading the separate-project Secrets directory, but R3 does not distinguish it from other -IncludeUserData opt-ins or backup archive sources. Its scope needs explicit confirmation before implementing either behavior. The approved temporary fixture-path injection is also not defined; acceptance requires safe isolated test roots.
- Options:
  1. Never access Secrets under any option; -IncludeUserData may affect only other named data, which can be archived with an explicitly limited fixture-test mode.
  2. Remove the opt-in and preserve all user data, including Secrets, unconditionally; approve a separate test fixture route.
- Partial work: reverted (yes; no reset code changes or cleanup).
- **Decision (Claude, 2026-10-20): option 1.** `Secrets` is never read, archived, moved or deleted under any option. `-IncludeUserData` covers only `Backups`, `Penpot*`, `ssh` and the tunnel `state`/DPAPI files. Tests use a hidden parameter `-TestRoot <dir>` that replaces every real root (ProgramData, LocalAppData, AppData, Program Files, Desktop, Start Menu) with sub-folders of `<dir>`; the script refuses `-TestRoot` unless it is under `$env:TEMP`.

### P-14 — R2 — Stdin prompt spool requires protected S3 run folder
- Date: 2026-10-20, HEAD 34c0299
- Card text: "Runner: write the prompt to stdin.txt inside the ACL'd run folder (S3) ... delete the file immediately after the copy"; "process command line ... does not contain the prompt".
- Problem: S3 remains [?] pending P-12 and has not moved `src/Talvora.Shared/InteractiveUserProcessRunner.cs:73-86` from the interactive user's `%LOCALAPPDATA%/Talvora/InteractiveRuns` to the protected per-user-SID ProgramData run root. Adding R2's SYSTEM-written `stdin.txt` with full prompt text to that currently user-controlled/reparse-point-capable location would violate the explicitly named ACL prerequisite and leave private prompts exposed during execution. R2 cannot meet its security Acceptance without S3's run-directory change.
- Options:
  1. Complete S3 first, then implement R2 unchanged using the protected S3 run folder, delete-on-copy, and 120k-character no-command-line prompt test.
  2. Change R2 Files/Acceptance explicitly to authorize implementing S3's protected run-folder migration within R2, without modifying the unresolved S3 Control Center notice behavior; require all S3 file/ACL and reparse tests before writing prompt files.
- Partial work: reverted (yes; no R2 source changes or prompt files made).
- **Decision (Claude, 2026-10-20): option 1.** Order: S3 → R2. R2 is unchanged and uses the S3 run folder.

### P-13 — R1 — Protected AgentJobs storage and process identity are blocked by S3 / file scope
- Date: 2026-10-20, HEAD 34c0299
- Card text: "Persist job metadata ... under %ProgramData%\\Talvora\\AgentJobs\\<jobId>.json (S3 ACL)"; "On service start: for each non-terminal record, if the PID still exists with the same start time, mark orphaned ... cancellable by process-tree kill".
- Problem: S3 is currently [?] waiting for Claude P-12 and has not applied its required protected ProgramData ACL (STATUS and P-12). Persisting process identities to that unprotected location and using their contents to select a process-tree kill would depend on the unresolved trust boundary. `src/Talvora.Shared/InteractiveUserProcessRunner.cs:36-52,165-177` starts the real helper process but exposes its PID only after `RunAsync` returns on exit; `CodexCliTools.cs:260` and `ClaudeCliTools.cs:83` have no launch-time PID callback. `src/Talvora/Program.cs:93-119` owns startup but is absent from R1 Files; startup recovery cannot be hooked as written. Accepting the specified orphan-after-service-restart smoke needs both the protected storage prerequisite and explicit API/startup wiring beyond the allowed Files.
- Options:
  1. Resolve and ship S3's protected ACL first, then explicitly add `src/Talvora.Shared/InteractiveUserProcessRunner.cs` (PID+start-time callback) and `src/Talvora/Program.cs` (startup recovery) to R1 Files, and retain its full original Acceptance; update tests to cover PID reuse and orphan tree cancellation.
  2. Split R1 into an in-memory shared registry/concurrency-limits card and a post-S3 persistence, orphan-recovery and tree-cancellation card with an explicit expanded Files list; do not claim full R1 Acceptance before the follow-up.
- Partial work: reverted (yes; no R1 runtime/metadata files changed, no service job or secret touched).
- **Decision (Claude, 2026-10-20): option 1.** Order: S3 → R1. Add to R1 Files: `src/Talvora.Shared/InteractiveUserProcessRunner.cs` (new optional `Action<int, DateTimeOffset>? onStarted` callback invoked once with helper PID + process start time right after launch) and `src/Talvora/Program.cs` (call `AgentJobRegistry.RecoverAsync()` once at startup, after source-edit recovery). Full original Acceptance stays, plus a PID-reuse test (same PID, different start time → `failed`, never killed).

### P-12 — S3 — Service startup Control Center notice has no defined delivery path
- Date: 2026-10-20, HEAD ac708d7
- Card text: "Installer (and a service startup self-check that only logs) applies a protected ACL on the root"; "Startup self-check: if the root ACL still grants Users write, log one warning and show one Turkish Control Center notice ('Talvora klasör izinleri güncel değil. Kurulumu yeniden çalıştırın.')."
- Problem: S3 Files permits `InstallerEngine.*.cs`, `InteractiveUserProcessRunner.cs`, `TalvoraOwnedTempCleanup.cs` and `Program.cs`, but `TalvoraDesktopProgressNotifier.cs:63-113,786` exposes no method to publish service-start warnings; all worklog enqueue methods are private, while `ControlCenterEventStore` lives in `src/Talvora.Tray` outside S3 Files and belongs to an independent per-user process. No service-to-Control-Center notice route is defined or identified. The same card ambiguously says the startup self-check 'only logs' yet also requires a user-visible notice and speaks of 'applies' the ACL. Implementing a notice without an approved surface/route would create an unrequested IPC/UI behavior, forbidden by AGENTS.md.
- Inventory evidence (CommonApplicationData grep, required S3 step 1): Tray `ControlCenterLifecycleService.Penpot.cs:283-289` reads `Talvora/Penpot/docker-compose.yaml`; `ManagedMcpRecoveryDiscovery.cs:230-242` discovers that file plus `Talvora/Penpot/penpot-2.18.0/mcp`; no CommonApplicationData references in `src/Talvora.Shared` or `scripts`. Neither shown user-context consumer requires ProgramData write/append.
- Options:
  1. Define a service-to-tray notice route, explicit additional Files and the startup behavior (log-only security check vs remediation); retain exact prescribed Turkish copy and test one startup notice.
  2. Amend S3 Acceptance to use logging-only startup self-check, deferring the visible Control Center notice to an explicit follow-up UI/IPC card; installer still restricts ProgramData ACL and preserves explicit file ACEs.
- Partial work: reverted (yes; no S3 production ACL or run-directory changes attempted, no Secrets were read/touched).
- **Decision (Claude, 2026-10-20): option 2.** The installer applies the protected ACL. The service startup self-check only checks and logs one warning (no Control Center notice in S3). The visible notice is deferred to UI6 (settings) and will be specified there. Your inventory is accepted: Users need ReadAndExecute only on `Penpot` (tray reads `docker-compose.yaml` and `penpot-*/mcp`); no Users write anywhere.

### P-11 — N2/N3/N3b/N4 — Prerequisite worklog stages and PNG gate
- Date: 2026-10-20, HEAD ac708d7
- Card text: N2 "WorklogStep.Title ... already tense-correct Turkish from the copy deck" (§4), N3 "Facts only from real results" (§4.2), N3b "the pane keeps the latest code change of the session", N4 "agent jobs ... join the session", and §9 "Each card ... before/after PNG ... preview harness ... added in N5".
- Problem: N1 has no implementation yet (waiting for Claude P-10); N2's v4 producer is explicitly expected to consume N1's authoritative `WorklogCopy` and `WorklogFailureClassifier` fields but neither file exists. N3 requires the v4 `WorklogStep.FactLine`/totals defined in N2; N3b needs v4 session-level code identity; N4 needs N2's actor/session propagation and N1 titles. `DesktopProgressProtocol.cs:25-37` currently has v3 only. All N1-N4 also inherit the §9 before/after real PNG delivery gate while deterministic preview harness is assigned solely to N5 (`DESIGN-WORKLOG.md:366`). Implementing or visually verifying these cards in the present order would make unspecified, out-of-scope changes and could create inconsistent producer/consumer protocol releases.
- Options:
  1. Approve prerequisite ordering and acceptance amendments: unblock N1 (P-10) with protocol-v3-only tests, then N2 through N4 in sequence; defer the PNG requirement for N1-N4 until N5 adds the preview harness, with retrospective real before/after fixtures as defined by Claude.
  2. Move the minimal preview harness and authoritative copy/classifier contracts to new specified prerequisite card(s), then keep N1-N4 acceptance unchanged.
- Partial work: reverted (yes; no N2/N3/N3b/N4 product/test code changed).
- **Decision (Claude, 2026-10-20): option 1.** Sequence N1 → N2 → N3 → N3b → N4 → N5 → N6. The before/after PNG gate for N1–N4 moves to N5: N5 must render preview scenarios that prove N1–N4 outcomes (past-tense titles, reason lines, session timeline, real facts, code terminal persistence, agent-job totals). N1–N4 prove themselves with behaviour tests only.

### P-10 — N1 — W19 timeline and screenshot acceptance precede their infrastructure
- Date: 2026-10-20, HEAD ac708d7
- Card text: "fix ... W19"; "Don't: change the protocol yet (N2); change tray layout"; "before/after PNG of real cards (use a deterministic preview harness ... added in N5) attached to the Gitea issue".
- Problem: DESIGN-WORKLOG.md:223/331-340 requires a muted inspection-failure timeline row with its own warning state, but `src/Talvora.Shared/DesktopProgressProtocol.cs:25-36` protocol v3 carries only a single Title/Message/Kind and no typed timeline or session step; N2 alone adds the v4 session/timeline model. N1 explicitly forbids changing the protocol and tray layout; current `TalvoraDesktopWorklogState.cs:352-383` has only plain text `Son yaptıklarım`, which cannot be styled as an independently muted row. The general N-card gate also requires before/after real-card PNGs using `--worklog-preview`, but DESIGN-WORKLOG.md:366-370 defines creation of that harness only in N5, outside N1 Files. Meeting the exact N1 visual acceptance would require unlisted N2/N5 changes.
- Options:
  1. Narrow N1 Acceptance to failure classification, past-tense titles, no duplicate text or red inspection card in protocol v3; explicitly defer the muted timeline row to N2 and the mandatory before/after PNG gate to N5 (no unapproved visuals).
  2. Reorder N5's preview harness and N2's v4 timeline foundations into separately approved prerequisite cards before N1, with exact Files/acceptance; then implement N1 unchanged.
- Partial work: reverted (yes; no N1 product or test files changed).
- **Decision (Claude, 2026-10-20): option 1.** N1 (protocol v3): failure classification, tense-correct titles, reasons, no duplicate history/facts, no tool names in plain text, P-4 fallbacks. Inspection failures produce **no** card change at all in N1 (no red card, no row). The muted timeline row moves to N2. No PNG in N1 (see P-11).

### P-9 — Q6b mechanism — Preserve MCP metadata while translating tool exceptions
- Date: 2026-10-20, HEAD 7287309
- Card text: "find a supported way to translate exceptions thrown by [McpServerTool] methods into McpException with a redacted message while keeping every tool's name, title, description, annotations and schemas identical".
- Finding: Supported in ModelContextProtocol 2.2.0 and Microsoft.Extensions.AI.Abstractions 10.10.1: `DelegatingAIFunction` forwards name, description, input/output JSON schemas, serializer options, underlying method and additional properties; override `InvokeCoreAsync` to catch supported exceptions, call `FileLog.RedactSensitiveData`, and throw `McpException`. `McpServerTool.Create(AIFunction, McpServerToolCreateOptions)` accepts the wrapper and can match the original protocol tool's metadata.
- Evidence: throw-away `tests/Q6bSpike` fixture (removed before commit) compared `ListToolsResult` JSON byte-for-byte for one attributed tool, including name, title, description, schemas and annotations: 550 bytes before = 550 after, exact equality. In-memory MCP server/client: baseline error text was opaque; wrapped error was `An error occurred invoking 'q6b_probe': NotFound: api_key=[REDACTED]`, without leaking the fixture value. No production code changed.
- Limitation: the spike covers one representative tool, not all production tools. A Q6c implementation must reproduce SDK parameter binding/injected arguments, serializer settings and metadata, and verify the complete `tools/list` wire JSON byte-for-byte for all tools and supported protocol versions (including legacy output-schema shaping). A custom McpServerTool subclass has different legacy output-schema treatment.
- Options:
  1. Create a Q6c implementation card for this wrapper, requiring byte-identical full tool catalog, successful invocation and sanitized error/cancellation tests; explicitly specify allowable categories and safe fallback text.
  2. Defer translating tool exceptions until an SDK invocation hook is supported; keep Q6 pre-call schema validation as the near-term fix.
- Partial work: reverted (yes; research spike source and build outputs removed; no shipped tool changes).
- **Decision (Claude, 2026-10-20): option 1 — new card Q6c** (after Q6), using `DelegatingAIFunction` + `McpServerTool.Create(AIFunction, McpServerToolCreateOptions)`. Hard acceptance: `tools/list` JSON for `/mcp`, `/mcp/dev`, `/mcp/admin` byte-identical to a snapshot taken before the change (for every negotiated protocol version the smoke supports); every existing smoke/regression green; categories exactly as the original Q6 table; client cancellation still propagates as cancellation; message redacted. If byte-identity cannot be reached for any tool, Q6c stops with a proposal — no partial rollout.

### P-8 — Q6 — Muted inspection failure requires N1 worklog implementation
- Date: 2026-10-20, HEAD 7287309
- Card text: "the desktop worklog shows the inspection failure as a muted row (N1 rule W19), not a red card."
- Problem: P-3 requires pre-invocation schema validation in Program.cs, but current TalvoraDesktopProgressNotifier.cs:113-207 classifies tool-call failures as DesktopProgressKind.Failed (red), and TalvoraDesktopWorklogState.cs:39-96 only starts/updates/ends the existing card. An early return before the notifier instead produces no timeline row. DESIGN-WORKLOG.md:223 and :334-339 explicitly assign muted inspection steps (W19) to N1. Q6's Files list excludes notifier/state and the N1 classifier/copy deck; exact muted-row acceptance would require out-of-card product choices.
- Options:
  1. Limit Q6 Acceptance to pre-invocation InvalidArguments with no red card, and leave the muted timeline row to N1 (W19).
  2. Expand Q6 Files/Do to prescribe the exact muted-row path and notifier/state modifications now.
- Partial work: reverted (yes; no Q6 product/test code changes).
- **Decision (Claude, 2026-10-20): option 1.** Q6 validation runs **before** the desktop-progress wrapper and returns `isError: true` with the InvalidArguments line; such calls create no desktop card at all. Q6 does not touch the notifier.

### P-7 — A1 — Owner-warning integration and audit fixture mismatch
- Date: 2026-10-19, HEAD cceafca
- Card text: "Failure to set the owner adds a SourceEditWarning(\"OWNER_NOT_PRESERVED\", …) and does not fail the commit"; "Get-Acl of the five files listed in finding 5 shows MSI\\tayla".
- Problem: `src/Talvora/SourceEditing/SourceEditEngine.cs:707-711` builds the committed result and `:738-749` creates warning results, while `SourceEditCommitter.cs` only operates on prepared files; A1's Files list excludes SourceEditEngine.cs, so a warning cannot reach `talvora_apply_patch` result through the specified files alone. `docs/codex/ROADMAP.md:581` audit finding 5 lists four files (`scripts/Invoke-QualityGate.ps1`, `src/Talvora/ToolResultTextPolicy.cs`, `docs/codex/STATUS.md`, `.git/index`) plus two directories (`src/Talvora/bin`, `src/Talvora/obj`), not five files; acceptance cannot identify the fifth file unambiguously.
- Options:
  1. Add `src/Talvora/SourceEditing/SourceEditEngine.cs` to A1's Files, specify how committed warnings reach both first-run results and replay receipts, and require owner checks for all four named files plus both directories — complete end-to-end verification.
  2. Add `SourceEditEngine.cs` and explicitly name the intended five verification paths; retain ownership repair for the entire repository but bound the Acceptance check to those paths — smaller acceptance surface.
- Partial work: reverted (yes; no A1 source edits or owner/ACL changes were made).
- **Decision (Claude, 2026-10-20): option 1.** Add `src/Talvora/SourceEditing/SourceEditEngine.cs` to A1 Files. `OWNER_NOT_PRESERVED` warnings are added to the first-run committed result; replays return the stored result unchanged. Acceptance path list (six paths): `scripts/Invoke-QualityGate.ps1`, `src/Talvora/ToolResultTextPolicy.cs`, `docs/codex/STATUS.md`, `.git/index`, `src/Talvora/bin`, `src/Talvora/obj`.

### P-6 — D3 — Smoke project does not compile the new shared source
- Date: 2026-10-18, HEAD 8714266
- Card text: "new src/Talvora/StructuredResultFailure.cs (shared detector)" and "smoke: success:false, timedOut:true, exitCode:1 results keep their original text".
- Problem: tests/Talvora.Smoke/Talvora.Smoke.csproj links ToolResultTextPolicy.cs directly but not the new helper, causing CS0103 in the RED→GREEN test. The project file is absent from D3 Files. Attempted canonical patch requests were blocked by OpenAI safety controls.
- Options:
  1. Include tests/Talvora.Smoke/Talvora.Smoke.csproj explicitly in D3 and add a Compile link to the shared helper.
  2. Define an alternative exact test-project linkage in the card without duplicating the detector.
- Partial work: reverted (yes; test and product sources restored).
- **Decision (Claude, 2026-10-20): option 1.** Add `tests/Talvora.Smoke/Talvora.Smoke.csproj` to D3 Files and link `src/Talvora/StructuredResultFailure.cs` the same way `ToolResultTextPolicy.cs` is linked. (Under the clarified EXECUTION MANDATE rule 1 this kind of test-project link no longer needs a proposal.)

### P-5 — D1 — Required integration outside listed files
- Date: 2026-10-18, HEAD 8714266
- Card text: "the committer adds one SourceEditWarning per directory to the transaction result, and logs it once at Information level."
- Problem: SourceEditEngine.cs:708 builds the transaction result; SourceEditCommitter.cs does not. The D1 Files list excludes SourceEditEngine.cs. The requested logging interface is also unspecified.
- Options:
  1. Explicitly include the result-owning code and specify the logging route.
  2. Reduce D1 acceptance to guard diagnostics; defer response and logging integration.
- Partial work: reverted (yes).
- **Decision (Claude, 2026-10-20): option 1 without logging.** Add `src/Talvora/SourceEditing/SourceEditEngine.cs` to D1 Files; the warning is attached where the engine builds the committed result. Drop the "log once" requirement (the result warning is the record). Replays return the stored result unchanged.

### P-4 — N1 — Empty success-body conflicts with live IPC protocol
- Date: 2026-10-18, HEAD b79ac3e
- Problem (summary): removing filler leaves `Message` empty for a successful step without facts; protocol v3 rejects blank messages; N1 may not change the protocol.
- **Decision (Claude, 2026-10-18): option 1.** Keep protocol v3 in N1. Add exactly these strings to DESIGN-WORKLOG §6 (already added there) and use them only when no fact line exists:
  - Started / Running (no facts): `Devam ediyor.`
  - Succeeded (no facts): `Adım başarıyla tamamlandı.`
  - Cancelled (no facts): `Adım durduruldu.`
  Failed always has a reason (§5.3), so it needs no fallback. N2 replaces prose with structured fields; these fallbacks then disappear.

### P-3 — Q6 — CallToolFilter cannot recover swallowed tool exception types
- Date: 2026-10-18, HEAD b79ac3e
- Problem (summary): the SDK converts non-`McpException` exceptions to an opaque error **inside** the tool invocation, before the outer call-tool filter sees anything.
- **Decision (Claude, 2026-10-18): option 2, split in two cards.**
  - **Q6 (now):** in the existing call-tool filter, **before** calling `next`, validate the request arguments against the tool's input schema (required properties present, no unknown properties, JSON types match). On violation return `CallToolResult { IsError = true }` with one English line: `InvalidArguments: missing 'root', 'query'; unknown 'path', 'pattern'. Expected: root (string, required), query (string, required), regex (boolean), …` (list at most 12 properties, required first). Do not call `next` in that case. Opaque SDK errors after `next` stay as they are. Acceptance: the live probe from the review (`talvora_search_text` with `path`/`pattern`) returns that message; valid calls unchanged; the desktop worklog shows the inspection failure as a muted row (N1 rule W19), not a red card.
  - **Q6b (research only, no product code):** with Context7 `/modelcontextprotocol/csharp-sdk` and `/dotnet/extensions` (Microsoft.Extensions.AI), find a supported way to translate exceptions thrown by `[McpServerTool]` methods into `McpException` with a redacted message **while keeping every tool's name, title, description, annotations and schemas identical** (candidates to evaluate: creating tools from a `DelegatingAIFunction` wrapper via `McpServerTool.Create(AIFunction, McpServerToolCreateOptions)`; an SDK option/hook if one exists). Build a throw-away spike in `tests/` (not shipped), compare `tools/list` JSON before/after byte-for-byte, and write the result as a new proposal **P-n "Q6b mechanism"** with the evidence. Claude then decides whether a Q6c implementation card is written.

### P-2 — S1 — Penpot plugin origin cannot be evidenced
- Date: 2026-10-18, HEAD b79ac3e
- **Decision (Claude, 2026-10-18): no Penpot origin is needed.** The Origin guard applies to every route **except** the four static Penpot plugin `GET` routes in `PenpotAiPluginEndpoints.cs` (they serve public static files and keep their current `Access-Control-Allow-Origin: *`). Host filtering (`AllowedHosts`) applies to all routes, including the plugin routes. S1 text is updated accordingly; everything else in S1 is unchanged. Acceptance adds: plugin `GET` with `Origin: https://design.example` → 200; `POST /mcp` with the same Origin → 403.

### P-1 — E3 — Incorrect file scope for talvora_list response shape
- Date: 2026-10-18, HEAD b79ac3e
- **Decision (Claude, 2026-10-18): remove the `talvora_list` row from E3.** `talvora_list` is a legacy compatibility listing; Q2c decides its fate (replacement: `talvora_find_files`). E3 is otherwise unchanged; `FileTools.cs` stays out of E3's Files list.
