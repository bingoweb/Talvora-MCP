# Talvora Deep Bug Audit

Last updated: 2026-09-19
Branch: main
Git HEAD: resolve with `git rev-parse HEAD` after final commit; c8d8e5f602b6cea26c3cb32d698f59b78553721d was the pre-audit baseline
Pre-commit exact deployed runtime: c8d8e5f602b6cea26c3cb32d698f59b78553721d-dirty-b02df0355735
Canonical tool count: 198
Status: runtime audit closed; no known blocker remains.

## Fixed findings

1. Stale hard-coded historical tool counts in tests/CI.
2. Regression tests pinned to historical package versions.
3. ProcessRunner could wait indefinitely after timeout/cancellation if termination failed.
4. Batch-file arguments could be corrupted by cmd expansion/metacharacters.
5. Embedded quote in a batch argument could collapse later argument boundaries.
6. ProcessRunner transport variables leaked into captured Visual Studio developer environments.
7. Failed background-job startup could orphan a child process and job directory.
8. Persisted background jobs could target an unrelated process after PID reuse.
9. Background-job output pump flushed every chunk unnecessarily.
10. Background-job exit observer could hide failures.
11. HTTP mock background failures were insufficiently observable.
12. File watcher disposed-state races and redundant sorting.
13. HttpClient handler/client ownership cleanup.
14. WinForms/Tray lifetime cleanup.
15. Culture-dependent machine-data parsing in multiple tool paths.
16. Visual Studio developer-environment capture quoting.
17. Dirty installer provenance was ambiguous.
18. CI referenced a ProcessRunner regression that did not exist.
19. Broad operational catches in multiple paths were narrowed where appropriate.
20. Installer I/O/metadata formatting cleanup.
21. SQLite backup publication/value handling.
22. Installer/Tray graceful-shutdown event mismatch.
23. Installer could update service while Tray stayed on a stale binary.
24. Installer Tray launch could race the old single-instance mutex.
25. CI WinGet scan recursively inspected generated binary output and could stall.
26. CI WinGet scan also matched the regression assertion that verifies WinGet is forbidden.
27. Dev-server smoke launched the apphost as though it were the dotnet host, so the DLL path was misread as the MCP endpoint.
28. Dev-server readiness failure path could leave a background job artifact behind.
29. Gitea tray health checked only backend port 3001, so a broken Caddy/3000 browser path could still show green; status now checks backend + proxy and exposes a degraded state.
30. Gitea tray restart performed an unnecessary MCP `tools/list` round trip and did not explicitly dispose the manually-created `HttpClientTransport`; it now uses direct `CallToolAsync` and async-disposes the transport per current official C# SDK lifecycle guidance.
31. Gitea tray shutdown could race an in-flight async status/restart operation with disposal of its semaphore; lifetime cancellation now lets operations unwind without disposing coordination primitives underneath them.
32. Gitea Secure MCP Tunnel persistence was incomplete: the logon task returned after connect and the managed runtime later stopped. The watchdog now stays alive for the runtime process, fails on unexpected exit so Task Scheduler retries, has unlimited execution time, and runs PowerShell with `-WindowStyle Hidden` so no terminal window remains open.

## Final runtime verification

- Pre-commit SourceCommit: c8d8e5f602b6cea26c3cb32d698f59b78553721d-dirty-b02df0355735
- Service and Tray run from the same version root.
- current.json ToolCount: 198.
- Current ChatGPT session exposes 198 Talvora tool wrappers.
- Installer log confirmed Tray remained stable after automatic launch.
- Fresh publish from current runtime source matched installed binaries by SHA256:
  - Service/Talvora.dll: match
  - Service/Talvora.Shared.dll: match
  - Tray/Talvora.Tray.exe: match
- Program Files contains only the current Talvora version root after installer cleanup.

## Final verification gates

- Talvora service/shared Release build: GREEN.
- Tray Release build: GREEN.
- Talvora.Smoke Release build: GREEN.
- Canonical native installer build/package: GREEN.
- NATIVE_INSTALLER_SOURCE_GREEN.
- NATIVE_INSTALLER_RUNTIME_GREEN.
- PROCESS_RUNNER_REGRESSION_GREEN.
- FILE_LIST_METADATA_GREEN.
- KNOWLEDGE_SEARCH_SCAN_GREEN.
- RUNTIME_IDENTITY_CACHE_GREEN.
- RUNTIME_METADATA_CACHE_GREEN.
- SMOKE_KNOWLEDGE_ROOTS_ISOLATION_GREEN.
- Business bootstrap self-test on PowerShell 7: GREEN.
- Business bootstrap self-test on Windows PowerShell 5.1: GREEN.
- Final exact deployed 198-tool MCP smoke: GREEN.
- Live batch quote/metacharacter boundary regression: GREEN.
- Live VS developer environment: GREEN with zero internal transport-variable leakage.
- GitHub workflow YAML parse: GREEN.
- git diff --check: GREEN.
- Static TODO/FIXME/HACK/legacy/blocking scan: no material hits.
- NuGet vulnerable-package scan in checked projects: no vulnerable packages reported.
- Gitea tray ModelContextProtocol package: 2.2.0; current package scan clean.
- Context7/official MCP C# SDK lifecycle check: Streamable HTTP client usage, direct tool call and transport disposal aligned with current docs.
- Gitea degraded-path test: Caddy deliberately stopped -> tray status non-green/exit 1 -> tray restart -> backend/proxy healthy again.
- Installed Tray Gitea status/restart integration: GREEN.
- Gitea backend 3001 / proxy 3000 / official MCP 8081: HTTP 200.
- Secure MCP Tunnel: process_running=true, healthy=true, ready=true.
- Hidden watchdog: visible user-session PowerShell window count 0.

## Toolchain verified

- Windows SDK 10.0.28000.0
- Temurin JDK 25.0.4.1 LTS
- Flutter 3.47.5 stable
- Dart 3.13.4 stable
- pnpm 12.4.2
- Yarn Modern 4.18.0
- Bun 1.4.2
- GitHub CLI 2.101.0

## Logging decision

The hourly Talvora-LogMaintenance task bounds tunnel/client and small operational logs and trims completed job stdout/stderr. Live-job stdout/stderr are intentionally left intact until job exit so offsets/full output semantics are preserved.

## Remaining work

No known runtime defect remains from this audit after the Gitea tray/tunnel integration fixes.

This audit batch is finalized under the user's explicit commit/push instruction. Verify the authoritative result with `git status --short --branch` and `git ls-remote --heads origin`.

If a future source/runtime change is made, use the mandatory live-update gate:
source -> targeted test -> canonical installer build -> deploy -> system_info/PID/version-root check -> relevant live behavior -> source-vs-installed SHA256 verification when finalizing.
