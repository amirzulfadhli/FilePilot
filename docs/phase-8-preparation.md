# Phase 8 preparation — stabilization and release readiness

This phase is prepared on codex/phase-8-stabilization. Product UI changes listed below are not implemented by this preparation commit. FilePilot remains the unreleased 0.1.0-beta.1 candidate; public release publication and release tags require separate owner approval.

## Verified starting point

Main and feature/phase-7-beta-ci were synchronized at dd6ec8d139eab16ba8886abb49965469f533006d after [CI run 39942231530](https://github.com/amirzulfadhli/FilePilot/actions/runs/39942231530) passed both jobs. The fast-forward from cab070d contains every completed Phase 2–7 feature head; existing branches and commits were retained. No force push or conflicting-history replacement was used.

The real-engine report has 163 passed tests and no skips. Both hosted jobs also passed five WPF, five cold memory-sampler and five allocation-churn checks in separate processes, with no retry-on-failure. The build job checks packaging and installation/upgrade/rollback/uninstall; the document job independently parses and renders a synthetic real-LibreOffice PDF. The downloaded Phase 7 candidate has SourceDirty=false, source dd6ec8d, 20 payload files and archive SHA-256 D024900ED3BB68ED1285F5145647C6E93AAD9E12EAAF3C87837667EBBBC22F90. This identifies that CI candidate only, not a later Phase 8 package or an approved public release.

Local Release verification passed all 163 tests with real LibreOffice, 24 packaging/script checks and the extended separate-process path. The same application code passed 8 installer and 21 packaged lifecycle checks. Extracted popup/widget measurements were below the existing startup/private-memory/growth smoke budgets. See phase-7-sta-stabilization.md for the earlier counts, runs and methods.

The confirmed WPF harness defect was an unpumped STA and unprocessed application shutdown. It now runs a real dispatcher, awaits rendered frames, verifies shutdown and isolates desktop initialization from concurrent suite work. The exact historical 20-second stall was not reproduced. A later terse memory-counter assertion also lacked sampled values; the sampler and original assertions remain unchanged, with counter-specific evidence and bounded stress coverage added. Neither historical event is assigned an unsupported root cause. Recurrence must stop the gate and preserve the exact checkpoint/counters before making a correction.

## Incremental implementation order

1. **Widget keyboard flow.** Focus Copy Path for a valid selection and Choose files for the fallback. Preserve Escape/dropdown behavior, busy/cancellation protection, tray reopening and current file-operation semantics. Check accessible names and feedback announcements with a screen reader; do not claim an audit from automation properties alone.
2. **Windows contrast handling.** Add system-color resources that take precedence over the saved light/dark choice in high contrast. React to Windows setting changes without polling, preserve the user's normal preference, cache unchanged resources and detach static event handlers on shutdown. Reuse the current WPF resource dictionaries and pumped STA tests.
3. **Performance and stability.** Keep the existing smoke budgets (first frame at most 5 seconds, hidden private memory at most 200 MiB, private growth at most 32 MiB after 20 popup cycles). Measure longer explicit sessions and more popup/settings cycles before changing budgets. Keep normal startup free of profiling, forced collections, working-set trimming and background polling. Capture managed thread stacks as development evidence if a checkpoint stalls again; do not upload full process-memory dumps automatically.
4. **Compatibility and deployment.** Exercise small work areas, negative monitor coordinates, monitor changes and 100/125/150/200 percent scaling. Recheck missing runtime/engine feedback, locked files, interrupted deployment, side-by-side activation and settings preservation. Keep the existing per-user scripts and guarded file ownership; evaluate a graphical installer/signing plan separately before expanding dependencies.
5. **Release evidence.** Review representative DOCX PDFs and installed-font fidelity, actual Explorer context-menu/shortcut/fallback flows, favorites/rename/tray behavior, accessibility and longer-duration CPU/memory results. Continue using beta-readiness.md as the release checklist.

## CI and acceptance

The preparation extends the existing build workflow to codex branches. Real-renderer acceptance is also gated on pull requests and main/Phase 7/Phase 8 stabilization pushes, with the existing opt-in manual dispatch retained. Action SHAs, read-only permissions, credential isolation, pinned signed CI engine setup and explicit short-lived artifact paths remain unchanged. There is no release publishing job.

For each implementation slice: review the diff, build Release, run all tests with real LibreOffice where available, exercise the affected native UI paths, and run the fresh-process desktop diagnostics. Packaging/deployment and extracted-package checks remain required for candidate distribution changes. Require both relevant hosted jobs to pass for the exact commit before integrating code into main. Do not skip tests, silently accept empty filtered runs, increase deadlines to hide a failure, or force-push.

Windows 10 remains explicitly unverified by the owner's direction. Windows 11 local results and Windows Server 2025 CI do not certify it. A supported Windows 10 test machine/VM, broad hardware/DPI and assistive-technology checks, longer sessions, arbitrary Word layout fidelity, graphical setup, signing and public-release approval remain outstanding. This preparation does not mark those gates complete.
