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

### P-53 — D54 — Installer could not stop the existing service
- Date: 2026-11-08, HEAD c671abbe1df9a99e07a53a0dd4740a6b13339d3c
- Card text: "deploy; `--metadata-policy-only` and the full installed smoke green; CI."
- Problem: The canonical installer for c671abb failed: "Windows service did not reach STOPPED after forced termination; registration preserved for rollback." Installer log records rollback to 4d8bdfe, and healthz confirms that old commit is running. D54 changed toolchain resolution, not service installation. The D54 commit is pushed, its CI was in progress at the single check, and installed tests remain pending.
- Options:
  1. Investigate the service stop failure and prescribe a safe installation retry.
  2. Assign a separate card if the installer or service needs a fix, then repeat D54's installed acceptance.
- Partial work: reverted (no; D54 is committed and pushed; the installer restored the previous running version).
- Decision (Claude): _pending_

## Decided

### P-52 — D57 — Full gate stops on the Control Center UI5 empty state
- Date: 2026-11-08, HEAD 4d8bdfecc65ee3dc6162311a59057ae4c0fff2be
- Card text: "Changed and Full `Hepsi geçti.`"
- Problem: Both README tests were red before the reference copy and green after; its normalized SHA-256 matched the reference. Changed passed 13/0. Full stopped at ControlCenter with exit 1 after 69 checks; the tray log reports "UI5 seven-day empty state was not rendered" at src/Talvora.Tray/ControlCenterSmoke.UI5.cs:49. D57 does not change the tray. The protocol prohibits running Full twice in a row without a code change.
- Options:
  1. Assign and fix the UI5 test failure, then run D57's full gate.
  2. Determine its cause and specify a verification path that preserves the full gate.
- Partial work: reverted (yes).
- Decision (Claude, 2026-11-08): option 2, and D57 changes hands. Cause (code trace): the UI5 smoke first awaits `eventsView.RefreshAsync`, which stores this machine's real events of the last seven days in `_events`, then applies its own fixture with `Model.ApplyEvents`; but `EventsView.UpdateServices`, called on every dashboard snapshot, re-applies `_events` whenever it is not empty, and `ShellWindow.UI5.cs` starts `RefreshAsync` without awaiting it, so either can replace the fixture with the real events between the apply and the check. Today's installs left real events, so the race shows; P-51's failure was the same family. Nothing in D57 caused it. D58 (queued first) removes the race. Until D58 is installed, TOKEN ECONOMY D4 holds: when the Full gate stops only at `ControlCenter` with a `UI5` message, run the built tray's `--control-center-smoke` once on its own; exit 0 makes the gate green (name both runs in the Log); a second failure is a proposal. D57: under the user's new working model (2026-11-08, "Doğrulayan teslim eder": Claude ships what it has written and tested), D57 is Claude's `[C]` item. In your next STATUS edit, change its line to `- [ ] [C] D57 — The new README goes live (Claude ships it; user decision 2026-11-08)` and never take it; Claude commits the README and the two test updates when your current card is done.

### P-51 — D53 — Full gate stops on the Control Center UI5 smoke
- Date: 2026-11-08, HEAD e3cafc48503efa40889d1db4891960e9816d2524
- Card text: "Changed and Full `Hepsi geçti.`"
- Problem: Changed passed 28/0; Full stopped at ControlCenter with exit 1 after 69 checks. The tray log reports "UI5 raw log timer must run when the panel is visible" at src/Talvora.Tray/ControlCenterSmoke.UI5.cs:79. D53 does not change the tray. The protocol says never run Full twice in a row without a code change.
- Options:
  1. Assign and resolve the unrelated UI5 failure, then run the full gate for D53.
  2. Specify how to verify the failure and continue without weakening the gate.
- Partial work: reverted (no; uncommitted D53 changes retained for review).
- Decision (Claude, 2026-11-08): option 2; no code change and no new card. The stop was Claude's: at 14:52:15 Claude opened the installed Control Center (`--open-control-center`, for README screenshots) while your Full gate ran `--control-center-smoke`; the smoke failed 16 s later (gate log 14:52:31) because its own window no longer stayed visible and in front. Claude's check with your unchanged working tree: the same `src/Talvora.Tray/bin/Release/net10.0-windows/win-x64/Talvora.Tray.exe --control-center-smoke` (built 14:48:26), run with nothing else on the desktop, exited 0 in 25.4 s and wrote every preview, `control-center-ui5-log-*` included. Run the Full gate once more for D53 (the earlier run did not count: it was disturbed from outside) and continue the card as written. While a gate or smoke runs, leave the desktop alone; Claude does the same from now on.

### P-50 — D52 — Installed metadata smoke fails on an existing D53 issue
- Date: 2026-11-08, HEAD 877662c451879c80e5823c94ace9f3a0e18067fe
- Card text: "Changed and Full `Hepsi geçti.`; deploy; `--metadata-policy-only` and the full installed smoke green; CI."
- Problem: After installing HEAD, metadata-policy-only stopped with "Live description contains grammar regression: talvora_dotnet_run" at tests/Talvora.Smoke/SmokeScenarios.MetadataPolicy.cs:206. Audit 43 already assigns this description normalization issue to D53; D52 does not change it. The installed npm argument probe now passes. CI run 42476029267 was in progress at the one permitted check.
- Options:
  1. Resolve D53 first, then repeat D52's installed acceptance — keeps the metadata check intact.
  2. Specify another acceptance order that preserves all required checks.
- Partial work: reverted (no; D52 was committed, pushed and installed before the live failure).
- Decision (Claude, 2026-11-08): option 2, and D52 is done. The stop is D53's documented defect, not D52's: the first `tools/list` after a service start is normalized once ("an caller-supplied"), every later list is repaired, and the install had just restarted the service, so the smoke was that first client. Claude's check: --metadata-policy-only against the installed 877662c green (TALVORA MCP METADATA POLICY LIVE GREEN); the full installed smoke green (TALVORA MCP SMOKE GREEN); signed-in Full gate 70/0 Hepsi geçti.; CI 42476029267 success; live talvora_npm_run ["--version","a&echo","TALVORA_BATCH_PROBE"] → stdout `12.2.1` only. D52 is `[x]`; continue with D53, whose own acceptance now requires the first `--metadata-policy-only` right after its install to be green. Claude's miss: audit 43 gave D52 an acceptance step that D53's open defect could stop.
