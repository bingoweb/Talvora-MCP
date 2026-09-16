# One-entry local maintenance

Run `TALVORA-DEVAM.cmd` from an extracted package. Windows UAC is the only elevation prompt. No external API key, tunnel, model provider or package installation is added.

The flow verifies the deployed current.json, Windows task and actual process behind /healthz. A healthy managed installation is reused, not reinstalled. A missing/stopped installation is started or repaired with the existing local installer. The compiled six-tool MCP smoke client must pass before legacy retirement.

Only tasks directly executing the known legacy `%LOCALAPPDATA%\Talvora\Gateway\Talvora.Gateway.exe` are exported and disabled. Only that old Gateway directory is moved to `%LOCALAPPDATA%\Talvora\Local\retired\<unique timestamp>\Gateway`. Restore metadata is saved beside it. This is reversible retirement, not permanent erasure. New `Local/`, source `Talvora-MCP/v2`, unrelated tasks, and model settings are preserved. Other Talvora Windows services are inventoried but not removed; if the legacy Gateway itself has a service registration, its folder is left in place and the report states that limitation.

The result opens in Notepad and is saved to `Local/TALVORA-SONUC.txt`, with structured evidence in `Local/maintenance-latest.json` and dated logs. A failed check produces Failed, not a success message. No logs or machine content are uploaded.

Local MCP client configuration is prepared, but a real local-client connection, UAC, reboot and owner logon still require device execution. A web-only chat does not gain localhost access from these files. The intended local model client can use ChatGPT account sign-in; the MCP itself has no model API dependency.

Validation: Windows PowerShell 5.1, real task export/disable, exact target matching including COM actions, byte-preserving legacy directory movement, sibling preservation, idempotency, actual deployed Gateway and two maintenance runs with unchanged Gateway PID and real six-tool MCP lifecycle.
