# Phase 6 — initial usability and installation slice

Status: incremental implementation on feature/phase-6-usability-installation, based on the verified and pushed Phase 5 commit 51741ee. This starts Phase 6; it is not a finished signed Windows installer or a completed accessibility/hardware certification.

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
