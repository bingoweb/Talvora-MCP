# Talvora Source Edit Engine — Architecture Handoff

## 2026-09-20 source audit update (deployment pending)

- Ordinary .cs/.csx edits now use Roslyn syntax Error diagnostics in transaction preflight. This is syntax validation with the bundled parser, not project compilation/type checking; validateSyntax=false remains explicit opt-out.
- Structural rule snapshots live outside the source mirror. The active ruleFile is a control input and is excluded from transformation targets; same-named user files remain ordinary targets.
- Private ast-grep cache reuse verifies package/version/SRI provenance and executable SHA-256 before execution; unverifiable legacy cache is refreshed through the verified installation route. Installer provenance uses a single npm metadata snapshot.
- Source Edit regression 60/60 GREEN, cache scenarios 2/2 GREEN, installer metadata 3/3 GREEN. Exact-installed claims below describe the older deployed baseline, not these source changes.

Date: 2026-09-20
Status: CORE + ROUTING V3 + STRUCTURAL + ROSLYN SEMANTIC ADAPTER EXACT-INSTALLED GREEN
Canonical issues: BUG-AUDIT #110 and #120
Canonical phase: MCP-CONTROL-CENTER-TODO.md / Faz 14

## Purpose

Build a commercial-quality, agent-first source editing subsystem for Talvora. The goal is not merely to add another text replacement tool. The subsystem must make source changes deterministic, transactional, concurrency-safe, diagnosable, and easy for an agent to select without trial-and-error.

The implementation must preserve Talvora's TAM YETKI philosophy. General shell/process capabilities remain available. However, direct legacy text mutation tools must no longer be usable as the normal source-code editing path inside development workspaces.

## Critical session state

- Core Source Edit Engine is implemented under `src/Talvora/SourceEditing`. `talvora_apply_patch` is the PRIMARY/default mutation tool, `talvora_read_source` is its revision handshake, `talvora_structural_edit` is the repetitive AST/syntax specialist, `talvora_semantic_edit` is the narrow Roslyn symbol-identity specialist, `talvora_apply_edits` is the exact-range specialist, and `talvora_source_edit_guide` is the read-only Routing Contract v3 surface.
- Dedicated Source Edit/Semantic regression is 33/33 GREEN, including the previous transaction/structural coverage plus solution-wide rename, stale semantic anchor, no-symbol, linked-context ambiguity, MSBuild load diagnostics, cancellation, UTF-16 BOM/newline preservation and no-formatting-churn evidence.
- Exact-installed raw MCP verification is GREEN at 204/204 unique tools using the server's latest supported protocol `2025-11-25`. It includes PRIMARY/default `talvora_apply_patch`, structural-specialist `talvora_structural_edit`, semantic-specialist `talvora_semantic_edit`, exact-range-only `talvora_apply_edits`, `talvora_source_edit_guide` and unrestricted explicit-admin routing.
- Exact-installed structural verification remains GREEN: canonical installer vendors verified ast-grep 0.45.3 with npm SRI + executable SHA-256 provenance; live pattern/rewrite with multibyte Unicode and live YAML rule/fix both committed through Source Edit receipts/WAL; same structural transaction replay returned `replayed=true`.
- Exact-installed semantic verification is GREEN on canonical deploy/live snapshot `sourceCommit=747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-8726cde50619`: raw `tools/list` is 204/204 unique; a real two-project `.slnx` rename resolved `LiveSmoke.Widget`, changed declaration + cross-project references only, preserved UTF-16 LE BOM plus exact CRLF/deliberate spacing, committed through the Source Edit receipt/WAL, and same transaction retry returned `replayed=true`.
- Do NOT start from the earlier idea that `talvora_apply_patch` is just a `git apply` wrapper.
- Git unified diff remains a useful compatibility format/backend, not the architectural core.
- Existing working tree is intentionally dirty from the completed Playwright MCP -> official Playwright CLI migration and deploy-helper #109 fix. Preserve every existing change.
- User has NOT requested commit/push.
- Before implementation, re-read CURRENT HANDOFF, BUG-AUDIT #110, Faz 14 TODO, this file, and APPLICATION-DEVELOPMENT-ROADMAP.md.
- Before selecting third-party libraries/CLIs, re-check current Context7 and official web documentation. Do not rely only on this snapshot.

## Research conclusions to carry forward

The research phase compared several approaches:

1. Agent-friendly custom patch DSL
   - Best candidate for the primary `talvora_apply_patch` interface.
   - The model should describe only intended changes rather than resend whole files.
   - Design should support update/add/delete/move and multiple files.
   - Do not blindly clone another agent's parser or fuzzy matching behavior.

2. LSP WorkspaceEdit
   - Strong reference model for multi-file edits, create/rename/delete operations, versioned edits, and transactional failure semantics.
   - Use as an architectural reference for the internal change-set model.

3. Microsoft Agent Host Protocol Changesets / optimistic concurrency
   - Strong reference for before/after resource state, revision matching and conflict reporting.
   - Use as a reference for expected-revision / if-match style concurrency guards and change receipts.

4. ast-grep
   - Strong candidate for future/current structural source rewrites across multiple languages.
   - Appropriate for syntax-aware repetitive transformations where text patching is inferior.
   - Revalidate current stable CLI/API before implementation.

5. Roslyn
   - Implemented for the narrow C# semantic layer with current stable Workspaces 5.9.0.
   - Appropriate for operations that genuinely require solution/project symbol identity; current capability is symbol-aware rename.
   - Keep semantic editing separate from generic patching. Ordinary C# edits remain on PRIMARY/default `talvora_apply_patch`.

6. DiffPlex
   - Useful candidate for diff rendering and three-way conflict analysis/merge suggestions.
   - Do not auto-commit fuzzy/three-way merge results without an exact new transaction.

7. Tree-sitter
   - Useful for polyglot syntax parsing/validation and syntax-aware anchors.
   - Tree-sitter itself is not the primary mutation engine.

8. Git apply
   - Mature unified-diff compatibility backend.
   - Useful for importing standard patches and optional compatibility mode.
   - Not the canonical Talvora agent edit language.

9. Coding-agent edit formats
   - Current coding agents commonly use model-friendly edit formats rather than forcing raw Git patches.
   - Research showed an important design lesson: fuzzy context matching can silently apply a syntactically valid but wrong edit. Talvora must never silently commit on fuzzy-only context.
   - Another important lesson: a mutation may commit even if a transport response is lost. Talvora therefore needs idempotent transaction IDs and durable commit receipts.

## Proposed architecture

```text
Agent / ChatGPT
      |
      +--> talvora_read_source        (revision-aware source read)
      +--> talvora_apply_patch        (PRIMARY/default source editor)
      +--> talvora_apply_edits        (specialist: already-known exact ranges)
      +--> talvora_source_edit_guide  (read-only routing contract)
      +--> talvora_structural_edit    (AST-aware specialist; ast-grep proposal-only)
      +--> talvora_semantic_edit      (C# symbol-identity specialist; Roslyn proposal-only)
      |
      v
Talvora Source Edit Transaction Engine
      |
      +--> workspace/path classification
      +--> snapshot + revision/hash capture
      +--> normalize requested operations
      +--> exact preconditions / anchors
      +--> in-memory proposed state
      +--> syntax/semantic validation where available
      +--> cross-file transaction validation
      +--> staged same-volume temp files
      +--> durable flush
      +--> atomic commit / rollback
      +--> transaction journal
      +--> before/after hashes + receipt
      +--> structured diagnostics
```

The tool surface should remain minimal. Do not create a separate tool for every internal engine capability merely to increase tool count.

## Mandatory transaction properties

### 1. Exact-by-default
Automatic mutation must never depend solely on fuzzy similarity. Fuzzy search may identify candidate locations and diagnostics, but an edit that cannot satisfy exact/versioned preconditions must fail without modifying files.

### 2. Optimistic concurrency
Every edit-capable operation should support a revision guard, preferably SHA-256 or an equivalent stable revision token.

Example semantics:

```text
expectedRevision = ABC
currentRevision  = DEF
=> SOURCE_EDIT_CONFLICT
=> zero files modified
```

### 3. Idempotency
Mutation calls should accept a client-generated `transactionId`.

If the transport response is lost after commit and the same transaction is retried, Talvora must return the prior committed receipt instead of reapplying the changes.

### 4. Multi-file all-or-nothing behavior
All requested operations are validated before any final commit. If one edit is invalid, no target is modified.

If the final filesystem commit spans multiple files, design a journal + rollback protocol so a mid-commit failure can recover deterministically.

### 5. Durable atomic writes
Stage new content in the destination directory/same volume where practical. Flush data before final replacement. Prefer the strongest Windows/.NET atomic replace primitive compatible with the target state. Preserve metadata intentionally rather than accidentally.

### 6. Clear receipts
Successful mutation response should include at least:
- transactionId
- status
- changed files
- operation type per file
- before revision/hash
- after revision/hash
- byte/line delta where useful
- warnings
- validation results

### 7. Structured failure taxonomy
Do not collapse failures into generic INVALID_ARGUMENT when the server can provide a meaningful source-edit diagnosis.

Candidate stable codes:
- SOURCE_EDIT_POLICY_VIOLATION
- SOURCE_EDIT_CONFLICT
- PATCH_PARSE_ERROR
- PATCH_CONTEXT_NOT_FOUND
- PATCH_CONTEXT_AMBIGUOUS
- EXPECTED_REVISION_MISMATCH
- EXPECTED_MATCH_COUNT_MISMATCH
- SOURCE_VALIDATION_FAILED
- TRANSACTION_ALREADY_COMMITTED
- TRANSACTION_ROLLED_BACK
- TRANSACTION_RECOVERY_REQUIRED

Names may be refined during planning, but the principle is mandatory.

## Canonical tool-routing contract

This contract must be encoded in tool descriptions, documentation and regression tests so an agent does not choose tools by trial-and-error.

### Read / understand
- Existing source/config/repository-document file before mutation: `talvora_read_source` so the edit carries the current SHA-256 revision.
- General-purpose exact full small file when no source mutation handshake is needed: `talvora_read_text`
- Known section/range: `talvora_read_text_range`
- Tail/log: `talvora_tail_text`
- Find text/symbol clue across workspace: `talvora_search_text`
- Find files: `talvora_find_files`
- Workspace/project discovery: `talvora_workspace_inspect`, `talvora_project_discover`
- Git status/diff/history: Talvora Git tools

### Source/document mutation inside a development workspace
1. **Ordinary agent-authored source/doc/config change** -> `talvora_apply_patch`. This is the PRIMARY/default editor for localized or broad work, one file or many files, and add/update/delete/move operations.
2. **Exact zero-based UTF-16 ranges/revisions are already known or generated programmatically** -> `talvora_apply_edits`. Multi-file scope by itself is never a reason to switch away from `talvora_apply_patch`.
3. **Large repetitive syntax-shaped transformation across files/languages** -> `talvora_structural_edit`; do not emulate it with dozens of regex replacements
4. **C# operation that genuinely requires solution/project symbol identity** -> `talvora_semantic_edit`; current capability is symbol-aware rename. Ordinary C# edits still use PRIMARY/default `talvora_apply_patch`; do not emulate semantic rename with global text replacement
5. **Create/delete/move text source/project/doc file** -> source-edit transaction tools, not legacy write/delete/move paths when the operation belongs to a source changeset

If tool choice is unclear, call `talvora_source_edit_guide`; do not probe mutation tools and learn by failure. The same contract is emitted in MCP server instructions, canonical tool titles/descriptions, `SOURCE_EDIT_POLICY_VIOLATION`, and regression tests.
6. **Import an external standard unified diff** -> compatibility mode/backend of `talvora_apply_patch`, not the primary agent DSL

### Non-source/general filesystem mutation
- Binary assets -> byte/file tools as appropriate
- Temporary fixtures outside development workspaces -> legacy text/file tools may remain valid
- General filesystem copy/move/delete unrelated to a source changeset -> existing file tools
- Logs/state/data maintenance -> existing specialized tools

### Execution
- Build/test/runtime commands -> typed ecosystem tools first
- Generic command that has no typed tool -> unrestricted `talvora_run_process` / ecosystem *-run
- PowerShell -> Windows automation/admin/orchestration; **not the canonical source-edit path**
- Do not use PowerShell `Set-Content`, `Out-File`, `WriteAllText`, heredocs, Python one-liners, shell redirection, or similar mechanisms for normal workspace source editing when the canonical Source Edit Engine is available.

## Legacy mutation enforcement

The user explicitly wants forgetting the new tools to be prevented, not merely discouraged.

Plan a server-side `SourceMutationPolicy`:

- For text mutations inside a recognized development workspace/repository, legacy mutation tools must reject the request with `SOURCE_EDIT_POLICY_VIOLATION` and point to the canonical tool.
- At minimum evaluate:
  - talvora_write_text
  - talvora_replace_text
  - talvora_append_text
  - text-like uses of talvora_write_bytes
- Decide during planning whether direct delete/move/copy of source text files should also be guarded when they are clearly source changeset operations.
- Do NOT cripple unrestricted PowerShell/process execution. Instead mark them explicitly as non-canonical source-edit mechanisms in their tool descriptions and use regression/instruction contracts. Full capability remains available for legitimate explicit administration and advanced-control use.
- Workspace detection must be robust enough not to block ordinary temporary text files or unrelated system files. Prefer repository/workspace containment + text/source classification rather than a naive global extension blacklist.

## Proposed primary patch language properties

Do not finalize grammar without a planning pass and tests.

Desired capabilities:
- Begin/end transaction envelope
- Update file
- Add file
- Delete file
- Move/rename file
- Multiple files per transaction
- Multiple hunks per file
- Exact context anchors
- Optional expected file revision
- Optional expected match count
- Explicit line/range form where useful
- Clear escaping rules
- Preserve encoding/newline policy deliberately
- No hidden fuzzy apply
- Parser produces a normalized internal changeset before any mutation

The internal transaction model should be independent of the external patch syntax so a future LSP/AHP/Git/IDE frontend can feed the same engine.

## Structural and semantic backends

Do not force every transformation through text patching.

### Structural edit
Implemented with ast-grep as a proposal-only structural backend:
- `talvora_structural_edit` is selected only for repetitive syntax/AST-shaped changes; ordinary work remains on PRIMARY `talvora_apply_patch`.
- Supports fast pattern/kind + rewrite and advanced workspace-relative YAML rule/fix modes.
- ast-grep runs only against an isolated UTF-8 mirror with `--json=stream`; canonical code never passes `--update-all` or `--interactive`.
- Proposal records are checked against both Unicode-scalar line/column positions and UTF-8 byte offsets, plus exact matched text and replacement offsets.
- Proposals become exact revision/range `SourceEditChangeInput` changes and enter the same transaction engine; no direct live-workspace write is allowed.
- Canonical installer vendors the official Windows platform binary + provenance so exact-installed runtime does not require global PATH/npm/network for normal operation.

2026-09-20 locked #114 implementation decisions:
- ast-grep remains the structural backend after current Context7/official-doc revalidation and a real Windows probe.
- Current verified package line is `@ast-grep/cli-win32-x64-msvc 0.45.3`, MIT. Chocolatey still has no ast-grep package; the official platform-specific npm package is therefore the canonical provisioning exception.
- Installer build resolves the current latest Windows x64 package, installs it into a private build staging prefix with scripts disabled, then vendors only the native `ast-grep.exe` plus provenance metadata into the Talvora Service payload. Runtime never depends on global PATH/npm/network.
- Build records npm package/version/integrity and the vendored executable SHA-256. The canonical build fails rather than silently using an unknown or stale binary.
- Canonical `talvora_structural_edit` invokes either `ast-grep run` (pattern/kind + rewrite) or `ast-grep scan --rule` (workspace-relative YAML rule/fix) only in non-mutating `--json=stream` proposal mode. It never passes `--update-all` or `--interactive`.
- JSON proposal output supplies exact matched text, replacement text and byte offsets. Talvora validates every result against a fresh source snapshot, converts encoding-aware byte offsets to .NET UTF-16 positions, rejects overlap/stale/invalid results, builds a normalized changeset and commits through the existing WAL/revision/rollback core.
- ast-grep reports UTF-8 byte offsets and Unicode-scalar line/column positions separately. A Windows emoji probe produced byteOffset=18 and column=15 where the corresponding .NET UTF-16 index is 16. Talvora therefore converts scalar columns to UTF-16 and independently cross-checks them against byte offsets before accepting a proposal; ast-grep columns are never passed directly as `talvora_apply_edits` UTF-16 positions.
- Structural/semantic selection rule: ordinary one/multi-file edits, including ordinary C# edits, remain PRIMARY/default `talvora_apply_patch`; already-generated exact ranges use `talvora_apply_edits`; repetitive syntax-shaped transformations use `talvora_structural_edit`; C# operations that genuinely require solution/project symbol identity use `talvora_semantic_edit`.
- `talvora_run_process` / PowerShell remain unrestricted TAM YETKI explicit-admin paths, but routing metadata must not select them for normal structural source editing.
- Exact-installed evidence: 203 tools; runtime `747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-b55f88d972ca`; ast-grep 0.45.3 executable SHA-256 `DAFF0F5963FAAB7617045833132A3538C85EEE65F3AFEEDF347F829A7B8D83FB`; live emoji pattern/rewrite + YAML rule/fix + durable replay GREEN.

### Semantic edit
Implemented with Roslyn as a proposal-only C# semantic backend:
- `talvora_semantic_edit` is deliberately narrow and does not compete with PRIMARY/default `talvora_apply_patch`. Current capability is solution/project-aware symbol rename.
- `Microsoft.Build.Locator` registers a deterministic compatible MSBuild instance before `MSBuildWorkspace` creation. Each request uses a disposable workspace with `SkipUnrecognizedProjects=false` and project references loaded as projects.
- Workspace failures are captured through current `RegisterWorkspaceFailedHandler` plus workspace diagnostics and reject before mutation. Diagnostics are bounded without hiding the total failure count.
- The revisioned physical C# anchor is resolved through Roslyn documents, `SemanticModel` and `SymbolFinder.FindSymbolAtPositionAsync`. Missing symbols and divergent linked/multi-project identities reject with zero mutation; optional project selection disambiguates.
- Rename uses current `Renamer.RenameSymbolAsync(..., SymbolRenameOptions, ...)` with `RenameFile=false`. Roslyn returns a changed in-memory `Solution`; Talvora never calls `Workspace.TryApplyChanges` and Roslyn never writes the live workspace.
- Only text-changing documents are reduced to exact zero-based UTF-16 `TextChange` proposals with `ExpectedText`. Linked physical files are deduplicated and contradictory proposals reject.
- Before accepting each proposal, Roslyn old text must exactly equal the revisioned Talvora disk snapshot. Encoding/BOM/newline remain owned by `SourceTextCodec`; Formatter/Simplifier are not automatically invoked, avoiding unrelated formatting churn.
- Semantic proposals enter `SourceEditEngine.ApplyGeneratedEditsAsync`, so durable receipt-first replay, SHA-256 revision checks, WAL, commit barrier, rollback/recovery and multi-document all-or-nothing semantics are inherited from the canonical transaction engine.
- Resource bounds cover projects, documents, changed documents, changed characters, diagnostics and timeout; caller cancellation remains cancellation with zero hidden partial success.
- Exact-installed evidence: 33/33 Source Edit/Semantic regression GREEN; raw `tools/list` 204/204 unique; canonical deploy/live snapshot `...-dirty-8726cde50619`; real two-project rename + same-transaction `replayed=true` GREEN.

## Conflict strategy

1. Exact/versioned apply first.
2. On mismatch, return a structured conflict; do not mutate.
3. Optionally calculate candidate context and/or three-way merge preview.
4. A three-way/fuzzy result is advisory unless it satisfies a fresh exact transaction.
5. Never silently choose among ambiguous candidate locations.

## Planning requirements for the next session

Before coding, produce a concrete implementation plan covering:

1. Exact external tool schemas.
2. Internal changeset/operation/revision/receipt data model.
3. Patch DSL grammar and parser strategy.
4. Transaction journal location, schema, retention and recovery.
5. Windows atomic commit algorithm and rollback behavior.
6. Workspace/source classification rules for legacy mutation guard.
7. Concurrency/idempotency semantics.
8. Encoding/BOM/newline preservation.
9. Symlink/reparse-point behavior.
10. Large-file limits and streaming strategy without arbitrary capability reduction.
11. File create/delete/move semantics.
12. Permission/read-only/ACL behavior.
13. Syntax validation architecture.
14. ast-grep integration decision.
15. Roslyn integration decision and phase boundary.
16. Git unified diff compatibility decision.
17. Diff/three-way merge library decision.
18. Stable error codes.
19. Telemetry/logging without leaking source content.
20. Targeted tests, failure injection and recovery tests.
21. Tool descriptions and routing contract.
22. Migration/compatibility strategy for existing 198-tool clients.
23. Installer/dependency implications.
24. Exact live deployment verification.

Only after the plan and TODO are updated should implementation begin.

## Verification philosophy

- Test only changed capability; do not rerun unrelated already-green suites.
- Source Edit Engine core requires unusually strong failure-path tests:
  - exact patch success
  - context not found
  - ambiguous context
  - stale revision
  - multi-file preflight failure -> zero mutations
  - mid-commit injected failure -> rollback/recovery
  - lost-response/idempotent retry
  - encoding/BOM/newline preservation
  - add/delete/move
  - legacy source write rejected
  - non-workspace legacy text mutation remains valid
  - syntax validation failure
  - large payload
  - concurrent writer race
- Build/deploy only after targeted source gates are GREEN.
- After deploy verify exact-installed tool discovery and live policy behavior.
- No commit/push until user explicitly requests it.

## Non-goals / guardrails

- Do not turn Git into a mandatory dependency for the canonical agent patch path.
- Do not make fuzzy matching an implicit mutation mode.
- Do not use regex replacement as the universal refactoring mechanism.
- Do not disable general shell/process capability merely to enforce preferred coding workflow.
- Do not add many thin alias tools.
- Do not overwrite or reset the existing dirty working tree.


## 2026-09-20 Research revalidation and locked implementation plan

Status: HISTORICAL PLAN — superseded by the implemented core, Routing Contract v3, structural adapter and Roslyn semantic adapter documented above.

### Revalidated technology decisions

| Technology / standard | Current finding | Talvora decision |
|---|---|---|
| Microsoft LSP WorkspaceEdit | Versioned TextDocumentEdit plus create/rename/delete resource operations and client-declared failure handling including transactional semantics remain the strongest public reference for ordered multi-file edits. | Adopt the conceptual model: ordered normalized changes, version/revision guards, non-overlapping text edits, preflight before mutation. Do not copy LSP transport/schema literally. |
| Microsoft Agent Host Protocol Changesets | Current Changesets 1.2 line provides capability/version evolution and action sequencing/reconciliation concepts. AHP changesets do not provide the per-file content revision contract Talvora needs. | Reuse sequencing/reconciliation ideas only. Talvora owns SHA-256 file revisions and transaction idempotency. |
| ast-grep | Active MIT project; current 0.45.x line exposes structural matches/replacements including JSON ranges/replacement text and is backed by Tree-sitter. Chocolatey currently has no ast-grep package. | Preferred future structural adapter. It must run in proposal/dry-run mode and feed proposed file states into the transaction core. Do not let ast-grep write the real workspace directly. Defer installer integration until the core is stable and a canonical latest-version provisioning route is selected. |
| Roslyn | Microsoft.CodeAnalysis.Workspaces.Common 5.9.0 is current on 2026-09-20, MIT, .NET 10 compatible. Renamer APIs return a changed Solution rather than requiring direct file writes. | Preferred C# semantic backend after the generic core. Roslyn produces changed document text; Talvora transaction engine performs commit/journal/recovery. |
| DiffPlex | NuGet 1.9.0, Apache-2.0, includes three-way diff/merge support. | Preferred in-process candidate for advisory conflict/three-way preview if that surface is added. No preview result may auto-commit. Core does not depend on DiffPlex initially. |
| Tree-sitter | Active incremental parser; current stable packaging is 0.27.x and Chocolatey has 0.27.0. Syntax trees expose ERROR nodes; parser/tree concurrency needs explicit ownership/copying. | Do not add raw Tree-sitter to the core initially. Existing config parsers provide immediate validation; ast-grep already embeds Tree-sitter for structural work. Re-evaluate standalone grammar packaging only when polyglot validation justifies it. |
| Git unified diff / git apply | Mature compatibility format. git apply can check before writing and is atomic by default unless reject-style partial behavior is requested, but it has Git-specific context/index/3-way semantics and no Talvora receipt/journal/idempotency model. | Compatibility adapter only. Never the canonical transaction backend. No --reject or implicit 3-way auto-commit. |
| Windows TxF | Microsoft recommends alternatives and warns TxF may not remain available. | Do not use TxF. |
| ReplaceFileW / FileStream.Flush(true) | ReplaceFileW can atomically replace one file and create a backup while preserving/merging metadata, but REPLACEFILE_WRITE_THROUGH is documented unsupported. .NET Flush(true) flushes intermediate file buffers. | Same-volume staged files + durable WAL + ReplaceFileW/rename primitives + explicit flush + deterministic rollback/recovery. Multi-file atomicity is implemented as recoverable transaction semantics, not claimed as a nonexistent Windows multi-file atomic syscall. |

### Tool surface — phase 14 core + specialist adapters

The core keeps canonical read/patch/exact-range surfaces plus the read-only routing guide. Structural and semantic mutation remain specialist proposal adapters that feed the same transaction engine rather than competing with PRIMARY/default `talvora_apply_patch`.

#### talvora_read_source

Purpose: source-aware read handshake that returns the exact revision token required by mutation tools. This avoids making the agent remember a separate talvora_file_hash call.

Schema:

```text
talvora_read_source(
  path: string,
  startLine: int = 1,
  lineCount: int = 0,
  startCharacter: int = 0,
  maxCharacters: int = 1048576
) -> {
  path,
  workspaceRoot,
  classification,
  length,
  revision,              // "sha256:<lower-hex>"
  encoding,              // utf-8, utf-8-bom, utf-16le-bom, utf-16be-bom, utf-32le-bom, utf-32be-bom
  newline,               // crlf, lf, cr, mixed, none
  hasFinalNewline,
  startLine,
  linesRead,
  endReached,
  text,
  startCharacter,
  nextStartLine,
  nextStartCharacter,
  responseLimited,
  responseLimitCharacters
}
```

For source editing, this is the canonical read tool. Whole-file SHA-256 plus encoding/newline/final-newline metadata are computed with bounded streaming buffers. The returned text window is bounded to 1 Mi UTF-16 characters by default and 4 Mi characters absolute; `lineCount=0` means continue toward EOF inside that response budget, not unbounded materialization. When `endReached=false`, continue from `nextStartLine/nextStartCharacter`. Existing talvora_read_text/read_text_range remain general-purpose compatibility reads.

#### talvora_apply_patch

```text
talvora_apply_patch(
  workspaceRoot: string,
  transactionId: string,       // required, 1..128 safe opaque characters; UUID recommended
  patch: string,
  inputFormat: "talvora" | "unified-diff" = "talvora",
  expectedRevisions?: map<string,string>, // unified-diff existing-file handshake
  validateSyntax: bool = true
) -> SourceEditTransactionResult
```

Talvora Patch DSL is the primary agent input. Git/traditional unified diff is implemented as a compatibility input on this same `talvora_apply_patch` tool and normalizes into the identical transaction/WAL core. Existing-file unified-diff paths require `expectedRevisions` from `talvora_read_source`. Unsupported external-diff syntax must stay on `talvora_apply_patch` by routing to the Talvora DSL rather than sending the agent to another editor.

#### talvora_apply_edits

```text
talvora_apply_edits(
  workspaceRoot: string,
  transactionId: string,
  changes: SourceEditChangeInput[],
  validateSyntax: bool = true
) -> SourceEditTransactionResult
```

This is a specialist surface, not the default alternative to patching. Choose it only when the caller already has exact zero-based UTF-16 range coordinates/revisions (for example a generated transformation). Several files changing together does not make `talvora_apply_edits` preferable to `talvora_apply_patch`.

#### talvora_semantic_edit

Current capability is C# solution/project-aware symbol rename only. It is selected only when semantic identity is required; ordinary C# edits remain on PRIMARY/default `talvora_apply_patch`, repetitive AST work remains on `talvora_structural_edit`, and exact generated ranges remain on `talvora_apply_edits`.

The request carries an explicit solution/project, revisioned C# document, zero-based UTF-16 anchor, transactionId and new name. Talvora registers `Microsoft.Build.Locator` before creating a disposable `MSBuildWorkspace`, captures current workspace diagnostics, verifies the Roslyn source text against the revisioned disk snapshot, resolves the symbol across relevant project contexts, and rejects missing or ambiguous semantic identity before mutation.

Roslyn produces only an in-memory changed `Solution`. Actual changed documents are reduced to exact UTF-16 `TextChange` ranges with expected text and SHA-256 revisions, then committed through `SourceEditEngine.ApplyGeneratedEditsAsync`. No Roslyn workspace write is used. Formatter/Simplifier are not run automatically, so encoding/BOM/newline and unrelated formatting remain under the existing Source Edit codec/transaction rules.

Resolved dependency evidence: Roslyn C# Workspaces/MSBuild Workspaces `5.9.0`, `Microsoft.Build.Locator 1.11.2`, and compile-only/private `Microsoft.Build.Framework 17.14.28` with runtime assets excluded. The live semantic receipt recorded the actual selected MSBuild runtime as `C:\Program Files\dotnet\sdk\10.0.401`.

`SourceEditChangeInput`:

```text
{
  operation: "update" | "add" | "delete" | "move",
  path: string,                        // workspace-relative
  destinationPath?: string,            // move only
  expectedRevision?: string,           // required for existing source: update/delete/move
  destinationExpectedRevision?: string,// only when explicitly replacing an existing move target
  content?: string,                    // add only
  encoding?: string,                   // add only; default utf-8
  newline?: "preserve"|"crlf"|"lf"|"cr"|"auto",
  edits?: [{
    startLine: int,                    // zero-based
    startCharacter: int,               // zero-based UTF-16 code units
    endLine: int,
    endCharacter: int,
    newText: string,
    expectedText?: string
  }]
}
```

Update ranges are non-overlapping and refer to the original pre-edit document, matching LSP-style deterministic semantics. They are applied from descending offsets after validation.

### Talvora Patch DSL v1

Grammar is deliberately small and familiar to coding agents while adding mandatory stale-read protection.

```text
*** Begin Talvora Patch
*** Update File: relative/path.cs
*** Revision: sha256:<64 hex>
@@ optional diagnostic label
 exact context
-old line
+new line
 exact context
*** Move to: optional/new/path.cs

*** Add File: relative/new.json
*** Encoding: utf-8
*** Newline: auto
+{
+  "enabled": true
+}

*** Delete File: relative/old.cs
*** Revision: sha256:<64 hex>
*** End Talvora Patch
```

Rules:
- Existing-file blocks (Update/Delete and Move source) require `*** Revision:`.
- Update supports multiple hunks and optional `Move to`.
- Hunk prefixes are exactly space/context, minus/delete, plus/add.
- Matching is exact by logical line content. Newline representation is not treated as fuzzy text; the target document's local/dominant newline policy is preserved.
- Every hunk old-side sequence must match exactly once in the current in-memory file state. Zero matches -> PATCH_CONTEXT_NOT_FOUND; >1 -> PATCH_CONTEXT_AMBIGUOUS.
- Hunk labels are diagnostic only and never grant mutation authority.
- No whitespace normalization, regex fallback, Levenshtein threshold, or hidden fuzzy application.
- Add-file content uses plus-prefixed lines. Default encoding is UTF-8 without BOM; auto newline is CRLF on Windows unless an explicit mode is supplied.
- Update preserves original encoding/BOM and final-newline state unless the exact requested edit changes end-of-file content.
- Paths are workspace-relative. Absolute paths, parent traversal, ADS/device paths and reparse-point escapes are rejected.

### Normalized internal model

External syntax is converted before any filesystem mutation:

```text
SourceEditChangeSet
  schemaVersion
  transactionId
  workspaceRoot
  requestHash
  inputKind
  validateSyntax
  files[] -> SourceEditFileChange
      operation
      sourcePath?
      destinationPath?
      expectedRevision?
      destinationExpectedRevision?
      beforeSnapshot?
      proposedDocument?
      diagnostics[]
```

`SourceFileSnapshot` records:
- canonical path and workspace-relative path
- existence/type
- SHA-256 revision
- byte length
- file attributes
- encoding/BOM
- newline classification + final newline
- last-write timestamp for diagnostics only

SHA-256, not timestamp, is the concurrency authority.

Adapters must produce this model:
- Talvora Patch DSL parser
- structured apply_edits
- unified-diff compatibility importer
- ast-grep proposal adapter
- Roslyn changed-Solution adapter

### Workspace and source classification

`SourceWorkspaceClassifier` is shared by the source tools and legacy mutation policy.

Workspace recognition:
1. canonical path containment;
2. nearest ancestor with strong development markers such as .git, .sln/.slnx, project files, package.json, pyproject.toml, Cargo.toml, go.mod, pom.xml, Gradle, CMake, pubspec.yaml, Docker/Compose markers;
3. reuse the existing project/workspace marker knowledge instead of a separate extension blacklist;
4. generated/cache directories (.git, bin, obj, node_modules, .gradle, build, dist, out, coverage, .vs, .idea, etc.) are classified separately.

Source/text classification combines:
- workspace containment;
- known source/config/doc filenames/extensions;
- existing-file byte sniffing (BOM, strict UTF-8/UTF-16 support, NUL/binary detection);
- generated/cache location classification.

A plain file outside a recognized development workspace remains compatible with legacy text tools. Temp directories without development markers remain compatible.

Reparse behavior:
- canonical source transactions reject a target, workspace root, or traversed path component that is a symlink/junction/reparse point with SOURCE_EDIT_REPARSE_POINT_UNSUPPORTED.
- This is a transaction-integrity boundary, not a global capability restriction. General filesystem/process/PowerShell tools remain unrestricted explicit-admin paths.

### SourceMutationPolicy

Server-side guard includes:
- talvora_write_text
- talvora_replace_text
- talvora_append_text
- talvora_write_bytes when the payload is text-like
- typed JSON/dotenv/INI/XML/YAML/TOML set/delete mutators
- talvora_delete when the target is a guarded development-workspace source/text file
- talvora_move when a guarded source/text file is moved inside the same recognized development workspace

When target classification is `development-workspace + source/text`, reject before mutation with a message containing stable code `SOURCE_EDIT_POLICY_VIOLATION` and direct routing:
- ordinary source/config/repository-document/lifecycle change -> `talvora_apply_patch` PRIMARY/default
- exact range/revision coordinates already known/generated -> `talvora_apply_edits`
- unclear choice -> `talvora_source_edit_guide`

This is not a global filesystem restriction. Directory deletion/move, generated/binary housekeeping, ordinary non-workspace operations, and cross-workspace file moves remain available through the general filesystem tools. `talvora_copy` remains general-purpose. The guard only prevents an agent from silently operating outside revision/WAL/rollback guarantees for source-file lifecycle changes that belong to the same development workspace.

talvora_run_powershell and talvora_run_process remain fully capable. Their descriptions will state they are explicit administrative execution surfaces, not the normal source-edit mechanism.

### Transaction identity and idempotency

- `transactionId` is mandatory.
- Normalize the changeset and hash canonical JSON with SHA-256 to produce `requestHash`.
- Persistent state key uses a SHA-256 digest of transactionId, not raw transactionId as a path.
- Retry with same transactionId + same requestHash after commit returns the durable prior receipt with `replayed=true`; it never reapplies.
- Same transactionId + different requestHash/workspace returns `TRANSACTION_ID_REUSE_MISMATCH`.
- Rolled-back transactions are terminal; retry requires a new transactionId.
- Pending transaction found after crash enters recovery before any new source mutation.

### Durable journal and retention

Control state:
`%ProgramData%\Talvora\SourceEdit\transactions\<tx-key>\journal.ndjson`

Receipt:
`%ProgramData%\Talvora\SourceEdit\receipts\<tx-key>.json`

Journal is append-only WAL:
- monotonic sequence
- timestamp
- event type
- compact payload
- checksum of each record
- every record appended with FileOptions.WriteThrough and FileStream.Flush(true)

Key events:
`Prepared`, `CommitStepStarting`, `CommitStepApplied`, `RollingBack`, `RollbackStepApplied`, `Committed`, `RolledBack`, `RecoveryRequired`.

A torn final WAL record is ignored if checksum/JSON is incomplete; prior durable records remain authoritative.

Retention:
- committed receipts/journals: 30 days, capped by count/size with oldest completed entries removed first;
- rolled-back completed transactions: 7 days;
- unresolved RecoveryRequired state is never silently purged;
- transient same-volume stage/backup files are cleaned only after a durable terminal record.

### Same-volume staging and Windows commit algorithm

For every final path, stage the proposed bytes in the destination directory using a hidden sibling name derived from transaction key + operation index. Stages are therefore on the same volume as their final targets.

Before mutation:
1. parse/normalize all input;
2. classify all paths/workspace;
3. snapshot all existing sources/destinations;
4. compare caller expectedRevision to snapshot SHA-256;
5. apply edits entirely in memory;
6. validate syntax;
7. encode proposed bytes preserving required metadata;
8. write every stage with write-through and Flush(true);
9. append and flush `Prepared`;
10. re-hash/re-stat every affected live path as a commit barrier. Any difference -> conflict and zero committed mutations.

Per-path commit:
- update existing file: `ReplaceFileW(target, stage, backup, flags=0)`; never use unsupported REPLACEFILE_WRITE_THROUGH.
- add absent file: atomic same-volume rename/move stage -> target; destination must still be absent.
- delete: atomic rename target -> backup rather than destructive delete.
- pure same-volume move: rename source -> destination with source preserved as the object where possible.
- move+edit: preserve source metadata through the backup/replace sequence; cross-volume moves are rejected in strict core v1 with `SOURCE_EDIT_CROSS_VOLUME_MOVE_UNSUPPORTED` rather than silently degrading metadata/atomicity.

Critical race closure:
- update backup created by ReplaceFileW is hashed after replacement. If the displaced original does not equal the preflight revision, immediately restore that exact displaced file and treat the transaction as a concurrency conflict.
- delete/move backup/source object is likewise verified immediately after rename.
- any failure after a prior file was committed starts deterministic reverse-order rollback.
- if rollback fully succeeds, terminal status is RolledBack with no hidden partial success.
- if rollback cannot prove restoration, journal records RecoveryRequired and later source mutations are blocked for the affected workspace until recovery resolves it.

This does not claim a nonexistent global Windows multi-file atomic syscall. It provides preflight-zero-mutation on detectable stale state plus durable deterministic rollback/recovery for commit-time failures.

### Crash and lost-response recovery

At service startup and before every source transaction:
- scan non-terminal journals;
- inspect final paths, stage files, backups and recorded before/after hashes;
- infer whether each step is before-state or after-state;
- if all paths prove the planned after-state, finalize the committed receipt;
- otherwise roll back all applied/ambiguous steps to recorded before-state where backup evidence permits;
- if state cannot be proven, mark RecoveryRequired and do not guess.

A crash after filesystem commit but before MCP response is therefore safe: retrying the same transactionId either returns the reconstructed committed receipt or a deterministic recovery result, never applies the edit twice.

### Encoding, newline and metadata

Supported text encodings in core:
- UTF-8 without BOM
- UTF-8 BOM
- UTF-16 LE/BE with BOM
- UTF-32 LE/BE with BOM

BOM-less non-UTF-8 text returns `SOURCE_EDIT_UNSUPPORTED_ENCODING` rather than being rewritten through a lossy guess.

Update preserves encoding/BOM. Structured edits operate on decoded .NET UTF-16 strings. Patch hunks match logical line content exactly while preserving existing newline style outside changed lines. Mixed-newline files keep untouched line terminators; inserted lines use the matched local terminator, then dominant terminator, then platform default in that order.

Read-only targets fail preflight with `SOURCE_EDIT_READ_ONLY`; the core will not silently clear attributes. Full-capability administration can deliberately change attributes first through general tools. ReplaceFileW is used without IGNORE_ACL_ERRORS/IGNORE_MERGE_ERRORS so metadata/ACL merge failure is a transaction failure, not silent metadata loss.

### Large-file strategy

No arbitrary small product limit. Canonical reads stream whole-file SHA-256 and text metadata while materializing only the bounded response window. A text mutation necessarily materializes the affected decoded document in memory, so canonical mutation snapshots use a high 256 MiB hard byte ceiling and fail with `SOURCE_EDIT_RESOURCE_LIMIT` before an oversized allocation begins; revision-only snapshots remain streaming. Stage/revision I/O remains streamed where full decoded text is not required. Candidate/fuzzy diagnostics are never allowed to become mutation authority. Targeted regressions include multi-megabyte edit behavior plus large single-line paged reads.

### Syntax validation

Core `validateSyntax=true` validates formats for which Talvora already has maintained parsers:
- JSON -> System.Text.Json
- XML -> System.Xml.Linq
- YAML -> YamlDotNet
- TOML -> Tomlyn

Validation occurs against proposed in-memory content before staging/commit. A parser error returns `SOURCE_VALIDATION_FAILED` and zero mutations.

C#/polyglot validation is not faked with regex. Roslyn/Tree-sitter-backed validation is a later adapter. Receipt records `not-applicable` or `validator-unavailable` rather than claiming validation occurred.

### Stable result/error model

`SourceEditTransactionResult`:
- success
- transactionId
- status: committed | rejected | rolled-back | recovery-required
- replayed
- workspaceRoot
- requestHash
- files[]: operation, oldPath/newPath, beforeRevision, afterRevision, beforeBytes, afterBytes
- validation[]
- warnings[]
- error? { code, message, path?, details? }

Stable domain codes for core:
- SOURCE_EDIT_POLICY_VIOLATION
- SOURCE_EDIT_WORKSPACE_NOT_FOUND
- SOURCE_EDIT_PATH_OUTSIDE_WORKSPACE
- SOURCE_EDIT_REPARSE_POINT_UNSUPPORTED
- SOURCE_EDIT_UNSUPPORTED_ENCODING
- SOURCE_EDIT_READ_ONLY
- SOURCE_EDIT_CROSS_VOLUME_MOVE_UNSUPPORTED
- PATCH_PARSE_ERROR
- PATCH_CONTEXT_NOT_FOUND
- PATCH_CONTEXT_AMBIGUOUS
- PATCH_OVERLAPPING_EDITS
- EXPECTED_REVISION_MISMATCH
- SOURCE_EDIT_CONFLICT
- SOURCE_VALIDATION_FAILED
- SOURCE_EDIT_RESOURCE_LIMIT
- TRANSACTION_ID_INVALID
- TRANSACTION_ID_REUSE_MISMATCH
- TRANSACTION_ROLLED_BACK
- TRANSACTION_RECOVERY_REQUIRED
- SOURCE_EDIT_IO_FAILURE

A same-request committed retry is a successful replayed receipt, not an error.

### Validation/failure-injection matrix

Create a dedicated no-third-party console regression project referencing Talvora source so internal failure injection can be deterministic.

Mandatory scenarios:
1. exact patch success
2. missing context -> zero mutation
3. ambiguous context -> zero mutation
4. stale expected revision -> zero mutation
5. external concurrent modification detected at commit barrier
6. multi-file preflight failure -> zero mutation
7. injected failure after first commit step -> reverse rollback
8. crash-style pending journal recovery
9. committed-but-response-lost retry -> same receipt, no duplicate edit
10. transactionId reused with different request -> reject
11. add/delete/move
12. UTF-8 BOM, UTF-16 BOM, CRLF/LF/mixed newline preservation
13. read-only file rejection
14. reparse target rejection
15. legacy source write/replace/append/text-like write-bytes rejection
16. legacy non-workspace text mutation compatibility
17. JSON/YAML/TOML/XML syntax-validation failure
18. multi-megabyte text edit
19. overlapping structured range rejection
20. cancellation before commit -> zero mutation

### Implementation sequence

A. Core model + text codec + classifier + DSL parser + structured-edit normalizer.
B. Durable WAL/receipt store + idempotency + recovery + same-volume commit/rollback.
C. MCP tools: read_source, apply_patch, apply_edits.
D. SourceMutationPolicy wired into required legacy mutation tools; routing descriptions updated.
E. Dedicated targeted regression/failure-injection project.
F. Manifest and smoke required-tool contract update.
G. Release build and only affected regressions.
H. Canonical Build-Windows-Installer.ps1.
I. Canonical scripts\Deploy-Windows-Installer.ps1; preserve #109 explicit --silent.
J. Exact-installed talvora_system_info + live tools/list/source-edit/policy smoke.
K. Close #110 only after exact-installed evidence; structural and Roslyn adapters are separate specialist subphases and are now implemented/tested under #114 and #120.

### Adapter status / deliberate phase boundaries

- Unified diff compatibility: IMPLEMENTED inside `talvora_apply_patch`; it generates normalized proposed states and does not let git mutate the live workspace.
- `talvora_structural_edit`: IMPLEMENTED ast-grep proposal adapter; JSON replacements -> normalized changeset -> Talvora commit.
- `talvora_semantic_edit`: IMPLEMENTED Roslyn C# semantic proposal adapter; changed Solution documents -> normalized changeset -> Talvora commit.
- Three-way conflict preview: optional DiffPlex 1.9.0 adapter. Advisory only; fresh exact transaction required for mutation.

