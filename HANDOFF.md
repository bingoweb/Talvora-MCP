# Talvora MCP — Canonical Handoff

## CURRENT — 2026-10-11 — Codex CLI reliability/security remediation

- Root audit found live failures: UTF-8 Turkish output mojibake, prompt text starting with `--help` parsed as a CLI option, and synthetic private-task text leaking through diagnostic stderr when its Unicode bytes were decoded incorrectly. Source-built `Talvora.Shared` now passes live interactive-user Codex tests for UTF-8 output, option separation and discarded stderr.
- `CodexCliInvocationPolicy` centralizes a literal `--` option terminator, output sanitization (remove full original prompt before generic secret redaction) and safe error summaries. `CodexCliTools` never returns raw Codex stderr; default `gpt-6.1-sol`, `medium`, `-a never` and read-only/workspace-write sandboxes remain unchanged.
- `InteractiveUserProcessRunner` pins an active session SID from discovery through `CreateProcessAsUser`, explicitly decodes UTF-8, bounds helper result JSON and persisted logs, and reports timeouts even when a killed helper left incomplete result JSON. Output pumping persists a bounded head immediately and a bounded tail on completion. In a real 2-second timeout test, the early output survived, process exited and the run directory was removed.
- `CodexMcpConfiguration` replaces unsafe TOML line skipping: recognizes quoted dotted table-key components and avoids interpreting header-shaped text inside multiline basic/literal strings; preserves unrelated registrations. Dynamic Codex executable discovery now filters candidates before taking newest 128.
- Added `--codex-integration-policy-only` deterministic .NET smoke (task args, Unicode/privacy, synthetic password, TOML quoted/multiline fixtures, huge output, stderr discard and blocked partial-output cancellation) plus a Windows CI behavior step; refreshed source gate and Context7 evidence. Full Release solution build had zero warnings/errors; 18 source/quality/installer/security gates and Codex behavior smoke passed.
- At this checkpoint, production Talvora service remains at source commit `a6ce585`. A canonical installer build and safe detached deploy with exact-installed acceptance are the next steps. Do not claim the installed runtime is fixed until that acceptance passes.

## CURRENT — 2026-10-11 — Codex CLI prompt-echo privacy hardening

- Following successful live deployment of the initial Codex CLI bridge (commit `17d950b`, full 236/Dev 199 MCP surface), an actual `talvora_codex_exec` read-only smoke succeeded with `gpt-6.1-sol` / `medium` but revealed Codex echoes the full task prompt in stderr.
- CodexCliTools now removes exact task-prompt occurrences from both bounded result streams using ordinal substring replacement, then applies existing Talvora secret redaction. Existing sandbox, account/session, model and timeout settings remain unchanged.
- Extended Codex CLI source regression to guard both redacted streams and non-echo contract. Context7 official .NET string replacement and ordinal comparison documentation verified and evidence refreshed. Follow-up GitHub Windows CI and exact-installed runtime acceptance are required before final closeout.

## CURRENT — 2026-10-11 — Codex desktop CLI bridge

- Existing Windows Codex desktop CLI is present as a real executable under the signed-in user's `LOCALAPPDATA\OpenAI\Codex\bin\<version-hash>\codex.exe`. The hash changes on updates, so resolution now enumerates bounded current desktop directories and falls back to the current WindowsApps Codex package.
- Added development-facing MCP tools `talvora_codex_info` (read-only discovery, version, interactive-user login status) and `talvora_codex_exec` (coding tasks executed through the existing `InteractiveUserProcessRunner`; never under LocalSystem).
- Defaults: `gpt-6.1-sol`, `model_reasoning_effort="medium"`. Task execution explicitly uses `workspace-write` or `read-only` with `-a never` to deny noninteractive privilege escalation, overriding unrelated broader desktop config. No API key, duplicate installation, prompt echo or raw credentials are returned.
- The reviewed MCP manifest grows to Full=236 / Dev=199 / Admin=88 / Shared=51. Source gate and the application-development smoke contract are updated. Quality evidence: Context7 `/openai/codex` and `/dotnet/docs` with official GitHub/Microsoft sources.
- Do not call this bridge a way to bypass security restrictions. The CLI and local sandbox remain bound to normal-user privileges and allowed workspaces.

## CURRENT — 2026-10-11 — Windows CI recovery (issue #18)

- GitHub windows-ci run 38087996308 passed locked restore, Release build, script parsing and WinGet rejection but failed at `PenpotOnDemandSourceRegression.ps1`: the test still required inline `!state.Registration.AutoStart` after health status was centralized in `ManagedMcpAggregateHealthPolicy.IsIgnored`. The original test failed both on GitHub and in the local workspace.
- Updated the Penpot regression to require the actual shared health policy, actionably counted offline states and tray self-test coverage; manually stopped / inactive opt-in MCPs stay neutral, while real required outages and active optional warnings remain visible.
- Same PowerShell regression host first loads `Talvora.Shared.dll` via `Add-Type`; `ProcessRunnerRegression.ps1` had rebuilt the file while it was locked. Regression now requires the prebuilt Release assembly and invokes dotnet run `--no-build`.
- Historical tool-surface pins still counted 28 deleted memory tools: reviewed Full=234, Dev=197, Admin=88, Shared=51; corrected exact count pins and the focused policy source check without restoring the removed tools or altering the Dev/Admin access policy.
- Verified: targeted Penpot regression GREEN, all 16 CI source contract scripts GREEN in the same PowerShell process after the no-build fix, Release solution 0 warnings/0 errors, desktop notification behavior GREEN, and live MCP full/dev/admin surface policy GREEN. Context7 official PowerShell regex and .NET LINQ set membership documentation checked; .context7/verification.json refreshed.
- Follow-up: GitHub Actions must finish a fresh complete windows-ci run before marking CI green. The installed Windows runtime should only be updated from a canonical verified installer if the new source policy needs deployment.

## CURRENT — 2026-10-11 — Stitch-backed Control Center WPF redesign

- Google Stitch CLI v0.11.0 authenticated (OAuth), generated actual native-desktop design direction under project `projects/9163047541738545802`, screen `cbab0028b2ea4193abd667faf1c74625`; Canvas URL: https://stitch.google.com/projects/9163047541738545802?node-id=cbab0028b2ea4193abd667faf1c74625.
- Implemented in native WPF, not an HTML/web overlay. New `ControlCenterWindow.Stitch.cs` owns working keyboard-accessible navigation: Genel Bakış, MCP Hizmetleri and Olaylar. Sidebar routes to existing live actions; there are no placebo navigation buttons.
- Stitch design system translated to `ControlCenterTheme.xaml`: bright #F7F9FF canvas, white cards, deep #101827 rail, indigo #384EC0 and restrained semantic state colors, Segoe UI Variable typography. Existing dark notification/trace palette remains intentionally independent.
- Snapshot-derived four metric tiles: ready, intentionally inactive, actionable warning/error, total registered. No fake CPU/traffic telemetry. Entire MCP roster moved from rigid 2/3-column card grid to full-width, keyboard-accessible service rows. On-demand/explicitly stopped registrations remain visible with NEUTRAL status rather than error red; true faults retain warning/error semantics.
- Service action handlers, component health, async dashboard refresh/debounce, detail lifecycle buttons, event log filters, raw log IO, installer/service dependencies, and existing desktop-notification size behavior are preserved. Detail lifecycle controls wrap on narrow windows.
- Added WPF smoke checks for sidebar navigation and a strictly opt-in PNG screenshot emitter, enabled only with `TALVORA_STITCH_PREVIEW_PNG`; source regression checks guard native WPF design, live metrics and expected state behavior. Existing unrelated uncommitted work must remain untouched.

## CURRENT — 2026-10-10 — MCP tray health and notification sizing

- Manually stopped MCP registrations are intentionally inactive and are ignored by the aggregate tray health. On-demand integrations with `AutoStart=false` are also ignored while offline or not yet checked; active attention states still count. Talvora Core remains mandatory and genuine running-service faults still show warning/red. Dashboard aggregate health follows the same policy; individual stopped MCPs stay visible as stopped.
- Code/diff notifications **never auto-increase width**. Restored user width or the 500-DIP initial width remains in force, and the user can resize it interactively. Code automatically expands **height only**, with 460–720 DIP target based on code length and bounded by the active monitor. Subsequent updates do not repeatedly change dimensions until the code section has been removed; code-free updates return to compact height and a later code update can expand vertically again.
- Only manual resize updates persistent user width/height. Automatic code height must not overwrite `desktop-progress-placement.json`. Old entries in this file describing automatic 960-DIP width or persisting computed diff geometry are superseded by this decision.
- Tests: `ManagedMcpAggregateHealthPolicy.AssertContract`, `Talvora.Tray --self-test`, `Talvora.Notification.Regression` WPF long-code/compact/restore cases, `ModernizationPolicyRegression.ps1` and Release build.

## CURRENT — 2026-10-01 — Notification Faz 18 code visibility
User scope: improve desktop notification design, automatically expand when code/diff arrives, show real changed code.
- Isolated feature/notification-code-view worktree preserves the three pre-existing installer changes in the primary repository.
- Saved compact geometry no longer blocks code expansion. First diff grows both axes to at least 960 DIP width and content-based 460–720 DIP height, clamped to monitor limits. Further terminal updates preserve size; native user resizing and existing position/size persistence remain.
- Code font 10 -> 12 DIP; dedicated lower code viewport stays wrapped, selectable, scrollable and copyable; header carries real +/- counts and current operation state.
- Removed 120-line / 7 KiB preview ceilings. Dedicated code budget is 524,288 characters; larger output has an explicit incomplete-content marker. Secret redaction remains. Frame maximum is 4 MiB, retaining cancellation, ACL and bounded reads.
- Unified diff file headers are preserved as well as hunks/context.
- Validation: Release solution build 0 warnings / 0 errors; 1000 old + 1000 new changed lines survive notifier and exact framed IPC; actual WPF compact-size expansion and terminal-size preservation GREEN. Modernization, Context7, privacy/security and UI responsiveness GREEN.
- Context7 /dotnet/docs plus official Microsoft Window.SizeToContent documentation checked: explicit dimensions require Manual mode.
- Gitea issue creation was rejected by automatic approval review; no issue created. Local records serve as the review trail.
- Runtime FINAL: exact-installed sourceCommit 990e40e6ca7914b60f4515379ef85eff6ec6c2e9; installer 342,629,647 B, SHA-256 D479365D08A1BD4DBA5781AC281E6C6A9F5BEF1622A4BACA70364C71C8A4399A.
- Installed Tray live QA: 1440x1021 native-pixel window; code viewport 1352x761; 10,308 code characters SHA-256 2D5E16376B650EE9AD275E7B30B844C9EB33012F6A52C0978ABE645237B0B92B matches sender exactly; CanResize true. Visual inspection confirms code is readable and unobscured.
- Visual follow-up 990e40e keeps new notifications without code compact instead of inheriting large diff geometry. Regression covers both code expansion and compact non-code generations; DESKTOP_PROGRESS_BEHAVIOR_GREEN.
- Installed Tray self-test exit 0; Talvora/Gitea/Caddy Running/Automatic. Source-affecting changes are committed on main; the three earlier installer edits remain untouched.
- Subsequent docs/test closeout commit does not require runtime redeployment.


## CURRENT — 2026-10-01 06:43+03:00 — #291 bounded lifecycle runtime-generation state FINAL

Bu bölüm en üst kanonik checkpoint'tir. #280–#290 kapalı kalır. Bu tur generic managed-MCP stop/restart verification içindeki son doğrudan unbounded `File.ReadAllTextAsync` yolu #291 olarak kapatıldı.

### Canonical runtime / repository state

- Runtime-affecting commit: `783e1242306c6dec2ee490d932971b67c7c8ee25` — `fix: bound lifecycle runtime state`.
- Runtime publish öncesi `HEAD = origin/main = github/main = 783e124...`; working tree clean.
- Canonical installer: **342,629,647 bytes**; SHA-256 `4E70930BED23C0AC71F1D8F82D427E66D370C77123F34FC3CED8B3FAA3C30C2F`; manifest source/head exact `783e124...`.
- Exact-installed runtime: `talvora_system_info.sourceCommit=783e124...`, LocalSystem / `S-1-5-18`, PID **1944**.
- Installed version root: `C:\Program Files\Talvora\Versions\783e1242306c6dec2ee490d932971b67c7c8ee25-20261001034241498`.
- Installer success: `2026-10-01T06:42:50.7868966+03:00`; Tray stable PID 11428.
- Takip eden docs-only closeout commit runtime fingerprint değildir; sırf repo HEAD ilerledi diye runtime yeniden deploy edilmemelidir.

### #291 — lifecycle stop/restart unbounded RuntimeGenerationStatePath

- Reachable chain:
  - generic managed MCP `Stop` -> lifecycle PowerShell stop -> `EnsureRegisteredRuntimeStoppedAsync`.
  - generic managed MCP `Restart` -> stop -> aynı verification -> start.
- Aynı `RuntimeGenerationStatePath` readiness/probe tarafında zaten `JsonFileStore.ReadBounded(..., 256 KiB)` ile bounded iken lifecycle verification eski `File.ReadAllTextAsync + JsonDocument.Parse` yolunda kalmıştı.
- Safe gerçek private-method fixture:
  - sahte `ManagedMcpRegistration`;
  - endpoint/backend `http://127.0.0.1:65534/mcp` (kapalı);
  - state yalnız `{"generation":"audit"}` + trailing whitespace, **launcherPid yok**;
  - dolayısıyla hiçbir gerçek PID/process/service kill yolu çalışmadı.
- Pre-fix **32 MiB** state:
  - **236,588,536 B managed allocation**
  - **184,500,224 B working-set delta**
  - **893 ms**
- Fix:
  - `MaximumRuntimeGenerationStateBytes = 256 * 1024`.
  - lifecycle restore artık `JsonFileStore.ReadBoundedAsync<JsonDocument>`.
  - cancellation token korunuyor.
  - `InvalidDataException` mevcut fail-soft log boundary'sine dahil.
  - process identity/launcher kill/endpoint-closure semantiği değiştirilmedi.
- Post-fix aynı fixture:
  - **424,056 B allocation**
  - **12,578,816 B working-set delta**
  - **904 ms**; kalan süre kapalı-loopback endpoint stop doğrulamasından geliyor.

### Permanent regression / quality

- `NativeInstallerSourceRegression.ps1` artık `LifecycleRuntimeGenerationStateReadIsBounded=True` şartını taşır:
  - explicit 256 KiB ceiling,
  - `JsonFileStore.ReadBoundedAsync<JsonDocument>`,
  - lifecycle `File.ReadAllTextAsync(statePath)` yokluğu.
- Targeted RED yalnız bu yeni contract'ta False oldu; fix sonrası Native Installer source GREEN.
- Full Release solution build **0 warning / 0 error**.
- Focused MCP surface GREEN; Gitea stop-chain terminal GREEN; privacy/security GREEN; ModernizationPolicy GREEN; Context7 GREEN; analyzer verify-no-changes exit 0; source Tray self-test + Control Center smoke GREEN.
- Context7 `/dotnet/docs`: FileStream async I/O, cancellation-aware bounded read yaklaşımı aynı change içinde yenilendi.

### Installed acceptance

- Canonical deploy service-hosted caller'dan yine `waitForCompletion=false` detach ile çalıştı.
- Exact installed `783e124...` Tray single-file layout'tur; ayrı `Talvora.Tray.dll`/Shared DLL publish edilmediği için external reflection harness hayali DLL path üzerinden koşturulmadı.
- Exact-installed kanonik yüzeyler:
  - `--self-test` exit **0**
  - `--managed-mcp-probe talvora-dev` exit **0**
  - `--managed-mcp-probe talvora-admin` exit **0**
  - Talvora/Gitea/Caddy `Running / Automatic`
  - deploy sonrası yeni SCM error event yok.
- Feature-specific 32 MiB real private-method behavior fixture source-built **aynı committed code** üzerinde GREEN; canonical installer source/head fingerprint exact aynı commit olarak doğrulandı.

### Sonraki audit

- #291 kapalıdır. Yeni deep-audit #292'den devam etmelidir.
- Öncelik: kalan local metadata / HTTP body / process-output / persisted state bounded-I/O ve lifecycle resource-release yolları.
- #280–#291 yeni gerçek kanıt olmadan yeniden açılmamalıdır.

## CURRENT — 2026-10-01 06:34+03:00 — #290 bounded tunnel health-url reads FINAL

Bu bölüm en üst kanonik checkpoint'tir. #280–#289 kapalı kalır. Bu tur Control Center component health ve Gitea tunnel readiness yollarında aynı küçük `.url` metadata dosyasının sınırsız okunması #290 olarak kapatıldı.

### Canonical runtime / repository state

- Runtime-affecting commit: `aa7efbd9f74776481fe25c077c87e7558db35b3f` — `fix: bound tunnel health url reads`.
- Runtime publish öncesi `HEAD = origin/main = github/main = aa7efbd9...`; working tree clean.
- Canonical installer: **342,629,647 bytes**; SHA-256 `15B34E0BDFA5BD1B6FAFDF01BB60F1313418B595D7DE09444D855EE977C263C4`; manifest source/head exact `aa7efbd9...`.
- Exact-installed runtime: `talvora_system_info.sourceCommit=aa7efbd9...`, LocalSystem / `S-1-5-18`, PID **4724**.
- Installed version root: `C:\Program Files\Talvora\Versions\aa7efbd9f74776481fe25c077c87e7558db35b3f-20261001033300039`.
- Docs-only closeout commit runtime fingerprint değildir; sırf repo HEAD ilerledi diye runtime yeniden deploy edilmemelidir.

### #290 — unbounded tunnel health URL metadata reads

- Reachable production paths:
  - `ControlCenterComponentHealthService` -> `health/<alias>.url`.
  - `GiteaTrayClient.ProbeTunnelAsync` -> `gitea-business.url`.
- Eski kod her iki yolda da `File.ReadAllTextAsync(...).Trim()` ile dosyanın tamamını allocate ediyor, ardından URI/HTTP işine giriyordu.
- Yeni shared `Talvora.Shared.TextFileStore.ReadBoundedAsync`:
  - tek `FileStream` açar;
  - aynı handle'ın `Length` değerini allocation öncesi doğrular;
  - explicit maximum byte ceiling uygular;
  - yalnız doğrulanmış snapshot kadar buffer ayırır;
  - `ReadExactlyAsync` ile aynı snapshot'ı okur;
  - UTF-8 decode eder.
- Her iki consumer için ceiling **4 KiB**. Oversized/bozuk dosya mevcut kullanıcı davranışıyla fail-soft unavailable/offline durumuna döner; URI/HTTP çağrısı yapılmaz.

### Gerçek A/B fixture kanıtı

- Fixture: SYSTEM profile `Gitea\McpTunnel\state\health\gitea-business.url`, **32 MiB**, başında geçerli `http://127.0.0.1:65534`, devamı whitespace. Test sonunda gerçek dosya birebir geri yüklendi.
- Pre-fix HEAD `6bdf076...` snapshot build, gerçek `GiteaTrayClient.ProbeTunnelAsync`:
  - **136,023,664 B managed allocation**
  - **155,729,920 B working-set delta**
  - **2337 ms**
  - daha sonra HTTP timeout yoluna girdi.
- Post-fix worktree:
  - **18,240 B managed allocation**
  - **4,767,744 B working-set delta**
  - **117 ms**
  - 4 KiB bound'da fail-soft döndü, URI/HTTP işine gitmedi.

### Kalıcı regression / quality

- `ControlCenterUiResponsivenessSourceRegression.ps1`: Control Center health URL 4 KiB ceiling + `TextFileStore.ReadBoundedAsync` + eski unbounded read yokluğu.
- `GiteaProtocolReadinessRegression.ps1`: Gitea health URL 4 KiB same-handle bounded contract.
- `StorageMaintenanceSourceRegression.ps1`: shared `TextFileStore` stream length / exact snapshot / byte ceiling sözleşmesi.
- `StorageMaintenanceBehaviorRegression.ps1`: 65 B text / 64 B ceiling gerçekten `InvalidDataException`.
- Full Release solution build **0 warning / 0 error**.
- Storage source/behavior, UI responsiveness, Gitea readiness, privacy/security, ModernizationPolicy, Context7, analyzer, source Tray self-test ve Control Center smoke GREEN.
- Context7 `/dotnet/docs`: FileStream async I/O, memory-based reads ve `ReadExactlyAsync` evidence aynı change içinde yenilendi.

### Deploy / installed acceptance

- İlk deploy denemesi yeni service start sırasında tek seferlik SCM 7009/7000 **1053 / 30 s timeout** yaşadı; installer doğru rollback ile `284a065d...` runtime'ı geri getirdi.
- Yeni `aa7efbd...` service EXE daha sonra kontrollü console bootstrap'ta Kestrel'e hızla ulaştı ve beklenen `7676 already in use` ile çıktı; binary/bootstrap bozukluğu görülmedi. Defender/Code Integrity tarafında Talvora blok kaydı yoktu.
- Aynı canonical artifact ikinci deploy'da başarılı:
  - 34% `06:33:00.660` -> 50% `06:33:01.026`: yaklaşık **0.37 s**
  - force-stop/delete/rollback yok
  - `Install succeeded` at `06:33:09.607`
  - yeni SCM error event yok.
- Installed self-test + Dev/Admin managed probes exit 0.
- Exact-installed 32 MiB `gitea-business.url` altında real `--control-center-smoke` exit **0**, peak WS **161,284,096 B**, elapsed **7172 ms**; fixture sonunda geri yüklendi.
- Talvora/Gitea/Caddy `Running / Automatic`.

### Sonraki audit

- #290 kapalıdır. Yeni deep-audit turu #291'den devam etmelidir.
- Öncelik: kalan persisted-state / lifecycle / local metadata / HTTP-body bounded-I/O yolları. Yalnız gerçek çağrı zinciri + fixture ile doğrulanan bulguyu aç.
- #280–#290 yeni gerçek kanıt olmadan yeniden açılmamalıdır.

## CURRENT — 2026-09-30 21:04+03:00 — #289 bounded Control Center version metadata FINAL

Bu bölüm en üst kanonik checkpoint'tir. #280–#288 kapalı kalır. Bu tur Control Center detail/version yüzeyindeki local JSON metadata restore yolunun gerçek bounded-I/O bulgusu #289 kapatıldı.

### Canonical runtime / repository state

- Runtime-affecting commit: `284a065d0f8842f3fcde2b2f00739580a383eab0` — `fix: bound control center version metadata`.
- Runtime publish öncesi `HEAD = origin/main = github/main = 284a065d...`; working tree clean.
- Canonical installer: **342,628,111 bytes**; SHA-256 `A60228B3196903F8F183485AAC89F1248A2FD80E006CBE777A9795CDAC842770`; manifest source/head exact `284a065d...`.
- SYSTEM deploy sonrası exact-installed `talvora_system_info.sourceCommit=284a065d...`, LocalSystem / `S-1-5-18`, service PID **22100**.
- Installed version root: `C:\Program Files\Talvora\Versions\284a065d0f8842f3fcde2b2f00739580a383eab0-20260930180339500`.
- Takip eden docs-only closeout commit runtime fingerprint değildir; sırf repository HEAD ilerledi diye runtime yeniden deploy edilmemelidir.

### #289 — unbounded local version metadata reads

- Gerçek production çağrı zinciri: `ControlCenterVersionService.GetVersionAsync(registration)`.
- `registration.Id=talvora` olduğunda `%LOCALAPPDATA%\Talvora\current.json` eski kodda `File.ReadAllTextAsync` ile sınırsız okunup sonra `JsonDocument.Parse` ediliyordu.
- Managed MCP package discovery de aynı şekilde `package-file` hint'ini sınırsız `File.ReadAllTextAsync` ile okuyordu.
- Gerçek pre-fix fixture:
  - küçük `current.json`: **59,120 byte managed allocation**, **4,050,944 byte WS delta**, 21 ms.
  - **32 MiB** valid JSON + trailing whitespace: **236,575,560 byte managed allocation**, **180,293,632 byte WS delta**, 324 ms; yine `3.0.0-dev` döndü.
- Fix:
  - `MaximumTalvoraVersionMetadataBytes = 512 * 1024`.
  - `MaximumPackageVersionMetadataBytes = 4 * 1024 * 1024`.
  - Her iki local metadata yolu shared same-handle `JsonFileStore.ReadBoundedAsync<JsonDocument>` kullanıyor.
  - Oversized/corrupt metadata mevcut fail-soft kullanıcı davranışıyla `Bilinmiyor` döner; normal version display semantiği değişmedi.
- Post-fix aynı 32 MiB Talvora fixture: **368,752 byte managed allocation**, **8,060,928 byte WS delta**, 71 ms; sonuç `Bilinmiyor`.
- Fixture testleri SYSTEM profile `current.json` içeriğini sonunda birebir geri yükledi.
- `ControlCenterUiResponsivenessSourceRegression.ps1` iki explicit ceiling'i, bounded async reader'ı ve local `File.ReadAllTextAsync` yokluğunu kalıcı kilitliyor.

### Final quality / live acceptance

- Targeted RED explicit metadata ceiling eksikliği nedeniyle exit 1; fix sonrası `CONTROL_CENTER_UI_RESPONSIVENESS_SOURCE_GREEN`.
- Tray Release + full solution build **0 warning / 0 error**.
- Privacy/security GREEN; `MODERNIZATION_POLICY_GREEN`; `CONTEXT7_QUALITY_GATE_GREEN`; analyzer verify-no-changes exit 0.
- Source Tray self-test + Control Center smoke GREEN.
- Context7 `/dotnet/docs`: local metadata size validation/allocation-before-parse riski aynı change içinde yenilendi.
- Exact-installed final self-test, Dev probe ve Admin probe exit 0; canonical live counts Dev **203**, Admin **84**, Ready + BrowserSmokePassed.
- Talvora/Gitea/Caddy `Running / Automatic`.
- Final deploy log 34% `21:03:40.212` -> 50% `21:03:40.725`: yaklaşık **0.51 s**; force-stop/delete/rollback yok; `Install succeeded` at `21:03:49.030`.
- Deploy sonrası yeni SCM **7034 yok**.

### Sonraki audit

- #289 kapalıdır. Yeni deep-audit turu #290'dan devam etmelidir.
- persisted-state / local metadata / health-url / HTTP-body bounded-I/O adaylarını production çağrı zinciri + gerçek fixture ile ele.
- #280–#289 yeni gerçek kanıt olmadan yeniden açılmamalıdır.

## CURRENT — 2026-09-30 20:55+03:00 — #288 bounded Control Center window placement FINAL

Bu bölüm en üst kanonik checkpoint'tir. #280–#287 kapalı kalır. Bu tur Control Center'ın persisted pencere konumu/boyutu restore yolundaki gerçek UI-thread bounded-I/O bulgusu #288 kapatıldı.

### Canonical runtime / repository state

- Runtime-affecting commit: `7e05e19503d55b1b9f2590a6bd4abb26ed99e0a9` — `fix: bound control center window placement`.
- Runtime publish öncesi `HEAD = origin/main = github/main = 7e05e195...`; working tree clean.
- Canonical installer: **342,628,111 bytes**; SHA-256 `3D24D39BB883C2259D1152F742A8E23083C4FE208B9EBFEEE9C4F07654B80AC0`; manifest source/head exact `7e05e195...`.
- SYSTEM deploy sonrası exact-installed `talvora_system_info.sourceCommit=7e05e195...`, LocalSystem / `S-1-5-18`, service PID **4800**.
- Installed version root: `C:\Program Files\Talvora\Versions\7e05e19503d55b1b9f2590a6bd4abb26ed99e0a9-20260930175420341`.
- Takip eden docs-only closeout commit runtime fingerprint değildir; sırf repository HEAD ilerledi diye runtime yeniden deploy edilmemelidir.

### #288 — unbounded `window-placement.json` restore on WPF startup

- Reachable production path: `ControlCenterWindow(false)` içinde `SourceInitialized` doğrudan `RestoreWindowPlacement()` çağırıyor; eski kod `File.ReadAllText(path)` + `JsonSerializer.Deserialize` ile dosyanın tamamını UI thread üzerinde allocate ediyordu.
- İlk installed `--control-center-smoke` fixture denemesi bu yolu ölçmedi çünkü smoke mode bilinçli olarak placement restore'u bypass ediyor. Bu false-negative bug sayılmadı.
- Ardından gerçek `ControlCenterApplication` resource bootstrap + `ControlCenterWindow(false)` + `Window.Show()` production harness ile SourceInitialized restore yolu doğrudan ölçüldü.
- Pre-fix:
  - küçük normal placement: **5,638,592 byte managed allocation**, **27,422,720 byte WS delta**, 370 ms.
  - **32 MiB** valid placement + trailing whitespace: **279,594,840 byte managed allocation**, **179,933,184 byte WS delta**, 368 ms.
- Fix: `MaximumWindowPlacementBytes = 64 * 1024`; restore artık shared same-handle `JsonFileStore.ReadBounded<WindowPlacementDocument>` kullanıyor. Oversized/malformed placement mevcut fail-soft davranışla restore edilmez ve varsayılan geometry korunur. Normal save/restore semantiği değişmedi.
- Post-fix aynı production WPF harness:
  - küçük placement: **5,626,096 byte managed allocation**, **26,701,824 byte WS delta**.
  - 32 MiB fixture: **5,999,456 byte managed allocation**, **34,824,192 byte WS delta**.
- Fixture testlerinin tümü sonunda önceki SYSTEM-profile placement state'i geri yüklendi.
- Kalıcı `ControlCenterUiResponsivenessSourceRegression.ps1` 64 KiB ceiling'i, same-handle bounded reader'ı ve UI-thread `File.ReadAllText(path)` yokluğunu kilitliyor.

### Final quality / live acceptance

- Targeted RED yeni ceiling eksikliği nedeniyle exit 1; fix sonrası `CONTROL_CENTER_UI_RESPONSIVENESS_SOURCE_GREEN`.
- Tray Release ve full solution build **0 warning / 0 error**.
- Privacy/security GREEN; `MODERNIZATION_POLICY_GREEN`; `CONTEXT7_QUALITY_GATE_GREEN`; analyzer verify-no-changes exit 0.
- Source Tray self-test + Control Center smoke GREEN.
- Exact-installed final self-test, Dev probe ve Admin probe exit 0; canlı canonical counts Dev **203**, Admin **84**, Ready + BrowserSmokePassed.
- Talvora/Gitea/Caddy `Running / Automatic`.
- Final deploy log 34% `20:54:21.143` -> 50% `20:54:21.699`: yaklaşık **0.56 s**; force-stop/delete/rollback yok; `Install succeeded` at `20:54:30.143`.
- Deploy sonrası yeni SCM **7034 yok**.

### Sonraki audit

- #288 kapalıdır. Yeni deep-audit turu #289'dan devam etmelidir.
- persisted-state / lifecycle / bounded-I/O adaylarında smoke/test bypass'larını özellikle kontrol et; yalnız production çağrı zinciri + gerçek fixture ile doğrulanan bulguyu aç.
- #280–#288 yeni gerçek kanıt olmadan yeniden açılmamalıdır.

## CURRENT — 2026-09-30 20:41+03:00 — #287 bounded DPAPI credential restore FINAL

Bu bölüm en üst kanonik checkpoint'tir. #280–#286 kapalı kalır. Bu tur persisted credential restore yolundaki yeni gerçek bounded-I/O bulgusu #287 kapatıldı.

### Canonical runtime / repository state

- Runtime-affecting commit: `fea12d219d4b6dd9998d92f9d2ba46714f7a3936` — `fix: bound DPAPI credential restore`.
- `HEAD = origin/main = github/main = fea12d219...`; working tree clean.
- Canonical installer: **342,628,111 bytes**; SHA-256 `2031E214D0F17119359847162AEF0F451CF07E22AFE040118BA7044EAAE73425`; manifest source/head exact `fea12d219...`.
- Exact-installed runtime: `talvora_system_info.sourceCommit=fea12d219...`, LocalSystem / `S-1-5-18`, service PID **9276**.
- Installed version root: `C:\Program Files\Talvora\Versions\fea12d219d4b6dd9998d92f9d2ba46714f7a3936-20260930171347189`.
- Install log: 34% `20:13:47.728` -> 50% `20:13:48.292` (~0.56 s); force-stop/delete/rollback yok; `Install succeeded` at `20:13:55.757`.
- Bu bölümün takip eden docs-only closeout commit'i runtime fingerprint değildir; sırf repository HEAD ilerledi diye runtime yeniden deploy edilmemelidir.

### #287 — DPAPI credential restore unbounded file read

- Kök neden: `DpapiSecretStore.ReadString` persisted credential blob'unu `File.ReadAllText` ile bütünüyle string'e alıyor, ancak dosya boyutuna hiçbir üst sınır koymuyordu. Credential dosyası bozulur/aşırı büyürse DPAPI decode'dan önce gereksiz büyük allocation oluşabiliyordu.
- Gerçek 32 MiB geçici credential fixture pre-fix yaklaşık **134.6 MB managed allocation / 135.2 MB working-set büyümesi** üretti.
- Fix:
  - `MaximumCredentialFileBytes = 64 * 1024`.
  - `ReadString` tek `FileStream` açıyor, aynı handle'ın `Length` değerini allocation/hex decode öncesi kontrol ediyor ve yalnız bounded text'i okuyor.
  - `WriteString` protected hex blob'u aynı 64 KiB persist ceiling'ini aşarsa yazmayı reddediyor.
  - Tray self-test gerçek credential dosyalarına dokunmadan normal DPAPI round-trip + oversized temporary credential rejection sözleşmesini doğruluyor.
- Aynı 32 MiB post-fix fixture yaklaşık **19.9 KB managed allocation / 0.56 MB working-set büyümesi** ile DPAPI decode öncesi reddedildi.
- Kalıcı `PrivacySecuritySourceRegression.ps1` 64 KiB ceiling'i, same-handle FileStream length check'i, write bound'u ve unbounded `File.ReadAllText` yokluğunu kilitliyor.

### Final quality / live acceptance

- `TALVORA PRIVACY SECURITY SOURCE REGRESSION GREEN`.
- Full Release solution build **0 warning / 0 error**.
- `CONTEXT7_QUALITY_GATE_GREEN`; `MODERNIZATION_POLICY_GREEN`; analyzer verify-no-changes exit 0.
- Context7 `/dotnet/docs`: large/untrusted local file için stream tabanlı okuma ve allocation öncesi size bound yaklaşımı yeniden doğrulandı.
- Exact-installed `fea12d219...` Tray `--self-test` exit **0**; oversized DPAPI self-test production binary içinde çalıştı.
- Installed Dev/Admin managed probe exit **0**; canlı canonical tool counts Dev **203**, Admin **84**, Ready + BrowserSmokePassed.
- Talvora/Gitea/Caddy `Running / Automatic`.
- Deploy sonrası yeni SCM **7034 yok**.

### Sonraki audit

- #287 kapalıdır. Yeni deep-audit turu #288'den devam etmelidir.
- persisted-state / lifecycle / bounded-I/O taramasında yalnız gerçek çağrı zinciri + fixture ile doğrulanan bulguyu aç.
- #280–#287 yeni gerçek kanıt olmadan yeniden açılmamalıdır.

## CURRENT — 2026-09-30 20:01+03:00 — #285 bounded manual-stop state + #286 canonical self-update cycle FINAL

Bu bölüm en üst kanonik checkpoint'tir. #280–#284 kapalı kalır. Bu tur iki gerçek deep-audit bulgusu kapatıldı ve final exact-installed runtime her ikisini birlikte taşır.

### Canonical runtime / repository state

- Runtime-affecting final commit: `8987aa67c90c9170238fbe4b514a71314631b4e8` — `fix: detach canonical self-update`.
- Parent runtime commit `29f009e3d8dcb9344fa82a69227b5f80028cc849` — `fix: bound manual-stop session state`.
- Final runtime publish öncesi `HEAD = origin/main = github/main = 8987aa67...`; working tree clean.
- Canonical installer: **342,628,111 bytes**; SHA-256 `CF0EC02B58183021C0AF09D1ABDE9BEA986515CBA6530E822DDBA3DC3163813F`; manifest source/head exact `8987aa67...`.
- SYSTEM deploy sonrası exact-installed `talvora_system_info.sourceCommit=8987aa67...`, LocalSystem / `S-1-5-18`, service PID **39076**.
- Installed version root: `C:\Program Files\Talvora\Versions\8987aa67c90c9170238fbe4b514a71314631b4e8-20260930165949075`.
- Takip eden docs-only closeout commit runtime fingerprint değildir; sırf repository HEAD ilerledi diye `8987aa67...` yeniden deploy edilmemelidir.

### #285 — Windows-session manual-stop state unbounded JSON restore

- Gerçek pre-fix fixture: SYSTEM profile `session-state.0.json` geçici olarak **32 MiB** valid JSON + trailing whitespace yapıldı. Gerçek `ManagedMcpSessionState.IsManuallyStopped("audit-probe")` çağrısı yaklaşık **268,941,928 byte managed allocation**, **167,563,264 byte working-set artışı**, **145 ms** üretti.
- Kök neden: current ve legacy state restore yolları doğrudan `File.ReadAllText` + `JsonSerializer.Deserialize` kullanıyordu.
- Fix: `MaximumSessionStateDocumentBytes = 256 * 1024`; current ve legacy state artık shared same-handle `JsonFileStore.ReadBounded<SessionStateDocument>` ile okunuyor. Oversized/corrupt state mevcut fail-soft davranışla fresh current-session state'e resetleniyor.
- Post-fix aynı 32 MiB fixture: **500,288 byte managed allocation**, **11,300,864 byte working-set delta**, **256 ms**; state otomatik **192 bayt** fresh document'e self-heal oldu.
- Exact-installed final `8987aa67...` acceptance: 32 MiB state altında installed Tray `--self-test` exit **0**, peak working set **58,011,648 bytes**, state **193 bayta** self-heal; test sonunda orijinal state geri yüklendi.

### #286 — canonical deploy service-hosted wait cycle + forbidden service deletion regression

- Live pre-fix failure 2026-09-30 19:45+03:00: Talvora MCP içinden `Deploy-Windows-Installer.ps1 -WaitForCompletion` aktif MCP isteğini SYSTEM installer task bitene kadar açık tuttu. Installer aynı servisi durdurmaya çalıştığı için graceful stop 8 saniyede tamamlanmadı; PID force terminate edildi, SCM 7034 oluştu. Mevcut `StopServiceForUpgradeAsync` daha sonra eski #60 invariant'ını delerek `sc delete` fallback'ine girdi; service deletion 15 saniyede tamamlanmayınca `Windows service silinemedi: Talvora` timeout ve rollback oluştu.
- Rollback doğru çalıştı; eski `866ea5a...` runtime yeniden ayağa kalktı. Veri/service registration kaybı olmadı.
- Historical #60/#82 contract yeniden kanonikleştirildi: upgrade yolu Talvora service registration'ını **asla silmez**; `StopAndDeleteServiceAsync` yalnız gerçek uninstall/legacy cleanup içindir.
- `StopServiceForUpgradeAsync`: force-stop sonrası SCM STOPPED için 30 saniye bounded bekler; hâlâ terminal değilse registration'ı koruyan TimeoutException atar ve recovery policy finally ile restore edilir. Upgrade path içinde `"delete"`/WaitForServiceDeletion yoktur.
- `Deploy-Windows-Installer.ps1`: parent chain içinde `Talvora.exe` varsa caller `-WaitForCompletion` vermiş olsa bile `WaitForCompletion=$false` yapar. SYSTEM task yine bağımsız başlar, fakat MCP request installer servisi durdurmadan önce serbest kalır.
- Kalıcı Native Installer regression artık iki boşluğu ayrı kilitler:
  - `CanonicalDeployDetachesServiceAncestorWait=True`
  - `InstallerUpgradeNeverDeletesCanonicalService=True`
- Microsoft Learn / Context7 doğrulaması: `DeleteService/sc delete` servis çalışırken veya açık service handle'ları varken yalnız marked-for-delete olur; gerçek silme ancak servis durmuş ve handle'lar kapanmışken tamamlanır. Upgrade için doğru model mevcut registration'ı stop/reconfigure etmektir.

### #286 gerçek live acceptance

- Source-fixed deploy launcher henüz runtime commit olmadan aynı `-WaitForCompletion` çağrısını Talvora MCP içinden aldı ve JSON'da **`waitForCompletion=false`** döndürdü; task instance `{C81DC40E-37C2-4983-85E0-A10C1D6436C1}`.
- Aynı mevcut `29f009e...` installer bu detach ile başarıyla deploy oldu:
  - log 34% `19:54:51.258` → 50% `19:54:51.573`: yaklaşık **0.31 s**
  - force-stop satırı yok
  - service-delete satırı yok
  - yeni SCM 7034 yok
  - install success `19:55:00.078`.
- Final packaged `8987aa67...` installer da Talvora MCP içinden bilerek yine `-WaitForCompletion` ile çağrıldı ve **`waitForCompletion=false`** döndürdü; task instance `{FABB71DE-028D-4887-8069-08288186576A}`.
- Final deploy:
  - 34% `19:59:49.688` → 50% `19:59:50.242`: yaklaşık **0.55 s**
  - force-stop yok
  - `sc delete`/rollback yok
  - yeni SCM event yok
  - `Install succeeded. Commit=8987aa67... PID=39076` at `19:59:58.510`.

### Final quality gates

- Full Release solution build **0 warning / 0 error**.
- `NATIVE_INSTALLER_SOURCE_GREEN`; `CANONICAL_DEPLOY_ARTIFACT_IDENTITY_GREEN`.
- Windows PowerShell 5.1 deploy-script parse GREEN.
- `CONTROL_CENTER_REPAIR_SOURCE_GREEN`; privacy/security source GREEN.
- `MODERNIZATION_POLICY_GREEN`; `CONTEXT7_QUALITY_GATE_GREEN`; analyzer verify-no-changes exit 0.
- Source Tray self-test GREEN.
- Exact-installed final self-test exit 0; Dev/Admin managed probes exit 0. Live log canonical counts: Dev **203**, Admin **84**, Ready + BrowserSmokePassed.
- Talvora/Gitea/Caddy `Running / Automatic`.

### Sonraki audit

- #285 ve #286 kapalıdır. Yeni deep-audit turu #287'den devam etmelidir.
- Özellikle kalan persisted-state / lifecycle / bounded-I/O yollarını gerçek çağrı zinciri ve fixture ile tara; sadece reproduksiyonla doğrulanmış bug'ı aç.
- #280–#286 yeni gerçek kanıt olmadan yeniden açılmamalıdır.

## CURRENT — 2026-09-30 19:38+03:00 — Control Center event-store bounded restore FINAL

Bu bölüm en üst kanonik checkpoint'tir. Notification #280–#283 kapalı kalır. Bu tur yeni deep-audit bulgusu #284'ü kapattı: Control Center'ın persisted `events.json` restore yolu sınırsız `File.ReadAllText` kullanıyordu.

### Canonical runtime / repository state

- Runtime-affecting commit: `866ea5a29f3a34616d307208e1f8bd53ecc9edd8` — `fix: bound control center event history`.
- Runtime commit publish edildiğinde `HEAD = origin/main = github/main = 866ea5a...`; working tree clean.
- Canonical installer: **342,628,111 bytes**; SHA-256 `962C97A2A868E6EE4E35CD158C917BE2F877C775CB3228FE9F38AB0664A847D4`; manifest source/head exact `866ea5a...`.
- SYSTEM deploy sonrası exact-installed `talvora_system_info.sourceCommit=866ea5a...`, LocalSystem / `S-1-5-18`, service PID 16060.
- Installed version root: `C:\Program Files\Talvora\Versions\866ea5a29f3a34616d307208e1f8bd53ecc9edd8-20260930163357491`.
- Bu bölümün takip eden docs-only closeout commit'i runtime fingerprint değildir; sırf repo HEAD ilerledi diye `866ea5a...` yeniden deploy edilmemelidir.

### #284 — persisted Control Center event history unbounded restore

- Pre-fix gerçek fixture: SYSTEM profile Control Center `events.json` geçici olarak **32 MiB** valid JSON + trailing whitespace olacak şekilde üretildi; gerçek `ControlCenterEventStore.ReadRecent` reflection çağrısı yaklaşık **268,986,408 byte managed allocation**, **167,612,416 byte working-set artışı** ve **263 ms** okuma maliyeti üretti.
- Kök neden: `ReadUnsafe()` doğrudan `File.ReadAllText(PathName)` ile persisted dosyanın tamamını string olarak allocate ediyordu.
- Fix: mevcut `MaxRecords=500` ve field-length sözleşmesi aynen korunarak `MaximumEventDocumentBytes = 8 * 1024 * 1024` eklendi; restore artık shared same-handle `JsonFileStore.ReadBounded<ControlCenterEventDocument>` kullanıyor.
- Malformed/oversized dosya mevcut davranıştaki gibi boş document'e fail-soft döner; normal event semantiği, dedup, retention ve write formatı değişmedi.
- Kalıcı source regression: UI responsiveness gate artık 8 MiB ceiling'i, bounded reader kullanımını ve `File.ReadAllText(PathName)` yokluğunu doğruluyor.

### RED → GREEN / live acceptance

- Targeted RED: yeni source contract pre-fix kodda explicit 8 MiB ceiling olmadığı için exit 1.
- Targeted GREEN: `CONTROL_CENTER_UI_RESPONSIVENESS_SOURCE_GREEN`; Tray Release build **0 warning / 0 error**.
- Aynı 32 MiB post-fix fixture: **10,688 byte managed allocation**, **2,785,280 byte working-set artışı**; sonuç tipi normal event-record array.
- Full Release solution build **0 warning / 0 error**.
- Privacy source regression GREEN, ModernizationPolicy GREEN, Context7 gate GREEN, analyzer verify-no-changes exit 0, source-built Control Center visual smoke GREEN.
- Context7 `/dotnet/docs` System.Text.Json / stream guidance yeniden doğrulandı: çok büyük JSON için tüm dosyayı tek string'e yükleme ve sınırsız buffer büyümesi kaçınılmalı.
- Installed self-test exit 0; Dev `Ready=True / BrowserSmokePassed=True / ToolCount=203`; Admin `Ready=True / BrowserSmokePassed=True / ToolCount=84`.
- Talvora/Gitea/Caddy `Running / Automatic`.
- Exact-installed `866ea5a...` binary ile 32 MiB SYSTEM-profile `events.json` varken gerçek `--control-center-smoke` exit **0** verdi; peak working set **161,009,664 bytes**, elapsed **6795 ms**. Fixture test sonunda önceki state'e geri alındı.

### Sonraki audit

- #284 kapalıdır. Sonraki iş yeni deep-audit bulgusudur; #280–#284 yeni kanıt olmadan yeniden açılmamalıdır.
- Sıra yine: gerçek çağrı zinciri/minimal reproduction -> targeted RED -> minimal fix -> GREEN -> full gates -> explicit commit/push -> gerekiyorsa canonical deploy -> live acceptance -> living-doc closeout.

## CURRENT — 2026-09-30 18:56+03:00 — Notification off-screen pin normalization FINAL

Bu bölüm en üst kanonik checkpoint'tir. Önceki size/diff persistence sözleşmesi korunmuştur; bu tur yalnız bağlı olmayan eski bir monitöre ait stale pinned placement'ın disk üzerinde tekrar dirilmesi edge-case'ini kapatmıştır.

### Canonical runtime / repository state

- Repository: `C:\Users\tayla\Talvora-MCP`.
- Runtime-affecting commit: `a029d4d3f868c1f94c17ddb07bd638e3035222b7` — `fix: normalize stale notification pin`.
- Runtime commit publish edildiğinde `HEAD = origin/main = github/main = a029d4d3...`; working tree clean.
- Canonical installer: **342,628,111 bytes**; SHA-256 `2A2ACE57154E1FBEA5B8BB2C4A4808253C940E0C16276A139B51AEE506C4FBCF`; manifest source/head exact `a029d4d3...`.
- SYSTEM deploy sonrası exact-installed `talvora_system_info.sourceCommit=a029d4d3...`, LocalSystem / `S-1-5-18`, service PID 22944.
- Installed version root: `C:\Program Files\Talvora\Versions\a029d4d3f868c1f94c17ddb07bd638e3035222b7-20260930155311120`.
- Bu bölümün docs-only closeout commit'i runtime fingerprint değildir; sırf repository HEAD ilerledi diye `a029d4d3...` yeniden deploy edilmemelidir.

### #283 — stale off-screen pinned placement

- Pre-fix izole reproduksiyonda v2 placement `isPinned=true`, `left/top=2,000,000,000` olarak yazıldı. Presenter bunun bağlı hiçbir monitor work-area'sında olmadığını görüp RAM'de `_isPinned=false` yapıyor fakat dosyayı güncellemiyordu; restore+dispose sonrasında disk hâlâ `isPinned=true` kalıyordu.
- Fix: restore sırasında stale/off-screen pin otomatik moda normalize edildiğinde aynı v2 snapshot `QueuePlacementSave()` ile kalıcı yazılıyor.
- Preferred size ve anchor evidence değiştirilmez. Regression `left/top` ile `598×237.333... DIP` değerlerinin aynı kaldığını, yalnız `isPinned=false` olduğunu doğruluyor.
- Test izolasyonu için presenter opsiyonel placement path alabiliyor; production default path davranışı değişmedi.

### Final quality / live acceptance

- Targeted RED: yeni executable regression önce production constructor/sözleşme eksikliği nedeniyle RED oldu; fix sonrası `DESKTOP_PROGRESS_BEHAVIOR_GREEN`.
- Full Release solution build: **0 warning / 0 error**.
- `MODERNIZATION_POLICY_GREEN`, `CONTEXT7_QUALITY_GATE_GREEN`, analyzer `--verify-no-changes` exit 0, `git diff --check` exit 0.
- Context7 `/dotnet/docs` multi-monitor/WPF restore guidance yeniden doğrulandı; stale saved monitor coordinate current monitor working areas'a göre normalize edilirken kullanıcı boyutu korunuyor.
- Installed Tray self-test exit 0 (`ManagedMcpCount=5; FocusedTalvoraCount=2`).
- Installed Dev probe: `Ready=True / BrowserSmokePassed=True / ToolCount=203`; Admin: `Ready=True / BrowserSmokePassed=True / ToolCount=84`.
- Talvora, Gitea ve Caddy `Running / Automatic`.
- Gerçek kullanıcı placement bu turda değiştirilmedi: `isPinned=true`, `left=56`, `top=946`, `widthDip=598`, `heightDip=237.333333...`.
- Installed Tray PID 29216, session 1. Yeni gerçek notification generation UI Automation ile **897×356 px / 56,946**, `CanResize=true`, `CanMove=true`, `IsOffscreen=false` ölçüldü. Aktif %150 DPI'da 897 px = **598 DIP**; yani mevcut kullanıcı tercihi deploy/restart ve yeni mesaj sonrası birebir korunuyor.
- Bu tur sırasında görülen yakın zamanlı `Desktop progress IPC client timed out while sending a frame` log satırları executable regression'ın kasıtlı stalled-client recovery senaryosuyla üretildi; bu kanıt yeni bir production notification bug'ı olarak yeniden açılmamalıdır.

### Sonraki oturum

- İlk adım yine `git status --short`, `git log -8 --oneline`, `HEAD/origin/main/github/main` ve canlı `talvora_system_info` doğrulaması olsun.
- Exact-installed runtime baseline `a029d4d3...` olmalıdır. Takip eden docs-only commit runtime redeploy sebebi değildir.
- Mevcut v2 placement/persistence, manual-resize precedence ve diff auto-width sözleşmesini yeni canlı kanıt olmadan yeniden yazma.
- #280–#283 kapalıdır; yalnız yeni gerçek regresyon/reproduction varsa yeniden aç.

## CURRENT — NEW SESSION TRANSITION CHECKPOINT — Notification geometry persistence

Yeni oturum bu bölümden başlamalıdır. Önce bu blok tamamen okunmalı; herhangi bir kod değişikliğinden önce `git status --short`, `git log -8 --oneline`, `HEAD/origin/main/github/main` ve canlı `talvora_system_info` doğrulanmalıdır. Kullanıcıdan tekrar onay istenmeden çalışmaya devam edilmelidir.

### Canonical state

- Repository: `C:\Users\tayla\Talvora-MCP`.
- Final repository/docs HEAD: `c1c584569dd9cfef532cc81ab950b5c64e45f290` — `docs: close notification geometry persistence`.
- `HEAD = origin/main = github/main = c1c584569dd9cfef532cc81ab950b5c64e45f290`; working tree clean.
- Exact-installed runtime-affecting commit: `0e9aa118144c744c30d9f2b118ab3992fd4814ce` — `fix: persist notification diff expansion`.
- Canlı `talvora_system_info`: `sourceCommit=0e9aa118...`, LocalSystem / `S-1-5-18`, PID 31804.
- `c1c584...` yalnız docs-only closeout'tur; sırf HEAD daha yeni diye runtime tekrar deploy edilmemelidir.

### Son kullanıcı problemi ve gerçek durum

Kullanıcının son bildirdiği regresyonlar şunlardı:
1. Kullanıcının verdiği notification pencere boyutu yeni mesajda unutuluyordu.
2. Diff/kod çıktısı geldiğinde eski otomatik genişleme davranışı kaybolmuştu.
3. Pencere konumu ve genişliği yeni generation'larda korunmalıydı; her mesajda tekrar küçülmemeliydi.

Bu üç konu mevcut runtime `0e9aa118...` içinde kapatıldı ve canlıda doğrulandı:
- Placement belgesi v2 `left/top/widthDip/heightDip` saklıyor.
- Manuel resize `WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE` zinciriyle preferred size'ı kalıcılaştırıyor.
- Gerçek diff görünür olduğunda kullanıcı özel boyut seçmemişse pencere 760 DIP'e auto-expand oluyor.
- Auto diff expansion render sonrası preferred size'a capture edilip placement v2'ye yazılıyor.
- Sonraki diff içermeyen yeni worklog generation aynı preferred width/height ile açılıyor; 500 DIP'e geri küçülme yok.
- Pinned native position ayrı korunuyor; auto-size persistence pencereyi zorla pinlemiyor.

### Canlı kabul kanıtı

- Release build: `0 warning / 0 error`.
- `DESKTOP_PROGRESS_BEHAVIOR_GREEN`, `MODERNIZATION_POLICY_GREEN`, `SIZE_POSITION_DIFF_GATES_GREEN`.
- Gerçek placement v1 → v2 yükseltildi: `widthDip=760`, `heightDip=237.656666...`, `left=160`, `top=947`.
- Diff kartı user-session UI Automation ile `1140×356 px / 160,947` ölçüldü; %150 DPI'da 1140 px = 760 DIP.
- Diff içermeyen sonraki yeni generation yine `1140×356 px / 160,947` açıldı.
- Manuel resize persistence ve Tray restart sonrası aynı geometri canlıda tekrar doğrulandı.
- Installed Tray self-test GREEN; Dev probe 203 / Admin probe 84; Ready + BrowserSmokePassed; focused tunnels live/ready.

### Yeni oturumda otomatik devam kuralı

- Önce yukarıdaki canonical state'i araçlarla doğrula; kullanıcıdan onay isteme.
- Son regression için yeni canlı kanıt yoksa boyut/konum/diff persistence kodunu gereksiz yere yeniden yazma.
- Eğer kullanıcı yeni notification isteği verirse mevcut v2 placement/persistence sözleşmesini koruyarak ilerle.
- Runtime-affecting bir değişiklik yapılırsa: targeted RED→GREEN → full Release build/analyzer/regressions → explicit-file commit → origin + GitHub push → canonical installer + SHA-256 → SYSTEM deploy → exact runtime → installed Tray self-test/probes → real user-session notification acceptance → HANDOFF/BUG-AUDIT closeout.
- `git add .`, reset/clean/stash/revert kullanma; kullanıcı açıkça istemedikçe mevcut çalışmayı silme.

## PREVIOUS — 2026-09-30 17:02+03:00 — Notification size/position persistence + diff expansion FINAL



Bu bölüm kanoniktir ve Faz 17 closeout'un üzerine gelir. Kullanıcının canlıda gördüğü iki son regresyon kapatıldı: notification boyut/konumu yeni mesajlarda sıfırlanıyordu ve diff ile oluşan otomatik genişleme sonraki generation'a taşınmıyordu.

### Runtime / installer

- Runtime-affecting commit: `0e9aa118144c744c30d9f2b118ab3992fd4814ce` — `fix: persist notification diff expansion`.
- `origin/main = github/main = 0e9aa118...` runtime publish öncesi doğrulandı.
- Canonical installer: **342,624,015 bytes**; SHA-256 `59ECCC53A24F82ED411CA78D0CD1E412FFAEAD76DF172ABC4382B9ECFA4F5671`; manifest exact source `0e9aa118...`.
- SYSTEM deploy sonrası exact-installed runtime `sourceCommit=0e9aa118...`, LocalSystem / `S-1-5-18`, service PID 31804.
- Installed version root: `C:\Program Files\Talvora\Versions\0e9aa118144c744c30d9f2b118ab3992fd4814ce-20260930135307745`.
- Talvora, Gitea ve Caddy `Running / Automatic`.

### Son persistence düzeltmeleri

- Placement belgesi v2; pinned native position yanında `widthDip` ve `heightDip` saklanır. Manual WM_EXITSIZEMOVE resize `UserResizeCompleted` üzerinden preferred size'ı günceller.
- Diff görünür olduğunda kullanıcı daha önce özel boyut seçmediyse pencere 760 DIP'e auto-expand olur.
- Auto diff expansion artık transient değildir: presenter render sonrası finite `ActualWidth/ActualHeight` değerlerini `CaptureAutomaticPreferredSize` ile yakalar, v2 placement'a yazar ve request'i ancak başarılı capture sonrası acknowledge eder.
- Böylece diff kartından sonraki yeni generation önce 500 DIP'e küçülmez; kaydedilmiş preferred size ile açılır. Kullanıcının sonraki manuel resize seçimi yine son sözü söyler.
- Pinned position ayrı korunur; auto-size persistence pencereyi zorla pinlemez.

### Final live acceptance

- Release build **0 warning / 0 error**; `DESKTOP_PROGRESS_BEHAVIOR_GREEN`; `MODERNIZATION_POLICY_GREEN`; `SIZE_POSITION_DIFF_GATES_GREEN`.
- Deploy öncesi gerçek kullanıcı placement dosyası v1 idi: `left=160`, `top=947`, width/height yok.
- Installed diff frame sonrası aynı dosya otomatik v2'ye yükseldi: `widthDip=760`, `heightDip=237.656666...`, `left=160`, `top=947`.
- User-session UI Automation diff kartını **1140×356 px** ve `160,947` konumunda ölçtü; aktif %150 DPI'da 1140 px = 760 DIP.
- Ardından diff içermeyen yeni worklog generation gönderildi; yeni pencere yine **1140×356 px / 160,947** açıldı. Yeni mesajda küçülme regresyonu canlıda kapandı.
- WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE kabulü placement width'ini değiştirerek manual resize persistence zincirinin çalıştığını kanıtladı; test sonrası tercih `760×237.656666 DIP / 160,947` olarak geri yüklendi ve Tray restart sonrası aynı **1140×356 px / 160,947** geometri doğrulandı.
- Installed Tray self-test GREEN (`ManagedMcpCount=5; FocusedTalvoraCount=2`).
- Dev probe `Ready=True / BrowserSmokePassed=True / ToolCount=203`; Admin probe `Ready=True / BrowserSmokePassed=True / ToolCount=84`; tunnel runtime `0.0.15+a390c168...`, live/ready, critical degradation false.

### Sonraki oturum

Notification size/position/diff persistence konusu kapalıdır. Yeni mesajların küçülmesi, diff auto-width veya placement v2 konusunda yeni canlı kanıt olmadan bu bölüm yeniden açılmamalıdır. Bu bölümden sonraki docs-only commit runtime fingerprint değildir; sırf docs HEAD ilerledi diye `0e9aa118...` yeniden deploy edilmemelidir.

## CURRENT — 2026-09-30 15:58+03:00 — Notification Faz 17 responsive diff + expanded work visibility FINAL

Bu bölüm kanoniktir ve alttaki Faz 16 notification closeout'un yerini alır. Kullanıcının üç isteği kapatıldı: alt teknik alan gerçek diff/değişen kodu daha geniş göstermeli ve sözcük kaydırmalı; pencere resize olduğunda diff viewport'u da responsive büyümeli; notification mümkün olduğunca gerçek değişiklik ve çalışma adımlarını göstermeli.

### Runtime / installer

- Runtime-affecting commit: `eb57cd3148c2591c6fa6969894a0843ee9eddd2f` — `feat: expand notification diff visibility`.
- Runtime commit `origin/main = github/main = eb57cd3...` olarak push edildi.
- Canonical installer: **342,619,919 bytes**; SHA-256 `3E3E7E68D916DF266B44D45160205FDB309ABBB24400E53D6F4CF041E8EC76C0`; manifest source tam `eb57cd3...`.
- SYSTEM deploy sonrası exact-installed runtime `sourceCommit=eb57cd3...`, LocalSystem / `S-1-5-18`, service PID 21952.
- Installed version root: `C:\Program Files\Talvora\Versions\eb57cd3148c2591c6fa6969894a0843ee9eddd2f-20260930124837404`.
- Talvora, Gitea ve Caddy `Running / Automatic`.

### Faz 17 değişiklikleri

- Diff TextBox artık `TextWrapping=Wrap`; horizontal scrollbar Disabled, vertical scrollbar Auto. Dar pencerede uzun kod satırları reflow olur.
- İlk compact ölçümden sonra outer diff row ve inner code row `GridUnitType.Star` olur; `_codePreview.MaxHeight` kaldırılır. Kullanıcı pencereyi büyüttükçe kalan alan doğrudan diff viewport'una gider.
- Notification max genişliği 680→1200 DIP, max yüksekliği 620→900 DIP; presenter aktif monitor work-area/DPI bütçesine göre gerçek MaxWidth/MaxHeight uygular.
- Diff varsa minimum yükseklik 220 DIP; diff yoksa 150 DIP compact davranış korunur.
- `apply_patch` preview artık file/hunk/context satırlarını da taşır. `apply_edits` / structural / semantic edit için old/new block evidence üretimi eklendi.
- Diff evidence legacy 18 satır / 2400 karakterden bounded **120 satır / 7 KiB** seviyesine çıkarıldı; Protocol v3 8 KiB evidence ve 64 KiB frame hard ceiling'leri korunur.
- `read_source`, `read_text`, search, knowledge-search, find-files, git-diff/log, file-hash/path-info ve HTTP verification gibi gerçek inceleme çalışmaları artık worklog üretir. Yüksek frekanslı pasif `*_status/_info/_list/_get` ve process/service/job polling sessiz kalır.

### Final quality / installed live acceptance

- Release solution build **0 warning / 0 error**.
- `DESKTOP_PROGRESS_BEHAVIOR_GREEN`, `MODERNIZATION_POLICY_GREEN`, `CONTEXT7_QUALITY_GATE_GREEN`.
- Analyzer verify-no-changes, Control Center UI responsiveness ve privacy/security gates GREEN.
- Installed Tray self-test GREEN (`ManagedMcpCount=5; FocusedTalvoraCount=2`).
- Dev probe `Ready=True / BrowserSmokePassed=True / ToolCount=203`; Admin probe `Ready=True / BrowserSmokePassed=True / ToolCount=84`; tunnel `0.0.15+a390c...` live/ready, critical degradation false.
- Installed responsive diff QA: `CanResize=true`, `HorizontalScrollable=false`, `VerticalScrollable=true`; Window **750×545 → 1200×800 px**, diff viewport **662×285 → 1112×539 px**; long `VeryLongLine` ve `changed-line-48` UI Automation value içinde mevcut.
- Installed work-visibility QA: 5 saniyelik 50 ms timeline boyunca gerçek `talvora_read_source` çağrısı `Talvora tamam bildirimi: İlgili kaynakları inceliyorum` olarak görünür kaldı.
- Tüm geçici Faz 17 QA dosyaları temizlendi.

### Sonraki oturum

Notification Faz 17 kapalıdır. Yeni evidence/resize/work-visibility regresyonu olmadan bu üç madde yeniden açılmamalı. Takip eden docs-only commit runtime fingerprint değildir; sırf docs HEAD ilerledi diye `eb57cd3...` yeniden deploy edilmemelidir.

## CURRENT — 2026-09-30 15:24+03:00 — Notification Faz 16 compact UX FINAL live closeout

Bu bölüm kanoniktir ve alttaki Faz 15 notification closeout'un yerini alır. Kullanıcı geri bildirimiyle notification arayüzü yeniden denetlendi; ilk Faz 16 tasarımındaki tekrar eden terminal/evidence/status katmanları gerçek 2560×1440 ekran görüntüsü üzerinden ayıklandı. Nihai model üstte tek anlaşılır bilgi katmanı + altta yalnız gerçek kod/diff varsa açılan tek teknik katmandır.

### Runtime / installer / remotes

- Runtime-affecting commit: `5cc41e3e7e8dbf6a004666764481dba7e1b4e512` — `fix: modernize desktop notification experience`.
- Runtime commit publish öncesi/sonrası `HEAD = origin/main = github/main = 5cc41e3e...`; working tree clean.
- Canonical installer: **342,619,919 bytes**; SHA-256 `CFD0BCF894A8608862BF900939A4268491A5528D3714371C6247A5A6BB77C0E1`; manifest `sourceCommit=5cc41e3e...`.
- SYSTEM deploy sonrası exact-installed runtime `sourceCommit=5cc41e3e...`, `LocalSystem / S-1-5-18`, service PID 38596.
- Installed version root: `C:\Program Files\Talvora\Versions\5cc41e3e7e8dbf6a004666764481dba7e1b4e512-20260930121854500`.
- Talvora, Gitea ve Caddy servisleri `Running / Automatic`.

### Faz 16 / #271–#276 kapanışı

- Terminal outcome lane hard-bound 1024 + observable overload; progress lane capacity=1 latest-wins coalescing; terminal sonrası pending progress retire edilir.
- `ToolName` canonical tool identity taşır; kullanıcı mesajları semantic operation category üzerinden üretilir; gelecekte yapılmamış iş vaatleri ve current/history tekrarları kaldırıldı.
- Notification surface artık ayrı terminal paneli, summary/result/files evidence blokları ve alt status tekrarını göstermez.
- Final görünüm: `TALVORA // TRACE` + semantic state, title/body, tek `tool • elapsed • time` meta satırı; yalnız gerçek `CodePreview` varsa `DIFF // GERÇEK DEĞİŞİKLİK`.
- Window default 500 DIP; minimum 360×150 DIP; `WindowChrome` native edge resize; legacy dotted resize grip yok; icon buttons compact/frameless.
- Stale detection gerçek payload timestamp'ine bağlı; automatic monitor anchor generation boyunca sabit; iki semantic lane monitor work-area bütçesini paylaşır.
- Semantic design tokens, Segoe UI Variable Text + Cascadia Mono, reduced-motion/high-contrast ve UI Automation coverage tamamlandı.

### Final quality / installed live acceptance

- Full Release build **0 warning / 0 error**.
- `DESKTOP_PROGRESS_BEHAVIOR_GREEN`, `MODERNIZATION_POLICY_GREEN`, Context7 gate GREEN.
- Analyzer verify-no-changes, UI responsiveness, privacy/security ve native-installer source regressions GREEN.
- Installed Tray `--self-test`: GREEN (`ManagedMcpCount=5; FocusedTalvoraCount=2`).
- Installed Dev probe: `Ready=True / BrowserSmokePassed=True / ToolCount=203`; Admin: `Ready=True / BrowserSmokePassed=True / ToolCount=84`.
- Dev/Admin tunnel health: schema 1; runtime `0.0.15+a390c168ff1b2d14e73a95991c186c6aba3ff5a0`; Live=True; Ready=True; critical degradation false.
- Source-built real visual acceptance: simultaneous Worklog + Alert windows, two-window `CanResize=true`; two open windows with global shutdown exited in **263 ms**; restarted Tray accepted fresh v3 Alert.
- Installed-runtime visual acceptance: real Talvora `dotnet build` produced service→Tray notification; user-session UI Automation identified installed Tray PID 7600 from the new version root, `CanResize=true`, rendered 750×225 physical pixels at active DPI. Screenshot confirms compact single-information-layer UI with no duplicate terminal/evidence/status blocks.

### Sonraki oturum

Notification Faz 16 kapalıdır. Yeni kanıt veya gerçek regresyon olmadan #271–#276 yeniden açılmamalı. Bu bölümden sonra yapılacak docs-only closeout commit runtime fingerprint değildir; sırf docs HEAD ilerledi diye `5cc41e3e...` runtime yeniden deploy edilmemelidir.

## CURRENT — 2026-09-30 07:07+03:00 — Desktop notification commercial reliability FINAL live closeout

Bu bölüm kanoniktir. Masaüstü bildirim görünümüne/kozmetiğine dokunulmadan servis → Named Pipe IPC → Tray listener → presenter → WPF notification hattı ticari kalite için modernize edildi ve canlıya alındı.

### Runtime / build

- Runtime-affecting commit: `c17f33c568db434e375b54d7baa9d8ef1b277f5d` — `feat: harden desktop progress reliability`.
- Runtime deploy öncesi `HEAD = origin/main = github/main = c17f33c...`; working tree clean.
- Canonical installer: **342,619,919 bytes**; SHA-256 `66DDCC0CEFB8F16E9F931E5901C0CC243B32CF666B3B5534F34C8781EE00F2A9`; manifest source tam `c17f33c...`.
- Exact-installed runtime `sourceCommit=c17f33c...`, LocalSystem / `S-1-5-18`.
- Talvora, Gitea ve Caddy `Running / Automatic`.

### Faz 15 / #264–#270 kapanışı

- Protocol v3: 4-byte length-prefixed UTF-8 frame, explicit version, 64 KiB hard ceiling, bounded read/write/connect timeout.
- Progress ve terminal delivery ayrı bounded lanes; terminal state heartbeat baskısıyla sessizce düşürülemez; shutdown producer completion → bounded drain → force-cancel sırasını kullanır.
- Her worklog burst unique generation id ve monotonic sequence alır; manual close aynı generation heartbeat'ini geri açmaz; late/out-of-order heartbeat terminal state'i geri oynatamaz.
- Concurrent terminal precedence `Failed > Cancelled > Completed`; kalan paralel iş varsa kart Running kalır.
- Worklog ve Alert ayrı visibility lanes; bağımsız Warning/Error worklog tarafından görünmeden emekliye ayrılmaz.
- Placement JSON 16 KiB same-handle bounded; wire/evidence deterministic bounded + canonical secret redaction.
- `Talvora.Notification.Regression` executable behavior gate olarak CI kapsamındadır.

### Final quality / live acceptance

- Context7 gate GREEN; ModernizationPolicy GREEN; notification executable regression `DESKTOP_PROGRESS_BEHAVIOR_GREEN`.
- Full Release build **0 warning / 0 error**; analyzer verify-no-changes exit 0.
- Source-built ve installed Tray `--self-test` GREEN (`ManagedMcpCount=5; FocusedTalvoraCount=2`).
- Installed probes: Dev `Ready=True / BrowserSmokePassed=True / ToolCount=203`; Admin `Ready=True / BrowserSmokePassed=True / ToolCount=84`; tunnel live/ready, critical degradation false.
- Real service→Tray smoke: actual MCP worklog activeken independent v3 Alert gönderildi; interactive user session 1 içinde Tray'e ait **2 görünür WPF window** aynı anda doğrulandı.
- Window enumeration LocalSystem/session 0'dan değil gerçek user session 1 içinde yapıldı; session-isolation false-negative'i ayrıştırıldı.
- İki açık notification window varken `Global\Talvora.Tray.Shutdown` sinyali eski Tray PID 9800'i **389 ms** içinde temiz kapattı.
- Restart edilen installed Tray PID 27036 fresh v3 Alert frame kabul etti; user session 1'de yeniden görünür WPF notification windows doğrulandı.
- NOTIFY-001…036 tamamlandı. #264–#270 final live verified.

### Sonraki oturum

Bu notification reliability fazı kapalıdır. Kozmetik görünüm ayrı iştir; bu closeout sırf tasarım değişikliği için yeniden açılmamalıdır. Bu bölümden sonraki docs-only commit runtime fingerprint değildir; sırf docs HEAD ilerledi diye `c17f33c...` runtime yeniden deploy edilmemelidir.

## PREVIOUS — 2026-09-30 01:13+03:00 — Storage/tunnel/Penpot FINAL edge re-audit / live closeout

Bu bölüm kanoniktir. Kullanıcının isteğiyle Faz A→E tekrar sıfırdan yürütüldü ve önceki GREEN sonuçlar doğru varsayılmadı. Bu son tur #250–#263 arasında yeni edge-case bug/kalite kusurları buldu; hepsi düzeltildi, test edildi, iki remote'a push edildi ve canonical installer ile canlıya alındı.

### Repo / exact-installed runtime

- Runtime-affecting commit: `d3fd95c8f76b4d0374ad6a0b459b40c252374882` — `fix: close storage maintenance edge cases`.
- Runtime commit push öncesi/sonrası `HEAD = origin/main = github/main = d3fd95c...`; çalışma ağacı clean.
- Canonical installer: **342,601,999 bytes**; SHA-256 `EAB89EDA66A60D32AA544A8006F091E8A485B012CC7703E14D9A25058C668EC7`; manifest source tam `d3fd95c...`.
- Exact-installed canlı runtime `sourceCommit=d3fd95c...`, `LocalSystem / S-1-5-18`, deploy sonrası PID 32748.
- Talvora, Gitea ve Caddy servisleri `Running / Automatic`.
- Dev/Admin tunnel health: schema 1, live/ready true, lifecycle `running`, runtime `0.0.15+a390c168ff1b2d14e73a95991c186c6aba3ff5a0`.
- Installed surface-policy live smoke: GREEN.
- Installed managed probes: Dev `Ready=True / BrowserSmokePassed=True / ToolCount=203`; Admin `Ready=True / BrowserSmokePassed=True / ToolCount=84`; tunnel live/ready ve critical degradation false.
- Installed Tray `--self-test`: GREEN (`ManagedMcpCount=5; FocusedTalvoraCount=2`).
- Installed Penpot supervisor, kurulu `Talvora.dll` canonical const ile byte-for-byte eşit; SHA-256 `DCED534E519045D5F1DCD5BE59EC39F6E7E5F47EEACB2448930D393C3B07CAB4`.
- Installed Windows PowerShell 5.1 Penpot rolling/hard-bound/child-stream self-test: `PENPOT_SUPERVISOR_ROLLING_SELFTEST_GREEN`, exit 0. On-demand Penpot scheduled task idle durumda Disabled.

### Bu son tekrar taramada kapanan #250–#263

- #250 partial cancellation sonrası stale directory yaşının now'a sıçraması ve retention'ın ertelenmesi kapatıldı.
- #251 separatorless temp-prefix yanlış eşleşmesi + trusted-root reparse-chain güvenliği kapatıldı.
- #252 user-writable tunnel/protocol/registry metadata bounded same-handle sync/async JSON okumasına geçirildi.
- #253 installer immediate/reboot/version-root cleanup cancellation/reparse/count açısından bounded hale getirildi; gerçek junction fixture eski recursive traversal'ı doğruladı.
- #254 interrupted update journal recovery bounded ve missing nested config alanlarına karşı null-safe oldu.
- #255 stale scan sonrası fresh dosya eklenmesiyle aktif verinin silinebildiği freshness TOCTOU gerçek fixture ile RED→GREEN kapatıldı.
- #256 Penpot locked `.1` archive altında child logun 64-byte sınırı 96 byte'a aşması kapatıldı; rotation blocked ise bytes observable biçimde drop edilir, disk bound aşılmaz.
- #257 Penpot orphan sweep yalnız command-line marker yerine canonical `node.exe` + exact server/vite marker şartına alındı; benign `cmd.exe` yanlış eşleşme fixture'ı kapandı.
- #258 registry recovery/ownership manifest store'ları 512 KiB/file + 4096-entry + canonical `v2-*.json` sınırlarına alındı; fallback limit/corruption hataları startup recovery'yi kesmez.
- #259 blank required registry alanlarının `ArgumentException` ile fallback zincirinden kaçması `InvalidDataException` sınıflandırmasıyla kapatıldı.
- #260 persisted registry null collection/nested shape acceptance kapatıldı.
- #261 fixed tunnel archive temp path + `FileMode.Create` hardlink redirection gerçek NTFS fixture ile doğrulandı; GUID temp + `CreateNew` ile kapatıldı.
- #262 Penpot cleanup/diagnostic failure containment ve pressure log spam'i iyileştirildi; pressure raporu en fazla dakikada bir, delta birikimli.
- #263 prefix hardening sonrası eski runtime self-test fixture drift'i real current-user RED→GREEN kapatıldı.

### Final kalite / storage acceptance

- Context7 quality gate, modernization, third-party, tunnel v0.0.15/update/pending, storage source+behavior, UI responsiveness, Penpot on-demand, native installer, Gitea readiness, focused surface ve ProcessRunner regressions: GREEN.
- Full solution Release build: **0 warning / 0 error**.
- `dotnet format Talvora.slnx analyzers --verify-no-changes`: exit 0.
- Diff hygiene: 105,821-character reviewed diff; trailing whitespace/conflict marker 0.
- Installed `--storage-maintenance`: `DeletedEntries=0; ReclaimedBytes=0; RotatedTunnelLogs=0`.
- `C:\Windows\Temp` Talvora candidate=0; SYSTEM profile temp candidate=0. User tempte repo ownership'i kanıtlanmamış `talvora-gh-auth-status.txt` bilinçli olarak bırakıldı.
- TunnelClient versions: yalnız `0.0.15`; Dev log 985,236 byte / Admin log 1,040,693 byte, 32 MiB threshold altında.
- Registry recovery manifests: canonical recovery 5, ownership 5; 4096 ceiling çok altında.
- Penpot logs: 16 legacy dosya / 1,281,322 byte; bounded retention korunuyor.

### Sonraki oturum

Bu storage/tunnel/Penpot edge-audit kapalıdır. #230–#263 yeni kanıt veya gerçek regresyon olmadan yeniden açılmamalı. Bu bölümden sonra yapılacak docs-only closeout commit runtime fingerprint değildir; sırf docs HEAD ilerledi diye `d3fd95c...` runtime'ı yeniden deploy etme.

## PREVIOUS — 2026-09-29 23:26+03:00 — Storage/tunnel/Penpot deep re-audit FINAL closeout

Bu bölüm kanoniktir ve aşağıdaki önceki checkpoint'in yerini alır. Kullanıcının isteğiyle ilk devir promptu yeniden okunarak audit Faz A→E sıfırdan tekrar yürütüldü; önceki GREEN sonuçlar varsayılmadı.

### Repo / remote / exact-installed runtime

- Runtime-affecting commit: `551f3b4ae03c8b8b5e6f1fc74d664b39c52d2931` — `fix: make maintenance log handling non-disruptive`.
- Runtime deploy öncesi `HEAD = origin/main = github/main = 551f3b4...`; çalışma ağacı clean.
- Canonical installer: **342,587,663 bytes**; SHA-256 `DF65680251E727D25E634239C0548DA39E870C16E30E600DEE5EC20D16A12DF0`; manifest source tam `551f3b4...`.
- Exact-installed canlı runtime `sourceCommit=551f3b4...`, `LocalSystem`, PID 37396.
- Talvora, Gitea ve Caddy servisleri `Running / Automatic`.
- Dev/Admin tunnel health: schema 1, `live=true`, `ready=true`, lifecycle `running`, runtime `0.0.15+a390c168ff1b2d14e73a95991c186c6aba3ff5a0`.
- Installed live surface policy smoke: `TALVORA MCP SURFACE POLICY LIVE GREEN`.
- Installed managed probes: Dev `Ready=True / BrowserSmokePassed=True / ToolCount=203`; Admin `Ready=True / BrowserSmokePassed=True / ToolCount=84`; ikisinde tunnel live/ready ve critical degradation false.
- Installed Tray `--self-test`: GREEN (`ManagedMcpCount=5; FocusedTalvoraCount=2`).
- Installed canonical Penpot supervisor dosyası, kurulu `Talvora.dll` içindeki `CanonicalSupervisorScript` ile byte-for-byte eşit; SHA-256 `AA92D1A1DA6A86DAA407E83A63BBB3E681709734BA97B8984F9F5BB91655D0D9`.
- Installed Windows PowerShell 5.1 Penpot rolling self-test: `PENPOT_SUPERVISOR_ROLLING_SELFTEST_GREEN`; `Talvora Penpot MCP` görevi idle durumda Disabled.

### Fresh re-audit sonucu — #239–#249

- #239 ProgramData smoke cleanup yanlış kullanıcı ownership riski; #242 descendant ownership/TOCTOU koruması ile birlikte fail-closed düzeltildi.
- #240 nested Structural/semantic-worker stale child retention per-child GUID semantiğine taşındı; #241 nested enumeration failure observability düzeltildi.
- #243 canlı `0.0.15+build` runtime version nedeniyle erişilemeyen tunnel log rotation gate düzeltildi.
- #244 büyük stale-tree deletion shutdown cancellation'ı artık recursive delete sırasında da izliyor; eski 50k fixture 13.533 s boyunca cancellation'ı yok sayıyordu.
- #245 tunnel provisioning/bind maintenance/lifecycle lease ile serialize edildi.
- #246 SYSTEM owner probe broad `SystemException` swallow daraltıldı.
- #247 user-writable Penpot supervisor drift dosyası bounded/same-handle karşılaştırılıyor.
- #248 Penpot log rotation artık sırf 8 MiB housekeeping için canlı MCP/plugin child'larını restart etmiyor; stdout/stderr `CopyToAsync` ile `TalvoraRollingLogStream` akışlarına sürekli drain edilir, tek `.1` archive tutulur.
- #249 bozuk/oversized detailed tunnel health `InvalidDataException` artık yalnız ilgili registration bakımını erteler; tüm bakım turunu kesmez.
- Bu tekrar taramada bunların dışında doğrulanmış açık storage/tunnel/Penpot maintenance bug'ı kalmadı. PeriodicTimer overlap ve Penpot legacy-log user ownership adayları kanıtla false-positive olarak kapatıldı.

### Final kalite / storage acceptance

- Context7 gate, modernization, third-party, tunnel v0.0.15, storage source + behavior, UI responsiveness, Penpot on-demand, native installer, tunnel rollback/pending ve ProcessRunner regressions: GREEN.
- Full solution Release build: **0 warning / 0 error**.
- `dotnet format ... analyzers --verify-no-changes`: exit 0.
- Embedded canonical Penpot supervisor PowerShell parser: GREEN; Windows PowerShell 5.1 real rolling + child stdout/stderr self-test: GREEN.
- Installed `--storage-maintenance`: `DeletedEntries=0; ReclaimedBytes=0; RotatedTunnelLogs=0`.
- `C:\Windows\Temp` Talvora candidate=0; SYSTEM profile temp candidate=0; user tempte repo ownership'i kanıtlanmamış `talvora-gh-auth-status.txt` bilinçli olarak dokunulmadan bırakıldı.
- TunnelClient versions: yalnız `0.0.15`; Dev/Admin logları yaklaşık 1 MiB ve 32 MiB threshold altında.
- Penpot logs: 16 legacy dosya / ~1.22 MiB; bounded retention sözleşmesi korunuyor.

### Sonraki oturum

Bu audit kapalıdır. #230–#249'u yeni kanıt veya gerçek regresyon olmadan yeniden açma. Bu bölümden sonra oluşturulacak docs-only closeout commit runtime fingerprint değildir; sırf HANDOFF/BUG-AUDIT HEAD ilerledi diye `551f3b4...` runtime'ı yeniden deploy etme.

## PREVIOUS — 2026-09-29 19:10+03:00 — Storage maintenance closeout / next-session checkpoint

Bu dosya kesinti ve yeni oturum devamı için kanonik handoff'tur. Yeni oturumda önce bu bölüm esas alınmalıdır. Ayrıntılı tarihsel bulgular `BUG-AUDIT.md`, görev geçmişi `MCP-CONTROL-CENTER-TODO.md`, Source Edit sözleşmesi `SOURCE-EDIT-ENGINE-ARCHITECTURE.md` içindedir.

## Repo / remote / canlı durum

- Repository: `%USERPROFILE%\\Talvora-MCP`
- Branch: `main`
- Repo HEAD: `c4583b450b551863e04d73d2411c8b7a9f62c039` — docs-only storage-maintenance evidence refresh.
- Runtime-affecting HEAD: `4e2cd1cb9a7ed407b17440914088d1be87eac644` — test-artifact retention hardening.
- Önceki ana storage feature commit: `8a9caf30c9b92125d8b8d627ee67b1271e5daac3` — bounded storage maintenance.
- `HEAD = origin/main = github/main = c4583b4...`; çalışma ağacı **clean**.
- Docs-only `c4583b4` runtime fingerprint değildir; sırf HANDOFF/docs HEAD ilerledi diye yeniden deploy etme.
- Exact-installed live Talvora runtime `sourceCommit=4e2cd1cb9a7ed407b17440914088d1be87eac644` bildiriyor.
- Canonical installer: 342,570,767 bytes; SHA-256 `69A97F252BA79ED7093FF72D58713BA9535139E91B0F0E7316D5022923F60CBA`.
- Son canonical SYSTEM deploy sonucu 0; Talvora service `Running / Auto / LocalSystem`.
- Live healthz: product Talvora, version 3.0.0-dev, sourceCommit `4e2cd1c...`.
- Live full/focused surface smoke GREEN; kurulu Tray `--self-test` GREEN.
- Gitea ve Caddy servisleri `Running / Auto / LocalSystem`.

## Bu oturumda eklenen son özellikler — yeniden derin denetlenecek ana kapsam

### 1. OpenAI tunnel-client v0.0.15 diagnostics entegrasyonu

- Dev/Admin/Gitea tunnel-client v0.0.15.
- Control Center artık component-health schema v1 kullanıyor.
- İzlenen sinyaller: control-plane, response-delivery, queue, dispatcher, MCP observation, runtime lifecycle/version.
- Critical degradation repair kararı yalnız ilgili tunnel katmanını hedefliyor.
- Detailed-health loopback-only, 256 KiB streaming ceiling, timeout dashboard'u düşürmüyor.
- Maintenance için queue depth / dispatcher active / response in-progress / last activity timestamp sinyalleri kullanılıyor.

### 2. Bounded Talvora storage maintenance

- Yeni shared motor: `src/Talvora.Shared/TalvoraOwnedTempCleanup.cs`.
- Yalnız açık Talvora allowlist/prefix'leri temizlenir; genel TEMP temizliği **yok**.
- Reparse point traversal yapılmaz.
- Candidate traversal bounded; bound yalnız eşleşen Talvora adaylarından sonra sayılır.
- General Talvora temp retention: 7 gün.
- Test-only Talvora temp retention: 2 gün.
- Kullanıcı Tray bakım döngüsü: startup + 6 saatte bir.
- SYSTEM maintenance background service: startup delay + 6 saatte bir.
- SYSTEM ve kullanıcı bakım katmanları ortak cleanup motorunu kullanır.

### 3. Tunnel log ve tunnel-client version retention

- Aktif tunnel log rotation threshold: 32 MiB.
- Rotation yalnız health live/ready iken ve queue=0, dispatcher active=0, response in-progress=0 iken yapılır.
- En az 5 dakika quiet window gerekir.
- Rotation hedefli disconnect/reconnect ile yapılır; reconnect caller cancellation'dan bağımsız tamamlanmaya çalışır.
- Tek `.1` archive tutulur.
- Tunnel-client `versions` altında aktif config'in kullandığı sürüm korunur ve en az iki yeni sürüm rollback için tutulur; stale staging/obsolete sürümler retention ile temizlenir.

### 4. Penpot log-churn / supervisor hardening

- Pre-fix canlı bulgu: `C:\ProgramData\Talvora\Penpot\logs` altında **7,412 timestamp log dosyası** oluşmuştu.
- Kök neden: eski/elle kalmış `Start-Talvora-Penpot-Mcp.ps1` child crash-loop sırasında her ~5 saniyede yeni timestamp log çifti üretiyordu.
- Yeni canonical supervisor artık repo kaynak kodu tarafından SYSTEM maintenance üzerinden atomik publish edilir.
- Sabit loglar: `local-mcp.out.log`, `local-mcp.err.log`, `plugin.out.log`, `plugin.err.log`.
- Child log restart rotation threshold: 8 MiB; supervisor error log: 1 MiB.
- Crash-loop bounded exponential backoff: 3 -> 6 -> 12 -> ... -> max 60 saniye.
- Legacy timestamp Penpot log retention: 1 gün; yalnız en yeni 16 legacy log tutulur; scan bounded.
- Canlı kabul sonrası 7,412 legacy log -> **16 dosya / yaklaşık 1.22 MiB**.

### 5. Canlı storage cleanup kabul sonuçları

- İlk maintenance turunda **32 stale Talvora artefact silindi**.
- Yaklaşık **1.482 GiB** disk alanı geri kazanıldı.
- Eski `Talvora-Deploy-*` temp installer kopyaları temizlendi.
- SYSTEM-owned `C:\ProgramData\Talvora\PenpotSmoke` temizlendi.
- `C:\Windows\Temp\talvora-pipe-test.ps1` 2 günlük test retention sonrası temizlendi.
- Son kontrolde `C:\Windows\Temp` altında Talvora temp kalıntısı: **0**.
- Son kontrolde `C:\Program Files\Talvora\Versions` altında yalnız canlı `4e2cd1c...` version root'u vardı.
- Aktif tunnel log boyutları son kontrolde yaklaşık Dev 0.92 MiB / Admin 0.99 MiB / Gitea 3.67 MiB; 32 MiB threshold'un çok altında.
- Penpot kurulum ağacı, ast-grep toolchain, repo `bin/obj` ve canonical installer artefact'ı bilinçli olarak otomatik çöp sayılmadı.

## Son kalite kapıları

- `STORAGE_MAINTENANCE_SOURCE_GREEN`
- `PENPOT_ON_DEMAND_SOURCE_GREEN`
- `TUNNEL_CLIENT_V015_INTEGRATION_GREEN`
- `CONTROL_CENTER_REPAIR_SOURCE_GREEN`
- `CONTROL_CENTER_UI_RESPONSIVENESS_SOURCE_GREEN`
- Full solution Release build: **0 warning / 0 error**
- Analyzer verify: GREEN
- `git diff --check`: GREEN
- Embedded canonical Penpot supervisor PowerShell parser: **0 parse error**
- Live `TALVORA MCP SURFACE POLICY LIVE GREEN`
- Installed Tray self-test: GREEN

## ZORUNLU YENİ OTURUM GÖREVİ — DERİN BUG + TİCARİ KALİTE FAZI

Yeni oturumun ana işi, bu oturumda eklenen **en son özellikleri baştan ve daha derin** incelemektir. Sadece rapor hazırlama; bulunan tüm geçerli bug ve kalite açıkları için faz oluştur ve hepsini tek tek düzelt.

Zorunlu sıra:

1. Önce `HANDOFF.md` dosyasını tamamen oku ve bu CURRENT bölümünü kanonik kabul et.
2. `git status --short`, `git log -8 --oneline`, `HEAD/origin/main/github/main` eşitliğini doğrula. Reset/clean/stash/revert yapma.
3. Özellikle şu dosyaları derin incele:
   - `src/Talvora.Shared/TalvoraOwnedTempCleanup.cs`
   - `src/Talvora.Tray/TalvoraStorageMaintenanceService.cs`
   - `src/Talvora.Tray/TrayApplicationContext.StorageMaintenance.cs`
   - `src/Talvora.Tray/ManagedMcpTunnelHealthService.cs`
   - `src/Talvora/TalvoraSystemStorageMaintenanceService.cs`
   - `src/Talvora/TalvoraPenpotSupervisorMaintenance.cs`
   - `tests/StorageMaintenanceSourceRegression.ps1`
   - ilgili installer/deploy ve Penpot lifecycle/discovery kodları.
4. Derin inceleme başlıkları:
   - race / cancellation / shutdown yarışları
   - symlink/reparse/junction/path traversal güvenliği
   - ACL/owner/LocalSystem-user boundary
   - yanlışlıkla kullanıcı verisi silme riski
   - stale temp kaçırma / unbounded directory scan
   - disk/handle/process leak
   - log writer/rotation/reconnect yarışları
   - crash-loop/backoff ve child process cleanup
   - Penpot supervisor drift / atomic publication / PowerShell quoting
   - tunnel-client version rollback/prune doğruluğu
   - 6 saatlik timer overlap / duplicate maintenance / shutdown davranışı
   - retention sürelerinin edge-case'leri
   - Control Center event/log gürültüsü
   - test coverage açıkları
   - duplicated/dead/yarım kod
   - ticari ürün için observability ve failure reporting
   - performans: 10k/100k temp entry senaryoları, büyük tree scan, large log folder
   - installer/update sırasında bakımın yarışması
5. Önce bulguları severity ve kanıtla sınıflandır; false-positive'leri ayır.
6. Ardından ayrıntılı bir **FAZ planı** oluştur. Önerilen yapı:
   - Faz A — correctness / data-safety
   - Faz B — concurrency / cancellation / lifecycle
   - Faz C — boundedness / performance / storage growth
   - Faz D — Penpot supervisor & process/log lifecycle
   - Faz E — regression / CI / live acceptance
7. Kullanıcıdan her küçük değişiklik için onay isteme. Fazları sırayla uygula.
8. Source değişikliklerinde `talvora_apply_patch` PRIMARY/default. PowerShell source editor olarak kullanılmaz.
9. Harici kütüphane/runtime davranışı gerekiyorsa Context7 + resmi kaynakları doğrula.
10. Her düzeltmeden sonra targeted regression; faz sonunda full relevant gates; runtime-affecting değişiklik varsa commit/push -> canonical installer -> SYSTEM deploy -> installed live acceptance.
11. Sonuçta çalışma ağacı clean, iki remote eşit ve live runtime exact commit'e bağlı olmalı.

## Yeni oturumda özellikle unutma

- Bu çalışma bir “genel Talvora audit” değil; önce **bu oturumda eklenen son storage/tunnel/Penpot maintenance özelliklerine** yoğunlaş.
- Bir bug bulursan sadece belgeleyip bırakma; gerekli test/regression'ı önce RED hale getir, minimal ama ticari seviyede fix uygula, GREEN doğrula.
- Gerçek kullanıcı verisini silme riski olan cleanup değişikliklerinde fail-closed davran.
- Active tunnel/Penpot işini sırf log cleanup için kesme; idle/health/lifecycle kanıtı kullan.
- `c4583b4` docs-only; live runtime baseline `4e2cd1c`. Yeni runtime commit oluşursa installer/deploy ancak o commit üzerinden yapılmalı.

## Mevcut ürün/mimari baseline

Aşağıdaki ana çalışma alanları tamamlanmış ve korunmalıdır:

- Canonical Windows installer + bağımsız SYSTEM deploy akışı; servis/tray aynı version root ve manifest/provenance doğrulaması.
- Control Center / Tray lifecycle, restart/recovery ve managed MCP operation koordinasyonu.
- Gitea backend + Caddy + MCP + Secure MCP Tunnel health/readiness zinciri.
- Playwright MCP kaldırıldı; resmi Playwright CLI yönü kullanılıyor.
- Source Edit Transaction Engine production baseline:
  - `talvora_read_source` revision handshake
  - `talvora_apply_patch` normal source/config/repository-doc değişikliklerinde PRIMARY/default
  - `talvora_apply_edits` yalnız exact generated range specialist
  - `talvora_structural_edit` ast-grep structural specialist
  - `talvora_semantic_edit` Roslyn C# symbol-aware specialist
  - SHA-256 optimistic concurrency, durable WAL/receipt, rollback/recovery, idempotency/tombstone, source mutation policy
- Response/resource bounds, pagination/continuation, process output bounding, watcher/HTTP mock backpressure ve archive/read/list sınırları.
- Focused MCP yüzeyleri canlı discovery ile **Full 240 / Dev 203 / Admin 84 / Shared 47** doğrulandı. Dev/Admin overlap artık explicit `SharedTools` allowlist'tir; `IsFocusedSurfaceReviewComplete` gerçek Dev∩Admin kesişiminin Shared ile exact eşitliğini doğrular. Package mutation + persistent env mutation + arbitrary PID kill Admin-only; long-running job ailesi Dev-only oldu. Full `/mcp` 240-tool capability aynen korunur; focused ayrım privilege sandbox değil discovery/tool-selection boundary'sidir.
- Installed live surface smoke GREEN: Dev'de excluded service/package/process-kill ve Admin'de excluded job direct invocation `Unknown tool` sınırından geçemiyor. Managed Business probe canlı **Dev Ready=True / BrowserSmokePassed=True / ToolCount=203**, **Admin Ready=True / BrowserSmokePassed=True / ToolCount=84**.
- 2026-09-27 repository-wide audit: çözüm build/analyzer temiz; vulnerable/deprecated NuGet bulgusu yok; Source Edit focused alt-regresyonları, metadata/surface/privacy/shared-infrastructure smoke ve live metadata/surface policy GREEN. Installer Tray process polling handle sahipliği düzeltildi; health handle regression kalıcı istemci modeline düzeltildi ve canlı 200 istekte **delta=0**; dört kopya `FirstNonEmptyLine` ortak `TextLines.FirstNonEmpty` helper'ına indirildi; stale append encoding regression düzeltildi.
- Final 212-tool full smoke **GREEN**. Kapanış sırasında `target=user` environment persistence aktif kullanıcının gerçek `HKU\<SID>\Environment` hive'ına deterministik hale getirildi; smoke user-process probe argüman yarışı giderildi; nullable continuation alanının JSON'da omitted olabilmesi teste işlendi; Yarn/Corepack info probe'u 10 saniyelik bounded/non-download probe'a dönüştürüldü.
- Masaüstü çalışma günlüğü zorunlu runtime sözleşmesidir: her MCP çağrısı sarmaldan geçer, düşük değerli read/search/health çağrıları sessizdir. Anlamlı iş tek kartta **Şimdi bunu yapıyorum** -> 30 saniyeyi aşarsa **Hâlâ bununla uğraşıyorum** -> **Bitti / Bir hata buldum** akışını kullanır. Aktif kart iş bitmeden kaybolmaz; başarı yaklaşık 3 dakika, hata yaklaşık 5 dakika okunabilir kalır. Hata sonucu exception olmak zorunda değildir: MCP `IsError`, structured `success=false`, nonzero sonuç ve timeout da sade kullanıcı diliyle failure kartına dönüşür. Teknik tool/komut/stack trace/exit-code metni kullanıcı kartına sızdırılmaz.
- Talvora self-update servis-içinden çağrıldığında 2.5 sn gecikmeli detached installer bootstrap kullanır; MCP isteği önce kapanır, SCM STOP normal tamamlanır ve forced process-kill fallback'e girilmez. Canlı acceptance sonrası yeni SCM 7034 veya Application/.NET Runtime hatası oluşmadı.
- Smoke test failure sınırı artık top-level exception'ı kontrollü nonzero exit + stderr'e çevirir; kasıtlı failure acceptance'ta yeni Windows Application Error/.NET Runtime crash kaydı oluşmadı. Önceki 212-tool full-smoke baseline GREEN
- Detached self-update masaüstü kartı `TALVORA_UPDATE_DETACHED` gerçek marker'ını okuyup `Kurucuya devrettim` mesajı gösterir; yeni runtime doğrulanmadan `etkinleştirdim` iddiasında bulunmaz.
- Focused Dev/Admin tunnel registry + config + DPAPI runtime credential recovery canlı doğrulandı; tunnel-client Dev/Admin için `process_running/healthy/ready=true`; güncel canlı MCP discovery Dev=203 / Admin=84 ve iki managed probe için `BrowserSmokePassed=true`.

## Penpot / Talvora entegrasyonu — CURRENT

- Resmi Penpot **2.18.0** self-host kurulumu Docker Compose ile `C:\ProgramData\Talvora\Penpot` altında çalışıyor. UI için kanonik adres `http://localhost:9001/`; host bind `127.0.0.1:9001` ile loopback'tır. Docker içindeki resmi MCP container'ı `--multi-user` modunda Penpot'un kendi ağı için kalır ve artık host `4401/4402` portlarını publish etmez.
- Resmi compose içindeki Penpot secret yerelde rastgele üretildi; secret değeri repo, HANDOFF veya kalıcı log içine alınmadı. `.env` `PENPOT_VERSION=latest` kullanıyor. Docker Desktop kullanıcı ayarında `AutoStart=True`; değişiklik öncesi `settings-store.json.talvora-penpot.bak` yedeği bırakıldı ve Penpot container restart policy `always`.
- Talvora'nın production design endpoint'i Docker multi-user MCP değildir. Exact Penpot **2.18.0** tag'ı `C:\ProgramData\Talvora\Penpot\penpot-2.18.0` altına sparse checkout edildi; upstream commit `5baffdc213f0deaaeb318e97a41d611ac0656a94`. `mcp` workspace'i `pnpm install --frozen-lockfile` + `pnpm run build` ile yerelde build edildi.
- Local/single-user Penpot MCP kullanıcı oturumunda `Talvora Penpot MCP` scheduled task'iyle gizli ve kalıcı çalışır: plugin manifest `http://127.0.0.1:4400/manifest.json`, MCP `http://127.0.0.1:4401/mcp`, WebSocket bridge `http://127.0.0.1:4402`. Supervisor `C:\ProgramData\Talvora\Penpot\Start-Talvora-Penpot-Mcp.ps1`; tüm host listener'lar yalnız `127.0.0.1`.
- NPM `@penpot/mcp` stable/latest canlı kontrolde 2.15.4 kaldığı için npm latest paketi production yolu yapılmadı; local MCP exact 2.18.0 release source'undan build edildi.
- Canlı local Penpot MCP 5 native araç yayımlıyor: `execute_code`, `high_level_overview`, `penpot_api_info`, `export_shape`, `import_image`. `import_image` acceptance gate'e özellikle eklendi; böylece Talvora yanlışlıkla yeniden Docker multi-user endpoint'ine bağlanırsa regression kırılır.
- Talvora Dev yüzeyine 4 wrapper eklendi: `talvora_penpot_status`, `talvora_penpot_overview`, `talvora_penpot_read_tool`, `talvora_penpot_call_tool`. Native Penpot text/structured/image result blokları korunur; bilinmeyen veya mutating araçlar full-call yolunda kalır.
- `e45ef95` sonrasında wrapper resolver, kullanılabilir olduğunda Penpot'un managed authenticated endpoint'ini tercih eder. Bu endpoint canlı durumda 4 araç yayımlıyor: `execute_code`, `high_level_overview`, `penpot_api_info`, `export_shape`. Direct local `127.0.0.1:4401/mcp` fallback'i 5 araçlıdır ve ayrıca `import_image` içerir. Dedicated smoke bu iki sözleşmeyi artık bilinçli biçimde ayrı doğrular; authenticated endpoint için yanlış 5-tool parity varsayımı kaldırıldı.
- Penpot 2.18 upstream tool metadata'sı read-only annotation yayımlamadığı için güvenli read yolu yalnız bilinen non-mutating `high_level_overview`, `penpot_api_info`, `export_shape` isimlerini fallback olarak kabul eder; `execute_code` generic mutating çağrı yolundadır.
- Control Center canonical managed-MCP recovery discovery'deki `penpot` kaydı artık `autoStart=false`; Penpot kullanılmadığında otomatik recovery/notification gürültüsü üretmiyor, Control Center üzerinden on-demand başlatılabiliyor.
- Lifecycle acceptance GREEN: Control Center stop semantiği taklit edildiğinde `4400/4401/4402` üçü de kapandı; start sonrası üçü de geri geldi ve plugin manifest HTTP 200 döndürdü. `TALVORA PENPOT INTEGRATION GREEN`, metadata live ve surface live targeted gate'leri deploy sonrasında tekrar GREEN.
- Penpot'un kendi `high_level_overview` sözleşmesine göre gerçek design mutation için açık bir Penpot dosyasının Penpot MCP Plugin ile MCP sunucusuna bağlanması gerekir; bu bağlantı dosya/proje kullanım bağlamında yapılır ve Talvora entegrasyonundan ayrı bir kullanıcı-proje oturumudur.
- Custom **Talvora AI** Penpot plugin'i artık Talvora Windows Service tarafından doğrudan servis edilir; ekstra Node servisi/port/startup görevi yoktur. Manifest `http://127.0.0.1:7676/penpot-ai/manifest.json`, health `/penpot-ai/healthz`; `plugin.js`, `index.html` ve `icon.svg` aynı loopback origin'den sunulur.
- Talvora AI manifest v2 permissions: `content:write`, `library:write`, `allow:downloads`, `allow:localstorage`. Plugin iframe/message modelini Penpot'un resmi API'siyle kullanır; canlı selection/page/theme bilgisini izler.
- Talvora AI v0.1 yetenekleri: selection inspection, premium radius/stroke/shadow polish, 390x844 mobile board clone, local library component oluşturma, solid fill -> color token extraction/binding, Penpot native `generateMarkup/generateStyle/generateFontFaces` ile HTML/CSS developer handoff ve prototype viewer açma.
- Serbest metin kutusu v0.1'de haricî LLM çağırmaz; yerel/deterministik intent router ile premium/mobile/component/token/handoff/prototype/inspect niyetlerini bir veya birden çok komuta dönüştürür. Gerçek generative model backend'i ayrı bir sonraki fazdır.
- Canlı Talvora AI acceptance GREEN: plugin demo profile'a manifest üzerinden kuruldu ve permission grant edildi; panel `Talvora hazir` gösterdi. Gerçek board üzerinde mobile clone, premium polish, 15 yeni color token + 27 token binding, component creation, doğal dil `incele + HTML CSS handoff` zinciri ve prototype viewer doğrulandı. Son handoff örneği component instance board için 13,042 karakter HTML + 13,849 karakter CSS üretti.
- Plugin ilk runtime commit'i `a0abcc3`; component dönüşümü sonrası selection continuity ve authenticated/direct smoke contract düzeltmesi `345947a`. İki commit Gitea + GitHub'a push edildi. `345947a` canonical installer SHA-256 `93CBB68249D9EC26298AE01FFB51345D41C28BFCFB97B9D66516AEF36F4B33E2` ile SYSTEM deploy edildi; live `sourceCommit=345947a...`. `TALVORA PENPOT INTEGRATION GREEN`.

## Modal / Qwen entegrasyonu — CURRENT

- Resmi Modal Python SDK/CLI **1.5.5** sistem Python 3.14 altına kuruldu; executable `C:\Python314\Scripts\modal.exe`.
- Modal için ayrı üçüncü taraf MCP yerine Talvora Dev yüzeyinde 4 native yönetim aracı vardır: `talvora_modal_info`, `talvora_modal_app_list`, `talvora_modal_endpoint_list`, `talvora_modal_run`. `app_list`, yeni Endpoint ürünü öncesi klasik Modal App deployment'larını da keşfeder.
- Modal CLI çağrıları credential/profile sahipliği için logged-on Windows user session'ında çalışır. `talvora_modal_info` aktif profili ve credential kullanılabilirliğini ayrı raporlar; credential değeri response'a alınmaz. Modal API/proxy/OAuth credential prefix'leri persistent log redaction kapsamındadır. Generic run, resmi CLI yüzeyini korur.
- Modal hardening aşamasındaki yüzey **Full 217 / Dev 187 / Admin 91** idi; Penpot entegrasyonu sonrasında yüzey bir aşamada **Full 221 / Dev 191 / Admin 91** oldu. Surface-policy live GREEN.
- Modal user profile setup tamamlandı; aktif profile `taylansoylu`. Native `modal endpoint list` boş çünkü bu model yeni Endpoint ürünü değil, custom Modal App olarak deploy edilmiş.
- Custom app `codepilot-huihui-qwen38` deployed; public OpenAI-compatible base `https://taylansoylu--codepilot-huihui-qwen38-serve.modal.run/v1`.
- Auth secret adı `codepilot-inference-api`; required env key adı `LLAMA_API_KEY`. Secret değeri okunmadı, loglanmadı veya HANDOFF'a yazılmadı.
- Cold start davranışı doğrulandı: ilk istek `503 Loading model`; loglarda GGUF yaklaşık 12 saniyede yüklenip `llama_server: model loaded` ve `0.0.0.0:8000` listen durumuna geçiyor. Auth header olmadan sıcak endpoint `401 Invalid API Key` döndürüyor.
- Model kimliği `codepilot-huihui-qwen38-27b`; context `n_ctx=65536`, train context 262144, 27.32B parametre.
- **2026-09-26 model refresh:** eski Huihui `UD-Q4_K_XL` içeriği, güncel stok-`llama.cpp` uyumlu `Huihui-Qwen3.8-27B-abliterated-UD-DW-Q4_K_M.gguf` ile değiştirildi. Yeni dosya 16,551,316,384 bayt; SHA-256 `0c7cfe3060493485bb9a6a51195897b0c1d347a49929206adb9a52170183ea03` olarak Volume içinde tekrar hash edilip doğrulandı.
- Mevcut deployed app kaynak kodunu ve public endpoint'i değiştirmemek için Volume'daki runtime dosya adı geriye uyumluluk amacıyla `Huihui-Qwen3.8-27B-abliterated-UD-Q4_K_XL.gguf` olarak bırakıldı; dosya içeriği ve hash'i artık yukarıdaki UD-DW Q4_K_M artefact'ına aittir.
- `codepilot-huihui-qwen38` recreate rollover sonrası yeni container 17:09 civarında modeli yeniden yükledi. Yetkili post-update `/v1/models` probe 200, `/v1/chat/completions` probe 200, content `UPDATED_OK`; `reasoning_content` alanı da mevcut.
- Tekrarlanabilir bakım yardımcıları `C:\\Users\\tayla\\Documents\\Modal-CodePilot\\refresh_model.py` ve `probe_endpoint.py` altında tutuluyor; refresh indirme boyutunu + SHA-256'yı doğrulamadan Volume publish etmez ve inference secret'ı response/log içine çıkarmaz.
- Dyad hedef yolu **Dyad -> Modal /v1**; Talvora yönetim/diagnostic yapıyor. `custom::modal-qwen` provider'ı base URL `https://taylansoylu--codepilot-huihui-qwen38-serve.modal.run/v1`, model `codepilot-huihui-qwen38-27b`, context 65,536 olarak kurulu ve seçili.
- 2026-09-26 API credential onarımı: eski inference key nedeniyle Dyad loglarında `401 Invalid API Key` görülüyordu. Yeni rastgele inference key oluşturuldu, Modal `codepilot-inference-api` Secret içindeki `LLAMA_API_KEY` değiştirildi ve app recreate rollover edildi. Yeni key ile `/v1/models` 200 ve chat probe 200 doğrulandı.
- Dyad'ın `providerSettings["custom::modal-qwen"].apiKey` alanı Electron Safe Storage (`v10`/OSCrypt) formatında yeni key ile yeniden şifrelendi; logged-on user context'inde decrypt round-trip doğrulaması GREEN. Dyad 1.17.0-beta1 yeniden başlatıldı. Plaintext key yalnız kullanıcının istediği `C:\\Users\\tayla\\Desktop\\MODAL-QWEN-API-KEY.txt` dosyasında tutuluyor; repo/HANDOFF/log içine yazılmadı.

## Son tamamlanan bug-fix zinciri

### #178 — Windows session scoped manual-stop state
- Tek ortak `session-state.json` farklı logon/RDP session'larında çakışabiliyordu.
- State `session-state.<SessionId>.json` olarak session bazında ayrıldı; güvenli legacy migration eklendi.
- Targeted regression GREEN; fix iki remote'a push edildi ve live verified.

### #179 — Control Center exit / window-placement kaybı
- Exit sırasında placement save fire-and-forget kalıp WPF shutdown ile yarışıyordu.
- Exit path son placement yazımını shutdown öncesi deterministik tamamlıyor; UI-context deadlock riski engellendi.
- Targeted regression RED -> GREEN; live verified.

### #180 — Gitea browser launch hata yolu
- Beklenen shell launch exception'ları async WPF event zincirinden kaçabiliyordu.
- Yalnız beklenen launch exception'ları kontrollü log/event/UI hata yoluna alındı.
- Targeted regression RED -> GREEN; live verified.

### #181 — Window-placement stale save yarışı
- Eşzamanlı async save'lerde eski snapshot daha sonra publish olup yeni konumu ezebiliyordu.
- Publication semaphore ile serialize edildi; monoton save-version stale queued snapshot'ı skip ediyor.
- Targeted regression GREEN; live verified.

### #182 — Browser launch Process handle sahipliği
- Control Center ve Tray `Process.Start` dönüşlerini dispose etmiyordu.
- Her iki yol disposable Process nesnesini scope sonunda serbest bırakıyor.
- Targeted regression GREEN; live verified.

### #183 — Source-edit worker kill/cleanup yarışı
- ast-grep ve semantic worker timeout/error yolları `Kill` sonrasında process exit'i beklemeden staging/response cleanup'a geçiyordu.
- Bounded `WaitForExitAsync` eklendi; cleanup process termination sonrasına taşındı.
- Targeted regression GREEN; live verified.

### #184 — HTTP mock CTS dispose yarışı
- In-flight handler'lar dispose edilmiş CTS üzerinden tekrar `Token` okuyabiliyordu.
- Runtime lifetime token constructor'da cache'leniyor; async yollar immutable token kopyasını kullanıyor.
- Runtime-bounds regression GREEN; live verified.

### #185 — HTTP mock reply retry claim
- Cancelled/failed response send `pending.Replied=1` claim'ini kalıcı bırakıp retry'ı engelliyordu.
- Başarısız/cancelled attempt claim'i atomik geri bırakıyor; başarılı send claim'i koruyor.
- Gerçek loopback regression RED -> GREEN; fix `43d2228`; live verified.

### #186 — HTTP mock stop pending request 503 semantiği
- Listener lifetime cancellation ile gerçek pending timeout aynı cancellation catch'ine giriyor; Stop yarışını handler kazanırsa timeout default response dönebiliyordu.
- Lifetime cancellation ayrı 503 yoluna ayrıldı; gerçek timeout mevcut default-response davranışını koruyor.
- 64 concurrent loopback regression RED -> GREEN.
- Fix: `60c5e9a`
- Canonical installer SHA-256: `F43AB687532B9E5211CD456CB58B484712AE526035F1FB63D8B00A074AB88B35`
- Gitea + GitHub push GREEN; exact-installed live runtime aynı commit.

### #187 — HTTP mock request/response encoding ayrımı
- `requestEncoding` incoming request body decode için doğruydu ancak text `defaultBody` response byte'larını da aynı encoding ile üretiyordu; varsayılan response content type UTF-8 kaldığı için örn. UTF-16 request encoding seçimi auto-reply gövdesini bozuyordu.
- Text default response artık `Reply()` ile tutarlı biçimde UTF-8 üretiliyor; request decode davranışı korunuyor, binary/custom byte yolu `defaultBodyBase64` üzerinden değişmeden kalıyor.
- Gerçek loopback regression önce RED, minimal fix sonrası `PASS http-mock-request-encoding-does-not-change-response`; tüm `--runtime-bounds-only` gate GREEN.
- Fix commit: `26c26e53b3ca58c3521148d67d3f6233d6cd63fd`; Gitea + GitHub `main` aynı commit'te.
- Canonical installer SHA-256: `A43F89339155318FE1E40842E044EA7A7301F2B6890895CFFD75C942E0D2A7A2`.
- Manifest-bound SYSTEM deploy sonrası exact-installed runtime aynı commit'i bildiriyor; #187 live verified.

### #188 — recovered background job retention zamanı
- Servis yeniden başladıktan sonra diskte hâlâ `Running` görünen fakat gerçekte bitmiş job `ExitedUnknown` olarak yenilenirken cleanup, retention zamanını refresh'ten önce eski `StartedAtUtc` üzerinden cache'liyordu.
- Uzun süre çalışmış bir job böylece yeni bitmiş olmasına rağmen anında expired sayılıp log/metadata klasörü silinebiliyordu.
- Retention/sıralama zamanı artık `RefreshStateAsync` sonrasında güncellenmiş `ExitedAtUtc` üzerinden hesaplanıyor.
- Targeted job-storage regression önce RED, minimal fix sonrası `TALVORA JOB STORAGE REGRESSION GREEN`; Release build 0 warning / 0 error.
- Fix commit: `08302059d0236349064ab2cfa81e23ce74ff1b97`; Gitea + GitHub `main` aynı commit'te.
- Canonical clean-worktree installer SHA-256: `ABB9D1D074D2AB22CB36FB279DC647916CE9183D2E72CE437A1837C015AA1AEF`.
- Manifest-bound SYSTEM deploy sonrası exact-installed runtime aynı commit'i bildiriyor; #188 live verified.

### #189 — SQLite zero-timeout lock wait — live verified
- Fix `d8f0469` olarak commit edildi, Gitea + GitHub `main` üzerine push edildi ve exact-installed runtime aynı commit'i bildiriyor.
- Microsoft.Data.Sqlite 10.0.12'de timeout 0 no-timeout anlamına geliyor. İzole locked-DB fixture timeout 1'de yaklaşık 1.1 s sonra SQLITE_BUSY döndürdü; timeout 0 + 1.5 s cancellation token 6 s dış process bütçesine kadar dönmedi.
- `ValidateTimeout` için `timeoutSeconds <= 0`; dört SQLite yüzeyini bağlantı açmadan koruyor.
- `PASS sqlite-zero-timeout-rejected`; targeted `--runtime-bounds-only` GREEN.
- #189 kapanmıştır.

### #190 — Privacy & Security Hardening — LIVE VERIFIED
- Talvora'nın tool/capability yüzeyi kısıtlanmadı.
- Persistent `FileLog` ve Control Center raw-log görünümü ortak secret redaction kullanıyor.
- Interactive-user process handoff `request.json` dosyasını deserialize sonrası hemen siliyor; 24 saatlik stale-run cleanup active lease'i koruyor.
- Tunnel-client hata/UI diagnostikleri secret-safe redaction kullanıyor.
- `.gitignore` local secret/credential dosyalarını dışlıyor; `SECURITY.md` trust-boundary ve privacy politikasını belgeliyor.
- Public dokümanlardaki gereksiz kullanıcı/path metadata'sı genelleniyor.
- Dedicated `--privacy-security-only` smoke ve privacy/security source regression GREEN; Tray Release build 0 warning / 0 error.
- 266 dosyalık high-confidence tracked-tree secret scan 0 bulgu.
- Security commit `689a7ba` iki remote'a push edildi.
- Clean `1d388ab` HEAD canonical installer SHA-256 `6CFA67594FDC8709F2773F214E7077882486B9D03FA4F8FDCF89A19E8A55F9D5`; SYSTEM deploy GREEN ve exact-installed runtime aynı commit'i bildiriyor.
- Installed `Talvora.Shared.dll` live redaction probe GREEN; MCP surface 204/204 unique tool.
- #190 kapanmıştır.

### #191 — MCP Host Metadata Accuracy — LIVE VERIFIED
- Exact-installed eski baseline: 204 tool, 170 `openWorldHint=true`.
- Kanonik host-facing metadata policy 204/204 tool'u review ediyor; final live dağılım 56 open-world + 148 closed-world.
- Tüm 204 tool için ReadOnly/Destructive/Idempotent/OpenWorld annotation alanları artık wire seviyesinde explicit; omitted değer 0.
- Read-only tool'lar explicit non-destructive + idempotent; gerçek external/open-ended tool'lar open-world kalıyor.
- Host-facing description normalizer ve yüksek-sinyal override'ları capability'yi değiştirmeden implementasyon-risk dilini sadeleştiriyor.
- Live `tools/list` taramasında `arbitrary/unrestricted/allowlist/denylist/escape-hatch` legacy terimleri 0 eşleşme.
- Initialize `ServerInstructions` artık `unrestricted` / `escape-hatch` içermiyor; `talvora_apply_patch` primary routing ve PowerShell/process administration capability korunuyor.
- Targeted gates: Talvora Release 0 warning / 0 error; Smoke Release 0 warning / 0 error; `TALVORA MCP METADATA POLICY SOURCE GREEN`; `TALVORA MCP METADATA POLICY LIVE GREEN`.
- Runtime commits: `8b16760` + final wording fix `6a31805`; iki remote'a push edildi.
- Final canonical installer SHA-256 `C42FFEF7D0720D057CFC4ABED2DACEED2318C0CB841FDA8B730DE70DA1027A4E`; exact-installed runtime `6a31805...`.
- Tool surface 204/204; PowerShell/process/Git/Docker/HTTP/TCP/ADB/registry/service dahil capability kaybı yok.
- #191 kapanmıştır.

### #192 — Focused MCP Surfaces + Tool Selection Quality — LIVE VERIFIED
- Legacy/full `/mcp` exact 204 tool olarak korunuyor.
- Yeni local focused endpoints: `/mcp/dev` = 174 tool; `/mcp/admin` = 91 tool.
- Focused surface filtering per-request ToolCollection seviyesinde; listede olmayan tool direct invocation ile bypass edilemiyor.
- Full ↔ focused ortak tool'larda title, input schema, output schema, description ve annotations birebir korunuyor; yalnız visibility değişiyor.
- Deterministik selection eval ordinary source edit / exact range / structural / semantic / build-test / Git / service-registry / negative non-Talvora intent fixture'larını doğruluyor.
- Description quality ikinci turu grammar/implementation noise azaltıyor; full capability ve tool schemas değişmedi.
- DestructiveHint deep audit sonrası reversible lifecycle/control örnekleri non-destructive; caller-controlled/data-loss sınıfları conservative destructive kalıyor.
- Control Center live registry `talvora-dev` + `talvora-admin` focused registrations içeriyor; ikisi tunnel-only, ana Talvora Windows service ownership full `talvora` kaydında kalıyor.
- Targeted gates GREEN: metadata live, surface source/live, focused surface source regression; Tray/Smoke Release 0 warning / 0 error.
- Runtime commits `24eee60` + `508e513`; final installer SHA-256 `9205EB952BBC243AC655391C792FFAF36BF3E1353075B08EE5043F3523E4602B`; exact-installed `508e513...`.
- Ayrı remote ChatGPT Dev/Admin tunnel publication yalnız dış prerequisite olarak bekliyor: OpenAI Admin credential bu makinede mevcut değil. Credential uydurulmayacak; local focused endpoints ve full existing tunnel bundan etkilenmiyor.
- #192 kapanmıştır; external publication prerequisite bug değildir.

### #193 — LocalSystem Git ownership / canonical build — LIVE VERIFIED
- Kullanıcıya ait repolar LocalSystem altında Git 2.55 `dubious ownership` korumasına takılıyor; structured Git family ve canonical installer build etkileniyordu.
- `GitTools` gerçek repo kökünü filesystem ile bulup yalnız process-local `-c safe.directory=<root>` ekliyor; `safe.directory=*` veya kalıcı global gevşetme yok.
- `Build-Windows-Installer.ps1` tüm repo Git çağrılarını aynı scoped `Invoke-RepositoryGit` helper'ından geçiriyor.
- Targeted gates: `GIT_SAFE_DIRECTORY_SOURCE_GREEN`, `NATIVE_INSTALLER_SOURCE_GREEN`, Talvora Release 0 warning / 0 error.
- Runtime commits `ba655ec` + `ab313ec`; iki remote'a push edildi.
- Canonical installer SHA-256 `1F9BDD3557C514511E479B84827C091F9B047E6D344420659B1A671C284E5639`; exact-installed runtime `ab313ec...`.
- Live structured Git family ana kullanıcı reposunda GREEN; #193 kapanmıştır.

### #194 — background job terminal metadata publication race — LIVE VERIFIED
- Interaktif `cmd.exe` job'ında stdin/stdout sonrası `talvora_job_stop`, terminal metadata publication sırasında `AtomicFile.MoveDurable` Win32 error 5 ile hata döndürebiliyordu.
- Job metadata read/write erişimi `MetadataAccessGate` altında serialize edildi; observer ile Stop stale terminal metadata'yı birbirinin üstüne yazmıyor.
- Dedicated 32/32 stop-race regression + job-storage regression + Release build GREEN.
- Fix `2f3620d`; canonical installer SHA-256 `A2D03A8E55CE0BE56CD500B5621F9FBFC15AB27A9DBC7AAB57802AFF96AFAD05`; live verified.

### #195 — .NET smoke / Source Edit policy drift — TEST VERIFIED
- Full smoke önce `.csproj` yazarak fixture'ı development workspace'e dönüştürüyor, sonra `talvora_write_text` ile `Program.cs` yazıp doğru Source Edit policy'ye takılıyordu.
- Runtime policy değiştirilmedi; fixture `Program.cs` marker'dan önce, `.csproj` sonra yazılacak şekilde düzeltildi.
- Full smoke bu aşamayı geçmiştir.

### #196 — read_text_range newline contract — LIVE VERIFIED
- `talvora_read_text_range` exact slice semantiği EOF olmayan seçili son logical line'ın line terminator'ını korur.
- Smoke artık `line-two\\r\\nline-three\\r\\n` + `endReached=false` bekliyor; host-facing description bu kontratı açıkça söylüyor.
- Fix `2ea6a3b`; canonical installer SHA-256 `AEC593991FC028CE011EF090623D984A15C5689BEA490624ACD6F454A4F3DFEB`; exact-installed live runtime aynı commit.

### #197 — StructuredConfig smoke workspace-marker drift — TEST VERIFIED
- StructuredConfig smoke, `compose.yaml` ve `pyproject.toml` güçlü workspace marker adlarıyla compatibility YAML/TOML mutation testini kendi kendine development workspace'e dönüştürüyordu.
- Fixture adları `config.yaml` / `config.toml` yapıldı; runtime source-mutation policy değiştirilmedi.
- Full 204-tool live MCP smoke GREEN.

### #198 — SSH Git credential context — LIVE VERIFIED
- LocalSystem Git, user-scoped Gitea SSH key/host trust'ını kullanamadığı için `origin` fetch/push başarısız oluyordu.
- SSH fetch/push ve GitHub HTTPS fetch/push artık logged-on user session'ında çalışıyor; local/status/history Git işlemleri SYSTEM altında kalıyor.
- `id_ed25519_gitea` ACL'i yalnız kullanıcı FullControl; repo-local `core.sshCommand` özel key ve `known_hosts_gitea` dosyasını seçiyor.
- Targeted regression + Release build + metadata source/live GREEN; canlı Gitea/GitHub fetch/push dry-run'ları exit 0.
- Fix `8eb6c60`; installer SHA-256 `06690E5CA2FEC527DC4A3FE593A0E9B8F7DA4D0B1326716E7F0C6B5905F1B8B2`; exact-installed runtime aynı commit.

### #199 — Admin Event Log response bounds — LIVE VERIFIED
- `talvora_eventlog_query` pre-fix `maxEvents=2147483647` kabul ediyordu.
- Runtime artık 1..500 event ve event message başına 16 KiB ceiling uyguluyor; `messageTruncated` structured metadata taşıyor.
- Fix `8dc8aaf`; live 501 reject + normal structured event payload GREEN.

### #200 — Talvora self service stop/restart — LIVE VERIFIED
- Self restart pre-fix host servisini durdurup MCP request'i response vermeden öldürüyordu.
- Talvora self stop/restart artık 2 saniye gecikmeli detached LocalSystem helper'a handoff ediliyor; tool önce `StopScheduled` / `RestartScheduled` döndürüyor.
- Live restart yeni PID ile otomatik geri geldi; controlled self-stop acceptance GREEN.
- Fix `8dc8aaf`.

### #201 — Admin registry list response bounds — LIVE VERIFIED
- Pre-fix geçici HKLM fixture 1,200 registry value'yu tek response'a döndürüyordu.
- `talvora_registry_list` artık combined ordinal subkey/value stream için max 5,000 entry + 8 MiB response ceiling ve `maxResults/resultOffset/nextResultOffset` pagination kullanıyor.
- Live 1,200-value acceptance: 500 + 500 + 200 deterministic pages; son sayfa `truncated=false`.
- Fix `e6fe0a8`; installer SHA-256 `39D1D70B9E6897B764131AB13E387A8C9C27B00CA281DC84FD67C2E09B02D0A1`; exact-installed runtime aynı commit.

### #202 — logged-on user environment scope — LIVE VERIFIED
- Pre-fix `target=user`, LocalSystem service account user profile'ını hedefliyordu.
- `user` / `interactive-user` artık logged-on user session'ında çalışıyor; eski service-account scope explicit `service-user` olarak korunuyor.
- Fix `8ae1a5a`; independent `MSI\tayla` user-session read ve service-user isolation GREEN.

### #203 — persistent user empty-string environment value — LIVE VERIFIED
- İlk #202 helper'ı Windows PowerShell 5.1/.NET Framework nedeniyle empty-string'i deletion'a dönüştürüyordu.
- Helper PowerShell 7 / .NET 10'a taşındı; persistent user empty value artık `found=true,value=""`.
- Fix `8f6887f`; installer SHA-256 `2525B66A2A7E22CCFD9F61BD06587ACAAB99FC558CB6A62C9B4D264190A83BC6`; exact-installed aynı commit; independent user-session `<empty>` probe GREEN.

### #204 — INI/.env EOF newline preservation — LIVE VERIFIED
- Pre-fix compatibility INI/.env set/delete, final newline olmayan existing file'a mutation sırasında CR/LF ekliyordu.
- Shared text helper original EOF newline presence'i ve CR-only newline style'ı izliyor; existing file formatting korunuyor.
- Fix `bb1eaf5`; installer SHA-256 `FA5B0E70E78D5687920C72DA998690D440E0800320FE2B3E0D563AE5D36CF27A`; exact-installed aynı commit.
- Live dotenv + INI set/delete no-final-newline acceptance GREEN; fixtures temizlendi.

### #205 — INI/.env list response bounds — LIVE VERIFIED
- Pre-fix `dotenv_list` ve `ini_list` live 12,000-entry fixture'ın tamamını tek MCP response'a döndürüyordu.
- Her iki liste artık filtered stream üzerinde default 500, absolute 10,000 entry + 8 MiB response ceiling ve deterministic continuation metadata kullanıyor.
- Fix `a1c71d8`; installer SHA-256 `B4641A788B4A24157EDD3583B9A4560F2701E930E0E38500DF5172AB8C4E52D3`; exact-installed aynı commit.
- Live dotenv 500+500+200 ve INI 200/1200 terminal page sırası GREEN; focused-surface + metadata live GREEN.

### #206 — XML query response bounds — LIVE VERIFIED
- Pre-fix node count bounded olsa da büyük node `value/outerXml/attributes` ve scalar XPath payload karakterleri sınırsızdı.
- Runtime node pages ve scalar sonuçlar için hard 8 MiB response-character ceiling kullanıyor; continuation destekli node pages korunuyor.
- Fix `aa55cad`; installer SHA-256 `B06CE2FFE231356AA117DB22EEC0BD1E1488B6399D3CF51FE43DD21E827E758D`; exact-installed runtime aynı commit.
- Live 4.3M-character fixture: oversized node-set + scalar reject, küçük scalar GREEN; fixture temizlendi.

### #207 — Structured config transport-safe response bounds — LIVE VERIFIED
- Canlı ölçümde 5.0M JSON response GREEN iken 5.5M/6.0M JSON ve 6.0M XML scalar generic transport failure veriyordu.
- JSON/YAML/TOML get ve XML node/scalar sonuçları artık ortak 4 MiB structured-value ceiling kullanıyor.
- Fix `b6fb7de`; installer SHA-256 `7CEC30CE7C422F484F9E8E386238BCF14B139483233E1085CCB77262F4149CDA`; exact-installed runtime aynı commit.
- Live 4.0M JSON GREEN; 4.3M JSON/YAML/TOML/XML controlled INVALID_ARGUMENT; metadata + focused surface live GREEN.

### #208 — Registry value transport-safe response bounds — LIVE VERIFIED
- Pre-fix 3 MB binary value 4.0M Base64 chars olarak dönüyordu; 4 MB binary value get/list yolunda generic transport failure üretiyordu.
- `registry_get` ve `registry_list` artık ortak 4 MiB structured-value response ceiling kullanıyor ve oversized tek value'yu serialization öncesi reddediyor.
- Fix `42b24dd`; installer SHA-256 `A090E582E315B31634CCA384C654B55179A2189CDBBD85915C7D8EE9F2EDEF46`; exact-installed runtime aynı commit.
- Live B3 get GREEN; B4 get/list controlled INVALID_ARGUMENT; registry fixture temizlendi; metadata + focused surface live GREEN.

### #209 — replace_text encoding/BOM preservation — LIVE VERIFIED
- Pre-fix UTF-16LE BOM dosya tek kelimelik replacement sonrası BOM'suz UTF-8'e dönüyordu.
- `replace_text` artık canonical `SourceTextCodec` ile encoding/BOM algılıyor ve aynı UTF-8/BOM, UTF-16 LE/BE BOM veya UTF-32 LE/BE BOM semantiğiyle geri yazıyor.
- Fix `5723afa`; installer SHA-256 `22E9320578CBC4ECAE64E424E6538D3683E69E5AC1BA2B86C815DFC87EA14FD3`; exact-installed runtime aynı commit.
- Live UTF-16LE BOM `FF FE` ve UTF-8 BOM `EF BB BF` korunuyor; newline + replacement içerikleri GREEN; metadata + focused surface live GREEN.

### #210 — Config mutation encoding/BOM preservation — LIVE VERIFIED
- Pre-fix JSON/YAML/TOML/XML set işlemleri UTF-16LE BOM dosyaları BOM'suz UTF-8'e dönüştürüyordu.
- Encoding probe + writer factory `SourceTextCodec` içinde merkezileştirildi; JSON set/delete, YAML/TOML set/delete ve XML set/delete aynı politika üzerinden orijinal encoding/BOM semantiğini koruyor.
- Fix `c69d7f8`; installer SHA-256 `48E7EB33A0F0B113749254374C542AEEF4821AE7A436E079A334A7C26B54269A`; exact-installed runtime aynı commit.
- Live UTF-16LE JSON/YAML/TOML/XML set fixture'larının tümü `FF FE` BOM'u ve yeni değeri korudu; metadata + focused surface live GREEN.

### #211 — Knowledge fetch transport bounds — LIVE VERIFIED
- Shared Admin/Dev `fetch`, `search` tarafından uygulanan 4 MiB knowledge-file budget'ını direct/stale document ID yolunda tekrar uygulamıyordu.
- Runtime file size ve decoded response text'i mevcut 4 MiB knowledge budget'ına göre serialization öncesinde doğruluyor; büyük dosyalarda `talvora_read_text_range` yönlendirmesi veriyor.
- Fix `73e13b7`; installer SHA-256 `7D8AD53D87E1F7C3BEEDBAC6BA1C3F0794C5B51275D1BF5FE17002592EC9D794`; exact-installed runtime aynı commit.
- Live Admin 4.0M-character fetch GREEN; 5.5M-character fetch controlled INVALID_ARGUMENT; fixture temizlendi.

### #212 — replace_text atomic publication — LIVE VERIFIED
- Encoding-preserving replace path hedefi hâlâ doğrudan in-place yazıyor ve interruption halinde truncate/partial publication riski taşıyordu.
- Fix `ed275bb`, target ve optional `.bak` publication'ını durable `AtomicFile.WriteAllTextAsync` üzerinden yapıyor.
- Dedicated durability regression + Release build GREEN; clean deploy HEAD `5b990da`, installer SHA-256 `AF38CDED1D4CE97520CB776D353AB4FF659937A7E41905DF25DBE53B861FDC38`.
- Live UTF-16LE BOM target + backup + replacement + no-temp-file acceptance GREEN; fixture temizlendi.

### #213 — replace_text encoding regression drift — TEST VERIFIED
- #210 encoding mapping'lerini `SourceTextCodec` içine merkezileştirdikten sonra eski regression implementation literal'larını yanlış dosyada arıyordu.
- Test artık replace_text delegation'ını ve canonical codec mapping ownership'ini doğruluyor; durability ayrı regression'da.
- `REPLACE_TEXT_ENCODING_SOURCE_GREEN`.

### #214 — write_text atomic publication — LIVE VERIFIED
- Admin-only complete-file UTF-8 overwrite doğrudan in-place yazılıyor ve interruption halinde truncate/partial publication riski taşıyordu.
- Fix `242f0a2`, BOM'suz UTF-8 kontratını değiştirmeden durable `AtomicFile.WriteAllTextAsync` kullanıyor.
- `WRITE_TEXT_DURABILITY_SOURCE_GREEN` + Release 0/0; installer SHA-256 `ED755C04E88B274480037A9ECA756388A12D4B4A10D6BD5381D4F340EC348AA0`; exact-installed aynı commit.
- Live exact Türkçe text/no-BOM/no-temp-file acceptance GREEN; fixture temizlendi.

### #215 — append_text existing encoding preservation — LIVE VERIFIED
- Pre-fix UTF-16LE dosyaya append, raw UTF-8 bytes ekleyip karma encoding oluşturuyordu; `beta` -> `敢慴`.
- Fix `5b2c899`, mevcut non-empty supported text dosyasının body encoding'ini kullanıyor; yeni/empty dosya strict BOM-less UTF-8 kalıyor ve append ikinci BOM üretmiyor.
- `APPEND_TEXT_ENCODING_SOURCE_GREEN` + Release 0/0 + metadata source/live + focused-surface live GREEN.
- Installer SHA-256 `68080D1EDCBF26F37AE6FE26DF8D152C1D7A363E9E461598B940DA8D3370494C`; exact-installed aynı commit. Live UTF-16 tail/body/BOM ve yeni UTF-8 no-BOM acceptance GREEN; fixture temizlendi.

### #216 — append_text newline preservation — LIVE VERIFIED
- Pre-fix `appendNewLine=true` mevcut LF/CR stilini yok sayıp Windows CRLF ekliyordu.
- Fix `a304b0a`, mevcut encoding ile bounded prefix probe yapıp ilk CRLF/LF/CR stilini koruyor; stil bulunmazsa platform default kullanılıyor.
- `APPEND_TEXT_NEWLINE_SOURCE_GREEN` + Release 0/0 + metadata source/live + focused-surface live GREEN.
- Installer SHA-256 `AE65AF7C91617915EA55B9B5E7690AF75E44181AC5D4672C5FCF7B2D1C929455`; exact-installed aynı commit. Live LF/CRLF/CR fixtures exact stilini korudu; fixture temizlendi.

### #217 — AtomicFile destination metadata preservation — LIVE VERIFIED
- Pre-fix existing-file publication `MoveFileEx(REPLACE_EXISTING)` ile temp file metadata'sını hedefe taşıyor ve custom DACL'i kaybediyordu.
- Microsoft'in documented metadata-preserving replacement primitive'i kullanılarak existing destination `File.Replace(..., ignoreMetadataErrors:false)` ile publish ediliyor; new destination write-through move yolunu koruyor.
- `ATOMIC_FILE_DURABLE_METADATA_REGRESSION_GREEN` + Job Storage + Native Installer source regression + Release 0/0 GREEN.
- Fix `668f77c`; installer SHA-256 `9729695F3D1CC48DF00AD673DFEF56FD91BCD2660459EFCA02D222C9F112D0CA`; exact-installed aynı commit. Live target ve backup exact SDDL/creation/Hidden+Archive/content preserved; temp file yok; fixture temizlendi.

### #218 — write_bytes complete replacement durability — LIVE VERIFIED
- Full replacement pre-fix `FileMode.Create` ile hedefi publish tamamlanmadan truncate ediyordu; kesinti existing binary target'ı bozabilirdi.
- Shared `AtomicFile.WriteAllBytesAsync` staged write-through + flush-to-disk + metadata-preserving publication ekliyor.
- Yalnız `append=false, offset=null` complete replacement bu yola taşındı; append ve offset write davranışı değişmedi.
- `ATOMIC_FILE_DURABLE_METADATA_REGRESSION_GREEN` + Talvora Release 0/0 + metadata/focused-surface live GREEN.
- Fix `bf91648`; installer SHA-256 `16380C9A48ED69447EBE30F97D580D2ABCB21BEB7CBDFC1F8E3ED4A69E31F085`; exact-installed aynı commit. Live binary replacement exact bytes + SDDL + creation time + Hidden/Archive + zero-temp ile GREEN; fixture temizlendi.

## Doküman tutarlılığı notu

- `BUG-AUDIT.md` dosyasının en üstteki CURRENT/current remediation summary bölümü otoritatiftir: #121–#218 tamamlandı; #199–#212 ve #214–#218 canlı, #213 test doğrulandı.
- Aynı dosyanın daha eski gövde satırlarında ve `MCP-CONTROL-CENTER-TODO.md` içinde tarihsel `pending`, eski `OPEN` veya pre-live ifadeler kalmış olabilir. Bunları yeni oturumda gerçek repo/remote/live durumunun önüne koyma.
- Eski tamamlanmış bug'ları tekrar test edip yeniden açma; yalnız yeni kanıt veya gerçek regresyon varsa dön.

## Sabit çalışma kuralları

1. Yeni oturumda önce bu HANDOFF'u oku.
2. Ardından yalnız `git status --short`, son birkaç commit ve `BUG-AUDIT.md` üst CURRENT özetini doğrula.
3. Normal source/config/doc editlerinde önce `talvora_read_source`, sonra PRIMARY `talvora_apply_patch`.
4. Bir seferde tek doğrulanmış bug: kanıt -> kök neden -> minimal patch -> en küçük anlamlı targeted test.
5. Aynı geniş test paketlerini gereksiz yere tekrar etme.
6. Her bug sonrasında ilgili yaşayan belgeleri güncelle.
7. Her düzeltilen bug için yalnız ilgili dosyaları stage et; ayrı commit oluştur.
8. Her bug commit'ini hem Gitea `origin/main` hem GitHub `github/main` üzerine push et.
9. Runtime-affecting değişiklikte canonical installer/deploy yolunu kullan; servis değişiminde beklenen MCP disconnect sonrası reconnect et ve `talvora_system_info.sourceCommit` ile exact-installed commit'i doğrula.
10. Docs-only commit için sırf HEAD değişti diye runtime redeploy etme.
11. `git add .`, reset, clean, stash, revert kullanma.
12. Broad catch/fallback ile gerçek hatayı gizleme; çalışan capability'yi gereksiz kısıtlama veya kaldırma.
13. Framework/API davranışı sürüme bağlıysa kurulu sürümü kontrol et; Context7/resmi dokümantasyonla doğrula.

## NEXT SESSION — kesin devam noktası

**#193–#218 kapanmıştır; Dev 174 / Admin 91 focused surface policy GREEN, full 204-tool smoke baseline GREEN ve #199–#218 Admin/live/test gates GREEN. Sonraki yeni doğrulanmış bulgudan deep audit'e devam et.**

Başlangıç sırası:

1. Bu HANDOFF'u oku.
2. `git status --short` ile tree'nin temiz olduğunu doğrula.
3. `HEAD`, `origin/main`, `github/main` durumunu kontrol et.
4. `talvora_system_info.sourceCommit` ile canlı runtime baseline'ını doğrula.
5. `BUG-AUDIT.md` üst CURRENT özetini oku; #121–#188'i tekrar tarama.
6. #190 privacy/security, #191 MCP metadata, #192 focused-surface/tool-selection, #193 Git ownership ve #194–#218 son smoke/runtime/Admin düzeltmelerini yeni kanıt veya gerçek regresyon yoksa kapalı kabul et.
7. Deep audit'te ilk yeni doğrulanmış bulguyu kullanıcıya bildir; şüpheyi bug diye yazmadan önce gerçek çağrı zinciri veya minimal reproduction ile doğrula.
8. Her yeni bulguda minimal fix + targeted regression uygula.
9. Fix sonrası docs -> commit -> Gitea push -> GitHub push -> gerekiyorsa canonical live deploy sırasını tamamla.
10. Sonraki bug'a ancak önceki bug tamamen kapandıktan sonra geç.

## Bitiş kriteri

Yeni audit turunda doğrulanmış açık bug kalmadığında:
- targeted/regression gate'ler GREEN,
- working tree clean,
- Gitea ve GitHub senkron,
- son runtime-affecting commit exact-installed live,
- ardından `%USERPROFILE%\\Desktop\\bitti.txt` oluştur ve final HEAD/remote/live/test özetini yaz.
