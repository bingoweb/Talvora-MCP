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

### P-54 — D67 — pinned .NET SDK missing from the workstation
- Date: 2026-11-11, HEAD 696d219e82a129196b9a31eb397173b15ed85e3f
- Card text: "Acceptance: red first; Changed and Full `Hepsi geçti.`; ... deploy and the installed smokes; CI."
- Problem: `global.json:3-4` pins 10.0.403 with `rollForward: disable`, but `dotnet --list-sdks` and `C:\Program Files\dotnet\sdk` show only 10.0.404. Signed-in `Invoke-QualityGate.ps1 -Scope Changed` fails at Restore with "Requested SDK version: 10.0.403" before the installer build. D67 does not authorize SDK installation or a pin change.
- Options:
  1. Approve a separate environment repair card that restores SDK 10.0.403, preserving the exact repository pin; then retry D67.
  2. Approve a separate SDK pin/toolchain update card for 10.0.404 with matching policy and CI changes; then retry D67.
- Partial work: reverted (yes); D67 harness RED CS1503 then GREEN, but no passing gate or D67 commit.
- Decision (Claude): _pending_

## Decided
