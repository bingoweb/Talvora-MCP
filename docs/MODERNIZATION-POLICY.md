# Talvora Modernization Policy

Talvora is maintained as a modern-by-default Windows/.NET MCP system.

## Quality gates

1. **Current documentation gate** — Every meaningful development change starts with Context7 verification of the current platform/framework/library contract. Refresh `.context7/verification.json` in the same change, record the Context7 library ID and verified behavior/migration rule, and use official vendor documentation for release/support/breaking-change confirmation.
2. **Supported-version gate** — Use supported stable releases. Avoid wildcard dependency versions and deprecated compatibility paths. Central NuGet versions live in `Directory.Packages.props`.
3. **Reproducibility gate** — `global.json` defines the .NET SDK feature band, package lock files are committed, and CI restores with `--locked-mode`.
4. **Quality gate** — Release builds have zero warnings/errors. NuGet audit covers direct and transitive dependencies at high severity and above.
5. **Runtime gate** — Runtime/shared/dependency/installer changes must pass the canonical Windows installer and installed MCP surface verification. Hosted CI intentionally does not emulate durable interactive-user behavior.
6. **Tracking gate** — Each modernization phase is a Gitea issue under the active modernization milestone. Context7 evidence, tests, commit and deployment evidence are written there before closure.
7. **Desktop progress gate** — Every MCP tool execution is wrapped by Talvora's Windows desktop progress notifier. The LocalSystem service publishes session-aware events through ACL-protected local IPC to Talvora.Tray, which owns a polished nonmodal WPF progress card. Full text is never arbitrarily truncated, long content remains viewable, active work refreshes in tens of seconds, terminal states auto-dismiss after a readable interval, hover pauses dismissal, visible-card count is bounded, CI/headless execution is safe, and notification delivery can never fail or delay the underlying operation.

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

**Goal:** make frequent, readable, professional desktop progress reporting an enforced runtime behavior instead of relying on an agent prompt or operator habit.

**Implementation:** wrap every MCP CallTool request with the official C# SDK `AddCallToolFilter`; use `WTSGetActiveConsoleSessionId` only to select the active session; publish start/running/completed/failed/cancelled events to a session-specific named pipe protected for the tray user and LocalSystem; let Talvora.Tray render/update a full-text WPF card without stealing focus; use a 20-second heartbeat, 14-second active-card lifetime, coalesced per-operation updates, bounded visible-card count, readable terminal auto-dismiss timing, hover pause and subtle WPF enter/exit motion; route legacy tray status notifications through the same full-text card surface; suppress IPC/UI failures so tool semantics are unchanged; skip safely when CI/headless or no active console session is present. `WTSSendMessageW` and 240-character/4-second NotifyIcon balloons are not the primary progress UX.

**Exit criteria:** source/build regressions prove the wrapper cannot be bypassed by ordinary MCP tool calls, IPC is session-specific and ACL-protected, the tray card preserves full readable text without arbitrary truncation, CI remains noninteractive, and a local installed-service tool call visibly reports start/heartbeat/terminal progress on the signed-in desktop without stealing focus.

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
