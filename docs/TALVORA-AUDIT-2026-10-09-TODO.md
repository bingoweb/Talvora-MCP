# Talvora quality audit TODO — 2026-10-09

- [x] Capture pristine source/installed baseline and remote divergence.
- [x] Read repo engineering rules and official .NET Windows service guidance through Context7.
- [x] Run baseline locked Release build, notification/source-edit/live MCP and delivery-policy tests.
- [x] Q-01: Add a regression that rejects blank/null vendor evidence and invalid Context7 library identifiers.
- [x] Q-01: Harden delivery evidence schema without weakening the gate.
- [x] Q-02: Add oversized-delivery-evidence regression and bounded file read.
- [x] Q-03: Add oversized job-generation regression and bounded sidecar read.
- [x] Q-04: Exercise exception-path file-handle disposal in job log snapshot.
- [x] Q-05: Cap installer runtime metadata read and preserve fail-soft /healthz contract.
- [x] Q-06: Bound Hostinger credential/version metadata reads without disclosing credentials.
- [x] Run targeted quality regression (five groups PASS), full locked Release build (0 warnings/0 errors), and source/desktop smoke checks.
- [x] Refresh Context7/Awwwards evidence, verify .NET official documentation via Context7, and rerun both delivery gates (GREEN).
- [x] Review targeted source diff and validate canonical installer provenance, deployment and exact installed source identity (see closeout report).
- [x] Publish closeout report with Q-01..Q-06 test evidence, known limits, exact git/installed state and Gitea issue #17.
