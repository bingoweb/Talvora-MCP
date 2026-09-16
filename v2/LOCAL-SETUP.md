# Local Windows setup

## What this fixes

The previously verified six-tool Gateway was started from a terminal and did not survive reboot. A legacy AppData Gateway task could occupy port 7676 and return 502. The new local entry point handles the identified legacy task, installs a separate runtime copy, registers a logon task, captures logs and configures a direct local MCP client. No tunnel or API-key setup is part of this path.

Run `TALVORA-KUR.cmd` from `v2/` or from the extracted recovery package. It requests UAC when needed. The existing compiled application is read from `%USERPROFILE%\Talvora-MCP\v2\src\Talvora.Gateway\bin\Release\net10.0`. This is a repair/deployment entry point, not a first-time dependency installer. No additional package manager is called.

The runtime is copied to `%LOCALAPPDATA%\Talvora\Local\releases\<timestamp>`. Logs are under `Local\logs`; previous task/client settings are backed up under `Local\backups`. The task is `Talvora Local MCP`, at user logon, interactive user, highest available token, no execution-time limit, with three one-minute failure retries. It is not a SYSTEM service and does not start before the owner logs in.

Configuration is saved under `Local\current.json`. The source repo is not deleted, checked out, reset or overwritten. Rebuilding source will not overwrite the running deployed DLL. Rerun the local installer to deploy a newly built version; this intentionally restarts only the identified Talvora process.

`TALVORA-DURUM.cmd` shows the scheduled task and Gateway health. A successful installation prints `TALVORA LOCAL READY` only after health and the real MCP smoke test pass.

## Direct client configuration

The installer adds `mcp_servers.talvora_local` to `%USERPROFILE%\.codex\config.toml` (or `CODEX_HOME`). It preserves unrelated servers/model configuration and saves a backup before changes. It sets the URL to `http://127.0.0.1:7676/mcp`, enables the server and requests approval mode `approve` for its tools. The client's own workspace policies still apply.

Restart the local ChatGPT desktop/Codex client and check `/mcp`. The client must be running on this Windows computer. Creating the config file does not install the desktop application, authenticate its model session, or establish access from a web-only chat. Use ChatGPT account sign-in for subscription-backed model access; this local MCP does not need a separately created API key.

Official references checked on 2026-09-16:
- https://developers.openai.com/codex/mcp/
- https://developers.openai.com/codex/auth/
- Microsoft ScheduledTasks documentation, retrieved through Context7.

## Verification boundaries

CI uses Windows PowerShell 5.1 to parse scripts, exercise config preservation/idempotency, inspect task settings, register a real task, invoke the actual installer without starting the interactive task in CI, then run the deployed launcher and the six-tool MCP smoke test. Interactive UAC, real machine reboot/logon and the owner's desktop client connection require on-device verification. Do not claim these are tested by CI alone.
