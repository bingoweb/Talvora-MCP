# Talvora — Current state

Updated: 2026-10-25. [STATUS](docs/codex/STATUS.md) is the live queue, acceptance and delivery record.

Read [AGENTS.md](AGENTS.md), the [permanent prompt](docs/codex/CODEX-PROMPT.md) and the selected card before editing; `talvora_agent_brief` provides bounded context. Work on main, one card per commit, with no history rewrite or unrequested engine delegation.

One LocalSystem Windows service exposes loopback health at `http://127.0.0.1:7676/healthz`; the signed-in user runs Tray/Control Center. Full/Dev/Admin have 250/213/88 tools, with 51 shared. Last installed runtime: D22 `5752edd`.

Q1 awaits Claude P-36. Q3 compacts documentation; the following implementation cards are UI6, N8, UI7 and UI8. STATUS holds the exact order and outstanding user evidence, including optional engine, visual, mixed-DPI and RDP checks.

Build/test as the signed-in user. Runtime changes require exact-commit installation and smoke; verify the pushed commit's CI before closing a card.

History is preserved unchanged: [handoff](docs/archive/HANDOFF.md), [bug audit](docs/archive/BUG-AUDIT.md), [Control Center TODO](docs/archive/MCP-CONTROL-CENTER-TODO.md), audit [plan](docs/archive/TALVORA-AUDIT-2026-10-09-PLAN.md) and [TODO](docs/archive/TALVORA-AUDIT-2026-10-09-TODO.md). Current decisions are in `docs/codex/`.
