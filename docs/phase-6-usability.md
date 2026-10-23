# Phase 6 — initial usability and installation slice

Status: Phase 6 MVP implementation on feature/phase-6-usability-installation. Managed installation, side-by-side upgrades/rollback, guarded uninstall and available Windows 11 usability/performance checks are implemented. Signing, graphical setup, Windows 10 and broad accessibility/hardware certification remain release limitations.

## Widget and allocation changes

Settings now keeps Save, Cancel and validation feedback in a fixed footer while its content scrolls. Enter saves; Cancel/Escape dismiss without saving. Validation messages have a polite accessibility live setting. The document panel exposes Alt+A for cancellation and Alt+O for opening the PDF folder, matching image actions. Existing themes, shortcut handling, image batches, file actions and local document engine remain in place.

DOCX preflight reuses one 64 KiB read buffer across sequential ZIP parts. Previously it allocated another buffer for each part (up to 2,048 parts). No decoding/resource limits were increased. This avoids repeated allocations; it is not a claim of measured startup improvement. No timers, polling, background services or runtime dependencies were added.

## Per-user installation preview

From a trusted source checkout with PowerShell 7.2+ on Windows x64:

```powershell
# Build-Package first produces a validated ZIP (see README).
pwsh -NoProfile -File scripts/Install-FilePilot.ps1 -PackagePath artifacts/packages/my-build/FilePilot-win-x64.zip
# Optional: a NEW absolute local installation directory:
pwsh -NoProfile -File scripts/Install-FilePilot.ps1 -PackagePath artifacts/packages/my-build/FilePilot-win-x64.zip -InstallDirectory 'C:\Users\YourName\AppData\Local\Programs\FilePilot\my-version'
```

The default is a fresh timestamp/id directory under LOCALAPPDATA/Programs/FilePilot. The script requires the matching Test-Package.ps1 beside it; it is a source-checkout deployment tool, not a setup executable bundled in the ZIP. Keep the whole installed directory, including runtime/native libraries, notices, README and docs. The framework-dependent package needs the .NET 10 Windows Desktop Runtime on the selected dotnet host. LibreOffice remains separate and optional.

The installer holds a read-sharing-only ZIP handle across manifest/hash/path validation and extraction, checks runtime prerequisites, refuses existing destinations, traversal, device/network/ADS/reserved/reparse/offline paths, and writes a new receipt with package SHA-256 and source revision/dirty state. It never overwrites an older install or deletes files. Failed partial installs remain for inspection. These path checks are not a defense against malicious concurrent filesystem swaps.

No administrator request, HKLM writes, automatic Explorer registration/restart, autostart, shortcuts, services, upgrades, uninstaller registration or application launch occurs. Run the returned executable explicitly. Register/unregister Explorer integration with the existing app commands only when desired. To update, install into another new directory, exit the old widget, switch your launcher, and explicitly re-register the new path if you use the Explorer verb. Retain old installs until verified. To remove an install, exit it and unregister its owned verb first, then remove only that installation directory yourself; preferences remain under LOCALAPPDATA/FilePilot. Automated uninstall/rollback, stable shortcuts, signing and installer UI remain future Phase 6 work.

Hashes detect mismatches, not publisher identity. Use trusted source/package downloads; the utility and generated ZIP are not Authenticode-signed.

## Verification on Windows 11 build 26200

Release build: zero warnings/errors. All 161 tests passed, zero skipped, with real LibreOffice enabled after the buffer change; real PDF structural/text/Unicode/image/metadata checks passed again. The WPF test verifies the fixed footer and default/cancel buttons along with both themes and earlier popup/document state tests. All 22 packaging/script checks passed. Eight installation checks passed: extraction with every payload hash verified, receipt hash, existing-install/sentinel preservation, relative/traversal/network/ADS/reserved-name rejection and corrupt-ZIP rejection before creating a target.

The installed app in a Unicode/spaced disposable directory passed six startup/memory/native-codec diagnostic sessions (three per mode). This exercised actual executable deployment rather than only ZIP extraction. On this .NET 10.0.13 host:

| Mode | Median first frame | Max first frame | Median hidden private | Median working set | Max growth after 20 popup cycles |
|---|---:|---:|---:|---:|---:|
| Popup | 1,634 ms | 2,541 ms | 64.2 MiB | 117.9 MiB | 4.2 MiB |
| Widget | 1,333 ms | 1,412 ms | 67.1 MiB | 129.9 MiB | 4.5 MiB |

All samples met the existing 5,000 ms / 200 MiB hidden private / 32 MiB cycle-growth smoke budgets. These runs shared the development host with builds/tests and benefit from OS/runtime caches; they are neither guaranteed cold starts nor proof of a speedup over Phase 4 or long-term leak freedom. The native LibreOffice process's peak memory is outside these idle widget measurements. The measured package recorded SourceRevision=51741ee and SourceDirty=true because Phase 6 was not yet committed; reports/ZIPs are ignored local artifacts, not published releases.

Reproduce installation checks with Test-InstallTools.ps1 -PackagePath <zip>; it keeps disposable fixtures under artifacts. Then run Measure-FilePilot.ps1 -ExecutablePath <installed-exe> -Runs 3. This does not test missing-runtime hosts, actual default-folder installation, shortcuts or uninstall behavior.

Remaining: Windows 10 runtime acceptance (explicitly unverified), broad monitor/DPI/hardware/keyboard/screen-reader/high-contrast checks, signed installer UX and upgrades/uninstall, long-running memory/engine resource characterization, and representative complex DOCX fidelity. Full manual coverage is not inferred from WPF loading tests.

New Phase 6 interactive Settings acceptance remains unverified: desktop automation could read the popup controls but could not activate/capture the new test window reliably (the capture showed the desktop). The completed Phase 5 interactive checks remain valid; fixed-footer layout and default/cancel button configuration in Phase 6 are covered by the WPF test. The idle test process was stopped and no saved preferences were changed during this attempt.

## Managed deployment completion (23 October 2026)

New ZIPs include their deployment tools under deployment/, so setup no longer requires a source checkout. Use PowerShell 7.2+ and a .NET 10 x64 Windows Desktop Runtime; no elevation is requested. The scripts do not download prerequisites, start FilePilot automatically or add autostart/services. They are a lightweight CLI installer, not an MSI/MSIX or graphical setup program. No Apps & Features registration or code signing is claimed.

```powershell
# From a trusted, unpacked new package; paths here are examples.
$versionA = & .\deployment\Install-FilePilot.ps1 -PackagePath 'C:\Downloads\FilePilot-win-x64.zip'
$manager = Join-Path $versionA.Directory 'deployment/Manage-FilePilot.ps1'
& $manager -Action Activate -InstallDirectory $versionA.Directory
# Add -RegisterContextMenu to opt into the classic per-user Explorer menu.

# Upgrade: install the next package in a new directory, then activate it.
# Exit the resident widget using its notification icon before switching versions.
$versionB = & .\deployment\Install-FilePilot.ps1 -PackagePath 'C:\Downloads\next-FilePilot-win-x64.zip'
$managerB = Join-Path $versionB.Directory 'deployment/Manage-FilePilot.ps1'
& $managerB -Action Activate -InstallDirectory $versionB.Directory
& $managerB -Action Rollback  # switches back to retained A; B stays on disk

# Inspect removal first, then remove one selected installation.
& $managerB -Action Uninstall -InstallDirectory $versionA.Directory -WhatIf
& $managerB -Action Uninstall -InstallDirectory $versionA.Directory
```

Activation writes a per-user FilePilot.lnk in the Start Menu and owned state under LOCALAPPDATA/FilePilot/Deployment. It never edits an unrelated or retargeted shortcut. Native IShellLinkW/IPersistFile APIs preserve Unicode shortcut paths; real testing found WScript.Shell corrupted those paths on this host. Upgrades retain the previous directory and settings; rollback changes the shortcut and any managed Explorer registration, never restores/deletes user files. Use the same StateDirectory/ShortcutPath overrides for all operations if testing/customizing those paths; keep them outside version directories. No version is forcibly stopped. A failed state commit restores the previous shortcut; version files stay available. A process crash or exceptional registry/rollback failure may require inspecting state/shortcut/menu before retrying; this is not an OS transaction.

Uninstall validates the receipt, manifest, every present payload hash and the complete directory inventory. It refuses unexpected files/folders, modified payloads, read-only/locked files, reparse/offline paths and running FilePilot instances. It preflights writable handles and rechecks bytes through those handles, unregisters only Explorer entries pointing to that executable, and deletes only its enumerated payload files and empty directories. No recursive removal is used. Preferences, sources, outputs and other installations remain untouched. If deletion is interrupted, an owned Removing receipt permits retrying missing payloads while still refusing modified or unexpected files. Retry using management tools from another retained/unpacked package if the interrupted version's tools were already removed. Exceptional failures removing the final control files may need manual cleanup of the reported directory. Legacy preview packages without deployment tools are not accepted for managed activation/removal; retain them and migrate to a new package first.

Explorer integration now includes DOCX alongside the four image extensions. Quiet registration commands return an exit status for deployment scripts, never restart Explorer and write only HKCU. Removing an old managed version cannot unregister a menu pointing to a newer executable. Registration remains opt-in; Windows 11 uses Show more options and single-file context-menu selection. Native primary-menu and multi-file shell extensions remain deferred.

Widget stability changes: reopening a minimized widget restores its normal window; Settings positions within the monitor work area, supports a smaller viewport with scrolling and wraps shortcut guidance. The modal dialog's shortcut field starts focused. An interactive Windows 11 check confirmed the fixed Save/Cancel footer, focus, Escape cancellation, popup recovery and light/dark rendering. No preferences were saved by this check. Windows 10 and broad multi-monitor/high-contrast/screen-reader tests remain explicitly unverified.

A packaging attempt stopped on an intermittent image-test fixture deletion lock after successful pixel assertions. Test-side image/codec inspection now uses byte buffers, and existing conversion checks explicitly verify exclusive access to both source and completed output before decoding. The affected 45-case suite passed three additional consecutive runs after the change. No product lock/overwrite protections were weakened. Final full-suite and packaged lifecycle results are recorded below.

Final Phase 6 validation: Release build passed with zero warnings/errors; all 162 application tests passed with real LibreOffice enabled (zero skipped). All 22 packaging checks, 8 installer checks and 21 lifecycle/fault checks passed using the final packaged tools, including running the uninstaller from the directory it removes. Real PDF pagination/text/Unicode/image/source-property inspection passed again. Six installed-app diagnostic sessions met all smoke budgets. Final popup/widget median first frames were 1,804/2,041 ms, max 1,900/2,177 ms, median hidden private memory 66.5/66.1 MiB and max 20-cycle private growth 2.9/5.4 MiB. The shared development host was busy; no performance improvement or long-duration leak-free claim is inferred. Final local ZIP hash: A75B56A21F0B5ECF1AC7D451ED3DE299E987F639FE7001AE90564EF6A061FEBC (source d7aa4dc, SourceDirty=true). Local test archives/reports remain ignored; no release was published.

Unsigned deployment scripts remain subject to local execution policy; the portable executable is available when policy prevents running setup scripts. Graphical/signed setup, Apps & Features integration, Windows 10 runtime and broad hardware/accessibility certification remain outside this verified MVP. Actual Explorer right-click launch and every native-picker/favorite workflow are still beta acceptance items; isolated HKCU tests and quiet deployment commands do not imply those manual flows passed.
