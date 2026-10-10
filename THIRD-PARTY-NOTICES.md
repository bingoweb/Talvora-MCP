# Third-Party Software Notices

Talvora includes, redistributes, or interoperates with third-party software. This document records the upstream attribution, license, version, and relationship to Talvora for the current Windows build.

This file is an attribution inventory. The upstream license text is authoritative. Nothing in this document transfers ownership of third-party software, trademarks, or other intellectual property to Talvora.

Talvora's own licensing is separate from the licenses listed here.

## Scope and maintenance

The bundled runtime inventory is derived from:

- `src/Talvora/packages.lock.json`
- `src/Talvora.Tray/packages.lock.json`
- the self-contained .NET 10 Windows runtime packs used by the installer

Optional integrations are listed separately because Talvora may manage or invoke them without incorporating them into the Talvora core binary.

When a bundled dependency, model, or managed integration changes, this notice must be reviewed in the same change.

---

## Bundled with the Talvora runtime

### Microsoft .NET, Roslyn, data, hosting, and Windows libraries

**Upstream / copyright attribution:** Microsoft  
**Primary license:** MIT  
**Relationship:** bundled directly or transitively in the Talvora service, Tray/Control Center, or self-contained Windows runtime.

The current locked package set includes:

- `Microsoft.Build.Framework 17.14.28`
- `Microsoft.Build.Locator 1.11.2`
- `Microsoft.CodeAnalysis.Analyzers 5.9.0`
- `Microsoft.CodeAnalysis.Common 5.9.0`
- `Microsoft.CodeAnalysis.CSharp 5.9.0`
- `Microsoft.CodeAnalysis.CSharp.Workspaces 5.9.0`
- `Microsoft.CodeAnalysis.Workspaces.Common 5.9.0`
- `Microsoft.CodeAnalysis.Workspaces.MSBuild 5.9.0`
- `Microsoft.Data.Sqlite 10.0.12`
- `Microsoft.Data.Sqlite.Core 10.0.12`
- `Microsoft.Extensions.AI.Abstractions 10.10.1`
- `Microsoft.Extensions.Caching.Abstractions 10.0.12`
- `Microsoft.Extensions.Configuration.Abstractions 10.0.12`
- `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.12`
- `Microsoft.Extensions.Diagnostics.Abstractions 10.0.12`
- `Microsoft.Extensions.FileProviders.Abstractions 10.0.12`
- `Microsoft.Extensions.Hosting.Abstractions 10.0.12`
- `Microsoft.Extensions.Hosting.WindowsServices 10.0.12`
- `Microsoft.Extensions.Logging.Abstractions 10.0.12`
- `Microsoft.Extensions.Options 10.0.12`
- `Microsoft.Extensions.Primitives 10.0.12`
- `Microsoft.NET.ILLink.Tasks 10.0.12`
- `Microsoft.VisualStudio.SolutionPersistence 1.0.52`
- `System.Composition 10.0.12`
- `System.Composition.AttributedModel 10.0.12`
- `System.Composition.Convention 10.0.12`
- `System.Composition.Hosting 10.0.12`
- `System.Composition.Runtime 10.0.12`
- `System.Composition.TypedParts 10.0.12`
- `System.Numerics.Tensors 9.0.0`
- `System.ServiceProcess.ServiceController 10.0.12`

The self-contained installer also carries the matching Windows runtime packs:

- `Microsoft.NETCore.App.Runtime.win-x64 10.0.12`
- `Microsoft.WindowsDesktop.App.Runtime.win-x64 10.0.12`
- `Microsoft.AspNetCore.App.Runtime.win-x64 10.0.12`

**Important:** The .NET runtime contains third-party components. Upstream third-party notices remain applicable in addition to the MIT license of the primary projects.

Upstream:

- https://github.com/dotnet/dotnet
- https://github.com/dotnet/roslyn

### Model Context Protocol C# SDK

**Upstream / attribution:** Model Context Protocol project contributors  
**License:** Apache-2.0  
**Relationship:** bundled.

- `ModelContextProtocol 2.2.0`
- `ModelContextProtocol.AspNetCore 2.2.0`
- `ModelContextProtocol.Core 2.2.0`

Upstream: https://github.com/modelcontextprotocol/csharp-sdk

### WPF UI

**Upstream / attribution:** Leszek Pomianowski and WPF UI Contributors  
**NuGet author metadata:** lepo.co  
**License:** MIT  
**Relationship:** bundled in the native Control Center / Tray UI.

- `WPF-UI 4.3.0`
- `WPF-UI.Abstractions 4.3.0`

Upstream: https://github.com/lepoco/wpfui

WPF UI documents that Segoe Fluent Icons is a Microsoft font and is not redistributed by WPF UI. Talvora likewise does not claim ownership of that font.

### SQLite and SQLitePCLRaw

**SQLite:** public domain software. The `SQLite 3.53.4` NuGet package is authored by Eric Sink; SQLite itself is dedicated to the public domain.

**SQLitePCLRaw:** Eric Sink, Apache-2.0.

Current bundled packages:

- `SQLite 3.53.4` — Public Domain
- `SQLitePCLRaw.bundle_e_sqlite3 3.0.5` — Apache-2.0
- `SQLitePCLRaw.config.e_sqlite3 3.0.5` — Apache-2.0
- `SQLitePCLRaw.core 3.0.5` — Apache-2.0
- `SQLitePCLRaw.provider.e_sqlite3 3.0.5` — Apache-2.0

Upstream:

- https://sqlite.org/
- https://github.com/ericsink/SQLitePCL.raw

### Configuration, serialization, and utility libraries

| Package | Version | Upstream attribution | License |
| --- | ---: | --- | --- |
| `YamlDotNet` | 18.1.0 | Antoine Aubry and contributors | MIT |
| `Tomlyn` | 2.10.1 | Alexandre Mutel and contributors | BSD-2-Clause |
| `Google.Protobuf` | 3.30.2 | Google Inc. / Protocol Buffers contributors | BSD-3-Clause |
| `Humanizer.Core` | 3.0.10 | Claire Novotny, Mehdi Khalili, and contributors | MIT |

Upstream:

- https://github.com/aaubry/YamlDotNet
- https://github.com/xoofx/Tomlyn
- https://github.com/protocolbuffers/protobuf
- https://github.com/Humanizr/Humanizer


---

## Optional or managed local integrations

The following projects are not Talvora-owned. They are separate products or companion components that Talvora may install, manage, connect to, or invoke.

| Component | Current reference version | Upstream / copyright attribution | License | Talvora relationship |
| --- | ---: | --- | --- | --- |
| Gitea | 1.27.3 | The Gitea Authors | MIT | local Git service managed/observed by Control Center |
| Gitea MCP Server | 1.7.0 | Copyright © 2025 The Gitea Authors | MIT | official companion MCP server |
| Caddy | 2.11.4 | Matthew Holt and The Caddy Authors | Apache-2.0 | local proxy used by the Gitea stack |
| Penpot | 2.18.0 | KALEIDOS SUBSIDIARY SL | MPL-2.0 | optional self-hosted design integration |
| Penpot MCP | 2.18.0 source tree | KALEIDOS SUBSIDIARY SL / Penpot contributors | MPL-2.0 | optional local Penpot MCP integration |
| OpenAI Secure MCP Tunnel client | v0.0.16 | OpenAI | Apache-2.0 | optional outbound transport for ChatGPT Business and managed companion tunnels |
| Modal SDK / CLI | 1.5.5 | Modal Labs | Apache-2.0 | optional external SDK/CLI integration |

Additional notes:

- The installed Gitea MCP binary carries its own MIT license notice identifying **The Gitea Authors**.
- Talvora Dev, Talvora Admin, and the current Gitea companion tunnel track OpenAI tunnel-client v0.0.16. The Windows amd64 release archive contains the executable together with `LICENSE`, `NOTICE`, a generated dependency-license report, and an SPDX SBOM.
- Penpot's own dependency tree and notices remain governed by the Penpot distribution. Talvora does not relicense Penpot or its npm dependencies.
- Caddy is a registered trademark of Stack Holdings GmbH; use of the name here identifies interoperability only.

Upstream:

- Gitea: https://github.com/go-gitea/gitea
- Gitea MCP: https://gitea.com/gitea/gitea-mcp
- Caddy: https://github.com/caddyserver/caddy
- Penpot: https://github.com/penpot/penpot
- OpenAI tunnel-client: https://github.com/openai/tunnel-client
- Modal SDK: https://github.com/modal-labs/modal-client

---

## External developer tools

Talvora can discover or invoke many user-installed tools, including Git, GitHub CLI, Chocolatey, Docker, Node.js, npm, pnpm, Yarn, Bun, Python, Java/JDK, Gradle, Maven, Go, Rust/Cargo, Flutter, Dart, Android SDK, CMake, Ninja, Visual Studio, MSBuild, and Windows SDK utilities.

These tools are **interoperability targets**, not Talvora-owned libraries. Unless a specific Talvora installer step explicitly vendors a component, Talvora does not claim ownership of or relicense the installed tool. Each installed distribution remains subject to its own vendor or open-source license.

---

## Trademarks and project names

Names such as Microsoft, Windows, .NET, GitHub, OpenAI, ChatGPT, Gitea, Caddy, Penpot, Modal, Docker, Hugging Face, and other third-party project or company names are the property of their respective owners. Their appearance in Talvora documentation describes compatibility, attribution, or integration and does not imply endorsement, sponsorship, or ownership.

## Release compliance rule

For commercial-quality release hygiene:

1. bundled dependency versions must stay locked;
2. a new bundled package or model must be added to this notice;
3. upstream `LICENSE`, `NOTICE`, or `ThirdPartyNotices` obligations remain applicable;
4. optional integrations must remain clearly separated from Talvora-owned code;
5. the repository CI should fail when the locked runtime package inventory is no longer represented in this file.


