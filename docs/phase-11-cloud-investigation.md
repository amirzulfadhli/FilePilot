# Phase 11 cloud-only investigation

This follow-up uses GitHub repository APIs and disposable GitHub-hosted Windows runners only. It does not read or execute the laptop checkout, control its desktop, change its registry, or use a self-hosted runner. Existing failure evidence in [the acceptance record](phase-11-acceptance-results.md) remains valid until a justified fix passes the matching checks.

## Initial diagnostics

The opt-in memory sampler now records process/GUI handles, allocated bytes, generation counts and last-GC heap/commit information without forcing collection. Explicit workloads record checkpoints every ten popup cycles and after each closed Settings window, plus separate post-popup/post-Settings snapshots. The final post-cycle sample, startup/idle intervals, cycle counts, and 5-second / 200-MiB / 32-MiB performance limits are unchanged. Normal use does not instantiate this runner.

The wrapper's optional Mode selects one mode for parallel, isolated hosted jobs; its default still runs both. Each hosted mode requests 1,800 seconds idle, 180 samples, 100 popup and ten Settings cycles. Both jobs must be reviewed; fail-fast is disabled so a failure in one preserves the other's evidence. New 45-minute job deadlines accommodate this explicit half-hour workload; existing build/test deadlines are unchanged.

A separate hosted-only script registers the actual published app under HKCU, then calls the native Shell IShellFolder/IContextMenu APIs for all five supported extensions and an unsupported TXT control. An owned all-file control verb distinguishes association-specific failures from a broader menu-enumeration failure. It never invokes any menu command, changes defaults, modifies HKLM, or restarts Explorer. It refuses pre-existing product/control entries and unregisters its owned entries in finally. Its guard refuses non-hosted execution before any mutation.

Native menu enumeration on hosted Windows Server is an automated integration check, not manual Windows 10/11 Explorer acceptance. Unicode/spaced executable and selected-file paths are included. A fresh-session Windows 11 classic-menu launch, Windows 10, physical monitors and assistive-technology testing remain unverified unless independently performed.

## Hosted evidence

[Run 40204028608](https://github.com/amirzulfadhli/FilePilot/actions/runs/40204028608), source 7fb76efeff522cfef45955b5e927bd00fdd45355, passed all five jobs on GitHub-hosted Windows Server 2025 (kernel build 26100). Release build had zero warnings/errors; the build report had 187 passes plus the three expected real-engine skips, and the real-engine report had all 190 tests passing without skips. Each full job passed 15 fresh-process desktop checks. Package/installer/lifecycle and independent PDF checks passed. No manual Windows test was performed in this follow-up.

Both 30-second controls and both 1,800-second workloads passed the same limits, with 100 popup and ten Settings cycles per run:

| Mode | Short control first frame / post-cycle growth | Long first frame / hidden private / post-cycle growth |
| --- | --- | --- |
| Popup | 3,645 ms / 25.80 MiB | 715 ms / 32.43 MiB / 27.62 MiB |
| Resident widget | 3,863 ms / 26.03 MiB | 799 ms / 34.27 MiB / 28.29 MiB |

Long-run checkpoints show one application window and zero owned windows after each closed Settings window. Resident GDI count stayed at 19 through popup cycles, rose to 23 at the first Settings window, then stayed at 23. USER count changed from 30 to 32 at the first popup and stayed at 32. Process handles were 502 through popup cycles, 503 for most Settings checkpoints, and 498 at the last Settings checkpoint. These samples do not establish an increasing window/GUI-handle leak on this runner.

Private memory still grew during both phases: resident checkpoint values were 34.309 MiB before popups, 49.000 MiB after popup 100 and 66.289 MiB just after Settings 10. The unchanged final measurement two seconds later passed at 28.29 MiB growth. A natural generation-0 collection occurred at the last Settings checkpoint; the last-GC committed-heap counter then reported 20.371 MiB. No collection was requested by the test. Those observations do not identify the allocation owner or explain the earlier Windows 11 46.61-MiB failure.

The native Shell probe found FilePilot for PNG/JPG/JPEG/WebP/DOCX, omitted it for TXT, and found the independent all-file control in all six single-file menus. This validates the existing per-user SystemFileAssociations method in that environment; no registration-location rewrite is justified by this result. It does not prove an actual Windows 11 Explorer click/launch or resolution of the earlier missing-menu case.

[Original-source controls](https://github.com/amirzulfadhli/FilePilot/actions/runs/40205331820) are retained separately on codex/phase-11-cloud-baseline (0845705a41e80bb579a1e152bae423cc83c72a43). Their jobs explicitly check out and publish 842847154b0357e0e6abae5220f6bb593b09fe32 without changing that checkout, then measure its original binary using the compatible wrapper and unchanged limits. This distinguishes a cloud environment difference from an instrumentation effect; review those independent results alongside the instrumented runs.

## Stronger menu regressions

The follow-up probe warms association lookups before registration, then checks the same fixtures afterward. Its owned all-file control is created before the product registration so the product notification covers both changes. It also checks that the Single-selection commands are absent for a two-file selection. Finally it verifies all temporary keys are absent and neither command appears in a newly constructed menu after cleanup. The report stays Passed=false until both menu and cleanup assertions succeed. These remain automated native Shell checks on a disposable hosted VM, not manual Explorer acceptance.

## Status and required remaining tests

No verified product root-cause fix is claimed. Passing on Windows Server does not close an earlier failure on Windows 11 build 26200 at 125 percent scaling. The original failures remain recorded and Phase 11 remains unmerged. Required before closing them:

- On an independently available Windows 11 test VM, reproduce the actual classic-menu installation and launch in both a fresh session and a session whose associations were already used. Check spaced/Unicode paths, single/multiple selection and unregistration, without changing defaults or restarting an existing user's Explorer automatically.
- Reproduce the 1,800-second resident workload at 125-percent DPI in a Windows 11 desktop with an actual notification area. Capture the new phase counters; include rendering/heap profiling if the 32-MiB growth gate fails. Compare the original binary and instrumented binary under matching conditions, including relevant assistive-technology clients.
- Run the same bounded checks after a justified product fix. A passing short control, fresh-server result or instrumentation change is not proof that the Windows 11 failure is fixed.

Windows 10, physical monitor transitions, assistive-technology acceptance and broader real-document fidelity remain separate unavailable coverage. This follow-up has not accessed the laptop. Neither blocker is declared resolved; Phase 12, main integration and public release remain gated.

## Independent controls and remaining failures

The original-source [control run 40205331820](https://github.com/amirzulfadhli/FilePilot/actions/runs/40205331820) completed both 1,800-second workloads. Post-cycle private growth passed at 28.59 MiB for popup and 27.80 MiB for resident mode. The overall control run nevertheless **failed**: the original popup's first frame was 5,409.22 ms, above the unchanged 5,000-ms startup limit. Its resident first frame was 4,166.91 ms. The failure is retained, not rerun or relabeled as passed. Its cause has not been established. The original source passing the memory limits on this server also prevents attributing the hosted passes to a product fix or the new counters.

The Phase 11 [repeat run 40207043728](https://github.com/amirzulfadhli/FilePilot/actions/runs/40207043728), source f4e75c7a6138bceef8f5dd5171baffdaecbdfe14, passed all five jobs, including the warmed/single-selection/cleanup native menu checks and both long workloads.

A separate [automated Explorer probe run 40209337576](https://github.com/amirzulfadhli/FilePilot/actions/runs/40209337576) opened and selected its fixture but **failed before product registration**, while opening the pre-registration classic menu. It timed out waiting for a context menu and verified cleanup. This is an unverified interaction check, not evidence that FilePilot's registered entry failed or that Explorer launch passed. Follow-up probe diagnostics record the input desktop/window station and bounded owned-window UI observations; no unlock, security-setting change or substitution of native menu enumeration for the actual Explorer test is allowed.

Additional Windows 11 ARM-host/x64-emulation controls are retained on [codex/phase-11-windows11-cloud](https://github.com/amirzulfadhli/FilePilot/tree/codex/phase-11-windows11-cloud). Their actual outcomes and limitations must be reviewed separately. Native x64 Windows 11 at 125-percent DPI, Windows 10 and the original client-specific failure conditions remain unverified. Main integration and Phase 12 remain blocked.

## Final cloud findings (2026-10-26, Malaysia)

The primary Phase 11 branch remains at f4e75c7a6138bceef8f5dd5171baffdaecbdfe14. Its [complete repeat run](https://github.com/amirzulfadhli/FilePilot/actions/runs/40207043728) passed all five jobs. Long resident/popup growth was 28.67/27.23 MiB respectively. Release tests, real LibreOffice acceptance, 15 fresh-process WPF repetitions per full test job, deployment checks and the warmed native Shell checks passed. The full real-engine suite contains 190 tests. This evidence does not reproduce or fix the earlier native Windows 11 failure.

The separate [Windows 11 ARM-host/x64-emulation run](https://github.com/amirzulfadhli/FilePilot/actions/runs/40207973454) used Windows 11 Enterprise build 26200 and a test-only self-contained win-x64 publish. It is not native x64 acceptance or a new supported ARM distribution. Both the instrumented and unchanged-original resident binaries stayed below the 32-MiB growth limit (31.38 and 29.75 MiB), but **both jobs failed the unchanged 5,000-ms startup gate**, at 6,401.32 and 5,619.10 ms. The native Shell job passed supported/unsupported extensions, a warmed association cache, single/multiple selection and removal after cleanup. No startup failure is dismissed as contention without evidence. No budget was relaxed or forced collection introduced.

Actual Explorer interaction is still unverified. The [final mouse-based probe](https://github.com/amirzulfadhli/FilePilot/actions/runs/40211511699), source 4c388430759ddf2368d164163cccfcb38a626cb4, opened a uniquely owned Explorer window, selected the fixture and injected a right-click. It timed out on the **pre-registration** context menu, before registering FilePilot. Its read-only diagnostics found WinSta0/Default for the current and input desktops, UserInteractive=true, session 2 and the expected 40-byte INPUT structure. Therefore neither a locked desktop nor a missing FilePilot entry has been established as the cause. The report retained MenuSeen=false, SelectedPathMatched=false, ClipboardMatched=false, OriginalPreserved=true, CleanupVerified=true and Passed=false. The full build/deployment and real LibreOffice jobs in this run passed; the Explorer job failed. Earlier keyboard-based probe timeouts and the corrected probe compile error remain visible in run history.

No verified product fix is claimed. The runtime registration and widget lifecycle remain unchanged; instrumentation, hosted integration regressions and this evidence record are the completed changes. The original native Windows 11 125-percent-DPI memory and classic-menu failures remain open. The isolated diagnostic branches retain successful and failed controls for review; they must not be described as an all-green release candidate.

### Release gates still required

Use an independent Windows test VM or tester, not the user's laptop without new approval:

1. Native x64 Windows 11, 125-percent DPI, actual notification area: compare the unchanged and instrumented binaries under the same 1,800-second resident workload with 100 popup and ten Settings cycles. Require growth <=32 MiB and first frame <=5,000 ms; if growth fails, capture allocation/rendering ownership with the new phase counters. Retain failures rather than substituting short or server controls.
2. Actual Explorer classic-menu installation, selection, launch and unregistration in both fresh and warmed sessions, including spaced/Unicode paths and multiple-selection behavior. Check the visible FilePilot command and exact selected argument. Native IContextMenu enumeration alone is insufficient.
3. Diagnose the retained cold-start failures on original Server and x64-emulated Windows 11 controls before treating those environments as performance-verified.
4. Windows 10, physical multi-monitor/DPI transitions, assistive-technology acceptance and broader real-document fidelity remain unavailable and explicitly unverified.

All work in this follow-up used remote GitHub APIs, GitHub-hosted VMs and cloud logs. No laptop filesystem, process, registry or desktop was accessed. Main remains at 84913cd462b7b91330b8768b6bd12b3b5392af66; no main integration, Phase 12 work or GitHub Release is authorized by these results. Both blockers must be verified resolved before Phase 11 can merge.
