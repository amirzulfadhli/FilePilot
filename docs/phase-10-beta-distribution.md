# Phase 10 — beta distribution and installation preparation

Phase 9 was integrated on 2026-10-24 by fast-forwarding main from 889db1aa6375c33db374b78b8ee7a43526f81887 to d346d500f4287e0a554105f67468cda8945d08e1 after both jobs in [run 40095785619](https://github.com/amirzulfadhli/FilePilot/actions/runs/40095785619) passed. Existing commits and branches were preserved; no force push or public release was used.

The downloaded real-engine report contains 172 passed tests without skips; both jobs passed 15 separate-process desktop/memory checks. Build/package, 24 package/script, 8 installer and 21 lifecycle checks, extracted-package startup/memory/native smoke and independent synthetic PDF inspection passed. The clean Phase 9 CI archive records d346d500f4287e0a554105f67468cda8945d08e1, SourceDirty=false, 23 payload files and SHA-256 1F1D0DF213E630578F58E4E351B5A3D675F21FD7DCD296B048E2B4856BEB21A5. This identifies that archive only; Phase 10 archives have different contents and provenance.

## First slice

- The existing installer now supports PowerShell's standard ShouldProcess/-WhatIf. Path, archive inventory/hash and runtime checks still run, but preview returns before the first installation directory, extraction or receipt write. It reports Installed=false and Preview=true with the proposed path and source provenance. Declining -Confirm has the same no-write result. Normal installation retains its existing behavior and safety checks.
- Installer tests cover a spaced Unicode destination under a nonexistent parent, verifying neither is created in preview. Existing destinations and corrupt archives remain refused in preview, with a sentinel preserved. No new runtime dependency or installer framework is introduced.
- [Beta quick start](beta-quick-start.md) covers trusted checksums/provenance, runtime prerequisites, portable use, installation preview, activation, side-by-side upgrades, rollback and removal. It is included by the existing documentation packaging path.
- Both CI jobs run on this Phase 10 branch. Action pins, read-only permissions, short-lived explicit artifacts and the absence of release publishing are preserved.

Local verification on Windows 11 (2026-10-24) passed: Release build with zero warnings/errors; all 172 tests with real LibreOffice and no skips; 24 package/script, 11 installer and 21 packaged lifecycle checks; independent PDF inspection and actionlint. Installer cases exercise both the source tool and the actual delivered copy. The package contains both new guides (25 payload files). Extracted popup/widget first frames were 1,004/1,126 ms, hidden private memory 63.6/66.2 MiB and growth after 20 popup cycles 2.70/5.01 MiB, within unchanged budgets. This is a dirty development package based on d346d50, not an approved clean distribution candidate. Require both exact-commit hosted jobs before treating the committed candidate as verified. A preview cannot guarantee future permissions, disk capacity or absence of locks.

## Remaining distribution gates

Version 0.1.0-beta.1 remains unreleased. Public release publication and tags require separate owner approval. No public download, release job, signing certificate, execution-policy bypass, autostart or updater is added. The owner's decision on unsigned portable/PowerShell beta distribution remains open.

Carry forward [Phase 9 acceptance limits](phase-9-release-readiness.md) and [beta readiness](beta-readiness.md): Windows 10 is explicitly unverified; physical multi-monitor/DPI, live contrast/screen-reader behavior, hours-long sessions, broader Explorer/favorites/tray and representative Word-layout checks need further acceptance. Graphical setup, Apps & Features integration and ARM64/x86 remain outside the current package. Phase 10's preparation branch is separate from main; integrating Phase 9 does not approve a public release.
