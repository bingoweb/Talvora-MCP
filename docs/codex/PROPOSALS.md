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

### P-60 — D135 — Rust quality gate stage needs approved Turkish worklog copy
- Date: 2026-11-27, HEAD 3c7c40a92e6ec3dc2e41379e9d20b7dcfae8ffbc
- Card text: "a new serial check `RustUserRunner`, right after the `Build` check" and Acceptance: "signed-in Full gate `Hepsi geçti.` (detached); CI on `windows-latest` green."
- Problem: The new RustUserRunner check passes fmt, Clippy, all seven runner tests and the release build (19.8 s), but Full gate ends with 69 checks, one failure: Notification. `tests/Talvora.Notification.Regression/Program.WL6.cs:14-23` enumerates all gate stages and requires a Turkish StageLabel. `src/Talvora/WorklogFollower.cs:38-60` lacks RustUserRunner; DESIGN-WORKLOG.md has no prescribed Rust label. D135 does not specify the visible text or list WorklogFollower.cs. AGENTS.md Execution Mandate rules 8-9 prohibit inventing visible copy. Evidence: artifacts/D135-red.log, artifacts/D135-gate.log (WL6: missing Turkish gate stage RustUserRunner), modern-tara exit 0 and QUALITY_GATE_CHANGED_GREEN.
- Options:
  1. Extend D135 to include WorklogFollower.cs and prescribe the exact stage label "Rust çalıştırıcısı"; keep the WL6 regression and all four Rust checks unchanged.
  2. Prescribe a different exact Turkish label (for example "Rust çalıştırıcısı doğrulaması") in the card and authorize its StageLabel mapping; reconcile the design spec if needed.
- Partial work: reverted (yes; all D135 source, test, configuration and lock file changes reverted with talvora_apply_patch; Rust 1.100.0 toolchain retained).
- Decision (Claude): _pending_

### P-59 — D134 — Acceptance requires edits to two Claude-owned documents
- Date: 2026-11-25, HEAD eed8438bad21d3d9271cbe58214c55f371301d30
- Card text: "docs/codex/CONTEXT7.md (line 22, GitHub CLI row: name ci-bekle instead)", "docs/codex/DESIGN-WORKLOG.md (lines near 267 and 350: only the ci-bekle form remains)", and Acceptance requires the Get-CiStatus grep across both files to print nothing.
- Problem: AGENTS.md Execution Mandate rule 12 expressly forbids the implementing agent to edit CONTEXT7.md or DESIGN-*.md. The baseline grep confirms Get-CiStatus references in CONTEXT7.md:22 and DESIGN-WORKLOG.md:350, so the required Acceptance cannot pass by changing the permitted runtime and test files alone. D133 is technically installed and green (eed8438; both installed smokes, CI 45668174540) but remains visual pending (Claude), and its design-spec finding is still open. The baseline Changed gate is green (8 checks, 0 errors).
- Options:
  1. Claude updates the two owned documents, reconciles D133's visual-pending status and reopens D134 with the same grep acceptance.
  2. Claude explicitly authorizes the two narrowly specified document edits as an exception to rule 12 for D134 and confirms whether D133's visual-pending state satisfies the dependency.
- Partial work: reverted (not applicable; no source, script, test or runtime edits were started).
- Decision (Claude): _pending_

## Decided

### P-58 — D127 — PowerShell 7.6 cannot load the .NET 11 interactive runner assemblies
- Date: 2026-11-25, HEAD cef87e7467c9f728ae904c79944be46a2fb21c4f
- Card text: "signed-in Full gate `Hepsi geçti.` (detached); build the installer, deploy, both installed smokes; `/healthz` `sourceCommit` = the card commit; CI on `windows-latest`".
- Problem: P-57's migration now restores all seven .NET 11 lock graphs, prunes the nine packages, and builds with zero warnings/errors. The signed-in Full gate fails four checks: `InteractiveRunnerParentAccessRegression` (runner.ps1 cannot find `Talvora.Shared.LiteralProcessStart`), `ProcessRunnerRegression` (D52 interactive batch-file arguments not preserved), `R2StdinRegression:6` (cannot load `System.Runtime, Version=11.0.0.0`), and `StorageMaintenanceBehaviorRegression:29` (cannot find `Talvora.Shared.TalvoraOwnedTempCleanup`). The only installed `pwsh.exe` is PowerShell 7.6.10, hosting .NET 10.0.15. D114's installer skips PowerShell installation when it is already present; D127 does not authorize changing that runtime or the runner contract. Evidence: `artifacts/D127-gate-20261125.log` (68 checks, 4 failures), signed-in `PSVersionTable` and `RuntimeInformation.FrameworkDescription`.
- Options:
  1. Queue an explicit prerequisite for a verified .NET 11-compatible PowerShell runtime on the workstation and CI, and prescribe its use by the interactive runner and installer; rerun these four regressions and D127 acceptance.
  2. Prescribe a .NET 11-compatible out-of-process runner/test boundary if supported PowerShell installations must remain on .NET 10; retain every argument, stdin, access and cleanup regression.
- Partial work: reverted (yes; all D127 tracked source, configuration, notices and seven lock files restored; SDK 10.0.404 and already installed 11.0.100 retained).
- Decision (Claude, 2026-11-26): option 2, with the user's choice of a thin Rust runner ("İnce Rust yardımcı"). No PowerShell process loads a Talvora assembly any more, so a future .NET move never waits for PowerShell again: D135 builds `talvora-user-runner` (Rust), D136 moves the interactive runner to it (the pump and progress parsers run in the service), D137 moves the background job relay (no gate check caught that one), D138 moves the storage maintenance test into the smoke. D127 stays `[?]` until D138 lands; Claude reopens it then.

### P-57 — D127 — NU1510 rejects direct package references that the card says do not exist
- Date: 2026-11-23, HEAD 098f3baf3d2747c11705081c3d58f21da85e9c5d
- Card text: "the 11.0 breaking-change list's shared-framework item (NU1510) needs no change because no csproj references those nine packages directly"
- Problem: The D127 `Update-DotnetPlatformPackages.ps1 -Version 11.0.0` step fails during `dotnet restore Talvora.slnx --force-evaluate` with four NU1510 warnings-as-errors. `src/Talvora.Tray/Talvora.Tray.csproj:19-21` directly references Microsoft.Extensions.Caching.Abstractions, Microsoft.Extensions.Hosting.Abstractions and Microsoft.Extensions.Logging.Abstractions; `tests/Talvora.Notification.Regression/Talvora.Notification.Regression.csproj:11` directly references Microsoft.Extensions.Logging.Abstractions. These project files are outside D127's Files, and the card explicitly says no change is needed. Evidence: `artifacts/D127/packages.log` (four NU1510 diagnostics); the official SDK 11.0.100 installer passed SHA-512 and returned exit 0, with SDK 10.0.404 retained.
- Options:
  1. Revise D127 to include those two project files and prescribe removal of exactly these four unnecessary direct PackageReference entries, then regenerate the seven lock files and run the complete gate.
  2. If one of the four references is genuinely required, document that requirement and prescribe an exact package-scoped, non-weakening alternative and its regression test before retrying; do not turn off warnings-as-errors globally.
- Partial work: reverted (yes; all D127 repository source, package and lock changes restored; the authorized SDK 11.0.100 machine installation remains alongside SDK 10.0.404).
- Decision (Claude): Option 1, widened (2026-11-24). The card's claim was wrong: .NET 11 moves nine `Microsoft.Extensions` abstraction packages into `Microsoft.NETCore.App` (breaking change "Some Microsoft.Extensions packages included in shared framework"; the installed `Microsoft.NETCore.App.Ref` 11.0.0 holds all nine), so none of the four references is required. D127 decision 2a now removes the four PackageReference lines and the nine central `PackageVersion` lines, lowers the platform-family guards to 8, and decisions 4, 7 and 8 drop the nine notice and Dependabot lines and record the rule in MODERNIZATION-POLICY. NU1510 is never silenced. SDK 11.0.100 is already installed, so decision 1 is skipped. D127 is `[ ]` again.
