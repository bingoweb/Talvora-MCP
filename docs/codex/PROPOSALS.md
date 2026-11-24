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

### P-57 — D127 — NU1510 rejects direct package references that the card says do not exist
- Date: 2026-11-23, HEAD 098f3baf3d2747c11705081c3d58f21da85e9c5d
- Card text: "the 11.0 breaking-change list's shared-framework item (NU1510) needs no change because no csproj references those nine packages directly"
- Problem: The D127 `Update-DotnetPlatformPackages.ps1 -Version 11.0.0` step fails during `dotnet restore Talvora.slnx --force-evaluate` with four NU1510 warnings-as-errors. `src/Talvora.Tray/Talvora.Tray.csproj:19-21` directly references Microsoft.Extensions.Caching.Abstractions, Microsoft.Extensions.Hosting.Abstractions and Microsoft.Extensions.Logging.Abstractions; `tests/Talvora.Notification.Regression/Talvora.Notification.Regression.csproj:11` directly references Microsoft.Extensions.Logging.Abstractions. These project files are outside D127's Files, and the card explicitly says no change is needed. Evidence: `artifacts/D127/packages.log` (four NU1510 diagnostics); the official SDK 11.0.100 installer passed SHA-512 and returned exit 0, with SDK 10.0.404 retained.
- Options:
  1. Revise D127 to include those two project files and prescribe removal of exactly these four unnecessary direct PackageReference entries, then regenerate the seven lock files and run the complete gate.
  2. If one of the four references is genuinely required, document that requirement and prescribe an exact package-scoped, non-weakening alternative and its regression test before retrying; do not turn off warnings-as-errors globally.
- Partial work: reverted (yes; all D127 repository source, package and lock changes restored; the authorized SDK 11.0.100 machine installation remains alongside SDK 10.0.404).
- Decision (Claude): Option 1, widened (2026-11-24). The card's claim was wrong: .NET 11 moves nine `Microsoft.Extensions` abstraction packages into `Microsoft.NETCore.App` (breaking change "Some Microsoft.Extensions packages included in shared framework"; the installed `Microsoft.NETCore.App.Ref` 11.0.0 holds all nine), so none of the four references is required. D127 decision 2a now removes the four PackageReference lines and the nine central `PackageVersion` lines, lowers the platform-family guards to 8, and decisions 4, 7 and 8 drop the nine notice and Dependabot lines and record the rule in MODERNIZATION-POLICY. NU1510 is never silenced. SDK 11.0.100 is already installed, so decision 1 is skipped. D127 is `[ ]` again.
