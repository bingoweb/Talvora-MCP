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

### P-54 — D92 — Worklog purpose data conflicts with immutable N5 reference checksums
- Date: 2026-11-15, HEAD 8ff0b070ad7d4b52971871b7bd7921c595b86da6
- Card text: "src/Talvora.Tray/Worklog/WorklogCardViewModel.cs and WorklogCardMapper.cs (WorklogTimelineRow gains an optional Purpose, filled by the mapper)" and "Acceptance: ... ModernizationPolicyRegression.ps1 green".
- Problem: D92 necessarily changes two files pinned as immutable N5 Phosphor sources. tests/ModernizationPolicyRegression.ps1:78-99 hashes both target files and their approved references against docs/codex/reference/worklog-phosphor/SHA256SUMS.txt; the test fails with "N5 Phosphor source differs from approved checksum: src/Talvora.Tray/Worklog/WorklogCardMapper.cs" (WorklogCardViewModel.cs would fail too). D92's first-red test demonstrated missing Purpose, then targeted D92_TIMELINE_PURPOSES_GREEN and full Notification DESKTOP_PROGRESS_BEHAVIOR_GREEN passed with all requested fields, but the immutable policy prevented Acceptance. An agent cannot approve new design references or silently change their hashes. The partial code/test/context edits were reverted, and both protected source hashes equal the approved manifest again.
- Options:
  1. Claude approves revised WorklogCardMapper.cs and WorklogCardViewModel.cs as the new N5 references and updates the two reference copies plus SHA256SUMS.txt; amend D92's Files to authorize this explicit reference refresh. No XAML or visible text changes; then D92 implements the entire data flow and both checks can pass.
  2. Split responsibility at the locked boundary: amend D92 to store and forward Purpose through the protocol, worklog state and history only; move view-model and mapper fields, their regression assertions and the N5 reference refresh into Claude-owned D92v. D92's policy check would remain green without touching the pinned files.
- Partial work: reverted (yes)
- Decision (Claude): _pending_

## Decided
