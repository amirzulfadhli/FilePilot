# Phase 9 — release readiness and usability

The first slice is on codex/phase-9-release-readiness. Version 0.1.0-beta.1 remains an unreleased candidate. Public release publication and release tags require owner approval; no release job, updater, runtime dependency, service or telemetry was added. Phase 9 is kept separate from main for review.

## Phase 8 integration evidence

On 2026-10-24 main fast-forwarded from dd6ec8d139eab16ba8886abb49965469f533006d to 889db1aa6375c33db374b78b8ee7a43526f81887, retaining the preparation and feature commits and existing branches. Both jobs in [run 40075007802](https://github.com/amirzulfadhli/FilePilot/actions/runs/40075007802) passed before the merge. No force push, discarded work or remote-history replacement was used.

Both jobs in the post-merge [main run 40075452149](https://github.com/amirzulfadhli/FilePilot/actions/runs/40075452149) also passed for the same commit. Phase 9 has not been merged into main.

Downloaded real-renderer evidence has 169 passed tests, no skips, and 15 passed fresh-process desktop/memory checks. The build job passed 166 tests with the three expected real-engine skips, then its own 15 checks, packaging, per-user installation/upgrade/rollback/uninstall and extracted-package startup/private-memory/growth/native-codec acceptance. Independent synthetic-PDF parsing and rendering also passed.

The downloaded clean candidate identifies source 889db1aa6375c33db374b78b8ee7a43526f81887, SourceDirty=false, 22 payload files and ZIP SHA-256 BC1F81BA94421BCE8F0B27443D84FBBF2B627D8589EFA06CEA15BA3FB7469DF4. This hash identifies that Phase 8 CI archive only; a later Phase 9 package has different contents and provenance. The retained hosted performance JSON passed the unchanged budgets: default popup/widget first frames 490/1,162 ms, hidden private memory 34.6/37.6 MiB and growth after 20 popup cycles 5.3/4.4 MiB. Server CI measurements do not certify a consumer desktop or long-duration stability.

See [Phase 8 local evidence and limitations](phase-8-stabilization.md), including the rendered-text contrast corrections, actual Windows 11 single-monitor 125-percent boundary check and simulated layouts down to 320 DIP width.

## First usability slice

- F1 and an accessible ? button open a compact, owned, offline help window. Its scrollable quick start covers the configured shortcut, Ctrl+O fallback, path copying, safe output behavior, image batches, optional DOCX export, rename/favorites, cancellation and resident versus one-shot lifecycle. Escape or Enter closes help; its footer stays visible under small work-area constraints. It does not open a website or save preferences.
- The widget shortcut previously ignored every busy operation. It now reopens the active operation without reading Explorer or replacing its files; cancellable image/document operations focus their Cancel control. Modal dialogs and an in-flight Explorer capture retain their existing guards. Move operations reopen without inventing cancellation support.
- Help is disabled during work, preventing a modal help dialog from blocking cancellation. The command tolerates the early WPF CanExecute query before DataContext initialization; the first development test exposed and corrected that startup case before commit.
- The existing pumped WPF acceptance now checks the actual F1 binding/help rendering/footer/focus, and exercises hidden conversion reopening with an injected waiting converter, source selection preservation, cancellation and busy-state recovery. The injected converter is a lifecycle test, not proof of native codec cancellation. Existing real conversion and document-engine tests remain required.
- Real-renderer CI is also enabled on this Phase 9 branch. Both relevant jobs must pass for any candidate; no tests are skipped or deadlines extended to mask failures.

## Verification and remaining readiness work

Local verification on Windows 11 (2026-10-24): Release build passed with zero warnings/errors; all 169 tests passed with real LibreOffice and no skips, including the busy/help assertions. All 24 package/script, 8 installer, 21 packaged lifecycle and 15 fresh-process desktop/memory checks passed. Independent PDF inspection and actionlint passed. Default extracted-package smoke passed unchanged budgets: popup/widget first frame 1,147/1,572 ms, hidden private 63.6/66.2 MiB and 20-cycle growth 4.8/3.3 MiB. This is a dirty development package based on 889db1a, not an approved clean release candidate.

The direct packaged F1/scroll/Escape check could not start: the desktop helper returned `failed to activate captured window` twice, including the permitted retry after fresh window selection. No manual F1/help success is claimed. Only the idle test-owned process was stopped afterwards; no user preferences or files were changed. The real WPF dispatcher test did render help, focus its scroll area, constrain its footer and verify the F1 command binding and help-disable/busy-reopen/cancellation behavior. A physical keypress during a real native conversion remains a separate manual check.

- Validate actual Explorer right-click launch and broader favorites/tray/shortcut-conflict flows; expand representative document/font visual review beyond the synthetic fixture.
- Windows 10 remains explicitly unverified. Other physical DPI/multiple-monitor configurations, live OS contrast switching, screen-reader announcements, text-size/magnifier combinations and hours-long sessions need suitable test environments.
- Review signing and unsigned-beta distribution terms. The current installer is per-user PowerShell plus a portable x64 framework-dependent ZIP; graphical setup, Apps & Features integration, ARM64/x86 and automatic updates remain unimplemented.
- Choose an exact clean CI candidate, validate its checksum/source revision/notices and retain appropriate acceptance evidence before requesting public-release approval. Reconfirm installation/runtime/optional-engine instructions and local-only data handling.

The Phase 7 historical timeout and terse memory-counter anomaly remain without a reproduced exact original cause. Strict dispatcher lifecycle/checkpoint and cold/stressed sampler gates stay enabled. No Windows 10 certification, screen-reader audit, arbitrary Word-layout guarantee or public release is implied by a green CI run.
