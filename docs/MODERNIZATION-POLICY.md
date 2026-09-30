# Talvora Modernization Policy

Talvora is maintained as a modern-by-default Windows/.NET MCP system.

## Quality gates

1. **Current documentation gate** — Every meaningful development change starts with Context7 verification of the current platform/framework/library contract. Refresh `.context7/verification.json` in the same change, record the Context7 library ID and verified behavior/migration rule, and use official vendor documentation for release/support/breaking-change confirmation.
2. **Supported-version gate** — Use supported stable releases. Avoid wildcard dependency versions and deprecated compatibility paths. Central NuGet versions live in `Directory.Packages.props`.
3. **Reproducibility gate** — `global.json` defines the .NET SDK feature band, package lock files are committed, and CI restores with `--locked-mode`.
4. **Quality gate** — Release builds have zero warnings/errors. NuGet audit covers direct and transitive dependencies at high severity and above.
5. **Runtime gate** — Runtime/shared/dependency/installer changes must pass the canonical Windows installer and installed MCP surface verification. Hosted CI intentionally does not emulate durable interactive-user behavior.
6. **Tracking gate** — Each modernization phase is a Gitea issue under the active modernization milestone. Context7 evidence, tests, commit and deployment evidence are written there before closure.
7. **Desktop worklog gate** — Every MCP tool execution is wrapped by Talvora's Windows desktop progress layer, but only meaningful user-visible work is surfaced. Low-value reads/searches/health probes stay silent. The LocalSystem service publishes session-aware events through ACL-protected local IPC to Talvora.Tray, which owns one polished nonmodal WPF worklog card. User-facing text is plain Turkish and must not leak raw tool names, commands, stack traces, exit codes, SDK/repository jargon or transport details. The visual identity may be dark terminal/neon, but fabricated telemetry is forbidden: visible timestamps, durations, files, diff counts, diff previews, results and error details must come from real runtime/tool data. A separate expandable evidence surface may expose bounded real technical evidence while preserving plain-language primary copy; all evidence passes canonical sensitive-data redaction first. Active meaningful work remains visible until terminal state; successful completion remains readable for about three minutes and failures longer. Both thrown exceptions and non-throwing failure results are recognized. Mixed-DPI placement, CI/headless safety, and best-effort delivery remain mandatory, and notification delivery can never fail or delay the underlying operation.

## 2026-Q3 modernization execution phases

### MOD-01 — Baseline, toolchain pinning and reproducible restore

**Goal:** make the build graph deterministic before changing behavior.

**Implementation:** define the .NET 10 SDK in `global.json`; use `Talvora.slnx` as the canonical non-installer solution graph; enable Central Package Management; commit per-project NuGet lock files; make CI restore in locked mode; keep warnings-as-errors, latest analysis and deterministic builds.

**Exit criteria:** clean locked restore, release build with zero warnings/errors, every project has a lock file, CI uses the same graph.

### MOD-02 — Dependency modernization and API compatibility

**Goal:** move every direct dependency to the newest stable version that is actually compatible with Talvora's .NET/Windows/MSBuild architecture.

**Implementation:** inventory direct/transitive packages; run outdated/vulnerable/deprecated scans; verify MCP/.NET/Roslyn guidance through Context7; update the MCP SDK, Microsoft.Extensions, SQLite, Roslyn, WPF-UI, YamlDotNet, Tomlyn and related packages. Compatibility pins require written evidence. `Microsoft.Build.Framework` is intentionally kept on the verified 17.14 line because the 18.10 line introduces a net11-only runtime asset path that fails this net10 + MSBuildLocator architecture.

**Exit criteria:** no outdated direct package except documented compatibility pins; no deprecated/vulnerable package findings; release build and source-edit regressions pass.

### MOD-03 — Context7 development quality gate

**Goal:** make current documentation verification a mechanically enforced part of development rather than a convention.

**Implementation:** keep the durable rule in `AGENTS.md`; require `.context7/verification.json` to change alongside development-scope changes; validate evidence freshness and library IDs with `scripts/Validate-Context7Gate.ps1`; retain the human-readable Context7/vendor evidence in the active Gitea issue/PR.

**Exit criteria:** CI fails a source/framework/toolchain change that does not include fresh Context7 evidence.

### MOD-04 — CI modernization and supply-chain reproducibility

**Goal:** make CI current, reproducible and resistant to silent dependency drift.

**Implementation:** pin external GitHub Actions to immutable full commit SHAs with release comments; let Dependabot update those pins; use the repository SDK pin; enable NuGet cache keyed by lock files; run locked restore, modernization contracts, source regressions, live MCP surface checks, installer build and installed-runtime verification. Hosted CI continues to exclude tests that fundamentally require a durable interactive Windows user session.

**Exit criteria:** the GitHub Windows build-and-smoke workflow passes from a clean runner and the installed service/surface contract remains green.

### MOD-05 — Test and regression modernization

**Goal:** make the canonical build include the regression projects that protect source editing and modernization rules.

**Implementation:** include SourceEdit regression in `Talvora.slnx`; keep fast source-contract checks separate from installed-runtime acceptance; add policy regressions for central versions, lock files, immutable Actions pins and Context7 enforcement.

**Exit criteria:** targeted regressions pass locally and the same contracts execute in CI.

### MOD-06 — Runtime, diagnostics and maintainability audit

**Goal:** remove stale implementation patterns without changing Talvora's public capability.

**Implementation:** audit .NET 10 APIs, async/cancellation/disposal/process/HTTP usage, nullable/analyzer output, logging and obsolete APIs. Use Context7 before changing external API patterns; remove legacy fallbacks only after a supported replacement is verified.

**Exit criteria:** zero analyzer/compiler warnings, no verified obsolete API usage, MCP surface unchanged unless a migration explicitly requires a contract update.

### MOD-07 — Installer, packaging and release provenance

**Goal:** keep the Windows-native installer aligned with the modern build graph and current Windows SDK.

**Implementation:** retain current Windows SDK discovery, self-contained publish, deterministic metadata and canonical installer flow; validate artifact creation and installed service identity/health/surface; preserve the installer-process-only wait behavior so persistent service/tray descendants do not deadlock CI.

**Exit criteria:** native installer builds, installs, starts LocalSystem service, reports expected health/SID/tool surface and cleans up successfully.

### MOD-08 — Continuous modernization loop

**Goal:** prevent this modernization from becoming a one-time cleanup.

**Implementation:** Dependabot monitors NuGet, .NET SDK and GitHub Actions weekly; package versions stay centralized and exact; lock files are committed; substantial development begins with dependency/toolchain freshness review; workstation toolchains are periodically compared with vendor stable releases; documentation describes policy while machine-readable pins remain the source of truth.

**Exit criteria:** automated update PRs are created on schedule, they must pass the same Context7/Windows CI gates, and Gitea/GitHub `main` remain synchronized after accepted updates.

### MOD-09 — Mandatory Windows desktop progress notification contract

**Goal:** make readable, plain-language desktop work reporting an enforced runtime behavior instead of relying on an agent prompt or operator habit.

**Implementation:** wrap every MCP CallTool request with the official C# SDK `AddCallToolFilter`; keep low-value read/search/health operations silent while surfacing meaningful work; use `WTSGetActiveConsoleSessionId` only to select the active session; publish start/running/completed/failed/cancelled events to a session-specific named pipe protected for the tray user and LocalSystem; let Talvora.Tray render/update full-text WPF worklog and alert lanes without stealing focus; recognize SDK `CallToolResult.IsError` plus structured unsuccessful/nonzero/timeout results so a failed test or command is never shown as success; translate all user-facing failure text into ordinary Turkish; coalesce parallel work into one logical worklog generation; allow direct WPF `DragMove` repositioning plus `Sabitle`; preserve native mixed-DPI placement; suppress IPC/UI failures so tool semantics are unchanged; skip safely when CI/headless or no active console session is present.

**Reliability profile:** desktop progress IPC uses an explicit protocol version plus a 4-byte length-prefixed UTF-8 frame with a 64 KiB hard ceiling. Connect, read and write phases have independent cancellation budgets. Non-terminal progress and terminal delivery use separate bounded queues; pressure is observable, terminal state is never displaced by heartbeat pressure, shutdown first completes producers and attempts a bounded drain before force cancellation. Each logical work burst receives a fresh generation id and monotonic sequence; late frames cannot overwrite a newer terminal state, manual close suppresses only that active generation, and the next real work generation may appear normally. Parallel work preserves final truth with `Failed > Cancelled > Completed` precedence. Operational Tray Info/Warning/Error messages use a separate alert lane so an active worklog cannot make them invisible. Placement JSON and wire evidence are bounded before parsing/presentation, and canonical sensitive-data redaction remains mandatory.

**Cadence and retention:** meaningful work is surfaced immediately, receives the first long-work refresh after roughly two seconds and periodic refreshes at roughly five-second intervals while fresh runtime evidence exists. Active work never auto-dismisses before terminal state; current product retention keeps completed work readable for roughly ten minutes and failures/cancellations/warnings for roughly fifteen minutes unless the user closes the card.

**Exit criteria:** source/build regressions prove the wrapper cannot be bypassed by ordinary MCP tool calls, low-value background chatter stays suppressed, SDK/structured error results become a visible plain-language failure state, the tray card preserves full readable text without arbitrary truncation, active work does not disappear before terminal state, dragging/persisted positioning remains correct, native placement stays on-screen across mixed-DPI monitors, CI remains noninteractive, and a local installed-service tool call visibly reports start/long-work/terminal states on the signed-in desktop without stealing focus. The exit gate also requires executable behavior tests for protocol round-trip/version mismatch/oversize/truncation, stalled-pipe recovery, queue pressure, bounded shutdown, concurrent failure/cancellation truth, manual-dismiss generation semantics and low-value filtering. Installed Tray `--self-test` must exercise the same production protocol and presentation-state code.

## Update cadence

- Automated dependency update proposals run weekly for NuGet and GitHub Actions on the GitHub mirror.
- At the start of substantial development, inspect the workspace/toolchain and check affected dependencies for current releases and deprecations.
- Do not update merely to change a version number: read migration notes and verify behavior. Conversely, do not retain an old version solely because it currently builds.
- Keep `docs/DEVELOPMENT-ENVIRONMENT.md` descriptive rather than a source of version truth; project pins and lock files are authoritative.

## Context7 evidence format

The machine-readable evidence lives in `.context7/verification.json`. Also record this compact human-readable block in the active Gitea issue or pull request:

```text
Context7:
- library: /org/project
- verified: <API, migration rule, or current recommended pattern>
Vendor:
- source: <official release/support documentation>
Impact:
- <what changed or why no code change was needed>
```

For development-scope changes, Context7 evidence is mandatory; there is no blanket documentation-only bypass for source/framework/toolchain work.
