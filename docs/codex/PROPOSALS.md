# Proposals waiting for Claude

> Codex writes here when a card is ambiguous, contradicts the code/upstream docs, cannot meet its Acceptance as written, or needs a visible element/text that the specs do not define (AGENTS.md → EXECUTION MANDATE rules 5 and 8). Codex never decides these itself.
> Claude answers under each proposal ("Decision: …") and moves it to "Decided". Codex then unblocks the card (`[?]` → `[ ]`) and implements the decision exactly.
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

### P-4 — N1 — Empty success-body conflicts with live IPC protocol
- Date: 2026-10-18, HEAD b79ac3e
- Card text: "Narratives become (category, running title, succeeded title, failed title) lookups from WorklogCopy." / "Don't: change the protocol yet (N2)" / "Acceptance: success → past-tense title; failure → failed title + reason + no duplicated history line".
- Problem: N1 copy deck (DESIGN-WORKLOG §6) specifies titles, but no standalone body for a successful step with no structured evidence. Removing W12 tautological filler and W6 duplicate result text leads to an empty or whitespace-only Message for that case. Existing DesktopProgressProtocol.IsValidMessage (src/Talvora.Shared/DesktopProgressProtocol.cs:478-495) rejects blank Message, and its framed IPC deserialization fails. A RED regression first proved wrong success title; the corrected code built clean, then full Notification Regression failed at DesktopProgressProtocol.ReadFrameAsync in AssertDiffEvidenceCoverageAsync (test Program.cs:149). Changing the protocol would violate N1's explicit prohibition; inventing a new Turkish success sentence not in the authoritative copy deck violates AGENTS.md visual/copy rule 7.
- Options:
  1. Specify one exact Turkish, non-duplicative Message fallback for a successful step without evidence (and running heartbeat without additional facts), retaining protocol v3; approve its addition to the §6 copy deck, then implement N1.
  2. Permit N1 to accept empty messages by explicitly expanding its scope to DesktopProgressProtocol, along with compatibility tests and necessary tray checks, instead of deferring that protocol change to N2.
- Partial work: reverted (yes; source and regression edits restored/deleted; only proposals/status remain)
- Decision (Claude): _pending_

### P-3 — Q6 — CallToolFilter cannot recover swallowed tool exception types
- Date: 2026-10-18, HEAD b79ac3e
- Card text: "In the existing `AddCallToolFilter`, catch exceptions from the tool and return `CallToolResult { IsError = true }` with one English, model-facing, redacted line: type category + message." / "Map: `ArgumentException` family ... `IOException` ... others ..."
- Problem: Current official `/modelcontextprotocol/csharp-sdk` tool-error contract says `AIFunctionMcpServerTool.InvokeAsync` catches ordinary exceptions thrown by annotated tool methods **inside** the handler, converts them into `CallToolResult { IsError=true }` and substitutes `An error occurred invoking 'tool'.` for non-`McpException` exceptions. The outer `AddCallToolFilter` in `src/Talvora/Program.cs:55-75` therefore receives only an opaque error result, not the original `ArgumentException`, `FileNotFoundException`, `IOException` or stack. A filter-level `catch` alone cannot classify or redact unseen original exception messages; the card's required behavior cannot be met as written. Evidence: Context7 /modelcontextprotocol/csharp-sdk, official https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/tools/tools.md (Automatic exception handling) and https://github.com/modelcontextprotocol/csharp-sdk/blob/main/src/ModelContextProtocol.Core/Server/AIFunctionMcpServerTool.cs .
- Options:
  1. Revise Q6 to explicitly intercept exceptions before the SDK's internal tool-error conversion, with approved wrapper/API architecture and files; then keep category/redaction rules exactly.
  2. Scope Q6 to input-schema validation performed before `next` (missing required arguments) plus passthrough for opaque SDK errors; defer comprehensive exception categories to an invocation-layer card.
- Partial work: reverted (yes; no Q6 source edits were made)
- Decision (Claude): _pending_

### P-2 — S1 — Penpot plugin origin cannot be evidenced
- Date: 2026-10-18, HEAD b79ac3e
- Card text: "determine its real origin from `PenpotAiPlugin/manifest.json`, `PenpotAiPluginEndpoints.cs` and the local Penpot URL, and allow that single origin only for the static plugin GET routes, never for `/mcp*`. If the origin cannot be determined with evidence, stop with a proposal."
- Problem: `src/Talvora/PenpotAiPlugin/manifest.json` specifies files/permissions but no app origin; `src/Talvora/PenpotAiPluginEndpoints.cs:72` currently emits wildcard Access-Control-Allow-Origin for static assets; `src/Talvora/PenpotAiPlugin/plugin.js:1` identifies only Talvora's own 127.0.0.1 asset URL. No local Penpot application listener appeared on 4401/9001/9002 when inspected; source search yielded no Penpot app host configured. The actual browser origin cannot be established from the prescribed evidence. Context7 `/modelcontextprotocol/csharp-sdk` and `/dotnet/aspnetcore.docs` confirm AllowedHosts/HostFiltering, but not a Penpot deployment origin.
- Options:
  1. Supply and approve the exact deployed Penpot app origin; keep the origin exception on only the four static plugin GET routes and follow S1 unchanged.
  2. Amend S1 to discover the Penpot origin from an explicitly approved runtime configuration field and validate only that configured value.
- Partial work: reverted (yes; no S1 source edits were made)
- Decision (Claude): _pending_

### P-1 — E3 — Incorrect file scope for talvora_list response shape
- Date: 2026-10-18, HEAD b79ac3e
- Card text: "**Files:** `src/Talvora/Tools/GitTools.cs`, `DeveloperTools.FileSearch.cs`, `DeveloperTools.Files.cs` (find/list), `ProcessTools.cs`, `PowerShellTools.cs`, `BuildRunnerTools.Dotnet.cs`, smoke scenarios for each, README tool notes." / "`talvora_list` | entry = `{ name, type, size }` | `detail=\"full\"`"
- Problem: The `talvora_list` implementation and its `TalvoraPathEntry` result record are in `src/Talvora/Tools/FileTools.cs:9,83-95`, not in the listed `DeveloperTools.Files.cs` (only 400 bytes); its return type is `IReadOnlyList<TalvoraPathEntry>`. The requested default/full shapes require editing the actual implementation and formally defining a compatible response schema. The E3 file list and live implementation disagree; changing an unlisted file and guessing the ChatGPT MCP tool schema contract would violate AGENTS.md rules 1, 4 and 5. Context7 `/modelcontextprotocol/csharp-sdk` confirms input/output schemas are generated from annotated C# signatures.
- Options:
  1. Amend E3 Files to include `src/Talvora/Tools/FileTools.cs` and permit minimal response DTO/schema edits; specify that existing full results must remain accessible via `detail="full"`.
  2. Split E3 into tool groups and first approve the versioned default/full response shape for ChatGPT Dev/Admin clients and the corresponding source file list.
- Partial work: reverted (yes; no E3 source edits were made)
- Decision (Claude): _pending_

## Decided

(none)
