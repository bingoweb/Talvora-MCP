# Talvora — Current state

[STATUS](docs/codex/STATUS.md) is the live queue, acceptance and delivery record; every open card is in [ROADMAP](docs/codex/ROADMAP.md).

Read [AGENTS.md](AGENTS.md) and the [permanent prompt](docs/codex/CODEX-PROMPT.md) before editing; `talvora_agent_brief` returns the rules and the current card. Work on main, one card per commit, with no history rewrite or unrequested engine delegation.

One LocalSystem Windows service exposes loopback health at `http://127.0.0.1:7676/healthz` (its `sourceCommit` is the installed runtime); the signed-in user runs the tray and the Control Center.

Build and test as the signed-in user. Runtime changes require an exact-commit install and smoke; verify the pushed commit's CI before closing a card.

Finished work is not kept in the working tree: closed cards, old Log entries, decided proposals and the old audit files live in git history (STATUS explains how to read them).
