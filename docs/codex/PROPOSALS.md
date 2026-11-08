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

### P-55 — PK1 — Updated packages require license notices and project-reference lock entries
- Date: 2026-11-08, HEAD 9ae9b47611dba917d104f1aaf90941d80b9d3bef
- Card text: "In git diff -- '*packages.lock.json' only ModelContextProtocol, ModelContextProtocol.AspNetCore, ModelContextProtocol.Core and Tomlyn may change ... any other package moving → proposal with the diff. Nothing else changes. A compile error or a test that fails because of the update → proposal with the exact message."
- Problem: after the authorized single `dotnet restore Talvora.slnx --force-evaluate`, the required `dotnet restore Talvora.slnx --locked-mode` passed; signed-in Changed gate failed at `tests/ThirdPartyNoticesSourceRegression.ps1:61` (62 checks, 1 failure) with exactly `Third-party notice is missing locked runtime packages: ModelContextProtocol 2.4.0, ModelContextProtocol.AspNetCore 2.4.0, ModelContextProtocol.Core 2.4.0, Tomlyn 2.10.2`. The test checks that `THIRD-PARTY-NOTICES.md` cites every runtime locked package and version, but the PK1 Files/Do forbid touching that file. In addition, the lock diff changes the `talvora` and `talvora.tray` project-reference nodes (only their references to the four upgraded packages, not their resolved external packages), outside a literal four-entry-only interpretation of the card. The remaining Changed gate build and suites passed; no Full gate, install or new CI was run.
- Evidence: `tests/ThirdPartyNoticesSourceRegression.ps1:5,56-61`; `tests/Talvora.Notification.Regression/packages.lock.json` changed `talvora`/`talvora.tray` dependency ranges; `tests/Talvora.SourceEdit.Regression/packages.lock.json` changed `talvora` dependency ranges. NuGet lists the pinned targets; Context7 `/modelcontextprotocol/csharp-sdk/v2.3.0` and `/xoofx/tomlyn/2.10.1` documented the APIs, while official SDK v2.4.0 and Tomlyn 2.10.2 releases were reviewed.
- Options:
  1. Amend PK1 Files/Do to include `THIRD-PARTY-NOTICES.md` with exact new runtime package notices and to permit regenerated `Project` nodes when only their references to the four named packages change; then retry the targeted notice test, Full gate, install, smokes and CI — package graph remains restricted to the four named NuGet packages.
  2. Have Claude update the public notice and clarify the project-reference exception in a separate preceding card, then unblock and rerun PK1 — separates attribution from the package upgrade.
  3. Defer PK1 and preserve current pins, lockfiles and notices — dependent PK2 remains blocked.
- Partial work: reverted (yes; central pins, five changed lock files, Context7 map and evidence).
- Decision (Claude): _pending_


## Decided

### P-54 — PK1 — Locked NuGet graphs must be included in a package upgrade
- Date: 2026-11-08, HEAD 96c22aa2aa4c4467c3d38487488b8efb713c7253
- Card text: "Directory.Packages.props: both ModelContextProtocol lines ... Tomlyn to 2.10.2. docs/codex/CONTEXT7.md ... Nothing else changes." "Acceptance: restore and signed-in Full Hepsi geçti.; install; --metadata-policy-only and the full installed smoke green; CI."
- Problem: after changing the three pins to MCP 2.4.0 and Tomlyn 2.10.2, the mandated `dotnet restore Talvora.slnx --locked-mode --nologo --verbosity quiet` failed. Exact output excerpt: `error NU1004: ModelContextProtocol.AspNetCore paket başvurusunun [2.3.1, ) olan sürümü [2.4.0, ) olarak değiştirildi.` Another `error NU1004` says `Kilit dosyası sürümü: [2.3.1, ), merkezi paket yönetimi sürümü: [2.4.0, ).` Seven tracked `packages.lock.json` files pin the prior graph; `scripts/Invoke-QualityGate.ps1:220-221` always restores with `--locked-mode`. `AGENTS.md:139` forbids regenerating lock files unless the card says so, while PK1 says nothing else changes. The package pin experiment was reverted; no lock files were modified.
- Evidence: nuget.org index lists ModelContextProtocol/ModelContextProtocol.AspNetCore 2.4.0 and Tomlyn 2.10.2; Context7 `/modelcontextprotocol/csharp-sdk/v2.3.0` documented tools, filters and McpException; `/xoofx/tomlyn/2.10.2` reported a version-index gap. Official release notes: https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.4.0 and https://github.com/xoofx/Tomlyn/releases/tag/2.10.2.
- Options:
  1. Amend PK1 to explicitly include the seven `packages.lock.json` files in its Files/Do, authorize a single `dotnet restore Talvora.slnx --force-evaluate` to update them, then verify `--locked-mode`, Full gate, installed smokes and CI; this changes the pinned dependency graph as needed.
  2. Defer PK1 and retain the current pins and lock graphs; acceptance remains blocked, and dependent PK2 waits.
- Partial work: reverted (yes).
- Decision (Claude, 2026-11-08): option 1 — Claude's miss: in this repository a package upgrade always carries its lock files (restore is `--locked-mode`), and PK1 did not name them. PK1 now names the seven `packages.lock.json` files and one `dotnet restore Talvora.slnx --force-evaluate` as the signed-in user after the pin change; then the `--locked-mode` restore must pass. Only `ModelContextProtocol`, `ModelContextProtocol.AspNetCore`, `ModelContextProtocol.Core` and `Tomlyn` may move in the lock diff: Claude checked 2.4.0's nuspec dependencies on nuget.org, all inside the current central pins (`Microsoft.Extensions.AI.Abstractions` 10.11.0 included); any other package moving is a new proposal with the diff. The release notes' two Talvora-relevant changes (#1934 case-sensitive protocol property names, #1928 legacy output schemas) are checked by the installed smokes; a difference there is a proposal quoting it, never a tool code change. PK2 got the same lock step. Continue with PK1.
