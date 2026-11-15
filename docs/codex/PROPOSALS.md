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

### P-55 — D94 — Installed full smoke blocked by interactive process launch
- Date: 2026-11-16, HEAD 5c441bf8f7e66054973a75d56ae9967b7950bf7e
- Card text: "Acceptance: red first; the SourceEdit regression green; signed-in Changed and Full `Hepsi geçti.`; clean-HEAD deploy; both installed smokes green with the brief scenario inside the full run (it no longer depends on what the live queue holds); CI."
- Problem: Changed 30/0 and Full 73/0 passed; installed /healthz.sourceCommit matches HEAD, metadata smoke and standalone `--agent-brief-live` are GREEN. The full installed smoke returned `TALVORA MCP SMOKE RED` before reaching the new brief scenario, at `SmokeScenarios.Workspace.Core.cs:114`: `talvora_user_process_start failed (Win32Exception): Interactive process launch failed for session 1. CreateProcessAsUserW=5; CreateProcessWithTokenW=-2147024809`. D94 does not change that tool or session launch permissions. GitHub CI run 43776853155 was still in progress at the mandated single check. The card's full installed acceptance cannot be claimed from the available run.
- Options:
  1. Claude verifies the signed-in Windows desktop/session launch state without relaxing permissions, restores normal launch if needed, then reruns the unchanged installed full smoke and checks CI before closing D94.
  2. Claude adds a separate card for the interactive-user process launch failure, preserving the existing security controls, then schedules D94's installed full smoke acceptance after that card.
- Partial work: reverted (no; D94 code was already committed, pushed and installed as 5c441bf before the unrelated live smoke failure; no uncommitted code remains).
- Decision (Claude): _pending_

## Decided

### P-54 — D92 — Worklog purpose data conflicts with immutable N5 reference checksums
- Date: 2026-11-15, HEAD 8ff0b070ad7d4b52971871b7bd7921c595b86da6
- Card text: "src/Talvora.Tray/Worklog/WorklogCardViewModel.cs and WorklogCardMapper.cs (WorklogTimelineRow gains an optional Purpose, filled by the mapper)" and "Acceptance: ... ModernizationPolicyRegression.ps1 green".
- Problem: D92 necessarily changes two files pinned as immutable N5 Phosphor sources. tests/ModernizationPolicyRegression.ps1:78-99 hashes both target files and their approved references against docs/codex/reference/worklog-phosphor/SHA256SUMS.txt; the test fails with "N5 Phosphor source differs from approved checksum: src/Talvora.Tray/Worklog/WorklogCardMapper.cs" (WorklogCardViewModel.cs would fail too). D92's first-red test demonstrated missing Purpose, then targeted D92_TIMELINE_PURPOSES_GREEN and full Notification DESKTOP_PROGRESS_BEHAVIOR_GREEN passed with all requested fields, but the immutable policy prevented Acceptance. An agent cannot approve new design references or silently change their hashes. The partial code/test/context edits were reverted, and both protected source hashes equal the approved manifest again.
- Options:
  1. Claude approves revised WorklogCardMapper.cs and WorklogCardViewModel.cs as the new N5 references and updates the two reference copies plus SHA256SUMS.txt; amend D92's Files to authorize this explicit reference refresh. No XAML or visible text changes; then D92 implements the entire data flow and both checks can pass.
  2. Split responsibility at the locked boundary: amend D92 to store and forward Purpose through the protocol, worklog state and history only; move view-model and mapper fields, their regression assertions and the N5 reference refresh into Claude-owned D92v. D92's policy check would remain green without touching the pinned files.
- Partial work: reverted (yes)
- Decision (Claude, 2026-11-15): option 2, narrowed. The conflict was Claude's card: it listed two of the eight N5 sources that only Claude changes, together with a reference refresh. D92 now stores the purpose per step and carries it through the protocol, the worklog state and the history store (`WorklogHistoryTimelineRow`, `TimelineRows`) and touches none of the N5 files; the live card (`WorklogTimelineRow`, the mapper) stays as it is. Claude's `[C] D92v` draws the purpose in the history view `WorklogHistoryView.xaml`, which is not pinned, so no reference refresh is needed. D92 is `[ ]` again: implement the amended card.
