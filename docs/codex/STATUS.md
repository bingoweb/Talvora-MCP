# Talvora — Codex status

> Single live status for agent work (Codex or ChatGPT via Talvora). English. Newest Log entry on top.
> Cards: every open card is in `docs/codex/ROADMAP.md`. Design specs: `docs/codex/DESIGN-SANTRAL.md` (Control Center, current), `docs/codex/DESIGN-WORKLOG.md` (desktop worklog), `docs/codex/DESIGN-CONTROL-CENTER.md` (Gözlemevi: its information architecture, copy deck §9 and accessibility §10 stay valid).
> Protocol: AGENTS.md → **CONTINUE PROTOCOL**. Shortcut for the user: "Talvora'ya devam et".

## Fixed facts

| | |
|---|---|
| Local folder | `C:\Users\tayla\Talvora-MCP` |
| Remotes | `origin` = local Gitea `ssh://git@127.0.0.1:2222/taylan/Talvora-MCP.git` (primary tracker) · `github` = `https://github.com/bingoweb/Talvora-MCP.git` (**PUBLIC** — never commit secrets, machine-specific credentials or personal data) |
| Branch | `main` only. Do not create branches. Dependabot PRs: apply with scripts/Apply-DependabotPr.ps1 (DEP1), never merge on GitHub. |
| Commit identity | `Taylan Soylu <6751394+bingoweb@users.noreply.github.com>` (do not change) |
| Push | **Authorized** (Q3, 2026-10-18): after each card `git push origin main` and `git push github main`; never force. |
| Gitea tracker | Mirror each card's Log entry as **one text comment** on Gitea issue **#9** (`taylan/Talvora-MCP`). No attachments. If the comment cannot be posted (connector approval, Gitea down), write `Gitea mirror skipped: <reason>` in the Log entry and continue — never stop or ask the user for it. |
| Audit baseline | Newest Claude audit tag `audit/2026-11-07-40` = the audit 40 commit. Claude creates and moves `audit/*` tags; the agent never does. |
| Executors | Codex **or** ChatGPT with the Talvora connector — identical rules (AGENTS.md → WHO MUST FOLLOW THESE RULES). Log entries name the executor. |
| Runtime | Windows service `Talvora` (LocalSystem), `127.0.0.1:7676`, installed under `C:\Program Files\Talvora\Versions\<commit>-<stamp>`; tray `Talvora.Tray.exe` in the user session. |
| Toolchain | .NET SDK 10.0.403 exact pin (SDK2; runtime 10.0.14) with matching locked NuGet graphs (K0 done). PowerShell 7 required by the interactive runner. |
| Install path | `scripts/Deploy-Windows-Installer.ps1` (SYSTEM task; when called from inside Talvora it does not wait — poll `/healthz.sourceCommit`). Never run `Reset-And-Install.ps1` unless a card says so. |
| Secrets | `C:\ProgramData\Talvora\Secrets\*` belong to the user's other projects. Never read, move, print or delete them. Listing names/ACLs is allowed. |
| User preferences | Personal single-user workstation: no local MCP token (Q1). Cosmetics matter a lot: every visible change needs before/after PNGs. The worklog's vertical **code terminal** is the user's favourite feature — never break or shrink it. |

## In progress

In progress: (none)

## Agent queue

Legend: `[ ]` todo · `[x]` done · `[~]` done but waiting for user evidence · `[?]` blocked on an open question · `[-]` won't do.

> **Only open work lives in these documents** (user decision 2026-11-07). At every audit Claude removes finished queue lines, Log entries older than the previous audit, answered findings, finished cards and decided proposals that no open card names. Git keeps all of it: `git show audit/<tag>^:<path>` is a file as it was before that audit's cleanup (first cleanup: `audit/2026-11-07-40`). The agent may tick a line `[x]` and leave it there.

- [ ] D30 — The agent brief always carries the current card (audit 40, critical)
- [ ] D31 — The parallel-gate timing regression runs on its own (audit 40; P-49 decided)
- [ ] D29 — Smoke clears its fixture traffic after the SN3 check (audit 39; unblocked: P-49 decided, D31 first; implement the card unchanged)
- [ ] D32 — The traffic meter never counts Talvora's own clients (audit 40, user decision)
- [ ] D33 — Desktop progress frames survive a busy machine (audit 40)
- [ ] D34 — Tunnel health without launching tunnel-client every round (audit 40)
- [ ] D35 — The GitHub release lookup waits after a failure (audit 40)
- [ ] D36 — Atomic publication also retries a busy destination (audit 40)
- [ ] D37 — Event slips in plain Turkish (audit 40)
- [ ] D38 — "Yalnızca hatalar ve ajan işleri" counts ChatGPT as an agent (audit 40, user decision)
- [ ] D39 — Penpot error text uses the shared secret masking (audit 40)
- [ ] [C] SN9 — Switchboard keeps focus and hover, draws calmly (Claude; audit 40)
- [ ] [C] WL2 — Çalışma günlüğü: no blank project line, a real empty state (Claude; audit 40)
- [ ] OB1 — talvora_note_write: write Obsidian vault notes, refuse secrets and the linked repo docs (user request 2026-11-01)

## User queue

(none)

## Open questions

(none)

## Findings for Claude

(The agent adds one line per observation outside the current card: `file:line — what was seen`. Do not fix them.)

## Log

- 2026-11-07 — Claude, audit 40 (deep, whole project; tag `audit/2026-11-07-40`): range `audit/2026-11-07-39..47b43a8` matches its cards; D26, D27, D28 and LB1 passed the visual check in both themes and SN7 is done (idle CPU goes to SN9): closed. User decisions: `talvora_delete` stays unguarded; ChatGPT counts as an agent (D38); Talvora's own test calls are never counted (D32); E1, E2, S5, TI1, UI2 and the user lines U2–U11 closed (the user reports problems himself); local `artifacts/` older than 7 days pruned at every audit; the 2.5 GB `C:\ProgramData\Talvora\Diagnostics` leftovers deleted.
  Token economy: finished work removed from the agent documents (STATUS 203 k → 18.5 k characters, ROADMAP 445 k → 64 k, PROPOSALS 105 k → 5.6 k, DESIGN-WORKLOG §1/§9, DESIGN-CONTROL-CENTER §1/§7/§11, `docs/archive/`, `APPLICATION-DEVELOPMENT-ROADMAP.md`, finished cards' reference folders, the stale HANDOFF text); AGENTS.md: rule 8 names DESIGN-SANTRAL.md, CONTINUE PROTOCOL step 3 reads only the proposals a card names, conditions for finished cards (A1, A2, A3, T3, N10, R3) removed; then AGENTS.md compacted without any rule change (file 38.4 k → 35.2 k characters, its brief part 30.6 k → 28.1 k). The brief is 43.6 k characters with the card first (before: cut at 60 k without the card). K1, UI1–UI8 and N1–N8 stay under "Kept for a regression test" until D30 moves `tests/QualityGateRegression.ps1` off finished cards. P-49 decided (option 2 → D31). New cards: D30–D39, [C] SN9, [C] WL2.

- 2026-11-07 — LB1, executor ChatGPT via Talvora: UI7 changed first and failed RED "UI7 About must expose version, short source commit and approved links" (Program.UI7.cs:52); Settings About now shows bare version with a separate 7-character commit, Gitea placeholder icon fallback removed, Turkish-cased Monogram/MonogramVisibility added, and exact prescribed 15px Bahnschrift brass-light text added inside the existing 40px socket. Notification regression GREEN, signed-in Full gate 68 checks/0 errors, Hepsi geçti. Both smoke PNGs artifacts/lb1-preview/control-center-ui4-services-{initial,alternate}.png visually inspected: brass G/P present, Talvora logos retained, no geometry change. Context7 /dotnet/docs (tr-TR casing) and /dotnet/docs-desktop (Grid general, documented overlap/null Source gap) plus official vendor Grid page checked. Exact-commit install and GitHub CI follow commit; Claude visual check pending. RV1 closed via 6.11 h post-install log data and slowest five entered in Findings for Claude.
  Self-review — cosmetics: exact prescribed socket typography/colour only; both original and alternate theme PNGs show G/P, Talvora logos unchanged.
  Self-review — usability: each serviceless-logo socket has a recognisable first letter; About displays plain version separately from short commit.
  Self-review — Turkish: first-letter uppercase uses tr-TR; existing UI words untouched.
  Self-review — correctness/security: UI7 RED/GREEN, Full 68/0, no token/sizing changes or unrelated source edits.
- 2026-11-07 — RV1, executor ChatGPT via Talvora: timed the complete ProcessRunner.RunAsync call via Stopwatch in RunClientAsync, recording only the first two safe positional verb words, elapsed milliseconds, the existing limit and the exit code or timeout. Added one log line on normal returns (including TimedOut results) and on thrown TimeoutException; original timeout and retries are unchanged. Source RED then GREEN, signed-in Tray build 0 warnings/0 errors, Full gate 68 checks/0 errors, Hepsi geçti. Context7 /dotnet/docs and Microsoft Learn verified Stopwatch.StartNew for awaited work. Exact-commit deployment and GitHub CI follow commit; live pending one hour of normal use and five slowest lines from tray.log for Findings for Claude.
  Self-review — cosmetics: no presentation, tokens or animation changed.
  Self-review — usability: command timing diagnostics aid diagnosis without changing the user workflow.
  Self-review — Turkish: no user-facing messages changed; technical log format follows the card.
  Self-review — correctness/security: RED then GREEN, Full 68/0; no option values, keys, environment or secrets are logged; timeout and retry logic preserved.
- 2026-11-07 — D29 blocked, executor ChatGPT via Talvora: prescribed overview.ApplyTraffic(null) added and source regression RED/GREEN; Tray build 0 warnings/errors. First signed-in Full failed Context7 gate; the explicitly allowed .context7 supporting evidence was added. Second signed-in Full failed unrelated PS:QualityGateParallelRegression at tests/QualityGateParallelRegression.ps1:48 (7.4040841 s >= 7 s, 61 checks/1 failure), while isolated regression returned QUALITY_GATE_PARALLEL_GREEN. To preserve the Full-gate requirement, changes to SN3 and Context7 reverted; P-49 requests the permitted next action. No installation, deployment or D29 commit. Gitea mirror skipped: no authenticated issue-comment route.
- 2026-11-07 — D28, executor ChatGPT via Talvora: replaced exactly the two user-visible generic health-maintenance failure strings in TrayApplicationContext.cs with the audit 39 approved Turkish text. RED source-copy check failed before the edit and GREEN check passed after; Tray build 0 warnings/0 errors; signed-in Full gate 68 checks, 0 errors, Hepsi geçti. Severity Warning, category recovery, dedupKey and English TrayLog unchanged. Context7 /dotnet/docs and Microsoft Learn confirmed ordinary C# literals preserve Unicode; .context7/verification.json refreshed as supporting evidence. Exact-commit install, live smoke and GitHub CI follow commit; visual check pending (Claude). Gitea mirror skipped: no authenticated issue-comment tool in Talvora Dev connector.
  Self-review — cosmetics: no layout, token, colour or animation changed; only the two prescribed text strings.
  Self-review — usability: the maintenance failure event now explains that a retry will occur.
  Self-review — Turkish: the title and detail match the approved copy with correct diacritics.
  Self-review — correctness/security: RED then GREEN, Full 68/0; log, severity, category and deduplication unaffected.
- 2026-11-07 — D27, executor ChatGPT via Talvora: exact UI3 test RED ("UI3: D27: a dropped line must say it does not answer"); prescribed StatusNarrator Offline branch now says "Talvora Admin yanıt vermiyor." with the reason on the second line, and Sentence adds missing terminal punctuation. Notification regression GREEN; signed-in Full gate 68 checks, 0 errors, Hepsi geçti. Context7 /dotnet/docs and Microsoft Learn verified String.EndsWith(char) and Trim(). Exact-commit installer, installed smoke and CI follow commit; visual check pending (Claude). Gitea mirror skipped: no authenticated issue-comment tool in the Talvora Dev connector.
  Self-review — cosmetics: no new layout, controls, tokens, animations or colours; prescribed text only.
  Self-review — usability: the unavailable service now has an explicit title and its reason remains visible.
  Self-review — Turkish: the approved wording "yanıt vermiyor." and punctuation match the card exactly.
  Self-review — correctness/security: RED then GREEN, Full 68/0; other narrator messages preserved.
- 2026-11-07 — D26, executor ChatGPT via Talvora: added the exact UI3 regression and confirmed RED "UI3: D26: only successful repairs count as otomatik onarım" (Info 2 + Warning 7 + Error 1 incorrectly summed to 10). Inserted Severity == Info into the existing 24-hour recovery filter as prescribed; Notification regression GREEN and signed-in Full gate 68 checks, 0 errors, Hepsi geçti. Context7 /dotnet/docs and Microsoft Learn confirmed Enumerable.Where filtering before Enumerable.Sum. Canonical exact-commit deployment, installed smoke and GitHub CI follow commit; visual check pending (Claude). Gitea mirror skipped: no authenticated issue-comment tool in the Talvora Dev connector.
  Self-review — cosmetics: no tokens, geometry, layout or empty states changed.
  Self-review — usability: actual successful repairs alone contribute to the 24-hour count.
  Self-review — Turkish: existing UI phrases remain unchanged.
  Self-review — correctness/security: RED then GREEN, Full 68/0; events and the Olaylar page are untouched.
- 2026-11-07 — UI11 blocked: prescribed Image wrapper in OverviewView.xaml cannot be applied because the page hosts the custom Switchboard Canvas rather than a service-logo XAML image; existing ServiceRow uses a distinct Santral brass socket. Context helper erroneously looks for UI11 in DESIGN-CONTROL-CENTER rather than ROADMAP. No UI11 source edits; P-48 requests exact visual placement and routing. Baseline signed-in Changed gate: 8 checks, 0 errors, Hepsi geçti. UX1 GitHub windows-ci run 42299173262 retry attempt 2 succeeded.
- 2026-11-07 — UX1, executor ChatGPT via Talvora: prescribed Local EventWaitHandle/AutoReset event signals the existing tray to show the Control Center only for --open-control-center; installer creates a CommonPrograms WScript.Shell shortcut to the installed tray. Before change, the all-users shortcut was absent. Signed-in tray build GREEN (0 warnings/errors); standalone installer csproj build requires generated Payload.zip and is handled by canonical installer build. Signed-in Full gate 68 checks, 0 errors, Hepsi geçti. Context7 /dotnet/docs verified named events and ThreadPool wait registration; Microsoft Learn confirmed CommonPrograms and WScript.Shell CreateShortcut behavior. Canonical installer, installed shortcut, live user-run and exact-commit CI follow delivery. Gitea mirror skipped: no authenticated issue-comment route in Talvora Dev connector. Visual check pending (Claude).
- 2026-11-07 — L1 closeout: installed /healthz.sourceCommit = eb7a8b6ce4ce9b788ce2c7cd0bb7159165b98274; live scratch repository artifacts/l1 had a Windows-1254 .bat file read as windows-1254, patched without UTF-8 conversion, and its bytes were exactly equal to the expected 1254 representation; scratch cleaned. Installed MCP surface-policy smoke GREEN; GitHub windows-ci 42296839637 success.
- 2026-11-07 — L1, executor ChatGPT via Talvora: exact prescribed test showed RED (legacy Turkish text rejected as UTF-8), then GREEN after SourceTextCodec distinguishes valid UTF-8 prefixes from Windows-1254 and retains the selected encoding for reads/writes. Windows SourceEdit regression GREEN, all existing encoding scenarios unchanged; signed-in Full gate 68 checks, 0 errors, Hepsi geçti. Context7 /dotnet/docs verified CodePagesEncodingProvider and exception fallbacks; Microsoft Learn confirmed Utf8.ToUtf16 isFinalBlock:false permits NeedMoreData and invalid data is rejected when replaceInvalidSequences:false. Installer deployment, artifacts/l1 live read/patch and exact-commit GitHub CI follow delivery. Gitea mirror skipped: Talvora Dev connector has no authenticated issue-comment tool.
- 2026-11-07 — W7 closeout: /healthz.sourceCommit matched c50f1f21cab5f0796a22f2a98c5cb5b44f7862ab; installed talvora_run_process with bare npm --version exited 0 and printed 12.2.0; signed-in installed surface-policy smoke GREEN. GitHub windows-ci 42294710574 success; both remotes up to date.
- 2026-11-07 — W7, executor ChatGPT via Talvora: added the prescribed bare-command regression to the Windows SourceEdit runner (RED: Win32Exception file not found, with a .cmd on PATH); inserted the exact ProcessRunner PATH/PATHEXT resolver block with the working-directory exclusion (GREEN: w7-bare-command-resolution and SourceEdit core). No CommandResolver or directory-qualified command changes. Signed-in Full gate: 68 checks, 0 errors, Hepsi geçti. Context7 /dotnet/docs verified the Windows rooted-vs-fully-qualified path distinction; its ProcessStartInfo query did not return a PATHEXT rule, so vendor ProcessStartInfo.UseShellExecute and CreateProcessW pages supplied executable search semantics. Exact-commit installation, live npm --version and CI follow commit. Gitea mirror skipped: the Talvora connector has no authenticated issue-comment tool.
- 2026-11-07 — W6 closeout: installed /healthz.sourceCommit = 50f7c03168cd983f2c03e4f79ddb67d6c5eda64a; live talvora_search_text in artifacts/w6 found "İzmir" exactly once at line 1 for both "izmir" and "IZMIR"; temporary fixture removed. Exact-commit GitHub windows-ci 41722195681 success.
- 2026-11-07 — [C] SN8 by Claude (user: "evet, şimdi yap"): each outside line shows its measured failures on a second line ("7 hata", `Sn.Word.Offline`; nothing when zero), a failed call's light travels in the error colour and ends in a short spark at the socket, sockets answer hover with a brass ring, and the screen-reader name adds "N hata". `ApplyTraffic` now returns (line, ok) pairs from the SN1 feed. Smoke: AssertSn8Failures (83 calls, 7 failed, three lights including the failed one, UIA name). A single long "83 çağrı · 7 hata" line ran into the next jack on the first capture, hence the second line.
- 2026-11-07 — Claude audit 39 of `audit/2026-11-03-38..e5fa73b`: W8, DEP18, W5, W6, W7, L1 and UX1 match their cards line for line; signed-in Full 68/0; CI success on every commit; installed ac95d41 (live traffic: dev 83 calls, 7 failed). P-48 decided (Claude's card defect): UI11 is replaced by LB1 in ROADMAP (the context helper routes `UI*` ids to the Gözlemevi spec), with the initial letter inside the Hizmetler socket. Findings: the overview's "otomatik onarım" counted failed attempts (15 instead of 7) → D26; a dropped line read "dikkat istiyor: Ulaşılamıyor" → D27; English/jargon in the health-maintenance event → D28; SN3 fixture traffic leaked into the live capture → D29; three 12 s tunnel-client timeouts → RV1 (user: measure first). OLASI: one MCP-Protocol-Version header/body mismatch right after the DEP18 install, not repeated. User chose: failed-call counts on the switchboard → [C] SN8. Tag `audit/2026-11-07-39`.