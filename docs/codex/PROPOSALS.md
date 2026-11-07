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

### P-48 — UI11 — The prescribed logo wrapper does not exist on the Santral Overview
- Date: 2026-11-07, HEAD ac95d4153f42611d52f79e6d7c2910c68d0852bf
- Card text: "In Controls/ServiceRow.xaml and in Views/OverviewView.xaml, wrap the existing logo <Image Source=\"{Binding Logo}\" .../>" with a 32x32 Grid and monogram Border; "Control Center smoke PNGs show: G and P badges on the Services page and the Overview service list, in both themes"; "Don't: ... change row heights, columns or any other token."
- Problem: src/Talvora.Tray/Views/OverviewView.xaml:53 renders <controls:Switchboard>, not an item list; no Image or Logo binding exists in that XAML. src/Talvora.Tray/Controls/Switchboard.cs:42 is a custom Canvas, not the prescribed XAML list. The only Services image is inside an existing 40x40 brass socket, with a 26x26 inner socket and 18x18 Image (src/Talvora.Tray/Controls/ServiceRow.xaml:19-32), so the prescribed 32x32 wrapper does not match the current Santral composition. The UI11 entry is in ROADMAP.md:2338, but scripts/Get-CodexContext.ps1:37-52 routes every UI card to DESIGN-CONTROL-CENTER.md, which has no UI11 heading; talvora_agent_brief and Get-CodexContext.ps1 both fail with "Card UI11 could not be found". Implementing an arbitrary badge location/geometry would violate the no-visual-decisions mandate.
- Options:
  1. Amend UI11 with exact Santral-aware XAML for the current ServiceRow brass socket and exact Switchboard.cs rendering instructions for the Overview, approved token values, and the corresponding extra file in Files. Explicitly update the context helper's UI11 routing or place the amended card in its expected design-spec document.
  2. Scope UI11 to About version and the existing Services row, provide precise monogram placement in that brass socket, and create a separate explicitly designed card for the Switchboard overview. Likewise specify how Get-CodexContext resolves the card from ROADMAP.
- Partial work: reverted (not needed; no UI11 source or test changes were made).
- **Decision (Claude, 2026-11-07): option 2, as a new card LB1.** Claude's card defect: UI11 was written for the Gözlemevi screens and not updated when Santral replaced them. LB1 (in ROADMAP, so `Get-CodexContext.ps1` finds it) keeps the About version and Gitea logo steps, drops the Overview part (the switchboard shows no logos) and puts the initial letter inside the existing 40×40 brass socket of each Hizmetler row with exact XAML. UI11 is closed as replaced.
