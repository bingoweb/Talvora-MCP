# Talvora MCP — Application Development Capability Roadmap

Date: 2026-09-19
Branch: refactor/deep-codebase-cleanup
Baseline manifest: 159 tools
Current development manifest: 198 tools
Repository: C:\Users\tayla\Talvora-MCP

## Goal

Turn Talvora into a first-class Windows-native software/application development MCP that can build, test, run, inspect, package, and troubleshoot projects across the major ecosystems already used on this machine, without artificial path/command/package/host/user/session restrictions.

## Standing implementation rules

- Windows-native implementation.
- Full-capability / TAM YETKI surface; arbitrary CLI arguments remain available.
- Chocolatey is the machine package manager. Do not use WinGet.
- Prefer a project's own wrapper when the ecosystem provides one (for example gradlew.bat or mvnw.cmd), then fall back to the machine installation.
- Reuse Talvora.Shared ProcessRunner and CommandResolver rather than creating new process-launch copies.
- Do not add Playwright to Talvora.
- Do not add aliases merely to increase tool count.
- Add focused typed tools where they materially improve discovery/automation, while retaining an unrestricted explicit-admin *-run path.
- Use Context7 plus official vendor documentation before implementing or changing third-party CLI behavior.
- Install missing required software immediately with Chocolatey, using the newest supported stable release; use the current LTS only where the vendor's non-LTS line is near end-of-support or unsuitable as a machine default.
- Do not retain deprecated CLIs, legacy SDK layouts, old runtime fallbacks, or compatibility aliases once an official modern replacement exists.
- Project-local wrappers are toolchain pinning, not legacy compatibility. New or upgraded projects should use current stable wrapper/toolchain versions.
- Validate only the changed capability. Do not rerun broad smoke/regression suites unless shared infrastructure changes.
- No Git commit/push until explicitly requested.

## Canonical source-edit routing contract (Faz 14 — CORE + STRUCTURAL + SEMANTIC IMPLEMENTED)

Detailed architecture contract: `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`.

- Inside recognized development workspaces, normal text/source/doc/config mutation will use the canonical Source Edit Engine rather than legacy whole-file/regex/shell write paths.
- PRIMARY/default for ordinary agent-authored code/doc/config changes -> `talvora_apply_patch`, whether one file or many; add/update/delete/move and unified-diff compatibility all stay on this tool.
- `talvora_apply_edits` is a specialist only when exact zero-based UTF-16 ranges/revisions are already known or generated programmatically. Multi-file scope alone is not a reason to select it.
- Existing source files are read with `talvora_read_source` before mutation for the SHA-256 revision handshake.
- If tool choice is unclear, call read-only `talvora_source_edit_guide`; never discover the editor by mutation-tool trial-and-error.
- Repetitive syntax-shaped transformations -> `talvora_structural_edit` (ast-grep proposal-only isolated mirror -> exact Source Edit transaction). Supports pattern/kind + rewrite and YAML rule/fix.
- C# operations that genuinely require solution/project semantic identity -> `talvora_semantic_edit`. Current capability is symbol-aware rename. Ordinary C# edits still use PRIMARY/default `talvora_apply_patch`; semantic or multi-file scope alone never demotes the primary editor.
- Roslyn loads the requested solution/project in memory, resolves semantic identity, produces a changed `Solution`, then converts only actual changed documents into exact revisioned UTF-16 Source Edit changes. It never calls `Workspace.TryApplyChanges` and never writes live source directly.
- Git unified diff remains a compatibility input/backend; it is not the canonical agent patch architecture.
- Fuzzy matching may suggest candidates but must never silently authorize a mutation.
- Legacy direct text/config mutation plus development-workspace source-file delete and same-workspace source-file move are guarded server-side with `SOURCE_EDIT_POLICY_VIOLATION`; directory/generated/binary/non-workspace and cross-workspace filesystem capability remains available, and general shell/process capability remains unrestricted.
- MCP server instructions + tool titles/descriptions + read-only routing guide + server policy + regressions encode one centralized Routing Contract v3 decision.
- Faz 14 core + ast-grep structural adapter + Roslyn semantic adapter are exact-installed live verified at 204/204 unique MCP tools. Canonical deploy/live evidence snapshot is `sourceCommit=747cbfc560c8f7e9d4c3def699ee986d5c164a71-dirty-8726cde50619`; latest two-project semantic rename preserves UTF-16 LE BOM/CRLF/deliberate spacing and same-transaction `replayed=true` is GREEN. Living-doc closeout follows this deploy snapshot without a self-referential docs-only rebuild loop.

## Phase 1 — JVM build stack (FIRST)

Status: DONE

Add first-class Java/JDK, Gradle, and Maven support.

Planned tools:
- talvora_java_info
- talvora_java_run
- talvora_javac_run
- talvora_gradle_info
- talvora_gradle_run
- talvora_maven_info
- talvora_maven_run

Behavior:
- Detect java/javac/JAVA_HOME and report versions.
- Gradle: prefer project-local gradlew.bat; otherwise resolve machine gradle.
- Maven: prefer project-local mvnw.cmd; otherwise resolve machine mvn/mvn.cmd.
- Preserve arbitrary Gradle tasks/options and Maven goals/options.
- Allow environment overrides, arbitrary working directories, and caller-controlled timeout.
- No task/goal/plugin/repository allowlist.

Dependencies:
- JDK machine default: Eclipse Temurin 25 LTS (25.0.4.1+1 installed); older JDK 21/JDK 8 defaults removed.
- Gradle machine installation only as fallback.
- Maven machine installation only as fallback. Installed for this phase: Apache Maven 3.9.16 via Chocolatey.
- If any fallback CLI is missing, install it with Chocolatey.

Targeted verification:
- Release compile of Talvora.
- Temporary Java source compile/run.
- Temporary Gradle wrapper or installed-Gradle project command.
- Temporary Maven project command.
- No broad MCP smoke run.

## Phase 2 — Go + Rust native/application development

Status: DONE

Planned tools:
- talvora_go_info
- talvora_go_run
- talvora_rust_info
- talvora_cargo_run
- talvora_rustc_run
- talvora_rustup_run

Behavior:
- Detect Go root/version and Rust/Cargo/rustup toolchain state.
- Unrestricted go and cargo argument vectors.
- Support project-local environment and target/toolchain arguments.
- Keep cross-compilation and arbitrary registries/targets available.

Targeted verification:
- Build/run a temporary minimal Go program.
- cargo check/build on a temporary minimal crate.

## Phase 3 — Flutter, Dart, Android SDK and device tooling

Status: DONE

Planned tools:
- talvora_flutter_info
- talvora_flutter_run
- talvora_dart_run
- talvora_android_sdk_info
- talvora_adb_run
- talvora_android_cli_run
- talvora_emulator_run

Behavior:
- Discover Flutter/Dart SDKs.
- Discover ANDROID_HOME/ANDROID_SDK_ROOT, platform-tools, cmdline-tools, emulator, installed platforms/build-tools.
- Expose unrestricted Flutter/Dart/ADB/modern Android CLI/emulator surfaces.
- Use Android CLI `android sdk` for SDK package management; deprecated `sdkmanager` is intentionally excluded.
- Do not restrict devices, packages, SDK components, ports, or emulator arguments.

Targeted verification:
- Version/info calls.
- adb devices.
- modern Android CLI version/path verification plus `android sdk` surface verification.
- Flutter/Dart analyze/build only on a temporary fixture when needed.

## Phase 4 — Modern JavaScript package/runtime expansion

Status: DONE

Planned tools:
- talvora_pnpm_info
- talvora_pnpm_run
- talvora_yarn_info
- talvora_yarn_run
- talvora_bun_info
- talvora_bun_run

Behavior:
- Respect packageManager declarations and lockfiles.
- Prefer project-pinned/package-manager-managed versions where available.
- Retain unrestricted npm surface already present.
- Do not install Yarn Classic; Yarn Modern is installed directly from @yarnpkg/cli-dist.
- Do not add a Corepack MCP surface; Node 25+ no longer distributes Corepack.
- Installed current versions for this phase: pnpm 12.4.2, Yarn Modern 4.18.0, Bun 1.4.2, npm 12.0.2.
- Install only missing machine fallbacks when necessary.

## Phase 5 — Workspace intelligence

Status: DONE

Extend project discovery into actionable workspace understanding.

Planned capabilities:
- talvora_workspace_inspect
- talvora_workspace_commands

Inspect:
- solutions/projects/workspaces,
- package manifests and lockfiles,
- SDK/toolchain pinning files,
- Gradle/Maven wrappers,
- Cargo/Go modules,
- Flutter/Android markers,
- Docker/Compose,
- common test/build scripts,
- likely build/test/run entry points.

The output should be structured and deterministic; it must not execute project code merely to inspect the workspace.

## Phase 6 — Quality, diagnostics, and test artifacts

Status: DONE

Planned capabilities:
- coverage summary for Cobertura, JaCoCo, lcov and common .NET coverage formats,
- compiler/linter diagnostic normalization where a structured parser adds real value,
- test report aggregation beyond current generic report summary,
- build artifact inventory with hashes/version metadata.

Do not wrap every linter individually when unrestricted ecosystem CLI tools already cover it.

## Phase 7 — Windows packaging and release engineering

Status: DONE

Planned capabilities:
- talvora_windows_release_tools_info
- talvora_signtool_run
- talvora_rc_run
- talvora_mt_run
- talvora_makeappx_run
- talvora_makepri_run
- Latest-installed Windows SDK discovery; no hardcoded legacy SDK fallback.
- MSIX/AppX packaging via current Windows SDK tools.
- Optional modern WiX only if a project requires MSI authoring; Inno Setup is not installed merely for compatibility.
- Release artifact inventory/checksum support is provided by Phase 6.

Signing tools retain arbitrary certificate/provider/timestamp arguments rather than imposing a policy layer.

Environment note (2026-09-19): Microsoft lists Windows SDK 10.0.28000.2705 as the latest stable release. The machine currently has 10.0.26100.0 installed. The official 28000 installer was downloaded and verified, but its Burn bootstrapper returns Windows Installer error 1618 even while msiserver is stopped and leaves a stuck child process tree. The stuck tree was cleaned. Re-run the official SDK upgrade after a Windows reboot; Talvora dynamically selects the highest installed SDK version so no code change will be required.

## Phase 8 — Developer service/data tooling

Status: DONE (covered by existing Docker capability; no redundant native wrappers)

Current decision:
- Do not install native Windows PostgreSQL/MySQL/Redis servers merely to duplicate Docker.
- Do not install unofficial/legacy Windows Redis ports.
- Existing unrestricted talvora_docker_run / talvora_docker_exec / talvora_docker_compose_run already cover current PostgreSQL, MySQL and Redis images without adding artificial engine/image restrictions.
- Add a database-specific client only when a real project requires local native CLI behavior that Docker cannot provide.

## Phase 9 — GitHub/repository workflow tooling

Status: DONE

Tools:
- talvora_gh_info
- talvora_gh_run

Use GitHub CLI without limiting repository, issue, pull request, release, workflow, API, extension, host, or authentication commands.
Installed current GitHub CLI 2.101.0 via Chocolatey.

## Architecture guidance

New ecosystem tools should normally live in focused partial families instead of new monoliths, for example:

- BuildRunnerTools.Jvm.cs
- BuildRunnerTools.GoRust.cs
- BuildRunnerTools.JavaScriptExtras.cs
- MobileTools.Flutter.cs
- MobileTools.Android.cs

Shared CLI execution should remain centralized around Talvora.Shared.ProcessRunner and CommandResolver.

## Manifest/tool-count policy

The tool count increases only for genuinely new capabilities. TalvoraToolManifest remains the canonical list; tests and CI must derive expectations from it. Legacy aliases stay removed.

## Current working-tree note

Before this roadmap, GitTools.cs and SqliteTools.cs already contained targeted, uncommitted fixes from the previous continuation step. Preserve those changes. Do not fold them away or reset them.

## Execution order

1. Phase 1 JVM stack.
2. Phase 2 Go/Rust.
3. Phase 3 Flutter/Android.
4. Phase 4 JavaScript extras.
5. Phase 5 workspace intelligence.
6. Phase 6 diagnostics/artifacts.
7. Phase 7 Windows packaging.
8. Phase 8 service/data tools as demanded by projects.
9. Phase 9 GitHub CLI workflow support.

Each phase is implemented, targeted-tested, and left in the working tree. Commit/push happens only when explicitly requested.

## Final implementation status

All nine roadmap phases are implemented as of 2026-09-19.

Current canonical development surface: 198 MCP tools.

Targeted verification completed phase-by-phase; broad smoke/regression suites were intentionally not rerun.

One environment update remains outside the Talvora code changes:
- Microsoft Windows SDK 10.0.28000.2705 is the latest stable SDK.
- Its official installer is downloaded and verified at C:\WINDOWS\TEMP\winsdksetup-10.0.28000.2705.exe.
- The current Windows session returns installer error 1618 from the Burn bootstrapper even though Windows Installer service is stopped and no msiexec process is active.
- Reboot Windows, then rerun that official installer once. Talvora already selects the highest installed Windows SDK dynamically.