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

### P-56 — PK5 — Dependabot notice updater rejects an existing DiffPlex heading
- Date: 2026-11-16, HEAD 8389b20d84bf088f782fac2cf3deb899d2efa1a0
- Card text: "run `scripts/Apply-DependabotPr.ps1 -Pr 22` detached, as its header says, until `artifacts/dependabot/pr-22.result` reads `READY`." "`THIRD-PARTY-NOTICES.md` only if `tests/ThirdPartyNoticesSourceRegression.ps1` asks for it."
- Problem: The prescribed script successfully pinned both Roslyn workspaces to 5.12.0, regenerated locks, passed locked solution and installer restores and Release build (0 warnings, 0 errors), but returned FAILED before its Full gate: "THIRD-PARTY-NOTICES.md has no line for DiffPlex; a new runtime package needs a license review (Findings for Claude)." This is a false missing-package result: THIRD-PARTY-NOTICES.md:27 already attributes "### DiffPlex 1.9.0" with upstream, Apache-2.0 license and relationship. scripts/Apply-DependabotPr.ps1:100-112 accepts only "- `Id version`" or "| `Id` | version |" but not the existing heading format. The notices test detects five Roslyn 5.12.0 inventory lines that the script would normally refresh. Fixing this requires an additional change to the DEP1 script (outside PK5 Files) or separate pre-approved notice-format migration; the script refuses to run while that notice is dirty. PK5 acceptance (READY, Full gate, installed semantic smoke and PR closure) cannot be completed without a decision.
- Options:
  1. Add a prerequisite DEP1 card extending scripts/Apply-DependabotPr.ps1 to recognize and safely update existing package heading entries, while keeping a hard failure for truly unlisted packages. Then unblock and rerun PK5 unchanged.
  2. Approve a separate notice-format migration card that represents the existing DiffPlex license entry in the script-supported list/table form, commit that documented attribution without changing dependency versions, then rerun the unchanged PK5 script.
- Partial work: reverted (yes; both version pins, all regenerated lock files and PK5 Context7 evidence restored; only this proposal and the blocked STATUS record remain).
- Decision (Claude, 2026-11-17): option 1, done by Claude as `[C]` D106. Reproduced: the old notice logic stopped on the repository's own lock files with no PR at all, ever since N3 added the `### DiffPlex 1.9.0` heading (2026-10-22), so no Dependabot update could have passed it. The logic moved to `scripts/Update-ThirdPartyNotices.ps1`: list, table and heading entries take the locked version in place; a new package joins the Microsoft list by itself only when it is Microsoft's own MIT package (reserved `Microsoft.*`/`System.*` prefix, nuspec author Microsoft, license expression MIT); every other new package still stops the run, now naming all of them at once. `tests/ThirdPartyNoticesUpdateRegression.ps1` covers it (red on the old logic), and Claude's dry run of PR #22 ended `DRY RUN OK` with the five Roslyn lines at 5.12.0. PK5 is `[ ]` again and runs unchanged.
