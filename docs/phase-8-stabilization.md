# Phase 8 — widget stabilization

FilePilot remains an unreleased 0.1.0-beta.1 candidate. These changes reuse the WPF application and existing file services; no runtime dependency, polling loop, service or network feature was added. Public release publication requires owner approval.

## Widget and accessibility

- Opening or restoring the popup focuses Copy Path for a valid selection, or Choose files for the fallback. Busy operations retain the existing protections. Escape still closes an open format/favorites dropdown before dismissing the popup. The close control identifies itself as Hide in resident mode and Close in a one-shot window.
- Windows high contrast takes precedence over the normal light/dark preference without changing it. A static Windows property-change event updates the palette; the handler is removed on actual close, retained when the resident window hides. System-color expressions follow contrast palette color changes. The theme toggle is disabled while high contrast is active; Settings can still choose the preference used afterwards.
- Selected/highlighted items and hovered buttons use paired background/text resources. Disabled text uses a dedicated readable brush rather than reducing the entire control's opacity. Keyboard focus borders remain visible.
- A regression check initially reproduced black generated text inside a correctly white-foreground selected item. The global TextBlock foreground setter overrode inherited template text color. Removing that setter lets text inherit the containing control's foreground. A subsequent visual check found the native Expander header default was black in dark mode; a targeted Expander foreground style fixes it without overriding item selection colors. Regression checks inspect the actual generated text in both cases, not just parent brushes; the complete real-engine suite passed afterwards.
- The status text raises a polite UI Automation live-region change after its text binding updates, only while visible and when a listener exists. This implements notification delivery; an automated peer check does not certify a screen reader's spoken output.
- The shortcut hint sits above the bottom actions so it does not compete for their width. Popup and Settings use layout rounding. Monitor work-area limits round down to whole DIPs to avoid a fractional-DPI edge spilling outside the work area. Existing per-monitor-v2 awareness, cursor placement and bound window height are preserved.

The implementation follows WPF's [dynamic system resource guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/systems/xaml-resources-how-to-use-system) and [live-region event pattern](https://devblogs.microsoft.com/dotnet/net-framework-4-7-1-accessibility-and-wpf-improvements/).

## Performance and reproduction

Each palette is loaded at most once per main window, and unchanged palettes are left in place. Normal launches do not profile, force garbage collection or trim working sets. The opt-in measurement script keeps the existing budgets: first frame at most 5 seconds, hidden private memory at most 200 MiB, and post-cycle private growth at most 32 MiB.

```powershell
./scripts/Measure-FilePilot.ps1 -ExecutablePath <absolute-path-to-FilePilot.exe> -Runs 3
./scripts/Measure-FilePilot.ps1 -ExecutablePath <absolute-path-to-FilePilot.exe> -Runs 1 -Extended
./scripts/Test-WpfReliability.ps1 -TestResultsDirectory artifacts/phase8-repeat -IncludeMemoryDiagnostics
```

Use new output directories. Default diagnostics use 20 popup cycles and a 3-second hidden CPU interval; Extended uses 100 popup cycles, 10 synthetic Settings open/close cycles and a 30-second hidden interval. Synthetic windows disable user actions, never save preferences and use only their own diagnostic files. The extended process deadline is 90 seconds rather than the default diagnostic deadline of 30 seconds; the WPF acceptance test still has its original 20-second deadline. Startup/private-memory/growth budgets are unchanged in both modes. A 30-second sample is not long-duration leak or cold-cache proof. Reports include the workload and assembly/executable hashes.

## Verification record — Windows 11, 2026-10-24

Release builds passed with zero warnings/errors. All 169 tests passed with real LibreOffice enabled, then passed again in the package pipeline without skips. The package pipeline passed all 24 packaging/script checks; the resulting dirty development package passed 8 installer and 21 packaged activation/upgrade/rollback/uninstall checks. Its manifest identifies the preparation base plus SourceDirty=true and must not be represented as a clean release candidate.

Independent inspection of the real-engine synthetic PDF passed pagination, A4 sizing, expected text/Unicode/image and sensitive-property checks. Actionlint accepted the workflow. The single pumped WPF test exercises actual rendering and dispatcher shutdown, plus high-contrast resources, selected/disabled template colors, 100 cached palette-switch cycles, primary focus and resident hide/reopen. Work-area calculations and constrained footer layouts cover 100/125/150/200 percent scaling as simulations, not physical monitor switching.

All 15 separate-process reliability checks passed with DOTNET_PROCESSOR_COUNT=2: five real WPF lifecycle tests, five cold memory samplers and five bounded allocation-churn tests. This constrains the .NET scheduler, not physical CPU affinity. The WPF deadline stayed at 20 seconds; there was no failure retry or test skip. NuGet auditing including transitive dependencies reported no known vulnerable packages on this date.

Extended measurements on Windows 11 build 26200 / .NET 10.0.13 passed in both modes:

| Mode | First frame | Hidden private | Growth after 100 popup + 10 Settings cycles | CPU over 30 seconds hidden |
| --- | ---: | ---: | ---: | ---: |
| Popup | 1,082 ms | 56.6 MiB | 20.4 MiB | 93.8 ms |
| Widget | 1,129 ms | 60.0 MiB | 21.0 MiB | 62.5 ms |

The development package's managed-assembly SHA-256 was 23ACF78D659D45B1680D1BB896021DC2455D81038F797BC009146303806190D6. Measurements used no forced GC and do not establish long-duration stability or a cold-cache startup guarantee.

Direct Windows 11 checks opened the first development package (before the inherited-text correction) with a disposable spaced Unicode filename: the initial Copy Path focus ring was visible and Tab moved to Move File; light/dark palettes rendered, the body scrolled with status and bottom actions fixed, Settings focused/selected the shortcut and exposed its Save/Cancel/version footer, and Escape cancelled Settings. Quick rename preserved the .txt extension and SHA-256, updated the selected path and showed success feedback. Ctrl+O opened the native picker; Escape cancelled it without changing selection. A pointer-based picker Cancel attempt was rejected by the computer-use helper's owner/child target check; keyboard cancellation then succeeded. Escape finally closed the one-shot process. No preferences were saved. These checks do not certify screen-reader speech or all picker flows.

Final default extracted-package sessions also passed: popup 1,008 ms / 67.5 MiB hidden private / 2.0 MiB growth, widget 1,162 ms / 66.3 MiB / 5.2 MiB. A test-owned popup was placed at the bottom-right of the available physical monitor at 120 DPI (125 percent): native bounds 1320,220..1920,1020 stayed within work area 0,0..1920,1020. This host exposes one monitor; no multiple-monitor validation is claimed. The WPF test now repeats native boundary checks for every monitor available on its host, in addition to simulated layouts down to 320 DIP width.

Hosted CI now also runs extracted-package startup/private-memory/growth/native-codec smoke acceptance. Both relevant jobs must pass for the exact commit before main integration; no failed gate is overridden by local passes. Exact integration hashes/run URLs belong in the subsequent phase record once CI has actually completed.

## Remaining release gates

Windows 10 remains explicitly unverified by owner direction. This Windows 11 host and Windows Server 2025 CI cannot certify it. Multiple physical monitors and other scaling factors, OS high-contrast switching while running, screen-reader announcements, magnifier/text-size combinations, broad hardware and hours-long sessions remain unverified unless separately recorded. Minimum-size safeguards cannot make an arbitrarily tiny monitor work area usable.

Broader Explorer context-menu/favorites/tray acceptance and rename edge cases through the UI, arbitrary Word font/layout fidelity, real cross-volume/removable/disk-full/power-loss scenarios, ARM64/x86, graphical setup, Apps & Features integration and signing remain beta limitations. The earlier historical STA timeout and terse memory-counter anomaly were not reproduced to establish their exact original causes; the corrected dispatcher lifecycle and strict counter/checkpoint gates remain in place. See beta-readiness.md and phase-7-sta-stabilization.md. No public release or support certification is implied.

After both text-color corrections, the final reviewed package passed the full 169-test real-engine/package pipeline, five additional fresh-process WPF repetitions, default and extended runtime budgets, and another direct light/dark check confirming readable rename/favorite headers. The earlier installer/lifecycle checks used the same deployment scripts and package schema; exact final clean-package deployment checks also remain mandatory in CI. CI retains only the explicit synthetic performance JSON reports alongside its test reports and candidate archive.
