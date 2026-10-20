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
- Decision (Claude): _pending_

### P-8 — Q6 — Muted inspection failure requires N1 worklog implementation
- Date: 2026-10-20, HEAD 7287309
- Card text: "the desktop worklog shows the inspection failure as a muted row (N1 rule W19), not a red card."
- Problem: P-3 requires pre-invocation schema validation in Program.cs, but current TalvoraDesktopProgressNotifier.cs:113-207 classifies tool-call failures as DesktopProgressKind.Failed (red), and TalvoraDesktopWorklogState.cs:39-96 only starts/updates/ends the existing card. An early return before the notifier instead produces no timeline row. DESIGN-WORKLOG.md:223 and :334-339 explicitly assign muted inspection steps (W19) to N1. Q6's Files list excludes notifier/state and the N1 classifier/copy deck; exact muted-row acceptance would require out-of-card product choices.
- Options:
  1. Limit Q6 Acceptance to pre-invocation InvalidArguments with no red card, and leave the muted timeline row to N1 (W19).
  2. Expand Q6 Files/Do to prescribe the exact muted-row path and notifier/state modifications now.
- Partial work: reverted (yes; no Q6 product/test code changes).
- Decision (Claude): _pending_


### P-7 — A1 — Owner-warning integration and audit fixture mismatch
- Date: 2026-10-19, HEAD cceafca
- Card text: "Failure to set the owner adds a SourceEditWarning(\"OWNER_NOT_PRESERVED\", …) and does not fail the commit"; "Get-Acl of the five files listed in finding 5 shows MSI\\tayla".
- Problem: `src/Talvora/SourceEditing/SourceEditEngine.cs:707-711` builds the committed result and `:738-749` creates warning results, while `SourceEditCommitter.cs` only operates on prepared files; A1's Files list excludes SourceEditEngine.cs, so a warning cannot reach `talvora_apply_patch` result through the specified files alone. `docs/codex/ROADMAP.md:581` audit finding 5 lists four files (`scripts/Invoke-QualityGate.ps1`, `src/Talvora/ToolResultTextPolicy.cs`, `docs/codex/STATUS.md`, `.git/index`) plus two directories (`src/Talvora/bin`, `src/Talvora/obj`), not five files; acceptance cannot identify the fifth file unambiguously.
- Options:
  1. Add `src/Talvora/SourceEditing/SourceEditEngine.cs` to A1's Files, specify how committed warnings reach both first-run results and replay receipts, and require owner checks for all four named files plus both directories — complete end-to-end verification.
  2. Add `SourceEditEngine.cs` and explicitly name the intended five verification paths; retain ownership repair for the entire repository but bound the Acceptance check to those paths — smaller acceptance surface.
- Partial work: reverted (yes; no A1 source edits or owner/ACL changes were made).
- Decision (Claude): _pending_

### P-6 — D3 — Smoke project does not compile the new shared source
- Date: 2026-10-18, HEAD 8714266
- Card text: "new src/Talvora/StructuredResultFailure.cs (shared detector)" and "smoke: success:false, timedOut:true, exitCode:1 results keep their original text".
- Problem: tests/Talvora.Smoke/Talvora.Smoke.csproj links ToolResultTextPolicy.cs directly but not the new helper, causing CS0103 in the RED→GREEN test. The project file is absent from D3 Files. Attempted canonical patch requests were blocked by OpenAI safety controls.
- Options:
  1. Include tests/Talvora.Smoke/Talvora.Smoke.csproj explicitly in D3 and add a Compile link to the shared helper.
  2. Define an alternative exact test-project linkage in the card without duplicating the detector.
- Partial work: reverted (yes; test and product sources restored).
- Decision (Claude): _pending_

### P-5 — D1 — Required integration outside listed files
- Date: 2026-10-18, HEAD 8714266
- Card text: "the committer adds one SourceEditWarning per directory to the transaction result, and logs it once at Information level."
- Problem: SourceEditEngine.cs:708 builds the transaction result; SourceEditCommitter.cs does not. The D1 Files list excludes SourceEditEngine.cs. The requested logging interface is also unspecified.
- Options:
  1. Explicitly include the result-owning code and specify the logging route.
  2. Reduce D1 acceptance to guard diagnostics; defer response and logging integration.
- Partial work: reverted (yes).
- Decision (Claude): _pending_

## Decided

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
