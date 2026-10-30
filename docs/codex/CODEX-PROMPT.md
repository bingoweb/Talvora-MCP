# Agent prompts — Talvora (Codex or ChatGPT + Talvora)

All prompts below are in English on purpose (agent-facing). Codex always answers the user in **Turkish**, and every user-facing text it writes into Talvora (Control Center, tray, desktop worklog, error messages) is **ordinary Turkish** (AGENTS.md).

## Shortest path (daily use)

The user only types:

```
Talvora'ya devam et
```

Codex applies `AGENTS.md` → **CONTINUE PROTOCOL**: takes the first `[ ]` item of `docs/codex/STATUS.md` → "Agent queue", implements it per its card (`docs/codex/ROADMAP.md`; `docs/codex/DESIGN-WORKLOG.md` for N cards; `docs/codex/DESIGN-CONTROL-CENTER.md` for UI cards), runs `scripts/Invoke-QualityGate.ps1` (after K1), commits once, pushes to `origin` and `github`, and reports in Turkish. If Codex is opened in another folder, the `talvora-devam` skill routes it here.

The same shortcut works in a **ChatGPT chat with the Talvora connector** — see "ChatGPT + Talvora" below.

## ChatGPT + Talvora

**One-time setup (recommended):** put this text into the ChatGPT project's custom instructions (or paste it as the first message of a chat). It is English on purpose; ChatGPT still answers in Turkish.

```
You are the implementing agent for the Talvora repository at C:\Users\tayla\Talvora-MCP, working only through the Talvora connector.
Rules are binding and identical to Codex's: C:\Users\tayla\Talvora-MCP\AGENTS.md (WHO MUST FOLLOW THESE RULES, EXECUTION MANDATE, TOKEN ECONOMY, CONTINUE PROTOCOL).
At the start of every chat, before any change: call talvora_agent_brief {"workspaceRoot":"C:\\Users\\tayla\\Talvora-MCP"} if it exists; otherwise read AGENTS.md lines 1-140 with talvora_read_text_range and run scripts/Get-CodexContext.ps1 with talvora_run_powershell.
Do only the first open card of docs/codex/STATUS.md, exactly as written; never make your own product, architecture or visual decisions; when something is unclear write a proposal in docs/codex/PROPOSALS.md and move to the next card.
Edit repository files only with talvora_apply_patch. Commit and push only with talvora_git_run. One card = one commit with trailer "Card: <ID>".
Always answer me in Turkish with correct diacritics; everything the product shows is Turkish; everything written into docs/codex is English. Commit messages are Turkish and read as written by the developer himself: one short plain subject, at most two short sentences, no `feat(...)` prefixes, no emoji, never any mention of AI, ChatGPT, Codex, Claude or Talvora; the last line is still exactly `Card: <ID>`.
```

Then in that chat the user types only:

```
Talvora'ya devam et
```

The prompts below are only for special situations (they work in Codex and in ChatGPT + Talvora).

## A specific card

```
Talvora: implement card <CARD> (e.g. S3, N3b, UI3) from docs/codex/ROADMAP.md, docs/codex/DESIGN-WORKLOG.md or docs/codex/DESIGN-CONTROL-CENTER.md. Do not move on to another card.
Follow AGENTS.md and the CONTINUE PROTOCOL; failing test first, then code; Context7 evidence; run the quality gate (last line "Hepsi geçti."); update docs/codex/STATUS.md in English; exactly one commit with trailer "Card: <CARD>". Report to me in Turkish.
```

## Code review (after a card, separate task)

```
Talvora: review the last commit against AGENTS.md and the "Don't" and "Acceptance" items of its card.
Do not change code. List only violations with file:line and the rule/item each one breaks. Report to me in Turkish.
```

## Queue status

```
Talvora: read docs/codex/STATUS.md and tell me briefly, in Turkish: finished cards, the next card, blocked cards and why, what is waiting for me in the user queue, open questions. Change nothing.
```

## Answering an open question

```
Talvora: answer to STATUS "Open questions" → <Q-ID>: <answer>. Record it in docs/codex/STATUS.md (move the question with its answer into the Log, in English), unblock the affected queue item ([?] → [ ]), update the card text if the answer changes it, one commit ("docs(codex): answer <Q-ID>"). Do not start a queue item. Confirm to me in Turkish.
```

## Audit fixes (after Claude's audit)

```
Talvora: implement the D-cards from the newest "Audit fixes" table in docs/codex/ROADMAP.md in order, one commit per D-card, each with a failing test first. Do not start other queue items. Report in Turkish.
```

## Deploy the current commit to this PC

```
Talvora: build and deploy the current HEAD with scripts/Deploy-Windows-Installer.ps1 (no code changes). Wait until /healthz.sourceCommit equals HEAD, run the installed-runtime smoke, and log the result in docs/codex/STATUS.md (English). Report in Turkish.
```
