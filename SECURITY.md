# Talvora Security and Privacy Policy

Talvora is intentionally a high-capability local development MCP. Security hardening must preserve its legitimate development and administration capabilities rather than silently reducing the tool surface.

## Trust boundary

- The Talvora MCP service binds to localhost by default.
- Remote ChatGPT access is provided through the configured secure tunnel rather than by exposing the local MCP listener directly.
- Administrative and runtime credentials are separate.
- Stored OpenAI credentials use Windows DPAPI in the current interactive user context.
- Secrets must never be committed to the repository or written to persistent logs in plaintext.

## Logging and privacy

Talvora keeps operational logging for diagnostics, but persistent logs must redact common credential forms such as authorization tokens, API keys, passwords, client secrets, GitHub tokens, OpenAI-style keys, and JWTs.

The Control Center raw-log viewer applies the same redaction layer to logs produced by managed MCP components before displaying them.

Do not place source bodies, request bodies, credentials, authentication headers, environment secrets, or private keys into new persistent audit records unless a narrowly scoped diagnostic explicitly requires it and the value is sanitized first.

## Interactive process secrets

Interactive-user process execution keeps its full environment and argument capabilities. When an environment contains credentials, the temporary request document is deleted by the interactive helper immediately after deserialization. Per-run lease files distinguish active runs from stale crash remnants so cleanup does not remove a live process handoff.

## Repository hygiene

Local secret files, DPAPI blobs, private keys, PFX/P12 bundles, and real environment files are ignored by Git. Example files may be committed only when they contain placeholders rather than live credentials.

Machine-specific evidence in public documentation should use generic placeholders such as `%USERPROFILE%`, `%APPDATA%`, `%LOCALAPPDATA%`, or `<interactive-user>` unless the literal identity is essential to reproduce a bug.

## Reporting a security issue

Do not post live credentials, private keys, or sensitive logs in a public issue. Use a private repository security-reporting channel when available, or contact the repository owner privately with a minimal reproduction and sanitized evidence.

## Non-goals

Security hardening must not:

- remove unrestricted developer/admin tools solely to reduce theoretical risk;
- add hidden command deny-lists to existing unrestricted tools;
- change successful tool semantics without a demonstrated security requirement;
- replace capability with silent failure.

When a capability is inherently powerful, Talvora prefers explicit documentation, secret minimization, safe persistence, bounded resources, clear auditability, and secure transport over capability removal.
