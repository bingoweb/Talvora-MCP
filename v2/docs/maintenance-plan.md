# Local maintenance continuation

Scope: reuse the working six-tool Windows MCP; no model API, tunnel, package installation or source reset. The owner requests as little manual work as possible.

1. Add a single self-elevating continuation entry point. Inspect current.json, task definition, health and actual process identity. Reuse a healthy managed installation; repair with the existing installer only when required.
2. Run the already compiled real MCP smoke client. Do not equate a health response with successful tool execution.
3. Retire only the positively identified legacy AppData/Gateway installation. Export and disable exact executable-matching scheduled tasks; preserve unrelated and new tasks. Move the old Gateway directory to a timestamped archive, never delete the parent Talvora directory or source repository. Record other Talvora services/tasks for later review without guessing ownership.
4. Save success/failure and diagnostic evidence locally and open a readable report. Repeated runs must not redeploy a healthy Gateway or repeatedly move the same legacy files.
5. Verify on Windows PowerShell 5.1: failing test first, real task definitions, real directory movement, preservation of sibling Local directory, real deployed Gateway and MCP tools. Keep changes on a feature branch. The user's device and desktop MCP connection remain unverified until local execution.
