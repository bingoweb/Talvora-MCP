# Talvora end-to-end quality audit — 2026-10-09

## Verified baseline
- Windows service Talvora: Running/Automatic, LocalSystem; /healthz HTTP 200.
- Exact installed source commit: eeb3d008b6a53a7d50d3ab68bdb98784a3cfa074.
- Git main: clean, origin/main ahead 2.
- Locked Release solution restore/build: 0 warnings, 0 errors.
- Notification behavior, source edit regression, live focused MCP surface, Context7 and delivery gates: all GREEN.

## Scope and safety
1. Check service/HTTP/MCP surfaces, tool contracts, Job persistence, source-edit durability, Windows tray/Control Center behavior, CI, supply-chain and installer.
2. Reproduce novel bugs on isolated temporary fixtures, not on real credentials, users' projects, persistent data or production services.
3. Apply one bounded behavior change at a time, introduce a meaningful regression, rebuild and verify no unintended contract regression.
4. Use talvora_apply_patch for all repository mutations; never reset/stash/clean pre-existing work.
5. Do not stop or replace the installed Talvora service until a tested canonical installer, rollback path and source-to-installed parity are available.
6. Comply with current Context7 verification and official vendor evidence before delivery. Distinguish code-level fixes from installed-runtime acceptance.

## Confirmed source-level deficiencies to reproduce and repair
- Q-01 (high): ProjectDeliveryGate treats vendorSources as valid if the array merely contains entries, even null/blank entries; some syntactically invalid library IDs (e.g. '/') can pass. This can undermine the mandatory release evidence contract.
- Q-02 (high): ProjectDeliveryGate.ReadEvidence uses unbounded File.ReadAllText on repository-controlled evidence; an unexpectedly large file can cause unnecessary memory pressure rather than a deterministic rejected gate.
- Q-03 (medium): JobTools.ReadLogGenerationAsync uses unbounded File.ReadAllTextAsync on a rotation-counter sidecar; a malformed oversized sidecar amplifies allocation on job streaming and output reads.
- Q-04 (medium): OpenStableJobLogSnapshotAsync opens a FileStream and reads the generation again without disposal if the second read throws; exception paths can leak handles under file errors/rotation races.
- Q-05 (medium): TalvoraRuntimeMetadata.LoadFromDisk reads runtime metadata without a byte ceiling, despite this being tiny installer-owned JSON.
- Q-06 (medium): Hostinger credential presence/version reads use unbounded reads on secret and package metadata. Verify remediation does not expose credentials or alter existing installation.

## Acceptance
- RED pre-fix fixture / GREEN after-fix fixture for each actionable finding.
- Locked Release 0 warnings/0 errors; targeted regressions, live MCP surface and CI source gates pass.
- Record unresolved risks separately instead of inventing a green result.
- Do not treat a source build as an installed runtime upgrade.

## Closeout — 2026-10-11
- All six Q-01..Q-06 changes are implemented. Isolated quality regression: 5/5 groups PASS, covering schema validation, bounded reads, file-handle disposal, runtime metadata and Hostinger secrets/package metadata.
- Context7 `/dotnet/docs` rechecked for JsonElement validation and FileStream disposal. Refresh evidence in `.context7/verification.json` and retain valid non-UI Awwwards reference in `.talvora/awwwards-verification.json`.
- Locked Release solution: zero warnings and zero errors. `Validate-Context7Gate.ps1`, `Validate-ProjectDeliveryGate.ps1`, focused MCP, privacy, installer dependency provenance, metadata cache and license source regressions PASS.
- Gitea tracking: issue #17. Final commit and installed runtime identity to be recorded in closeout comment after safe canonical deployment.
- Risk boundary: all regressions use disposable local fixtures and avoid reading actual Hostinger credentials; no production vulnerability exploit was performed. A source-only pass does not certify the installed runtime until the exact canonical installer has been installed and `/healthz` plus tray verified.
