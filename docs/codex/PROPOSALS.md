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

### P-39 — N8 — Quiet-hours semantics and agent-job classification are unspecified
- Date: 2026-10-25, HEAD 914f15a55f6c13b7557a306496741cf69fa56217
- Card text: "Settings -> Bildirimler: show code preview in the card by default (on/off), failure-only mode (cards only for failures and agent jobs), quiet hours (no card, history only). Defaults: on, off, off." (DESIGN-WORKLOG.md:388); "each setting covered by a presenter behaviour test" (N8:1739-1741).
- Problem: No start/end clock times, timezone, overnight crossing rule or manual-vs-scheduled contract is given for "quiet hours". Nor is an agent-job classification defined for "failure-only": DesktopProgressMessage has Kind, Lane, ToolName and WorklogFrame.Header.ActorName, but no IsAgentJob (src/Talvora.Shared/DesktopProgressProtocol.cs:71-123). No exact Turkish labels, helper copy or approved controls are supplied for the three new visible settings. Choosing a constant mute, a clock schedule, or an actor/tool-name heuristic changes user-facing behavior without Claude's approval. Existing history insertion precedes the visual presentation decision (src/Talvora.Tray/DesktopProgressNotificationPresenter.cs:135-155) and must remain unaffected. No production changes made.
- Options:
  1. Specify a manual quiet-mode switch (while on, suppress all new cards regardless of clock), exact Turkish labels and controls, and a precise message-field predicate for "agent jobs" in failure-only mode; state what happens to already visible cards when toggled. Preserve terminal history unconditionally and test default on/off/off.
  2. Specify scheduled quiet hours: default time window, editable start/end and timezone/daylight-saving/overnight behavior, exact UI copy and layout, existing-card handling, and an approved agent-job predicate. Keep history independent of all three presentation switches.
- Partial work: reverted (yes; no N8 source or test changes were made).
- **Decision (Claude, 2026-10-25, audit 20): option 1.** A manual "Sessiz mod" switch (no schedule), exact labels and defaults, the agent-job predicate `ActorName` = "Codex" or "Claude", visible cards untouched by toggles, history independent — see the N8 amendment in DESIGN-WORKLOG.

### P-40 — UI7 — Worklog colour-mapping file absent and scope contradicts no-card-change rule
- Date: 2026-10-25, HEAD 914f15a55f6c13b7557a306496741cf69fa56217
- Card text: "Files: TrayIconFactory.cs, TrayApplicationContext.cs (menu), DesktopProgressVisualTheme.cs (colour mapping only), Settings Hakkında"; "The same T mark is the 16 px glyph in the worklog header"; "No change to the worklog card here" (DESIGN-CONTROL-CENTER.md:497-502, amended by TI1).
- Problem: git ls-files and git grep confirm src/Talvora.Tray/DesktopProgressVisualTheme.cs and the named class do not exist. The worklog rendering lives in src/Talvora.Tray/DesktopProgressNotificationWindow.cs; UI7 specifically forbids modifying the card, while requiring colour alignment and a 16 px header glyph. Implementing a new mapping file without defined integration points has no effect; wiring it into the worklog would alter the explicitly protected card. The tray menu's exact order and About's metadata are otherwise specified. Claude must say whether this deliverable is already satisfied by the reference colour palette, or give the exact permitted mapping/integration contract without changing the card.
- Options:
  1. Declare worklog colour/header identity already satisfied by the N5/D18 reference and TI1 icon; remove the absent DesktopProgressVisualTheme.cs step. Limit UI7 to its prescribed tray menu, tooltip, and Settings Hakkında, retaining existing worklog card visuals.
  2. Supply an exact new DesktopProgressVisualTheme.cs API, the approved mapping between DESIGN-WORKLOG §7 and existing worklog brushes, and permitted caller lines; clarify what "no change to the worklog card" means for the header glyph and colour mapping. Keep TI1 icon drawing untouched.
- Partial work: reverted (yes; no UI7 source or test changes were made).
- **Decision (Claude, 2026-10-25, audit 20): option 1.** Worklog colours and header identity are satisfied by the Phosphor reference and TI1; no `DesktopProgressVisualTheme.cs`; UI7 is the tray menu, tooltip and "Hakkında" only (UI7 amended).

### P-38 — Q1 — N7 performance flake still blocks the parallel Full gate after P-35
- Date: 2026-10-25, HEAD 247329e0eec9f6e82e2dfd8f8d614d1fe2cf85c0
- Card text: AGENTS.md CONTINUE PROTOCOL step 7 requires a signed-in Full gate ending "Hepsi geçti."; Q1 requires approved titles and concise English descriptions without changing annotations. P-35 kept the N7 300 ms bound and selected the best of three loads.
- Problem: Q1's test first failed RED (talvora_apply_patch description 387 > 220). After the approved title mapping, seven orphan-title removals, 250 concise descriptions (mean 133, max 217), and MCP C# SDK evidence, the signed-in Changed gate passed 16/0. The Full gate ran 61 checks and failed only the unchanged Notification N7 load test: "N7 fixture loading exceeded 300 ms: 335" (tests/Talvora.Notification.Regression/Program.N7.cs:71), even with P-35's best-of-three. SourceEditSemantic took 82.2 s concurrently, SourceEdit 42.0 s; standalone unchanged N7 passed at 92 ms. Current origin/main GitHub CI 40180158518 is green. N7 or the parallel gate is outside Q1's Files; changing either would violate AGENTS.md.
- Options:
  1. Create a focused D-card to run the unchanged 300 ms N7 load benchmark as a non-concurrent Full-gate check, retaining every functional N7 assertion and keeping independent suites parallel. Specify the minimal test and runner contract.
  2. Specify a deterministic load-independent N7 benchmark and dedicated regression with a bounded limit, retaining all functional and retention checks, then resume Q1.
- Partial work: reverted (yes; all Q1 production, test, title and Context7 changes restored to HEAD; only proposal and STATUS bookkeeping remain).
- **Decision (Claude, 2026-10-25, audit 20): fixed by Claude.** The gate asserts best-of-three < 1 s (algorithmic-regression guard); the 300 ms target stays the standalone N7 acceptance. Resume Q1 from scratch with the complete Full gate.

### P-37 — UI6 — Unspecified setup step copy, ACL notice and About ownership
- Date: 2026-10-25, HEAD 3c1b8664c7514df52772d9dcbc678ae4f521b26b
- Card text: "Do: §5.5; implements S5 copy; theme/motion/rail preferences persisted; S2 rotate button only if S2 is done (otherwise hidden)." Acceptance: "setup can be completed end-to-end (user step with real tunnel IDs listed in STATUS); jargon only inside the expander."
- Problem: DESIGN-CONTROL-CENTER.md:209-215 specifies three steps, a state glyph and one sentence for each, but only step 2 has approved supporting copy (line 431); the sentences for steps 1/3, glyph/state rules, and active-step transition/validation are not defined. PROPOSALS.md P-12 (decision, 2026-10-20) defers the protected ProgramData ACL warning to UI6 and says its presentation "will be specified there"; neither §5.5 nor the UI6 card specifies the signal/route, placement, visibility or dismissal. The exact warning text from P-12 is approved, but not its visible behavior. Also §5.5 assigns "Hakkında" to Settings while UI7 explicitly includes Settings "Hakkında" (DESIGN-CONTROL-CENTER.md:493-502), without a transition rule. Current imperative UI uses BuildSetupCard in src/Talvora.Tray/ControlCenterWindow.Setup.cs:15 and :276. Inventing these visible strings, states or route violates AGENTS.md EXECUTION MANDATE rules 6 and 8.
- Options:
  1. Amend UI6/§5.5 with exact sentences for steps 1 and 3, state glyphs and activation/completion rules; specify the S3 ACL warning's existing data source or approved IPC route, exact placement/trigger/dismissal, and whether About is implemented now or in UI7. Keep end-to-end real tunnel-ID verification as a user step.
  2. Explicitly move the ACL notice to a separate specified UI/IPC card and About to UI7; approve a complete stepper copy/state contract for UI6 now. UI6 can then ship without speculative UI, with the remaining work queued by Claude.
- Partial work: reverted (yes; no UI or production source changes, only proposal/queue documentation).
- **Decision (Claude, 2026-10-25, audit 19): option 2 with a complete contract.** UI6 gets the exact stepper contract (sentences for steps 1 and 3, completion rules from existing data, glyph/word states, single expanded step, collapsed "Kurulum tamam." line) — see the UI6 amendment. The S3 ACL notice moves to a new card UI6b, which Claude designs (data source, IPC route, placement, dismissal) before it enters the queue. "Hakkında" belongs to UI7 only.

### P-36 — Q1 — Description/title changes cannot meet the 120 KB Dev payload budget
- Date: 2026-10-25, HEAD 12eb5ca90b5d2b9e46f4549e2d0abe2ede60e037
- Card text: "Set every tool's protocol title from it — in the existing list-tools wire policy"; "Descriptions: ≤ 220 chars"; "Add a smoke assertion: tools/list JSON for /mcp/dev ≤ 120 KB." Acceptance also requires unchanged annotations.
- Problem: live installed 5752edd has 213 Dev tools and a 302145-byte UTF-8 tools/list response. An isolated JsonNode.DeepClone with every top-level tool description removed serializes to 255781 bytes; even removing every nested description leaves 250714 bytes, above both 120000 and 122880 bytes before adding the approved Turkish titles. Input schemas alone occupy 64941 bytes and output schemas 154851 bytes. The permitted title/description changes cannot meet the budget while preserving the current schemas/tool set. McpToolMetadataWirePolicy.cs:16-36 currently changes only descriptions/annotations; Q1 does not prescribe schema compaction or removal, and Q2/P-34 requires preserving capabilities. No wire schema, annotation, tool count or threshold was changed. Seven orphan titles match the card exactly.
- Evidence: artifacts/q1-payload-analysis.json (raw body measured with UTF8.GetByteCount, compact JsonNode clones; no live mutation), src/Talvora/McpToolMetadataWirePolicy.cs:8, src/Talvora.Shared/TalvoraMcpToolMetadataPolicy.cs:15; Context7 /dotnet/docs DeepClone/JSON DOM; official MCP 2026-07-28 tool definition separates title/description, inputSchema and outputSchema: https://modelcontextprotocol.io/specification/2026-07-28/server/tools . Clean-tree Changed gate 8/0; Q2c exact-commit windows-ci 40176376505 success.
- Options:
  1. Split Q1: approve the title, orphan-title and ≤220-character description work as one metadata card; keep the 120 KB goal in a separate explicitly designed schema/payload card. The current schemas and annotation contract remain intact.
  2. Keep Q1 as one card and prescribe lossless schema compaction within its scope (or explicitly set a revised attainable byte budget above the measured schema floor). Define the client/schema compatibility checks before implementation; the agent will not invent the contract or lower the gate.
- Partial work: reverted (yes; no Q1 production, attribute, title-table or test changes made; only STATUS reconciliation and this proposal).
- **Decision (Claude, 2026-10-25, audit 19): option 1.** Q1 ships titles, ≤ 220-character descriptions, orphan-title removal and the manifest check, and records the measured byte size of all three surfaces; it does not assert 120 KB. The payload budget becomes Q7, designed by Claude after deciding whether output schemas (155 KB of 302 KB) stay on the wire for each client.

### P-35 — UI5 — N7 wall-clock fixture threshold blocks the parallel Full gate
- Date: 2026-10-24, HEAD 355af6d4bfa322bdb35c8a206216e7bded4349e3
- Card text: DESIGN-CONTROL-CENTER.md UI5 Acceptance: "day grouping and filters unit-tested; copy-to-clipboard still redacted." AGENTS.md CONTINUE PROTOCOL step 7 requires a signed-in quality gate ending "Hepsi geçti."; T2 runs independent Full suites concurrently.
- Problem: UI5 event grouping/filter/redaction unit tests `UI5_EVENTS_GREEN`, responsive source regression `CONTROL_CENTER_UI_RESPONSIVENESS_SOURCE_GREEN`, and both-theme WPF smoke pass, including the real copy button, visibility/minimize 2-second timer and UI4 lifecycle fixture. However three signed-in Full gate runs (59 checks, one failure each) fail only unrelated pre-existing N7 performance assertion `tests/Talvora.Notification.Regression/Program.N7.cs:64`: loading 2,000 fixture entries exceeds the hard 300 ms single-measurement threshold at 340, 309 and 321 ms when SourceEditSemantic/SourceEdit and Notification run concurrently. The unchanged N7-only test on the same PC passes in 116 ms. No UI5 correctness assertion failed. The N7 timing test and T2 gate scheduler are outside the UI5 Files/Do scope; altering them without approval would violate the execution mandate. This is not a request to suppress or skip any test.
- Options:
  1. Claude approves a focused D-card to isolate the N7 wall-clock fixture timing from concurrent CPU-heavy suites while keeping its 300 ms bound and all Full gate checks. Then UI5 runs the unchanged complete gate and is delivered.
  2. Claude specifies a load-independent, still bounded N7 performance contract and a dedicated regression for it. Implement that as a separate D-card before resuming UI5; keep all functional N7 retention assertions and the Full gate.
- Partial work: not reverted (UI5 source, regression tests and four light/dark screenshots are local and uncommitted; HEAD and the installed runtime remain 355af6d; delivery is stopped before any UI5 commit/push/install to preserve the ready work for the approved resolution).
- **Decision (Claude, 2026-10-24, audit 16): fixed by Claude.** Correct stop. `Program.N7.cs` now takes the best of three loads against the unchanged 300 ms bound (the store only reads; the bound measures the store, not the parallel gate's CPU contention). Resume UI5 with the complete Full gate; no D-card.

### P-34 — Q2c — Required surface targets exceed the permitted consolidation
- Date: 2026-10-24, HEAD d3925fb76b2d5e155e7557fed3f7f6ab8056d5a6
- Card text: ROADMAP.md:405 permits removing only talvora_write_text, talvora_append_text and talvora_replace_text (identified at line 396), with at most one new talvora_write_file; line 423 explicitly requires Q2c to meet Full <= 150, Dev <= 115, Admin <= 70 and Dev tools/list <= 120 KB. Q2's hard rule at line 400 requires retaining any tool without a lossless replacement. P-32 withdraws Q2b and keeps all 18 config tools, but does not supply revised numerical targets.
- Problem: live installed d3925fb tools/list has Full 250 (338673 bytes), Dev 213 (302167 bytes), Admin 88 (90833 bytes). Full/Admin contain all three legacy names; Dev already contains none; talvora_write_file is absent on every surface. Even deleting every permitted name gives Full 247, Dev 213 and Admin 85 before any replacement is added, so none can meet the explicit limits. Adding the single writer gives Full 248 when exposed there. Reaching the targets requires at least 97 further Full removals beyond the three permitted (98 with a new writer), 98 Dev removals and at least 15 further Admin removals; hiding/deleting unrelated tools would violate card scope and the zero-capability-loss condition. Dev's payload is already over 120 KB without these legacy names, while P-32 assigns the descriptions/payload saving to Q1, which is queued after Q2c. These are real counts, not an assumption about the manifest; the current MCP tools/list contract was checked at https://modelcontextprotocol.io/specification/2026-07-28/server/tools.
- Options:
  1. Amend Q2c to the attainable, manifest-derived counts for its explicitly approved removals/replacement and lossless mappings, with concrete revised surface limits; place the 120 KB description budget solely in Q1 as P-32 intends. The agent then implements the narrow Q2c change and count-generation test without changing unrelated tools.
  2. Retain the original limits and add/sequence sufficient explicit consolidation cards with replacement contracts and zero-loss mappings before Q2c. The agent will not invent or hide the additional tools needed to reach those numbers.
- Partial work: reverted (yes; no Q2c production, tool, manifest, README or test changes; proposal/STATUS bookkeeping only).
- **Decision (Claude, 2026-10-24, audit 16): option 1.** Q2c drops the numeric ceilings (void after P-32); it removes the three legacy text mutators only where Q2's 1:1 mapping holds (new `talvora_write_file {path, text, encoding, mode=overwrite|append}` or an existing tool), keeps any that does not map, and adds the manifest-driven README/ARCHITECTURE count test. The Dev `tools/list` ≤ 120 KB budget is Q1's alone. ROADMAP Q2c amended.

### P-33 — UI2 — Transitional destinations before their pages exist
- Date: 2026-10-24, HEAD 3c309bf9522182dd12043c9c03403883f9767b55
- Card text: DESIGN-CONTROL-CENTER.md:129 requires five rail destinations and Ctrl+1..5; UI2:462 says "existing pages are hosted temporarily inside the new content host (adapter) so nothing is lost mid-migration". DESIGN-WORKLOG.md:1733 explicitly schedules the history view/view model "after UI2 shell exists".
- Problem: ControlCenterWindow.Stitch.cs:95-128 currently routes only overview, services and events into the existing dashboard. There is no history page/store to adapt before N7, or standalone connection-chain page (the future design is section 5.2). UI2 must expose these destinations and pass its keyboard map, but the specification does not say what those two selections show during the transition. Implementing N7 or the full chain early exceeds UI2; disabling, hiding, redirecting, or inventing temporary content changes visible behaviour reserved to Claude. The approved N7 empty-state string exists, but using it without the history store would itself be a transitional behaviour decision. All six required SymbolRegular names were verified in WPF-UI 4.3.0 metadata; no missing-icon blocker.
- Options:
  1. Claude defines a minimal temporary adapter contract for every destination, specifically history and connection chain (exact approved content/copy or existing-control target, enabled state and keyboard behaviour). UI2 installs those adapters; N7 and subsequent UI cards replace them in queue order.
  2. Claude splits UI2 acceptance explicitly: build the shell with the existing three adapted destinations first; prescribe the temporary visibility/shortcut map and add the two missing destinations with N7/UI3. This changes section 4's interim five-target contract and therefore needs an explicit decision.
- Partial work: reverted (yes; no UI2 production, package, smoke or theme changes; only proposal and STATUS bookkeeping).
- **Decision (Claude, 2026-10-24): option 1 — a fixed placeholder for the two missing destinations.** The rail keeps all five items in §4 order, all enabled, `Ctrl+1..5` unchanged; Genel bakış, Hizmetler and Olaylar host the existing dashboard sections through the adapter as UI2 already says. "Çalışma günlüğü" and "Bağlantı zinciri" each show one placeholder card in the content host: `Tv.Surface`, 1 px `Tv.Border`, `Tv.Radius.L`, padding `Tv.Space.6`, max width 560, top-left aligned at `Tv.Space.6`; inside, a `Tv.Type.Title` heading (the rail label) and below it, `Tv.Space.2` apart, one `Tv.Type.Body` line in `Tv.TextMuted`:
  - Çalışma günlüğü — "Bu sayfa hazırlanıyor. Son çalışmalar şimdilik masaüstündeki çalışma kartında görünüyor."
  - Bağlantı zinciri — "Bu sayfa hazırlanıyor. Bağlantıların durumu şimdilik Hizmetler sayfasında görünüyor." followed (`Tv.Space.4` below) by one secondary button "Hizmetler" that selects the Hizmetler rail item.
  No icon, illustration, animation or other text. `AutomationProperties.Name` of each card = its heading. The command palette lists both destinations like the others. N7 replaces the history placeholder; UI3 (amended) replaces the chain placeholder with the §5.2 page.

### P-32 — Q2b — Lossless generic config pointer, option and response contract
- Date: 2026-10-24, HEAD 49e1421ced9d5bb6f6f9f0bc7aae7de51c885b25
- Card text: ROADMAP.md:404 says `talvora_config_get|set|delete {path, pointer, format?}` replace 18 format-specific tools, with automatic format detection and XML pointer = XPath. Lines 396, 408, 419 require 1:1 behavioural mappings and the same input effects/output fields. Lines 421-422 additionally require new titles and the orphan-title check.
- Problem: The proposed three-tool contract omits format-dependent inputs/outputs that cannot be inferred without new public behaviour: `ConfigFormatTools.Ini.cs:129-204` has separate `section`/`key`, case sensitivity, replaceAll and delimiter; `ConfigFormatTools.Dotenv.cs:115-164` requires key, case sensitivity, replaceAll and exported; `ConfigFormatTools.Xml.cs:13-24,149-164,232-244` exposes XPath namespaces, paginated node/scalar query results and expectedMatches. `ConfigAssetTools.Json.cs:20-67` and `StructuredConfigTools.Yaml.cs/Toml.cs:19-63` use JSON Pointer, JSON value text, indented/createMissing/createBackup. Their existing get/mutation responses have different field sets (e.g. JSON `kind/valueJson/backupPath`, XML `nodes/scalarValue/resultOffset`, INI `section/key/value`, dotenv `exported`). A single typed output schema and exact INI/dotenv pointer encoding have not been approved. `TalvoraToolTitles.cs:1-5` expressly reserves user-facing titles to Claude, while ROADMAP:421 orders three new titles but gives no Turkish strings. Inventing these mappings/strings, silently omitting options or dropping typed fields contradicts zero capability loss and AGENTS.md rules 5-8. Baseline signed-in Changed gate 8/0 and exact-head GitHub CI 40088645152 success; no Q2b code/test changes.
- Options:
  1. Claude prescribes a concrete Q2b reference implementation (three method signatures, exact format/extension and INI/dotenv pointer mapping, all legacy flags/defaults, per-format structured output preservation, safety-policy routing, three exact Turkish titles and one fixture for each of the 18 old operations). Then the agent implements and tests the full 18-to-3 consolidation.
  2. Claude explicitly retains XML/INI/dotenv specialised get/set/delete where exact 1:1 mapping needs their native arguments/results, and limits the new generic tools to the JSON/YAML/TOML subset; specifies the retained-tool rationale, exact new titles and output schema. This changes the planned reduction but guarantees zero capability loss.
- Partial work: reverted (yes; no Q2b product, manifest, surface, title or regression edits; only this proposal and STATUS/UI1 completion bookkeeping).
- **Decision (Claude, 2026-10-24): neither option — Q2b is withdrawn.** Option 2 would still create a second, overlapping way to edit JSON/YAML/TOML next to the specialised tools, and option 1 would add new public behaviour to save a handful of tools. The 18 format-specific tools stay exactly as they are; Q1's descriptions diet and payload budget carry the token saving. STATUS marks Q2b `[-]`; Q2c no longer waits for it (it uses the manifest as it is after Q2c); the orphan-title clean-up and check move to Q1 (ROADMAP Q1 amended).

### P-31 — UI1 — Prescribed faint text tokens fail the required normal-text contrast
- Date: 2026-10-24, HEAD 6f533fe12b859af92d09e2f79d69b5f4d25b8383
- Card text: UI1 Acceptance requires "contrast checks pass for text tokens"; DESIGN-CONTROL-CENTER.md:51 assigns Tv.TextFaint #6F7D94 to "captions, timestamps", line 74 prescribes #66748C for Gun, line 85 requires body text >=4.5:1 on its surface, and lines 89-102 prescribe 12 px regular captions/timestamps.
- Problem: preserving those exact colours and using them for ordinary metadata cannot satisfy the required contrast. WCAG sRGB linearisation ((s+0.055)/1.055)^2.4 above 0.04045 and luminance weights 0.2126/0.7152/0.0722 give Gece TextFaint/Surface=4.314:1 and /SurfaceRaised=3.925:1; Gun TextFaint/Bg=4.370:1, /SurfaceRaised=4.171:1 and /SurfaceSunken=3.983:1. Primary and muted text pass on all four surfaces; AccentInk/Accent passes in both themes. The existing metadata uses TertiaryTextBrush on these surfaces (ControlCenterWindow.cs:33, 465-472). W3C SC 1.4.3 requires 4.5:1 for normal text and explicitly forbids rounding a failing value up: https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html . Changing the palette, choosing a new metadata mapping or omitting failing pairs from the required test is a design/acceptance decision reserved to Claude.
- Options:
  1. Claude supplies revised Tv.TextFaint values for each theme that pass 4.5:1 on every intended metadata surface; keep the caption/timestamp mapping and contrast acceptance.
  2. Keep the prescribed palette but explicitly assign small captions/timestamps to Tv.TextMuted; restrict Tv.TextFaint to approved non-body/large-text uses or enumerate the allowed background pairs and update the spec/test matrix accordingly.
- Partial work: reverted (yes; no UI1 product or test files changed; only this proposal and STATUS bookkeeping).
- **Decision (Claude, 2026-10-24): option 1.** New `Tv.TextFaint`: Gece `#7C899E` (Bg 5.46, Surface 5.08, SurfaceRaised 4.62, SurfaceSunken 5.59 : 1), Gün `#5D6A81` (Bg 5.05, Surface 5.46, SurfaceRaised 4.82, SurfaceSunken 4.60 : 1) — same hue, minimal shift toward Text, still distinct from Muted. The caption/timestamp mapping and the contrast acceptance stay. DESIGN-CONTROL-CENTER.md updated; UI1 continues unchanged otherwise. The worklog card had the same flaw (D20).

### P-30 — N6 — Evidence panel needs an approved extension of the immutable Phosphor reference
- Date: 2026-10-24, HEAD 78fce407e4d42c9e58c641cd8c06786d9a0f8402
- Card text: DESIGN-WORKLOG.md:1676–1678 names `Views/WorklogCard.xaml`, `Controls/EvidencePanel` and redaction tests; requires the §3 expanded technical view, redacted copy, per-user expander persistence, and a UIA check that tool names appear only in details. N5 step 4 (line 1567) says: "the \"ayrıntılar ▸\" button stays hidden (`HasDetails` defaults to `false`). N6 sets it."
- Problem: The implemented card is `src/Talvora.Tray/Worklog/WorklogCardView.xaml`, not the N6 Files path. Its footer binds `HasDetails` (line 372); the view only raises `DetailsRequested` (WorklogCardView.xaml.cs:207), and DesktopProgressNotificationWindow.cs:78–89 neither handles that event nor adds an evidence panel. WorklogCardViewModel.cs:228–229 leaves HasDetails false. Wiring the prescribed embedded panel and setting its visibility therefore changes at least the view or its window/mapper. All these files are deliberately pinned: tests/ModernizationPolicyRegression.ps1:73–99 compares BOTH the eight production files and their reference copies against Claude's immutable SHA256SUMS.txt and throws at line 98 on any content change. DESIGN-WORKLOG.md:363 makes Claude's reference code authoritative; N6 supplies neither an updated reference/manifest nor a prescribed amendment to this guard. Inventing an alternate visual integration, changing the approved reference hashes, or exempting production files from the guard would be an unapproved design/check decision under AGENTS.md EXECUTION MANDATE rules 6 and 8.
- Evidence: signed-in MSI\tayla Fast gate 13 checks/0 errors, `Hepsi geçti.` (artifacts/n6-baseline.log); exact origin/main GitHub windows-ci 40022688918 success; all eight production normalized source hashes match; `pwsh -NoProfile -File tests/ModernizationPolicyRegression.ps1` returns MODERNIZATION_POLICY_GREEN. Context7 `/dotnet/wpf` and Microsoft Learn Expander/Clipboard.SetText guidance confirm the ordinary WPF APIs; the blocker is the repository's approved-reference contract, not an unsupported API.
- Options:
  1. Claude supplies the N6 Phosphor reference extension (EvidencePanel, view/window wiring, persistence, redaction/UIA tests, target PNGs and updated checksum manifest), and corrects the N6 Files list — the implementing agent copies and verifies the approved design.
  2. Claude explicitly prescribes the narrow N6 extension and corresponding policy-test amendment, including how the original reference integrity and unchanged code-terminal behaviour remain checked — the implementing agent follows that recipe without inventing a design or silently relaxing a guard.
- Partial work: reverted (yes; no product, test, reference or manifest files changed; only this proposal and STATUS are updated, preserving the existing D18/N5 delivery closeout).
- **Decision (Claude, 2026-10-24): option 1.** Claude extended the Phosphor reference with the evidence panel (view model rows/state/preference, mapper rows, XAML panel, window toggle + copy, presenter passes `WorklogDetails.From(message)`), added `Program.N6Details.cs` (redaction through the real protocol, tool name only in details) and the target render `10-details-open.png`; the reference sits in `worklog-phosphor-next/` and N6 promotes it in its own commit, so the checksum guard is never relaxed. N6 is rewritten with the exact steps.

### P-29 — D17 — Signed-in quality gate cannot access S3-protected source-edit and toolchain storage
- Date: 2026-10-23, HEAD 6cbb126728cd90de8a610734abd5d33ecef99770
- Card text: D17 Acceptance: "Full gate `Hepsi geçti.` (bootstrap)" and "one normal `talvora_user_run` Fast gate → `Hepsi geçti.`".
- Problem: Before any D17 product/test edit, a detached Fast gate as the signed-in user failed SourceEdit (7 checks, 1 error). Two semantic tests reported access denied at C:\ProgramData\Talvora\SourceEdit; structural YAML and both ast-grep cache tests reported access denied at C:\ProgramData\Talvora\Toolchains. Evidence: artifacts/d17-baseline.log (FAIL lines 8, 11, 12, 28–30, 71); src/Talvora/SourceEditing/SourceEditTransactionStore.cs:158–164 defaults to CommonApplicationData/SourceEdit; AstGrepToolchainManager.cs:39–45 unconditionally creates CommonApplicationData/Toolchains. S3 deliberately grants no normal-user write permission there. D17's named files cannot make its mandatory signed-in gate green. Making the protected directories user-writable would change the approved S3 security contract.
- Options:
  1. Specify a prerequisite regression/test-fixture card that directs source-edit transaction storage and ast-grep cache to isolated user-writable temporary roots while retaining the installed service's protected ProgramData contract; unblock D17 after the signed-in Fast gate passes.
  2. Specify a service-mediated test setup/cleanup mechanism for protected ProgramData while keeping the main gate executor signed in; define the identity and safety boundaries explicitly.
  3. Explicitly approve narrow child ACL changes for SourceEdit/Toolchains and revise S3's security acceptance; increases user-writable service state, so must not be inferred.
- Partial work: reverted (yes; no D17 source or regression files changed; only proposal/status documentation).
- **Decision (Claude, 2026-10-23): option 1, folded into D17.** The service keeps S3's protected ProgramData; the regression runner points the shared engine (`SourceEditRuntime.UseStateRootForTests`) and the ast-grep cache (`AstGrepToolchainManager.CacheRoot`) at `%LOCALAPPDATA%\Talvora\RegressionState`, and the semantic replay test reads receipts from the engine's state root. Two old source assertions follow the helper's new `[IO.File]::Delete`. Claude ran the Full gate as `MSI\tayla` on a clean clone with all D17 steps: 57/0 `Hepsi geçti.`. No ACL is loosened.

### P-28 — N4 — Live Codex JSONL probe conflicts with the no-Codex user instruction
- Date: 2026-10-23, HEAD 8f379adafffca6386f9a86f5def8b157e92a6c49
- Card text: DESIGN-WORKLOG.md:266: "check with Context7 and a real `codex exec --json` run which events carry file changes and whether they include diff text"; N4 Do: "§5.4 and §5.4.1 (Codex code terminal)".
- Problem: STATUS.md User queue U7/U8 explicitly defers live Codex execution under the user's "do not use Codex" instruction. AGENTS.md also prohibits agent delegation without explicit permission in the current chat. The user said only "devam et" in this chat. Context7 `/openai/codex` documents JSONL `item.completed`, while its app-server v2 diff types are not evidence for the CLI exec payload. A real live run cannot be performed without contradicting the existing user instruction, and skipping it silently changes the prescribed validation.
- Options:
  1. Amend N4 §5.4.1 to accept the documented CLI JSONL schema plus sanitised recorded fixtures and an explicit no-live-Codex limitation. Leave a separate optional live acceptance in User queue if Codex is later authorised.
  2. Keep N4 unchanged and blocked until the user explicitly authorises an isolated live Codex JSONL probe in a disposable workspace (never use Codex to implement the card).
- Partial work: reverted (yes; no N4 product or test files changed).
- **Decision (Claude, 2026-10-23): option 1.** The user's "do not use Codex" stands. N4 implements §5.4.1 from the documented CLI JSONL schema plus sanitised recorded fixtures, and records "no live Codex run" in the Log; an optional live probe is a User-queue item only. The N4 card carries the amendment.

### P-27 — N3b — Workspace legacy-mutator acceptance contradicts the source-edit guard
- Date: 2026-10-23, HEAD 504fa7bc123cd3ac343228dbab2b0d9bf325f7ba
- Card text: N3b Acceptance (4): "`replace_text`/`json_set` inside a workspace produce diffs"; Do: "fix C1–C4 and C7 per §3.1, §5.4.1 (Claude part here; Codex part in N4), §5.5"; §3.1 requires "text/config/file mutators inside a recognized workspace".
- Problem: DeveloperTools.FileMutation.cs:43–45 and ConfigAssetTools.Json.cs:75–77 call SourceMutationPolicy.EnsureLegacyTextMutationAllowed before reading/writing. SourceMutationPolicy.cs:28–45 deliberately throws for guarded workspace text/config paths; both tool descriptions explicitly promise SOURCE_EDIT_POLICY_VIOLATION there. SourceEditRegressionRunner.Misc.cs:932–960 requires JsonSet/YamlSet/TomlSet/DotenvSet/IniSet/XmlSet to reject workspace writes and preserve every original file. Therefore Acceptance (4) cannot produce any committed change under the current contract. Adding diff reporting alone cannot satisfy it; enabling those writes would change an explicit safety/behaviour contract. AGENTS.md rule 6 forbids weakening/bypassing a guard without a proposal. No existing adapter routes these two legacy entry points through SourceEditEngine.
- Options:
  1. Keep the legacy workspace rejection contract and amend N3b Acceptance (4)/§3.1 to prove no preview on these rejected calls; require real previews for successful source-edit and already-supported workspace copy/move routes. Ordinary non-workspace mutators keep the prescribed privacy-only summary.
  2. Explicitly authorize and specify legacy workspace adapters through SourceEditEngine (revision validation, WAL, rollback, encoding, receipt and replay rules retained), identify the affected legacy surfaces and amend their policy tests/descriptions. Then implement actual committed-result previews for those routes; never remove the guard and write directly.
- Partial work: reverted (yes; no N3b product/test changes were made). N3 delivery evidence is retained with the user's explicit approval; only proposal/status documentation is local. P-11 still defers PNG evidence to N5. N4 was not started because this request is specifically N3b.
- **Decision (Claude, 2026-10-23): option 1.** The legacy-mutator guard stays exactly as it is; N3b acceptance (4) is amended: `replace_text`/`json_set` on a workspace file are rejected and produce no preview; inside workspaces only the source-edit family and `copy`/`move` produce diffs. N3b now runs after N10 and also writes hunk-header context with `DeclarationLine` (amendment in the card).

### P-26 — DEP17 — Concurrent Dependabot working-tree mutations block the exact approved runner
- Date: 2026-10-22, HEAD a28f77b
- Card text: "talvora_user_run → pwsh -NoProfile -File scripts/Apply-DependabotPr.ps1 -Pr 17 (workingDirectory = repo root). It must end with READY DEP17: …. Anything else → stop; restore with talvora_git_run ... and report."
- Problem: Repeated attempts on 2026-10-21 and 2026-10-22 fail at scripts/Apply-DependabotPr.ps1:27-29, where the approved runner refuses dirty files except docs/codex/STATUS.md and .context7/verification.json. Immediately before invocation, both Talvora Git status and an independently executed signed-in-user `git status --porcelain=v1` show only those two permitted files. Yet the run returns the preflight exception in about 0.8 seconds, while a subsequent status contains a staged Directory.Packages.props update to 10.11.0, five modified packages.lock.json files and updated THIRD-PARTY-NOTICES.md. The runner checks cleanliness before any writes, so those package changes cannot come from the same invocation after the thrown preflight exception; overlapping execution or another writer is suspected, but its identity is unproven. The exact DEP17 script was not weakened or edited. Local signed-in Fast gate 13 checks / 0 errors / `Hepsi geçti.`; exact HEAD GitHub windows-ci 39632553300 green. Acceptance (READY DEP17, one commit, closed PR, green CI and installed HEAD) remains unmet.
- Options:
  1. Define and approve a new diagnostic/serialization card to identify the overlapping Dependabot writer or duplicate invocation, prevent concurrent workspace mutation without weakening the cleanliness guard, and then unblock DEP17's exact script path.
  2. Explicitly approve a one-time Talvora-tool-driven DEP17 update instead of the script: apply only the PR's version edit via talvora_apply_patch, run the same signed-in lock-file regeneration, notice review, lockstep/locked restores, Release build and Full gate, then commit/push only with talvora_git_run and close PR #17 with talvora_gh_run. No gate is bypassed; this changes the implementation path and therefore needs Claude's decision.
- Partial work: reverted (yes — all version, lock and notice changes via the card's exact selective git restore; DEP17 Context7 edits separately reversed; no commit, push, PR close or deployment).
- **Decision (Claude, 2026-10-22): option 1 → card D14; DEP17 step 2 amended.** Cause (verified by Claude): the ~6-minute script run as one blocking `talvora_user_run` call outlives the chat client's tool-call limit, so a retried or overlapping call meets the first run's files — the dirty-at-0.8 s symptom. The guard stays. The revised reference script takes an exclusive lock (a second start exits 3 and changes nothing), names the unexpected files, and writes `artifacts/dependabot/pr-<n>.log` / `.result`. DEP17 now starts it once, detached, with `talvora_user_process_start` and polls the result file. Claude ran the whole path in a fresh clone, including a concurrent second start and the installed-service detached start.

### P-24 — Git transport policy — `git pull` must use the signed-in identity
- Date: 2026-10-21, HEAD ba1bb8b
- Card text: User request to record a permanent project-rule requirement and continue the existing queue: fix Git/SSH pull without loosening private-key permissions. No Claude-approved implementation card exists yet.
- Problem: `src/Talvora/Tools/GitTools.cs:787-803` selects the interactive user's credentials only for remote `fetch`/`push`; `git pull --ff-only origin main` instead ran as LocalSystem and failed with `UNPROTECTED PRIVATE KEY FILE`/`bad permissions` for the private SSH key owned solely by the signed-in Windows user. The file has inheritance disabled and only the owner has FullControl; `fetch origin` succeeds. This is a process-identity mismatch, not a reason to weaken SSH ACLs.
- Options:
  1. Claude defines a dedicated fix card routing SSH/credential-based `pull` through the logged-on user, adds regression coverage and a binding AGENTS.md Git-identity invariant; assess equivalent remote-read verbs separately.
  2. Claude defines a card that keeps `fetch` in the logged-on user context and performs a verified local fast-forward-only update instead of remote `pull`, with a corresponding AGENTS.md protocol change and regression proof.
- Partial work: none; no key ACL or GitTools implementation changes.
- **Decision (Claude, 2026-10-21): option 1 → card D12 (exact code in ROADMAP).** `pull` joins `fetch`/`push` in `ShouldUseInteractiveUserForRemoteGitAsync`; the source regression asserts it. The key ACL stays exactly as it is. Claude added the binding Git identity invariant to AGENTS.md ("Validation and source editing"). Other remote-read verbs (`ls-remote`, `remote update`) are not used by the protocol and stay out of scope.

### P-25 — DEP17 — Canonical Dependabot script cannot pass required Context7 gate
- Date: 2026-10-21, HEAD eff0f85 (DEP1 GitHub CI 39614105176 green).
- Card text: DEP1 Step 4 / DEP17 requires running the byte-exact `scripts/Apply-DependabotPr.ps1 -Pr 17` after DryRun; the script must pass the Full gate, commit once (`Card: DEP17`), push both remotes and close the PR without merging on GitHub.
- Problem: DryRun succeeded and listed six changed files (one `Directory.Packages.props` and five `packages.lock.json`). The real run applied PR #17 and passed lockstep/locked restores/Release build, but Full gate failed `Context7` at `scripts/Validate-Context7Gate.ps1:53` because `.context7/verification.json` must be refreshed **in the same change**. `scripts/Apply-DependabotPr.ps1:26-27` rejects any pre-edited file except `docs/codex/STATUS.md`; line 63 stages only version/lock files and STATUS, excluding `.context7/verification.json`. Consequently the exact reference script cannot satisfy its mandated Full gate without violating the card's verbatim-copy instruction or weakening a safety gate. The script also performs Git mutation/commit/push directly; Claude should clarify its scoped exception to AGENTS.md's tool-only edit/commit policy if retaining that approach.
- Options:
  1. Claude creates a dedicated follow-up card to revise the canonical script, source reference and checksum to accept, require and stage fresh `.context7/verification.json` in the DEP<n> commit. Keep the unchanged Context7 gate, lockstep test, locked restore, Full gate, and both-remote delivery. Explicitly authorize or replace direct Git writes from the script.
  2. Claude amends DEP17 to use the Talvora tool-driven path: patch `Directory.Packages.props` and `.context7/verification.json` through `talvora_apply_patch`, run the prescribed restore/build/gates as the signed-in user, then commit/push only via `talvora_git_run` and close PR #17 with `talvora_gh_run`. This deviates from DEP1 Step 4 and therefore requires Claude approval.
- Partial work: DEP1 infrastructure commit eff0f85 is on both remotes with CI green. PR #17 DryRun was green, real run stopped at Context7 before commit/push/close. All six version/lock changes were reverted selectively to HEAD using `talvora_git_run restore --source=HEAD --staged --worktree`; repo main and remotes remain at eff0f85 and PR #17 remains OPEN. No DEP17 dependency commit. Only STATUS and this proposal record the blocker.
- **Decision (Claude, 2026-10-21): option 2 in script form → card D13, DEP17 rewritten.** The fault was Claude's script. The revised reference script accepts a pre-edited `.context7/verification.json`, still runs lockstep, locked restores, Release build and the Full gate, and then stops with `READY DEP<n>` — it no longer commits, pushes or closes. The agent commits and pushes with `talvora_git_run` and closes the PR with `talvora_gh_run`, so the delivery gates apply. No gate is weakened.

### P-23 — D9 — Byte-identical fixture blocked by newline normalization
- Date: 2026-10-21, HEAD 2ce3fc0
- Card text: "Step 3 - copy `docs/codex/reference/ci/IdentityPortabilityRegression.ps1` → `tests/IdentityPortabilityRegression.ps1` byte-for-byte (verify with `SHA256SUMS.txt`)."
- Problem: The reference has mixed newlines (20 LF, one final CRLF), 1208 bytes, SHA-256 `b992ef454e4605d55512bb8f5be5ac71c29a56c5525641b7c43055f3ff7752e2`. Canonical `talvora_apply_patch` Add File normalized it to CRLF, 1228 bytes, SHA-256 `0af4f534643e4b5d1ec49a111b161ceca57af395ae5b225bff8b8398c3f3d360`. `src/Talvora/SourceEditing/TalvoraPatchParser.cs:59-62` normalizes every carriage return in patch input; Add File only has a uniform newline policy. The mandated SHA therefore cannot be reproduced using the mandated editor.
- Options:
  1. Amend D9 to allow LF-normalized fixture and a changed reference/checksum; explicitly add reference files to its Files list.
  2. Authorize one exact raw byte copy for D9 Step 3 using a controlled filesystem copy (all other source edits still via `talvora_apply_patch`), followed by SHA-256 verification.
- Partial work: reverted (yes; both C# tests and generated fixture restored/deleted).
- **Decision (Claude, 2026-10-21): option 1, generalised.** The fault was Claude's reference file (mixed newlines). All files in `docs/codex/reference/**` are now pure LF without BOM, and both `SHA256SUMS.txt` files list the hash of the **normalised content** (CRLF→LF, BOM removed). New AGENTS.md rule 5 sentence: copy a reference file with `talvora_apply_patch` Add File (its CRLF policy is fine) or `Copy-Item`, then verify the normalised hash. D9 step 3 is amended accordingly; D9 is `[ ]` again. No other change to D9.

### P-22 — Q6c — No public SDK accessor for attributed tool functions
- Date: 2026-10-21, HEAD 3bb0bd6
- Card text: "wrap every assembly tool's AIFunction in RedactingToolFunction ... and register it with McpServerTool.Create(AIFunction, McpServerToolCreateOptions) carrying the attribute's name, title, description and annotations"; "tools/list JSON ... byte-identical ... for every negotiated protocol version"; "If byte-identity cannot be reached for any tool, Q6c stops with a proposal — no partial rollout."
- Problem: current production uses Program.cs:105 WithToolsFromAssembly(), so the SDK constructs the attributed functions and its internal injected-parameter binder. ModelContextProtocol 2.2.0 exposes public McpServerTool.Create(AIFunction, options), but the existing AIFunctionMcpServerTool.AIFunction getter is non-public (confirmed with reflection: GET=False); McpServerTool exposes only ProtocolTool/Metadata. Rebuilding from AIFunctionFactory.Create(MethodInfo) requires choosing a replacement ConfigureParameterBinding, serializer, method instance and injection behavior, while using reflection to extract the SDK-created function requires approval of a private-API dependency. The card/P-9 does not select either approach. Selecting one independently could change behavior of existing tools and violate the mandatory byte-identity/invocation acceptance.
- Evidence: before any implementation, captured current live tools/list result bytes for full=250 (331190 bytes, SHA-256 aa31f232cfd54bf2ed2a73556615922f29ff2fe462bc2bc3cdf3cb699aa39e0c), dev=213 (294684 bytes, SHA-256 46fa27df4d6bbc0e9b50eae6add4026b6c13cba4962d67ac7099c6d5d6285a88), admin=88 (89822 bytes, SHA-256 e977049d0c1b746fa61eb90f505a476c248af0dedfa3f59d711ce1d9c5d796c2). Each was identical across MCP 2025-11-25, 2025-06-18 and 2025-03-26 in the pre-change baseline. A smoke catalog comparison passed all 9 cases. Q6c category/redaction and DelegatingAIFunction cancellation/success unit tests passed in isolation. No production tool registration was modified and no Q6c deployment occurred.
- Options:
  1. Explicitly authorize extracting the SDK-created AIFunction through its non-public getter during a post-configuration wrapper pass; specify fail-closed version/metadata guards and required invocation tests. The 9 byte comparisons must remain strict.
  2. Specify public AIFunctionFactory recreation including SDK-equivalent ConfigureParameterBinding, method instance/DI and metadata options for all assembly tools; no private reflection, but larger implementation and regression scope.
  3. Defer Q6c until a supported public SDK wrapping/decoration hook is available. Q6's existing schema validation remains installed.
- Partial work: reverting all Q6c implementation/tests/snapshot files to HEAD (yes); no commit or deployment.
- **Decision (Claude, 2026-10-21): option 3 — defer Q6c.** Neither a private-API dependency (option 1) nor re-implementing the SDK's parameter binding for 250 tools (option 2) is worth the risk now: Q6 already gives agents the most common correction (argument errors). Q6c becomes `[-] deferred` with the note "revisit when ModelContextProtocol exposes a public tool-invocation hook or the wrapped AIFunction". Keep your baseline numbers in the Log; nothing to implement.

### P-21 — A1 — Pre-deploy Full gate expects post-deploy tool count
- Date: 2026-10-21, HEAD ee38286
- Card text: "Part D — from now on the agent runs builds/tests/gate with talvora_user_run"; "One-time repair ... then git status must still be clean"; AGENTS.md CONTINUE PROTOCOL step 7 requires a green Full gate before the one-card commit/push/deploy.
- Problem: A1 adds talvora_user_run and increases source manifest count from 250 to 251 while the installed ee38286 service still exposes 250 tools. tests/NativeInstallerRuntimeRegression.ps1:5-6 derives expected count from the uncommitted source manifest; :55 and :81 then compare it to the installed current.json ToolCount. The local Full gate passed compilation, SourceEdit, notifications, metadata, and 31+ checks but failed NativeInstallerRuntime solely because SOURCE_TOOL_COUNT=251 and INSTALLED_TOOL_COUNT=250. Pre-commit installation cannot report the final sourceCommit because the A1 commit does not yet exist; the new signed-in-user runner also cannot execute until deployment. Two stale count assertions were repaired in supporting tests; all targeted owner/surface/metadata tests are green. This is an ordering/acceptance contradiction, not a request to skip an unsuccessful runtime test.
- Options:
  1. Specify an A1 delivery exception: commit and push after targeted source/smoke tests but before the Full gate; install the exact A1 commit; run Full as MSI\tayla using talvora_user_run; require both exact-commit CI and Full green before marking A1 complete. Leave the native installed-runtime guard unchanged.
  2. Add an exact prerequisite card revising NativeInstallerRuntimeRegression to compare the installed current.json tool count with the installed runtime's actual tools/list rather than a possibly newer working-tree manifest; keep a separate strict source-manifest check. Then A1 can run a green pre-deploy Full gate without weakening installed-runtime assertions.
- Partial work: reverting A1 source/tests to HEAD; no recursive icacls repair or A1 deployment attempted.
- **Decision (Claude, 2026-10-21): option 2 — new card D10 (exact code in ROADMAP).** `tests/NativeInstallerRuntimeRegression.ps1` compares installed `current.json` with the installed runtime's live `tools/list`, not with the working-tree manifest. Claude ran the D10 version as `MSI\tayla`: `NATIVE_INSTALLER_RUNTIME_GREEN`. A1 then runs unchanged after D10.

### P-20 — D5 — N5 subsection acceptance conflicts with the prescribed UTF-8-only change
- Date: 2026-10-21, HEAD 6664eda
- Card text: "Step 1 — directly after Set-StrictMode insert exactly [Console]::OutputEncoding and $OutputEncoding; Step 2 — Nothing else in the tool changes." "Step 4 — talvora_agent_brief {workspaceRoot, card: N5} → context contains ### 3.1 \"Kod terminali\" exactly."
- Problem: DESIGN-WORKLOG.md:592 cites both §3 and §3.1 for N5. Get-CodexContext.ps1:86-97 discards §3.1 when parent §3 is cited; lines 147-177 then limit each selected section to the remaining context budget. After applying the exact D5 UTF-8 script change, the real N5 command printed 305 lines and §3/§7/§4 headers but no ### 3.1 \"Kod terminali\". The live tool output also lacked that heading. Meeting the literal acceptance requires changing section-selection/budget logic beyond D5's exact two-line script change.
- Options:
  1. Amend D5 acceptance to test UTF-8 on the D5 context (contains ç, ğ, —, §) and verify the exact 3.1 heading directly in DESIGN-WORKLOG.md; retain the existing parent-section selection policy.
  2. Amend D5's exact implementation or add a prerequisite D-card to include explicitly cited child §3.1 and its heading alongside §3, with a defined context budget and regression proof; preserve the original live N5 acceptance.
- Partial work: reverted (yes; production and tests restored byte-for-byte to HEAD).
- **Decision (Claude, 2026-10-21): option 1.** D5 acceptance step 4 becomes: `talvora_agent_brief {workspaceRoot, card: "N5"}` → `context` contains `ç`, `ğ`, `—` and `§`, and the heading line `### 3.1 "Kod terminali"` exists in `docs/codex/DESIGN-WORKLOG.md` (checked with `Select-String`). The parent-section selection of the context helper stays as it is. Everything else in D5 is unchanged.

### P-19 — D7 — Pre-existing full-gate failure from oversized N1 context
- Date: 2026-10-20, HEAD 429591f
- Card text: D7 Acceptance: "pwsh scripts/Invoke-QualityGate.ps1 -Scope Full" must end with "Hepsi geçti." in both the signed-in user and LocalSystem identities.
- Problem: On clean HEAD, the Fast gate ended with 12 checks, 1 failure (PS:QualityGateRegression). The same failure persisted during the D7 Full gate (37 checks, 1 failure). tests/QualityGateRegression.ps1:211 enforces 250 lines per real card context, but scripts/Get-CodexContext.ps1 -Card N1 produces 343 lines. This is unrelated to D7 profile resolution and prevents its required green gate; reducing N1 content or relaxing the line budget is an unapproved scope/behavior decision. The D7 source and S6 regression compiled cleanly and SourceEdit passed before reversal.
- Options:
  1. Add a dedicated prerequisite D-card to make the N1 context fit 250 lines while preserving all binding acceptance details; then unblock and rerun D7 with the original green-gate requirement.
  2. Explicitly revise the quality-gate size budget for N1 (and its policy documentation) if 343 lines of required context are acceptable; then unblock D7.
- Partial work: reverted (yes; KnowledgeTools, S6 regression, quality-gate identity line, and Context7 evidence are restored to HEAD).
- **Decision (Claude, 2026-10-20): option 2, refined — new card D8 (first in the queue).** The 250-line budget stays, but it no longer counts the STATUS header (required reading) or `#### Exact implementation` blocks (must reach the agent complete). Exact test code is in ROADMAP → D8. Caused by Claude's guide commits `3d176bf`/`429591f`, not by D7. After D8, D7 and D6 run unchanged.

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
