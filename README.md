<p align="center">
  <img src="assets/Talvora.png" alt="Talvora" width="112" />
</p>

<h1 align="center">Talvora</h1>

<p align="center">
  <strong>A Windows-native MCP runtime for serious local development and administration.</strong><br />
  One service. Focused Dev/Admin surfaces. Transactional source editing. A desktop Control Center that can actually diagnose and repair its managed components.
</p>

<p align="center">
  <a href="https://github.com/bingoweb/Talvora-MCP/actions/workflows/windows-ci.yml"><img alt="Windows CI" src="https://github.com/bingoweb/Talvora-MCP/actions/workflows/windows-ci.yml/badge.svg?branch=main"></a>
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0B65C2?logo=windows11&logoColor=white">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white">
  <img alt="MCP 250 tools" src="https://img.shields.io/badge/MCP-250%20tools-198754">
  <img alt="Active development" src="https://img.shields.io/badge/status-active%20development-6C757D">
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-2EA44F"></a>
</p>

[Control Center preview](docs/media/control-center.png)
## Current state

[HANDOFF.md](HANDOFF.md) is the short entry point; [STATUS](docs/codex/STATUS.md) is the live queue. Agent rules and approved cards are in [AGENTS.md](AGENTS.md). Historical audits are in [docs/archive](docs/archive).

- **Lean MCP surface:** **250 total**, **213 Dev**, **88 Admin**, with **51 explicitly shared** tools. The former automatic memory and embedding subsystem has been removed.
- One LocalSystem Windows Service owns loopback MCP; the native WPF Tray/Control Center observes health and manages lifecycle, diagnostic repair, events and redacted logs.
- Source edits use revision checks, durable WAL/receipts, syntax validation, atomic commit, replay protection, rollback and recovery.
- Desktop worklog cards retain the user's width, show real code evidence and progress, and expose a persistent session history.

## Architecture

~~~text
                          ChatGPT Business
                         /                \
                 Talvora Dev          Talvora Admin
                   213 tools             88 tools
                         \              /
                          Secure MCP Tunnel
                                 |
                                 v
Local MCP clients ------>  Talvora Windows Service
                            127.0.0.1:7676
                                 |
                  +--------------+--------------+
                  |              |              |
              /mcp           /mcp/dev       /mcp/admin
            250 tools        213 tools        88 tools
                  |
        +----------------+----------------+----------------+
        |                |                |                |
   Source editing     Windows        Toolchains      Integrations
   Git / builds       control        & jobs            & MCP
        |
        +---- Control Center / Tray
              health · lifecycle · diagnostics · repair
~~~

The implementation keeps transport, capability and desktop management separate. The Windows Service is the canonical runtime. Dev/Admin are focused views over that same implementation. The Tray and Control Center observe and manage the runtime rather than replacing it.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the implementation-level model.

## Focused MCP surfaces

Talvora exposes three local endpoints:

| Endpoint | Purpose | Tools |
| --- | --- | ---: |
| <code>/mcp</code> | Complete capability surface | **250** |
| <code>/mcp/dev</code> | Development, source editing, builds, Git, jobs, Penpot and Modal workflows | **213** |
| <code>/mcp/admin</code> | Windows administration, services, registry, environment and machine management | **88** |

Dev and Admin share **51 reviewed tools** for diagnostics and common fallback operations. The overlap is explicit and regression-tested; it is not accidental duplication.

The counts in the badge, summary, diagrams and table are validated from the compiled manifest by `pwsh -NoProfile -File tests/ToolManifestDocumentationRegression.ps1`. The same check validates `docs/ARCHITECTURE.md` and runs in the quality gate.

## Development and administration

Read `talvora_read_source` before ordinary `talvora_apply_patch` edits. Use `talvora_apply_edits` for precomputed exact ranges, ast-grep structural editing for repetitive syntax transformations, and Roslyn semantic editing for C# symbol-aware changes. `talvora_agent_brief` supplies the workspace rules and active card.

Tooling covers .NET/Windows SDK, Git/GitHub, JavaScript, Python, JVM, Go/Rust, mobile/ADB, Docker, SQLite/config formats, archives and networking. [Architecture](docs/ARCHITECTURE.md) documents each suite. Chocolatey is the production package manager; WinGet is excluded.

Compact defaults: Git logs 20 short commits, file discovery 200 relative entries, search 100 matches/300-character lines. Raw Git, complete metadata and larger output remain explicit options; process output keeps head/tail and .NET build/test includes diagnostics.

Repair diagnoses the failed layer, applies the smallest repair, then verifies readiness before escalating. It preserves explicit manual-stop intent. Tunnel control-plane/response delivery and Gitea backend/proxy/MCP/tunnel failures are diagnosed separately.

## Integrations

- **Gitea:** local Git server, Caddy, official Gitea MCP and optional Secure MCP Tunnel, with end-to-end health/repair.
- **Penpot:** self-hosted design environment, local MCP bridge, status/overview/read/call tools and the service-hosted Talvora AI plugin.
- **Modal:** native CLI, app and endpoint tools with locally managed deployment workflows.
- **ChatGPT Business:** optional Secure MCP Tunnel for Dev/Admin; Talvora keeps its loopback listener. Local use needs no OpenAI API key. Run `TALVORA-BUSINESS-KUR.cmd` for optional connection setup.
- **Codex:** the signed-in user's installed CLI and existing ChatGPT session; the installer limits only Codex's registration to eight Dev tools. Business connectors keep their complete surfaces.
- **Remotion:** start with `talvora_remotion_guide`; detached run/status tools create projects, inspect compositions and render stills, MP4/WebM and GIF with desktop progress.

### Optional Claude Code engine

Use `talvora_claude_exec` for bounded requests or `talvora_claude_start` for longer work only when the user explicitly requests Claude Code. Codex tools and the user's developer profile retain their behavior.

Install and log in as the signed-in user in their terminal:

```powershell
& ([scriptblock]::Create((irm https://claude.ai/install.ps1))) stable
& "$env:USERPROFILE\.local\bin\claude.exe" auth login
```

`talvora_claude_info` reports installation/version/auth state without credentials. The bridge resolves `~/.local/bin/claude.exe` and the user's PATH, auth and environment; Claude runs with that user's privileges.

```json
talvora_claude_exec {"workingDirectory":"C:\\path\\to\\repo","prompt":"Bu kodu Claude Code ile düzenle: belirtilen hatayı düzelt.","permissionMode":"auto","timeoutSeconds":1800}
talvora_claude_status {"jobId":"<jobId>","includeResult":true}
```

Omit `model`/`effort` to retain native defaults. Permission modes are `auto`, `default`, `acceptEdits`, `plan`, `dontAsk` and explicit `bypassPermissions`; native policy still applies, with no automatic bypass fallback. Denied actions, missing final results, timeouts and structured errors cannot report success.

Native settings, CLAUDE.md, skills/hooks/plugins/MCP/network behavior are preserved. `extraArguments`, `additionalDirectories` and `environment` retain native options; coding-tool framing is reserved. `talvora_claude_run` accepts native subcommands. Login/TTY setup belongs in the user's terminal: no PTY/interactive stdin. Token output is withheld; results are redacted without argv/environment echoes.

Use `talvora_claude_list`/`cancel` to inspect/cancel jobs and `resumeSessionId` to resume. Both engines share a protected registry: verified surviving processes become `orphaned`, others `failed`. Maximum four jobs, one per directory unless `allowParallel=true`; worklog heartbeat is 25 seconds.

References: [CLI](https://code.claude.com/docs/en/cli-reference), [setup](https://code.claude.com/docs/en/setup), [programmatic execution](https://code.claude.com/docs/en/headless), [permissions](https://code.claude.com/docs/en/permissions).

## Quick start

Requirements: Windows 10/11 x64, an administrator-capable installation account, the SDK pinned by `global.json` for development, and Chocolatey.

From an elevated terminal:

```powershell
git clone https://github.com/bingoweb/Talvora-MCP.git
cd Talvora-MCP
.\TALVORA-KUR.cmd
```

The canonical installer publishes the service and Tray/Control Center, then verifies the deployed MCP surface. Health: `http://127.0.0.1:7676/healthz`; MCP paths: `/mcp`, `/mcp/dev`, `/mcp/admin`.

### Safe reset

`scripts/Reset-And-Install.ps1` is an optional destructive reset. Preview with `pwsh -File scripts/Reset-And-Install.ps1 -WhatIf`; real execution requires administrator rights and `-ConfirmReset`. `-IncludeUserData` is a separate opt-in.

A completed backup under `%ProgramData%\TalvoraBackups\` precedes deletion. `Secrets` is never opened, copied, archived or deleted. `Backups`, `Penpot*`, `ssh`, tunnel state and DPAPI files are preserved by default. Cleanup is limited to Talvora's named shortcuts/processes; personal similarly named items are preserved. See [development environment](docs/DEVELOPMENT-ENVIRONMENT.md).

## Engineering quality

Run `pwsh -NoProfile -File scripts/Invoke-QualityGate.ps1` for locked restore, Release build, PowerShell/policy checks, behavioral suites, live/installed MCP checks and desktop smoke. `-Scope Changed` selects affected checks. CI also builds and installs the canonical artifact; local interactive checks cover the real desktop and both themes.

## Security and license

The service runs as LocalSystem; focused endpoints organize discovery and are not privilege sandboxes. Persistent logs redact common credential forms; tunnel credentials are separate and protected in the interactive user context. General process/PowerShell tools remain available. Read [SECURITY.md](SECURITY.md) before exposing or redistributing Talvora.

Original code and documentation use the [MIT License](LICENSE): commercial use and modification are permitted with the copyright/permission notice retained; software is supplied as is, without warranty.

### Third-party software & license attribution

Third parties retain their own licenses, copyrights and trademarks. [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) is the complete versioned inventory of locked NuGet runtime packages, self-contained .NET runtime packs and managed companions. Upstream notices remain authoritative.

## Documentation

- [Current state](HANDOFF.md) · [Live queue](docs/codex/STATUS.md) · [Architecture](docs/ARCHITECTURE.md)
- [Development environment](docs/DEVELOPMENT-ENVIRONMENT.md) · [Engineering policy](docs/MODERNIZATION-POLICY.md)
- [Application roadmap](APPLICATION-DEVELOPMENT-ROADMAP.md) · [Source-edit architecture](SOURCE-EDIT-ENGINE-ARCHITECTURE.md)
- [Historical handoff](docs/archive/HANDOFF.md) · [Historical audit](docs/archive/BUG-AUDIT.md)
