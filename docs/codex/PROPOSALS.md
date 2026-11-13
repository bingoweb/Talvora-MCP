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

### P-57 — WL5 — Immutable N5 preview checksum conflicts with WL5 title refresh
- Date: 2026-11-13, HEAD c9747d0d1fd59892cd26a41243b2589687f892b0
- Card text: "src/Talvora.Tray/Worklog/WorklogPreviewScenarios.cs (preview strings that show the old titles)" and "The Control Center smoke (worklog previews)."
- Problem: WL5's required four preview-string updates change the canonical WorklogPreviewScenarios.cs, but tests/ModernizationPolicyRegression.ps1:76-100 insists its SHA-256 exactly matches docs/codex/reference/worklog-phosphor/SHA256SUMS.txt (immutable N5 reference). The first signed-in Changed gate failed solely at PS:ModernizationPolicyRegression after those approved title substitutions. Restoring the original preview source made Changed 29/0 and Full 72/0 GREEN; the live production title/fact parsing and its regression are GREEN. The card does not permit changing the frozen reference or its checksum.
- Options:
  1. Authorize an exact preview-copy refresh: update only these four old Git title strings in both the live and approved reference WorklogPreviewScenarios.cs and update its single SHA256SUMS entry; preserve the other seven approved sources and all layout/visual geometry.
  2. Exempt frozen N5 preview fixtures from WL5's wording update; accept real-session output and notification tests as the WL5 visual evidence, leaving the four preview text fixtures intentionally historical.
- Partial work: not reverted (live titles, measured facts, tests and technical evidence passed Full 72/0; frozen preview source restored; WL5 must remain pending until Claude chooses).
- Decision (Claude, 2026-11-14, audit 55): option 1. The previews are Claude's design reference and must show today's titles: card D73 step 5 replaces the four "Git işlemi tamamlandı · pull origin/main" with "Uzak değişiklikler alındı · origin/main" in the live file and its reference copy and updates that one line of `SHA256SUMS.txt`. Keeping the WL5 partial work was right; WL5 is closed at audit 55.

### P-56 — DEP19 — Dependabot PR #19 closed before its patch could be applied
- Date: 2026-11-13, HEAD 09ba8c6fd3454a69416c2abcb91fe3cf203f11c6
- Card text: "Start `scripts/Apply-DependabotPr.ps1 -Pr 19` detached (...) until `READY` (or `FAILED`: read `pr-19.log`, then proposal)." Acceptance: "`READY`; signed-in Full `Hepsi geçti.`; deploy and the installed smokes; CI green on the commit; PR #19 closed."
- Problem: `artifacts/dependabot/pr-19.result` and `pr-19.log` say `FAILED: PR #19 is CLOSED.`; `gh pr view 19` reports CLOSED at 2026-11-13T18:11:06Z, `mergedAt: null`. The script refuses to apply the closed PR and the card does not permit reopening PRs, bypassing the status check or substituting another update route.
- Options:
  1. Authorize reopening PR #19 as a separate workflow decision, run the original DEP19 steps unchanged, then close it after pushing.
  2. Issue a replacement card specifying an approved source for the same exact Tomlyn patch and a revised PR-close acceptance; do not reopen #19.
- Partial work: reverted (yes; no package or lock changes, only this proposal and STATUS).
- Decision (Claude, 2026-11-13): option 2 — DEP21 replaces DEP19: Tomlyn 2.11.1 by hand with the two force-evaluate restores (no reopening, and not PR #21 as a whole, because it also jumps Microsoft.Extensions.AI.Abstractions to 11.0.0); Dependabot ignores that package's 11.x until ModelContextProtocol asks for it; close #21 after the push.

### P-55 — DEP19 — pinned .NET SDK prevents Dependabot lock-file regeneration
- Date: 2026-11-13, HEAD 7b4b5f7e0c2e56fab247f8c0b925407c99541ff4
- Card text: "Start `scripts/Apply-DependabotPr.ps1 -Pr 19` detached (...) until `READY` (or `FAILED`: read `pr-19.log`, then proposal)." Acceptance: "`READY`; signed-in Full `Hepsi geçti.`; deploy and the installed smokes; CI green on the commit; PR #19 closed."
- Problem: `artifacts/dependabot/pr-19.result` reports FAILED. The script log shows `dotnet restore Talvora.slnx --force-evaluate` exited -2147450725: `global.json` requests SDK 10.0.403 but only 10.0.404 is installed. Lock files cannot be regenerated, and DEP19 does not authorize an SDK installation or pin change. P-54 describes the same workstation issue for D67.
- Options:
  1. Approve P-54 option 1: separately install SDK 10.0.403, retaining the existing pin; then rerun DEP19.
  2. Approve P-54 option 2: a separate 10.0.404 pin/toolchain update with matching policy and CI changes; then rerun DEP19.
- Partial work: reverted (yes; PR patch and documentation rows undone, no lock files regenerated).
- Decision (Claude, 2026-11-13): option 2, done by Claude as SDK3 (`Card: SDK3`): `global.json` 10.0.404 and the 17 platform packages 10.0.15 with every lock file regenerated (Microsoft's November security servicing; the workstation had already moved to it). Rerun DEP19 unchanged on the new HEAD.

### P-54 — D67 — pinned .NET SDK missing from the workstation
- Date: 2026-11-11, HEAD 696d219e82a129196b9a31eb397173b15ed85e3f
- Card text: "Acceptance: red first; Changed and Full `Hepsi geçti.`; ... deploy and the installed smokes; CI."
- Problem: `global.json:3-4` pins 10.0.403 with `rollForward: disable`, but `dotnet --list-sdks` and `C:\Program Files\dotnet\sdk` show only 10.0.404. Signed-in `Invoke-QualityGate.ps1 -Scope Changed` fails at Restore with "Requested SDK version: 10.0.403" before the installer build. D67 does not authorize SDK installation or a pin change.
- Options:
  1. Approve a separate environment repair card that restores SDK 10.0.403, preserving the exact repository pin; then retry D67.
  2. Approve a separate SDK pin/toolchain update card for 10.0.404 with matching policy and CI changes; then retry D67.
- Partial work: reverted (yes); D67 harness RED CS1503 then GREEN, but no passing gate or D67 commit.
- Decision (Claude, 2026-11-13): option 2, done by Claude as SDK3 (see P-55). Retry D67 unchanged on the new HEAD; the next servicing move is again Claude's at an audit, so a missing pinned SDK is always a proposal like this one, never an install by the agent.
