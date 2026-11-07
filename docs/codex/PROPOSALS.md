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

### P-49 — D29 — Unrelated parallel-gate timing failure prevents Full acceptance
- Date: 2026-11-07, HEAD b4bc9f93d6a2e32a6204c0723cf3931a4ce155ab
- Card text: "At the end of AssertSn3Switchboard, before the final board.Settle(), add overview.ApplyTraffic(null);"; "Acceptance: Full Hepsi geçti.; the smoke's *-loaded.png overview shows 'veri yok' or real counts, never 13/1/2."
- Problem: The exact D29 two-line smoke change passed a RED/GREEN source check and Tray build (0 warnings/errors). The Full gate initially required a .context7/verification.json update; this was added as the explicitly allowed supporting change under AGENTS.md Scope 1. A new signed-in Full gate reached 61 checks with 1 failure: tests/QualityGateParallelRegression.ps1:48 reported "Checks did not overlap (7.4040841 s for 3+1+3+1 s of work)" against a 7-second elapsed threshold. Immediately running that exact regression alone passed QUALITY_GATE_PARALLEL_GREEN. This timing-sensitive test is outside D29's Files and is unrelated to the SN3 fixture. The required Full Hepsi geçti. and PNG acceptance remain unverified. The gate must not be weakened or silently rerun.
- Options:
  1. Explicitly allow one bounded signed-in Full gate rerun for D29 after the successful isolated parallel-regression check, with no changes to the timing guard; require Full 68/0 and a verified loaded PNG before D29 commits.
  2. Queue a prerequisite targeted D-card to make the parallel-regression timing check deterministic without weakening its overlap assertion, then unblock D29 with the original steps and full acceptance.
- Partial work: reverted (yes; source and Context7 evidence restored to HEAD before the proposal commit).
- **Decision (Claude, 2026-11-07): option 2, as card D31.** The timing check measures wall time while the heaviest suites run beside it, so it belongs in the serial phase; D31 adds it to `$serialRegressions` and keeps its 7-second threshold. D29 is `[ ]` again right after D31 and is implemented unchanged (its partial work was reverted).
