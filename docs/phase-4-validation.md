# Phase 4 validation — 2026-10-23

## Environment and scope

Windows 11 x64 build 26200, .NET SDK 10.0.402 / Desktop Runtime 10.0.13, Release portable x64 package. Validation runs from a new extraction, not the build directory. Windows 11 still reports NT version `10.0` through OS APIs; the build number identifies this host.

The user explicitly approved proceeding with **Windows 10 unverified**. No Windows 10 machine or VM was available. Runtime probe success on Windows 11 is not evidence of runtime success on Windows 10.

Microsoft's current [.NET Windows support table](https://learn.microsoft.com/en-us/dotnet/core/install/windows) limits Windows 10 support to listed LTSC/Enterprise releases (21H2, 1809, 1607 as applicable), rather than every Home/Pro installation. See the [.NET 10 OS policy](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md). FilePilot has no separate compatibility certification for those Windows 10 editions. Its `GetDpiForWindow` API has a [Windows 10 version 1607 minimum](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow). Existing manifest declarations include per-monitor DPI awareness and run-as-invoker; no Windows 11-only menu/visual API is required.

| Check | Windows 11 build 26200 x64 | Windows 10 |
| --- | --- | --- |
| Release build and automated file/conversion/registry/WPF tests | Passed | Not run |
| Fresh portable ZIP validation and extraction | Passed | Not run |
| First WPF content rendering, popup hide/reopen cycles, light/dark resources | Passed (runtime probe + WPF tests) | Not run |
| Resident hotkey registration and disposal | Passed in synthetic widget process | Not run |
| Packaged native PNG → JPEG/WebP decoding/encoding, dimensions, original preservation, collision refusal | Passed | Not run |
| Live Explorer/tab/picker behavior on every OS configuration | Broader manual acceptance remains | Not run |
| Other CPUs, ARM64/x86, multiple monitors/DPI, high contrast and screen readers | Broader manual acceptance remains | Not run |

## Repeatable commands

Close other FilePilot instances before diagnostics so the default shortcut can be registered without conflict. Use Windows, the .NET 10 Desktop Runtime, and PowerShell 7.2+:

```powershell
# Builds, runs tests, publishes and validates a NEW portable ZIP:
pwsh -NoProfile -File scripts/Build-Package.ps1 -OutputDirectory artifacts/packages/validation-build
# Validates and extracts it into another new artifacts folder, then runs both modes:
pwsh -NoProfile -File scripts/Test-ExtractedPackage.ps1 -Path artifacts/packages/validation-build/FilePilot-win-x64.zip -Runs 3
# Or measure an already built/extracted executable:
pwsh -NoProfile -File scripts/Measure-FilePilot.ps1 -ExecutablePath artifacts/packages/validation-build/publish/FilePilot.exe -Runs 3
```

Reports and synthetic codec fixtures stay under ignored `artifacts/`. Existing report paths/output packages are never replaced. Diagnostic mode is opt-in (`--diagnostics <new absolute report.json> popup|widget`), uses default preferences and disables input to its synthetic window. It does not load/save the user's settings, use the ordinary resident mutex, inspect Explorer selections, copy to the clipboard, or register an Explorer menu. It temporarily registers Ctrl+Alt+F in widget mode and disposes that registration/tray icon on exit. A conflict is an explicit failed check, not a silently omitted test.

The runner waits for real WPF ContentRendered, allows two seconds of settling, samples a three-second hidden interval, opens/hides the popup 20 times through the dispatcher, waits two more seconds, and records memory again. The codec workload runs after idle sampling. It does not force collection, trim the working set, flush OS caches, or make network calls. A 30-second process timeout fails the command and terminates only the launched diagnostic process; reports/fixtures are retained for investigation.

## Measurement interpretation

Startup is process creation to first WPF ContentRendered, including runtime startup and the selected mode's initialization. It is an observed wall-clock measurement, not a guaranteed cold boot or a measure of clicking a hotkey on an already running widget. Repeat runs benefit from OS/runtime caches. Disabling synthetic input and recording metrics also add small probe overhead; normal launches do not run the probe.

Private bytes are process-private committed memory. Working set includes resident/shared runtime/code pages, so the two figures are not interchangeable. Managed heap size is also reported. Idle CPU is CPU time consumed during the sample interval, not a claim of zero CPU forever. A finite 20-cycle memory check cannot prove absence of all leaks.

| Mode (3 runs each) | Median first frame | Maximum first frame | Median hidden private bytes | Median hidden working set | Maximum private growth after 20 cycles |
| --- | --- | --- | --- | --- | --- |
| Popup | 1,223 ms | 1,443 ms | 67.0 MiB | 113.2 MiB | 4.5 MiB |
| Widget | 895 ms | 1,003 ms | 70.4 MiB | 128.4 MiB | 1.3 MiB |

These are machine-specific final observed baselines, not performance guarantees. Default smoke budgets are deliberately conservative: each startup at most 5,000 ms, each hidden private sample at most 200 MiB, and growth after 20 cycles at most 32 MiB. The measurement command accepts explicit budget overrides, writes the complete summary before failing a budget, and exits with an error on missing/failed diagnostics. Maximums, not just medians, are checked.

Final verification: Release build completed with zero warnings/errors; all **125 application tests** and **22 packaging/script checks** passed. Six extracted-package diagnostic sessions (three per mode) passed their startup/private-memory/cycle-growth budgets and native codec checks. Median idle CPU time over each three-second sample was 78.1 ms (popup) and 46.9 ms (widget).

## Issues fixed and remaining limits

A second one-shot launch previously kept the named resident marker alive after the original widget exited. It now immediately releases the duplicate handle. A regression test confirms a new resident can start after its owner exits.

Automated diagnostic tests cover argument validation, no-overwrite report output, cleanup after serialization failure, process-memory snapshots, native API exports and resident-marker restart. Script syntax is now checked by the packaging suite before builds.

Windows 10 runtime/Explorer acceptance, extended idle/stress runs, every codec workload's peak memory, disk-full/unplug/power-loss cases, broad accessibility testing, and production signing/installer verification remain limitations. Do not label the package "Windows 10 tested" based on this report. Ordinary application execution still needs no administrator privileges, profiling service, cloud account or telemetry.
