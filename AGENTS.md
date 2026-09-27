# Talvora engineering rules

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

## Mandatory Windows desktop progress reporting

- Talvora must proactively report meaningful work to the active interactive Windows desktop. This is a software/runtime contract, not a prompt preference and not an optional courtesy.
- Every MCP tool execution passes through the desktop-progress wrapper. Long-running calls emit a heartbeat at a bounded short cadence; completion, cancellation, and failure use concise milestone notifications where appropriate.
- Notifications must be deduplicated/throttled so they stay useful, but the progress wrapper itself may not be bypassed for ordinary tool execution.
- Notification delivery is best-effort and must never fail, cancel, delay, or change the result of the underlying operation. Headless, CI, and no-active-session execution must remain safe.
- Windows service code must target the active interactive session through supported Windows APIs rather than assuming a fixed session ID. Prefer modern generated interop for new native calls.
- Substantial future Talvora project work must keep the user informed through Windows desktop progress updates at milestones and at intervals that are not excessively long.

## Validation and source editing

- Use `talvora_apply_patch` as the primary source/config/document editor and read existing files before mutation.
- Keep targeted validation fast during implementation; run the canonical installer/exact-installed smoke when runtime, packaging, shared infrastructure, dependency graph, or release behavior changes.
- Gitea is the primary modernization tracker. Keep the active phase issue updated with findings, validation evidence, commit SHA, and closeout state.
