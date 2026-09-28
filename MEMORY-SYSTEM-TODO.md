# Talvora Memory — Development TODO

## Product goal

Talvora should retain useful durable context across chats and development sessions without turning raw conversation history into permanent truth. Memory is a local, inspectable product capability. HANDOFF.md remains the short human-readable canonical project handoff; memory complements it.

## Design principles

- Local-first: canonical V1 storage is SQLite under %ProgramData%\Talvora\memory.
- Explicit before automatic: V1 exposes deliberate memory operations; automatic extraction/consolidation follows after retrieval quality is proven.
- Scoped: every item belongs to global, user, project, or session.
- Searchable: FTS5 is the V1 retrieval engine; semantic embeddings are a later hybrid layer.
- Bounded: retrieval always has finite item and character budgets.
- Auditable: memory records retain category, source, confidence, importance and timestamps.
- Correctable: update, expiry, supersession and forget paths are first-class.
- Repository/runtime truth outranks stale memory.

## Phase 1 — Memory V1 foundation

- [x] Create canonical memory architecture/TODO.
- [x] Verify current SQLite FTS5/WAL/transaction guidance with Context7 + vendor docs.
- [x] Create Gitea tracking issue.
- [x] Add SQLite memory store with lazy schema initialization.
- [x] Enable WAL, foreign keys and bounded busy timeout.
- [x] Add canonical memory_items table.
- [x] Add FTS5 external-content index and synchronization triggers.
- [x] Add scope/category validation.
- [x] Add expiry and supersession metadata.
- [x] Add talvora_memory_remember.
- [x] Add talvora_memory_search.
- [x] Add talvora_memory_get.
- [x] Add talvora_memory_update.
- [x] Add talvora_memory_forget.
- [x] Add talvora_memory_context with bounded context text.
- [x] Add tools to canonical manifest.
- [x] Expose memory tools on Full + Development surfaces, not Administration.
- [x] Add smoke coverage for CRUD, scope isolation, FTS, update, forget and expiry.
- [x] Add source regression for surface counts/manifest.
- [x] Release build 0 warnings / 0 errors.
- [x] Run targeted smoke/regressions.
- [x] Update architecture docs.
- [x] Update HANDOFF checkpoint after installed-runtime verification.
- [x] Commit and push Gitea + GitHub.
- [x] Deploy canonical installer because runtime/tool surface changes.
- [x] Verify installed service health + live MCP discovery.

## Phase 2 — Memory quality

- [x] Add contradiction/supersession workflow.
- [x] Add duplicate detection and consolidation.
- [x] Add source authority ordering.
- [x] Add retention classes and automatic expiry policies.
- [x] Add stale-memory suppression when repository/runtime evidence disagrees.
- [x] Add project identity normalization.
- [ ] Add session close summary -> candidate-memory pipeline.
- [ ] Add promotion rules so transient session facts do not become durable by default.
- [x] Add memory health/integrity diagnostics.

## Phase 3 — Automatic learning

- [ ] Observe completed tool operations and extract candidate lessons.
- [ ] Require confidence/importance thresholds before durable persistence.
- [ ] Coalesce repeated equivalent lessons.
- [ ] Record provenance without storing secrets/raw credentials.
- [ ] Add automatic error -> solution memory after verified successful recovery.
- [ ] Add decision memory from explicit user/project decisions.
- [ ] Add opt-out/suppression scopes.

## Phase 4 — Hybrid semantic retrieval

- [ ] Select embedding strategy after benchmark.
- [ ] Add embedding schema/version metadata.
- [ ] Add vector index or compact vector store.
- [ ] Hybrid rank: lexical + semantic + importance + recency + scope.
- [ ] Re-embedding/migration workflow.
- [ ] Benchmark Turkish + English retrieval.
- [ ] Keep FTS5 as deterministic fallback.

## Phase 5 — Control Center

- [ ] Memory inspector page.
- [ ] Search/filter by project, scope, category and date.
- [ ] View source/provenance/confidence.
- [ ] Edit/expire/forget/supersede actions.
- [ ] Memory DB size/index health.
- [ ] Backup/export/import.
- [ ] Privacy-safe diagnostic view.

## Phase 6 — Handoff integration

- [ ] Generate handoff candidates from high-value project memories.
- [ ] Never silently rewrite HANDOFF.md.
- [ ] Detect stale handoff facts against live repository/runtime state.
- [ ] Explicit handoff refresh during project closeout workflows.

## Acceptance criteria for V1

1. Memory survives service restart.
2. Project-scoped search never leaks unrelated projects when project is supplied.
3. Expired records are excluded from normal search/context.
4. Forget removes canonical row and FTS result.
5. Update is reflected in FTS immediately.
6. Context obeys item and character budgets.
7. Talvora does not auto-capture raw chat or credentials in V1.
8. Full/Development expose memory tools; Administration remains unchanged.
9. Release build is warning/error free and targeted smoke is green.

