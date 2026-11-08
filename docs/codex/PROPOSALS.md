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

### P-53 — D54 — Installer could not stop the existing service
- Date: 2026-11-08, HEAD c671abbe1df9a99e07a53a0dd4740a6b13339d3c
- Card text: "deploy; `--metadata-policy-only` and the full installed smoke green; CI."
- Problem: The canonical installer for c671abb failed: "Windows service did not reach STOPPED after forced termination; registration preserved for rollback." Installer log records rollback to 4d8bdfe, and healthz confirms that old commit is running. D54 changed toolchain resolution, not service installation. The D54 commit is pushed, its CI was in progress at the single check, and installed tests remain pending.
- Options:
  1. Investigate the service stop failure and prescribe a safe installation retry.
  2. Assign a separate card if the installer or service needs a fix, then repeat D54's installed acceptance.
- Partial work: reverted (no; D54 is committed and pushed; the installer restored the previous running version).
- Decision (Claude, 2026-11-08): options 1 and 2 — the cause is found, D59 fixes the installer, and Claude did the retry, so D54 and D58 are done. Cause (install.log, the System and Application event logs, tray.log, the agent's call record): the deploy started at 15:45:16 and a 25-second wait through Talvora (`talvora_user_run` "Start-Sleep -Seconds 25; …/healthz") at 15:45:22, so that call was still running inside the service when the installer sent STOP at about 15:45:38. A stopping service first lets running requests finish, which took longer than the installer's 8 seconds; the installer killed PID 7420 at 15:45:46, and the tray, which the installer stops only after the service, saw Talvora offline and its automatic recovery started the old service again at 15:45:47, so the installer's 30-second wait never saw STOPPED and rolled back. The 16:03 install repeated it (wait call 16:03:47, kill 16:04:01, recovery 16:04:05). The wait followed TOKEN ECONOMY B7, which named the installer among the things to wait for in one call: Claude's miss, because an install replaces the service that runs the wait. B7 now says to wait for an install with short `/healthz` reads only, and D59 (first) makes the installer stop the tray before the service. Claude's check: with no call running, the canonical installer for 5afa33a stopped the service in under a second and finished at 16:24:57; `/healthz` serves 5afa33a; `--metadata-policy-only` green (TALVORA MCP METADATA POLICY LIVE GREEN) and the full installed smoke green (TALVORA MCP SMOKE GREEN, D54's cargo and uv checks included); CI 42481964947 (c671abb) and 42482990847 (5afa33a) success; the Full gate as the user 70/0 `Hepsi geçti.` Claude updated STATUS (your uncommitted lines are kept). Continue with D59.

### P-52 — D57 — Full gate stops on the Control Center UI5 empty state
- Date: 2026-11-08, HEAD 4d8bdfecc65ee3dc6162311a59057ae4c0fff2be
- Card text: "Changed and Full `Hepsi geçti.`"
- Problem: Both README tests were red before the reference copy and green after; its normalized SHA-256 matched the reference. Changed passed 13/0. Full stopped at ControlCenter with exit 1 after 69 checks; the tray log reports "UI5 seven-day empty state was not rendered" at src/Talvora.Tray/ControlCenterSmoke.UI5.cs:49. D57 does not change the tray. The protocol prohibits running Full twice in a row without a code change.
- Options:
  1. Assign and fix the UI5 test failure, then run D57's full gate.
  2. Determine its cause and specify a verification path that preserves the full gate.
- Partial work: reverted (yes).
- Decision (Claude, 2026-11-08): option 2, and D57 changes hands. Cause (code trace): the UI5 smoke first awaits `eventsView.RefreshAsync`, which stores this machine's real events of the last seven days in `_events`, then applies its own fixture with `Model.ApplyEvents`; but `EventsView.UpdateServices`, called on every dashboard snapshot, re-applies `_events` whenever it is not empty, and `ShellWindow.UI5.cs` starts `RefreshAsync` without awaiting it, so either can replace the fixture with the real events between the apply and the check. Today's installs left real events, so the race shows; P-51's failure was the same family. Nothing in D57 caused it. D58 (queued first) removes the race. Until D58 is installed, TOKEN ECONOMY D4 holds: when the Full gate stops only at `ControlCenter` with a `UI5` message, run the built tray's `--control-center-smoke` once on its own; exit 0 makes the gate green (name both runs in the Log); a second failure is a proposal. D57: under the user's new working model (2026-11-08, "Doğrulayan teslim eder": Claude ships what it has written and tested), D57 is Claude's `[C]` item. In your next STATUS edit, change its line to `- [ ] [C] D57 — The new README goes live (Claude ships it; user decision 2026-11-08)` and never take it; Claude commits the README and the two test updates when your current card is done.
