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
