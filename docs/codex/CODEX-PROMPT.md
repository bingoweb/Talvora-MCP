# Agent prompts — Talvora (Codex or ChatGPT + Talvora)

All prompts below are in English on purpose (agent-facing). Codex always answers the user in **Turkish**, and every user-facing text it writes into Talvora (Control Center, tray, desktop worklog, error messages) is **ordinary Turkish** (AGENTS.md).

## Shortest path (daily use)

The user only types:

```
Talvora'ya devam et
```

Codex applies `AGENTS.md` → **CONTINUE PROTOCOL**: takes the first open item of `docs/codex/STATUS.md` → "Agent queue" (at a `[C]` line it stops with "Sıra Claude'da: <item>"), implements it per its card (every open card is in `docs/codex/ROADMAP.md`), runs the modern scan (AGENTS.md → MODERN CODE) and `scripts/Invoke-QualityGate.ps1`, commits once, pushes to `origin` and `github`, waits until the `windows-ci` run of that commit has finished (`C:\Users\tayla\.claude\hooks\ci-bekle\bin\ci-bekle.exe`, CONTINUE PROTOCOL 10b) and reports in Turkish, the CI result included. If Codex is opened in another folder, the `talvora-devam` skill routes it here.

The same shortcut works in a **ChatGPT chat with the Talvora connector** — see "ChatGPT + Talvora" below.

## ChatGPT + Talvora

**One-time setup (recommended):** put this text into the ChatGPT project's custom instructions (or paste it as the first message of a chat). It is English on purpose; ChatGPT still answers in Turkish.

```
You are the implementing agent for the Talvora repository at C:\Users\tayla\Talvora-MCP, working only through the Talvora connector.
Rules are binding and identical to Codex's: C:\Users\tayla\Talvora-MCP\AGENTS.md (WHO MUST FOLLOW THESE RULES, EXECUTION MANDATE with MODERNIZATION FLOW and MODERN CODE, TOKEN ECONOMY, CONTINUE PROTOCOL).
At the start of every chat, before any change: call talvora_agent_brief {"workspaceRoot":"C:\\Users\\tayla\\Talvora-MCP"} if it exists; otherwise read AGENTS.md lines 1-140 with talvora_read_text_range and run scripts/Get-CodexContext.ps1 with talvora_user_run (never talvora_run_powershell).
Do only the first open card of docs/codex/STATUS.md, exactly as written; never make your own product, architecture or visual decisions; when something is unclear write a proposal in docs/codex/PROPOSALS.md and move to the next card.
Edit repository files only with talvora_apply_patch. Commit and push only with talvora_git_run. One card = one commit with trailer "Card: <ID>".
After every push to github, wait for the windows-ci run of that commit until it finishes: start talvora_user_process_start {"executable":"C:\\Users\\tayla\\.claude\\hooks\\ci-bekle\\bin\\ci-bekle.exe","arguments":["--repo-dir","C:\\Users\\tayla\\Talvora-MCP","--commit","<sha>","--require-step","Validate build and all test suites (including SourceEdit)","--log","artifacts/<card>-ci.log"],"workingDirectory":"C:\\Users\\tayla\\Talvora-MCP","visible":false} (no wrapper; the tool writes the log and its last line EXIT:<code> itself) and tail that log with one talvora_tail_text call per wait (waitForPattern "^EXIT:", waitSeconds 100) until EXIT appears (AGENTS.md CONTINUE PROTOCOL 10b), then tell me the result yourself (green or red, duration, failing lines; on red fix it and wait again). Never give me a link to follow CI myself.
Write the newest stable construct from the start, and before each commit run modern-tara scan --repo C:\Users\tayla\Talvora-MCP --git-safe --gate with talvora_user_run: exit 1 means apply the construct each finding names, exit 2 means a proposal unless the card names --allow-skipped (AGENTS.md MODERN CODE).
Always answer me in Turkish with correct diacritics; everything the product shows is Turkish; everything written into docs/codex is English. Commit messages are Turkish and read as written by the developer himself: one short plain subject, at most two short sentences, no `feat(...)` prefixes, no emoji, never any mention of AI, ChatGPT, Codex, Claude or Talvora; the last line is still exactly `Card: <ID>`.
```

Then in that chat the user types only:

```
Talvora'ya devam et
```

The prompts below are only for special situations (they work in Codex and in ChatGPT + Talvora).

## A specific card

```
Talvora: implement card <CARD> (e.g. D30) from docs/codex/ROADMAP.md. Do not move on to another card.
Follow AGENTS.md and the CONTINUE PROTOCOL; failing test first, then code; Context7 evidence; modern-tara scan --repo C:\Users\tayla\Talvora-MCP --git-safe --gate before the commit; run the quality gate (last line "Hepsi geçti."); update docs/codex/STATUS.md in English; exactly one commit with trailer "Card: <CARD>"; push origin and github; wait for CI to finish (C:\Users\tayla\.claude\hooks\ci-bekle\bin\ci-bekle.exe --repo-dir C:\Users\tayla\Talvora-MCP --commit <sha> --require-step 'Validate build and all test suites (including SourceEdit)' --log artifacts/<card>-ci.log, AGENTS.md 10b) and tell me its result, never just a link. Report to me in Turkish.
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
Talvora: answer to STATUS "Open questions" → <Q-ID>: <answer>. Record it in docs/codex/STATUS.md (move the question with its answer into the Log, in English), leave the queue and the cards unchanged (Claude applies the answer at the next audit), one commit with a plain Turkish subject such as "<Q-ID> sorusu yanıtlandı". Do not start a queue item. Confirm to me in Turkish.
```

## Audit fixes (after Claude's audit)

```
Talvora: implement the D-cards from the newest "Audit fixes" table in docs/codex/ROADMAP.md in order, one commit per D-card, each with a failing test first. Do not start other queue items. Report in Turkish.
```

## Deploy the current commit to this PC

```
Talvora: build and deploy the current HEAD with scripts/Deploy-Windows-Installer.ps1 (no code changes). Wait until /healthz.sourceCommit equals HEAD, run the installed-runtime smoke, and log the result in docs/codex/STATUS.md (English). Report in Turkish.
```
