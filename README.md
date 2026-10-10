<p align="center">
  <img src="assets/Talvora.png" alt="Talvora" width="112" />
</p>

<h1 align="center">Talvora</h1>

<p align="center">
  <strong>A Windows-native MCP runtime for serious local development and administration.</strong><br />
  One service. Focused Dev/Admin surfaces. Transactional source editing. A desktop Control Center that can actually diagnose and repair its managed components.
</p>

<p align="center">
  <sub>Windows için yerel MCP sunucusu: yapay zekâ destekli yazılım geliştirme, kaynak kod düzenleme ve sistem yönetimi.</sub>
</p>

<p align="center">
  <a href="https://github.com/bingoweb/Talvora-MCP/actions/workflows/windows-ci.yml"><img alt="Windows CI" src="https://github.com/bingoweb/Talvora-MCP/actions/workflows/windows-ci.yml/badge.svg?branch=main"></a>
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0B65C2?logo=windows11&logoColor=white">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white">
  <img alt="MCP 234 tools" src="https://img.shields.io/badge/MCP-234%20tools-198754">
  <img alt="Active development" src="https://img.shields.io/badge/status-active%20development-6C757D">
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-2EA44F"></a>
</p>

<p align="center">
  <a href="LICENSE">MIT License</a>
  ·
  <a href="docs/ARCHITECTURE.md">Architecture</a>
  ·
  <a href="docs/DEVELOPMENT-ENVIRONMENT.md">Development environment</a>
  ·
  <a href="docs/MODERNIZATION-POLICY.md">Engineering policy</a>
  ·
  <a href="SECURITY.md">Security & privacy</a>
  ·
  <a href="THIRD-PARTY-NOTICES.md">Third-party notices</a>
  ·
  <a href="BUG-AUDIT.md">Engineering audit</a>
</p>

---

<p align="center">
  <img src="docs/media/control-center.png" alt="Native Talvora Control Center with navigation, health cards and MCP services" width="100%" />
</p>

<p align="center">
  <sub>Actual WPF Control Center smoke-test render from the current Windows build. Health reflects the test workstation.</sub>
</p>

## What's new

- **Rebuilt desktop experience:** a Stitch-inspired, native WPF Control Center with clear sidebar navigation, live status cards, full-width MCP service rows and a collapsible setup panel.
- **Honest service health:** intentionally stopped or on-demand MCPs are neutral, not red errors. Genuine outages and warnings remain visible.
- **User-controlled notifications:** code output may expand the notification vertically without changing its user-set width; non-code updates return to a compact layout.
- **Lean MCP surface:** **234 total**, **197 Dev**, **88 Admin**, with **51 explicitly shared** tools. The former automatic memory and embedding subsystem has been removed.
- **Reproducible Windows delivery:** locked dependencies, GitHub Actions, a canonical installer and installed-runtime MCP smoke tests.

## Why Talvora exists

Talvora started from a practical problem: I wanted an AI coding client to work on a real Windows workstation as a development machine, not as a thin command wrapper and not as a pretend Linux sandbox.

That means dealing with the things Windows development actually involves: services, interactive user sessions, long-running processes, source edits, Git, SDKs, package managers, Event Log, registry, installers, local databases, desktop applications and repairable background infrastructure.

Talvora packages those workflows behind MCP without turning every operation into an opaque shell command. Common tasks have structured tools. Unusual tasks still have an escape hatch. The local runtime stays on loopback.

The project is deliberately opinionated:

- Windows is a first-class platform.
- One canonical Windows Service owns the runtime.
- One canonical installer owns deployment.
- Chocolatey is the Windows package manager.
- Structured tools improve reliability; they are not the capability ceiling.
- A change is not finished until the installed runtime is verified.

## Current system at a glance

| Area | Current implementation |
| --- | --- |
| **Runtime** | Windows Service, LocalSystem, loopback MCP on <code>127.0.0.1:7676</code> |
| **MCP surfaces** | Full, Dev and Admin endpoints with live tool discovery and a shared-tool policy |
| **Source editing** | Revision-aware transactional edits, WAL/receipt recovery, rollback, Roslyn and ast-grep specialists |
| **Control Center** | Native WPF desktop UI, live health, component lifecycle, event timeline, real diagnostic repair |
| **Transport** | Local-first; ChatGPT Business uses optional Secure MCP Tunnel connections |
| **Integrations** | Gitea, Penpot, Modal, GitHub tooling, Windows toolchains and local developer infrastructure |
| **Release discipline** | Locked dependencies, source regressions, installer provenance and installed-runtime smoke verification |

> Talvora is high-capability software. The focused Dev/Admin endpoints improve discovery and tool selection; they are not security sandboxes. See [Security & Privacy](SECURITY.md) before exposing or redistributing the runtime.

## Architecture

~~~text
                          ChatGPT Business
                         /                \
                 Talvora Dev          Talvora Admin
                   197 tools             88 tools
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
            234 tools        197 tools        88 tools
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
| <code>/mcp</code> | Complete capability surface | **234** |
| <code>/mcp/dev</code> | Development, source editing, builds, Git, jobs, Penpot and Modal workflows | **197** |
| <code>/mcp/admin</code> | Windows administration, services, registry, environment and machine management | **88** |

Dev and Admin share **51 reviewed tools** for diagnostics and common fallback operations. The overlap is explicit and regression-tested; it is not accidental duplication.

Examples of intentional role separation:

- package installation and persistent machine environment changes are Admin-oriented;
- long-running development jobs are Dev-oriented;
- read-only process/network/file diagnostics can be shared;
- the full <code>/mcp</code> endpoint preserves the complete tool set.

## Control Center

The Control Center is a native WPF desktop application, not a web dashboard bolted onto the service.

It provides:

- live health for Talvora Core, Talvora Dev, Talvora Admin and managed integrations;
- a light, Stitch-inspired layout with real sidebar navigation, snapshot-derived metrics and full-width service rows;
- neutral status for intentionally stopped or on-demand integrations;
- clear component roles instead of treating every connection as another service;
- start, stop and restart lifecycle actions;
- OpenAI tunnel-client v0.0.16 component health for control-plane polling, response delivery, queue pressure, dispatcher activity and tunnel-side MCP observations;
- an event timeline and redacted raw-log view;
- responsive desktop layout and keyboard/UI Automation support;
- visual smoke tests against the rendered WPF surface.

### Repair is diagnostic, not a renamed restart

The current repair path follows a simple rule:

~~~text
diagnose
   |
   v
repair the smallest failed layer
   |
   v
verify readiness
   |
   +---- ready ------> report success
   |
   +---- still broken
             |
             v
      controlled escalation
             |
             v
        verify again
~~~

For example, if a secure tunnel is down while the local MCP remains healthy, Talvora reconnects the tunnel instead of restarting the service. With tunnel-client v0.0.16, Control Center can also distinguish critical control-plane or response-delivery degradation from healthy queue/dispatcher activity and renew only the tunnel runtime when that narrower repair is sufficient. The same approach is used for the Gitea chain: backend, proxy, MCP process and tunnel can be repaired independently before a full-chain restart is considered.

Manual stop intent is preserved; automatic repair does not silently undo a user's explicit stop decision.

## Transactional source editing

Source editing is a first-class subsystem rather than a wrapper around <code>git apply</code>.

The normal workflow is:

~~~text
talvora_read_source
        |
        | revision
        v
talvora_apply_patch
        |
        +--> optimistic concurrency check
        +--> durable transaction / WAL
        +--> syntax validation
        +--> atomic commit
        +--> receipt / replay protection
        +--> rollback and recovery on failure
~~~

Specialist paths are available when the job calls for them:

- <code>talvora_apply_patch</code> — default repository editor;
- <code>talvora_apply_edits</code> — exact precomputed ranges;
- <code>talvora_structural_edit</code> — ast-grep structural transformations;
- <code>talvora_semantic_edit</code> — Roslyn symbol-aware C# edits.

The source-edit regression suite covers stale revisions, concurrent writers, crash recovery, WAL corruption, encoding/newline preservation, rollback, transaction replay and large-edit scenarios.


## Developer and machine tooling

Talvora covers the workflows I actually use on Windows rather than trying to hide them behind one generic executor.

| Area | Examples |
| --- | --- |
| **.NET / Windows** | dotnet, MSBuild, Visual Studio discovery, Windows SDK, CMake, Ninja, SignTool, MakeAppx, MakePri, RC, MT |
| **Git / GitHub** | status, diff, log, branches, generic Git execution, GitHub CLI |
| **JavaScript** | Node.js, npm, pnpm, Yarn Modern, Bun |
| **Python** | interpreter discovery, venv, pip and general execution |
| **JVM** | Java, javac, Gradle, Maven |
| **Native** | Go, Rust, Cargo, rustup |
| **Mobile** | Flutter, Dart, Android SDK, ADB, emulator |
| **Containers** | Docker, images, logs, exec and Compose |
| **Windows** | processes, services, registry, environment, Event Log, interactive sessions |
| **Networking** | DNS, ping, TCP listeners/connections, TLS inspection, HTTP and readiness checks |
| **Data/config** | SQLite, JSON, YAML, TOML, XML, INI, dotenv and archives |

Chocolatey is the only Windows package manager used by the production/bootstrap path. WinGet is intentionally excluded.

## Integrations

### Gitea

The reference workstation uses a local Gitea stack alongside GitHub:

~~~text
Git / Talvora
     |
     v
   Gitea ---- Caddy
     |
 Official Gitea MCP
     |
 Secure MCP Tunnel
~~~

The Control Center tracks the chain end-to-end and can repair the failed layer without blindly restarting healthy services.

### Penpot

Talvora integrates with a self-hosted Penpot environment and its local MCP bridge. The Dev surface includes Penpot status, overview, safe read and generic call wrappers. A Talvora AI plugin is served directly by the Talvora Windows Service for local design workflows.

### Modal

The Dev surface includes native Modal CLI/app/endpoint tools and supports locally managed deployment workflows without introducing a second general-purpose MCP layer.

### ChatGPT Business

ChatGPT Business connectivity is optional. Talvora itself remains loopback-only.

The Business setup uses OpenAI Secure MCP Tunnel to attach the focused Dev/Admin endpoints without exposing a public Talvora listener.

## Quick start

### Requirements

- Windows 10 or Windows 11, x64
- Administrator-capable account for installation
- .NET SDK pinned by <code>global.json</code> for development builds
- Chocolatey for Windows package management

### Install or rebuild

Clone the repository, then install from an **elevated Windows PowerShell or Terminal**:

~~~powershell
git clone https://github.com/bingoweb/Talvora-MCP.git
cd Talvora-MCP
.\TALVORA-KUR.cmd
~~~

The canonical installer publishes the runtime, installs the Windows Service, updates the Tray/Control Center and verifies the deployed MCP surface.

Health endpoint:

~~~text
http://127.0.0.1:7676/healthz
~~~

MCP endpoints:

~~~text
http://127.0.0.1:7676/mcp
http://127.0.0.1:7676/mcp/dev
http://127.0.0.1:7676/mcp/admin
~~~

### Optional ChatGPT Business connection

~~~bat
TALVORA-BUSINESS-KUR.cmd
~~~

Talvora does not require an OpenAI API key to run locally. The Business tunnel credential belongs to the transport layer and is stored separately from the Talvora runtime.

## Engineering quality

The repository is built around the installed runtime, not around the assumption that a successful compile means the product works.

The Windows CI pipeline verifies, among other things:

1. locked dependency restore;
2. Release build;
3. PowerShell syntax;
4. Chocolatey / no-WinGet policy;
5. Context7 + vendor-documentation quality gate;
6. focused source regressions;
7. live MCP surface policy;
8. ChatGPT Business bootstrap self-test;
9. canonical native installer creation;
10. LocalSystem installation and health verification;
11. installed-runtime MCP smoke tests.

Local release work goes further where a hosted runner cannot reproduce an interactive Windows desktop session: Control Center WPF visual smoke, focused Business connection probes, installer identity checks and targeted live acceptance tests are run against the installed build.

## Security and trust model

Talvora is intentionally powerful.

- The MCP listener binds to loopback by default.
- The Windows Service runs as LocalSystem.
- Focused Dev/Admin endpoints are discovery boundaries, not privilege sandboxes.
- Persistent logs redact common credential forms.
- Business tunnel credentials are kept separate from the runtime and protected in the interactive user context.
- Public documentation must not contain live secrets, private keys or machine-specific credentials.
- General-purpose process and PowerShell tools remain available by design.

If you are evaluating or redistributing Talvora, read [SECURITY.md](SECURITY.md) first.

## License

**Talvora's original code and project documentation are released under the [MIT License](LICENSE).** Anyone may use, copy, fork, modify, merge, publish, redistribute, sublicense, or sell copies—including in commercial or closed-source projects.

The license only requires that the original copyright and permission notice accompany copies or substantial portions of Talvora. The software is provided **as is, without warranty**.

**Türkçe:** Talvora'nın özgün kodunu herkes ücretsiz olarak indirebilir, çoğaltabilir, değiştirebilir, paylaşabilir ve ticari projelerinde kullanabilir. Kopyalarda telif hakkı ve MIT lisans bildirimi korunmalıdır.

Third-party libraries, bundled runtime components, optional integrations, and upstream trademarks remain subject to their **own** rights and licenses. See [Third-Party Notices](THIRD-PARTY-NOTICES.md).

## Third-party software & license attribution

Talvora is built on open-source software and also integrates with separately owned tools. Those projects keep their own copyright, license, and trademark rights; Talvora does not relicense them or present them as Talvora-owned work.

| Component family | Upstream / attribution | License | Relationship |
| --- | --- | --- | --- |
| **.NET 10, Roslyn, Microsoft.Extensions** | Microsoft | MIT | bundled / self-contained runtime |
| **Model Context Protocol C# SDK** | Model Context Protocol contributors | Apache-2.0 | bundled |
| **WPF UI** | Leszek Pomianowski and WPF UI Contributors | MIT | bundled Control Center UI |
| **SQLite / SQLitePCLRaw** | SQLite project / Eric Sink | Public Domain / Apache-2.0 | bundled |
| **YamlDotNet / Tomlyn** | Antoine Aubry / Alexandre Mutel | MIT / BSD-2-Clause | bundled |
| **Google.Protobuf / Humanizer.Core** | Google Inc. / Humanizer contributors | BSD-3-Clause / MIT | transitive bundled dependencies |
| **Gitea / official Gitea MCP / Caddy** | Gitea Authors / Caddy Authors | MIT / MIT / Apache-2.0 | optional managed local stack |
| **Penpot / Penpot MCP** | KALEIDOS SUBSIDIARY SL and Penpot contributors | MPL-2.0 | optional design integration |
| **OpenAI Secure MCP Tunnel / Modal SDK** | OpenAI / Modal Labs | Apache-2.0 / Apache-2.0 | optional external integrations |

The complete, versioned inventory covers the locked NuGet runtime packages, self-contained .NET runtime packs, and managed companion components:

**[Read THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)**

Upstream license and notice files remain authoritative. Product and company names are used only for attribution and interoperability; their trademarks belong to their respective owners.

## Repository map

| Path | Purpose |
| --- | --- |
| <code>src/Talvora</code> | MCP host and application/development tool implementations |
| <code>src/Talvora.Shared</code> | shared contracts, focused-surface policy and canonical tool manifest |
| <code>src/Talvora.Tray</code> | Tray, Control Center, repair/recovery and desktop UX |
| <code>src/Talvora.Installer</code> | canonical native Windows installer |
| <code>tests/Talvora.Smoke</code> | MCP surface and installed-runtime smoke validation |
| <code>tests/Talvora.SourceEdit.Regression</code> | transactional source-edit regression suite |
| <code>scripts</code> | installer, bootstrap, verification and operational scripts |
| <code>docs</code> | architecture, development environment and engineering policy |
| <code>THIRD-PARTY-NOTICES.md</code> | versioned third-party ownership, license, and integration inventory |
| <code>HANDOFF.md</code> | canonical project handoff |
| <code>BUG-AUDIT.md</code> | living engineering audit |

## Design decisions

A few project choices are deliberate and unlikely to change casually:

- **Windows-native over cross-platform abstraction.**
- **One service over a collection of hidden helper daemons.**
- **Local-first over public ingress.**
- **Chocolatey over multiple Windows package-manager paths.**
- **Structured operations first, general execution still available.**
- **Minimal repair before restart.**
- **Exact installed-runtime verification before calling work complete.**

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Development environment](docs/DEVELOPMENT-ENVIRONMENT.md)
- [Modernization policy](docs/MODERNIZATION-POLICY.md)
- [Security & privacy](SECURITY.md)
- [Third-party software notices](THIRD-PARTY-NOTICES.md)
- [Application-development roadmap](APPLICATION-DEVELOPMENT-ROADMAP.md)
- [Source Edit architecture](SOURCE-EDIT-ENGINE-ARCHITECTURE.md)
- [Engineering audit](BUG-AUDIT.md)

---

<p align="center">
  <strong>Talvora is built for a real Windows workstation: local, inspectable, repairable and explicit about what it can do.</strong>
</p>
