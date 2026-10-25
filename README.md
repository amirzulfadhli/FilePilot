# FilePilot

A local Windows 10/11 file-action utility built with C# / .NET 10 and WPF. Phase 1 provides file details, Copy Path, and safe Move File. Phase 2 adds per-user Explorer integration and PNG/JPEG/WebP conversion. Phase 3 provides a borderless floating popup, configurable global shortcut, batch conversion, quick rename, and favorite folders. No background service, cloud API, accounts, database, telemetry, or administrator privileges are required by the application.

The build version is **0.1.0-beta.1 candidate**, not an approved public release. See [beta readiness and CI](docs/beta-readiness.md) and [CHANGELOG](CHANGELOG.md). Main integration and public release require separate owner approval.

For a maintainer-provided candidate, start with the [beta setup guide](docs/beta-quick-start.md): runtime and checksum checks, portable widget use, installation preview, activation, rollback and removal. No SDK is needed to run the packaged application.

Phase 11 acceptance found two unresolved issues: the HKCU entry did not appear in the actual Windows 11 classic menu, and the requested hour-long diagnostic workload exceeded the existing resident post-cycle memory-growth budget (46.61 MiB versus 32 MiB). Right-click launch is not verified; the resident shortcut and Ctrl+O picker remain available. See [current acceptance results and merge blockers](docs/phase-11-acceptance-results.md). Phase 11 integration is held pending investigation.

## Build and run

Install the .NET 10 SDK with Windows Desktop support. From the repository directory in PowerShell:

```powershell
dotnet restore FilePilot.slnx
dotnet build FilePilot.slnx -c Release
dotnet test FilePilot.slnx -c Release

dotnet run --project src/FilePilot.App -c Release -- "C:\Files\photo with spaces.png"
```

Pass exactly one absolute local file path, including quotes around paths with spaces. Unicode names are supported. With no argument (or `--widget`), FilePilot stays in the notification area and registers its global shortcut. Passing one file opens a one-shot popup that exits when closed. Unsupported image extensions can still use Copy Path and Move File.

Create a Windows x64 distribution folder:

```powershell
dotnet publish src/FilePilot.App -c Release -r win-x64 --self-contained false -o artifacts/publish
& .\artifacts\publish\FilePilot.exe "C:\Files\photo.png"
```

Distribute the **whole folder**, including SkiaSharp's native library and the `notices` directory. This build requires the .NET 10 Desktop Runtime. Use `--self-contained true` for a larger distribution that bundles the runtime. There is no signed setup executable yet. Phase 6 adds packaged per-user deployment and version-management scripts; see [Phase 6 installation and validation](docs/phase-6-usability.md). Do not register an executable in a temporary location that will later be deleted.

## Portable packages

From Windows with the .NET 10 SDK, Git, and PowerShell 7.2 or later:

```powershell
pwsh -NoProfile -File scripts/Build-Package.ps1
# Optional: choose a NEW output directory inside artifacts:
pwsh -NoProfile -File scripts/Build-Package.ps1 -OutputDirectory artifacts/packages/my-build

pwsh -NoProfile -File scripts/Test-Package.ps1 -Path artifacts/packages/my-build/FilePilot-win-x64.zip
pwsh -NoProfile -File scripts/Test-PackageTools.ps1
```

The builder runs the packaging checks, Release build, and all application tests before publishing Windows x64. It creates a fresh directory each time, refuses existing output directories and paths outside this repository's `artifacts` folder, and leaves earlier output untouched. A failed build/validation can leave diagnostic output or a `.zip.tmp`; no old packages are deleted.

The finalized ZIP contains the runtime files, README, supporting setup/validation docs, both SkiaSharp license notices, and `package-manifest.json`. Debug symbols are excluded, including the roughly 96 MB native Skia PDB otherwise included by NuGet publishing. The manifest records each file's size/SHA-256, source commit, working-tree dirty flag, runtime, and creation time. A separate `.zip.sha256` records the archive hash. Dirty builds are labeled explicitly; a clean source checkout is recommended for distribution. ZIP timestamps mean packages are not promised to be byte-for-byte reproducible.

The validator checks file hashes/lengths, required runtime files and notices, duplicate/unlisted files, Windows-unsafe paths/reserved names, links, and accidental development/secret filenames. It rejects archives over 512 MiB, declared expanded totals over 1 GiB, more than 1,024 entries, and manifests over 2 MiB. It reads without extracting or executing the package. Hashes detect corruption; they do not authenticate a publisher or replace code signing. Validation checks package structure/integrity, not runtime compatibility or every malicious archive scenario.

Extract the ZIP to a normal local folder, keep all its contents together, and run `FilePilot.exe`. The .NET 10 Desktop Runtime is still required. Packages are currently Windows x64 only and unsigned; deployment scripts are included under deployment/. Activation can create a Start Menu shortcut and opt into the classic context menu; no automatic startup is installed. The builder's raw `publish/` folder is diagnostic output; distribute the validated ZIP.

Verification on 2026-10-23: Release build passed with zero warnings/errors; all 118 application tests and 21 packaging checks passed. An actual Windows x64 ZIP was created and validated (about 5.6 MiB compressed / 13.3 MiB expanded for the tested build, with 10 payload files plus the manifest). Tests include damaged bytes, length mismatch, missing native codecs/notices/manifest, invalid metadata/JSON, duplicate/unlisted paths, traversal/absolute/reserved names, debug/secrets exclusion, and existing/outside output protection. Testing was local; no package has been published as a GitHub release.

Phase 4 now includes opt-in startup/memory measurements and extracted-package runtime/codec acceptance. See [Phase 4 validation](docs/phase-4-validation.md) for commands, methodology, smoke budgets, measured results, and the OS test matrix. Normal launches do not profile or write reports. Windows 10 remains explicitly unverified by agreement; no Windows 10 machine/VM was available. As of 2026-10-23, Microsoft's [.NET Windows support policy](https://learn.microsoft.com/en-us/dotnet/core/install/windows) limits Windows 10 runtime support to specified LTSC/Enterprise editions, so "Windows 10/11" is not a promise for every Windows 10 installation.

```powershell
pwsh -NoProfile -File scripts/Test-ExtractedPackage.ps1 -Path artifacts/packages/my-build/FilePilot-win-x64.zip -Runs 3
pwsh -NoProfile -File scripts/Measure-FilePilot.ps1 -ExecutablePath artifacts/packages/my-build/publish/FilePilot.exe -Runs 3
```

Close other FilePilot instances for an uncontested diagnostic shortcut. Synthetic reports/fixtures stay under `artifacts/`; no existing reports are overwritten. Final measured medians were about 1,223 ms / 67.0 MiB hidden private memory for the popup and 895 ms / 70.4 MiB for the hidden widget on this Windows 11 x64 host. These are observed baselines with runtime/OS caching, not guaranteed cold-start times or hard memory caps. Working-set figures and repeat-run maximums are documented separately. The final Phase 4 build passed all 125 application tests, 22 packaging/script checks and six extracted-package diagnostic sessions.
## DOCX → PDF (Phase 5)

The first document-conversion slice is implemented on `feature/phase-5-documents`. It uses an optional local LibreOffice installation selected in Settings (`program/soffice.com`), keeping the engine outside FilePilot's lightweight package. Single DOCX files expose an Export PDF panel with destination, cancellation, collision protection and Open folder. Without a configured engine, existing file/image actions work and PDF export explains the setup requirement.

See [Phase 5 setup, safety boundaries and validation](docs/phase-5-documents.md) before using it. The initial policy refuses external links, active/embedded content and unsupported media. Real LibreOffice 26.8.1.1 conversion passed Windows 11 acceptance: two-page styled text/table/image/Unicode rendering, source preservation, no-overwrite collisions, native cancellation/exit, cleanup, timeout and malformed input. All 161 tests passed with the real engine enabled, and the rendered pages were visually inspected. Source document properties are removed from an owned copy; PDF producer and export time remain. Other fonts/layouts, engine versions and Windows 10 need separate acceptance. Three real-engine tests are explicitly skipped when FILEPILOT_LIBREOFFICE is not configured.

Ctrl+O opens the file picker. Escape closes an open format/favorite dropdown before dismissing the widget; single-file views hide empty batch details.

## Floating widget

```powershell
dotnet run --project src/FilePilot.App -c Release
# Or start the published executable without a path:
& .\artifacts\publish\FilePilot.exe --widget
```

1. Leave FilePilot running, select 1–100 regular local files in an Explorer folder, and press **Ctrl+Alt+F**.
2. The borderless popup opens near the cursor and clamps to the monitor's working area. Small work areas use scrolling. The visible popup remains available through Alt+Tab.
3. **Escape**, the × button, and Alt+F4 hide the resident popup. **Exit** or the notification icon's Exit FilePilot command stops the process and releases the shortcut. Closing a one-shot popup exits it. Active operations must finish before exiting; resident mode can hide while work continues.
4. Use **Settings** (gear) to choose a shortcut, save the theme, and add/remove up to 20 favorite folders. A shortcut needs Ctrl or Alt plus a letter, digit, or F1–F11; Shift is optional. A conflicting registration produces an error and retains the previous shortcut when changing it.

The global shortcut requires a small resident user process; no Windows service, automatic startup entry, or scheduled task is installed. A second resident launch opens a one-shot fallback instead of registering another shortcut. File arguments/context-menu launches continue to work independently.

Explorer selection is read on demand through local Shell COM interfaces **before** activating the popup. The reader matches the foreground frame, requests the active shell view, ignores hidden views, rejects ambiguous results, and rechecks the foreground window. It never simulates Copy, changes the clipboard, watches Explorer, or polls for selection changes. A read has a three-second UI wait limit and at most one outstanding worker; a blocked COM call cannot safely be terminated and remains a background thread until Explorer returns or FilePilot exits.

If Explorer is unavailable, the selection is empty/ambiguous, a virtual item/folder is selected, or validation fails, the old selection is cleared. Use **Choose files** as the fallback. Desktop icons, third-party file managers, elevated Explorer, and unusual shell extensions may require this picker. Selection is a snapshot; paths are validated again by each action. Tab combinations and Explorer updates still require broader acceptance testing.

## Batch conversion, rename, and favorites

- Select up to 100 files through Explorer's shortcut or the multi-select picker. Copy Path copies all validated absolute paths, one per line. Move and Rename require a single selection.
- Batch output formats include those available for any selected image. Inputs already in the chosen format and unsupported extensions are skipped and reported. All outputs go to the displayed destination (initially the first selected file's folder); review it before converting mixed-folder selections.
- Conversion runs sequentially with the existing converter and limits. A failed/colliding input does not prevent subsequent files from running. Nothing is overwritten, including two inputs that would produce the same output name. Batch details list each result. Cancellation keeps completed outputs and reports unprocessed files; originals remain unchanged. Open folder uses the latest completed output.
- Expand **Quick rename**, enter a new stem, and choose Rename. The original extension is appended by default. Check **Change extension explicitly** to edit the complete filename; changing a suffix does not convert its contents. Invalid/reserved Windows names, paths, trailing dots/spaces, unchanged/case-only names, and file/folder collisions are refused. Successful rename updates Copy Path.
- Expand **Favorite folders**, choose a saved folder, then choose Use for output or Move here. Move here asks for confirmation and reuses the safe single-file move service. Favorites are revalidated at use time; a removed/inaccessible folder produces an error.

Shortcut, favorite paths, and the theme chosen in Settings are saved locally in `%LOCALAPPDATA%\FilePilot\settings.json` using a temporary-file replacement. Malformed/oversized settings fall back to defaults with feedback; settings are limited to 32 KiB. The header theme button changes the current session; save through Settings to persist it. JPEG background and output destination are session choices. FilePilot does not upload settings or file paths.
## Explorer context menu

Registration is explicit and per user. Run the published executable from its final location:

```powershell
& .\artifacts\publish\FilePilot.exe --register-context-menu
& .\artifacts\publish\FilePilot.exe --unregister-context-menu
```

Each command shows a success or failure dialog and exits (0 for success, 1 for failure). Add `--quiet` to suppress dialogs in deployment scripts. Unregistration removes only entries pointing to that executable, preserving a newer version. To wait for its result in a script:

```powershell
$process = Start-Process .\artifacts\publish\FilePilot.exe -ArgumentList '--register-context-menu' -Wait -PassThru
$process.ExitCode
```

Right-click a `.png`, `.jpg`, `.jpeg`, `.webp`, or `.docx` file and choose **FilePilot**. On Windows 11, use **Show more options**. This is a classic static shell verb; no COM extension or primary-menu integration is installed.

Only these per-user locations are written:

```text
HKCU\Software\Classes\SystemFileAssociations\<extension>\shell\FilePilot
HKCU\Software\Classes\SystemFileAssociations\<extension>\shell\FilePilot\command
```

The command is `"C:\Full Path\FilePilot.exe" "%1"`: the executable and the one selected path are separately quoted. `MultiSelectModel=Single` requests single selection; the menu should be absent for multiple selections. The application also rejects multiple arguments. This context-menu entry remains single-file only. Use the widget shortcut or Choose files for batches. Folders, network items, and shell virtual items are not supported.

Registration/unregistration are repeatable. An ownership marker prevents replacing or removing an unrelated entry with the same name. Only FilePilot's verb keys are removed; file associations, default programs, and other verbs are preserved. No HKLM changes and no Explorer restart occur. A shell association-change notification is sent; if Explorer caches an old menu, close that menu and reopen it. If you move the published app, register it again at the new path. Registration paths containing a percent sign are rejected to avoid ambiguity with shell substitution.

## Image conversion

| Input | Available outputs |
| --- | --- |
| PNG | JPG, WebP |
| JPG / JPEG | PNG, WebP |
| WebP | PNG, JPG |

1. Select an image. FilePilot shows its name, extension, size, and the available output formats.
2. Select the output format. For JPEG, set an opaque background as `#RRGGBB`; the default is white (`#FFFFFF`).
3. Use **Destination** to choose an existing local folder. The source folder is the default.
4. Select **Convert**. A busy indicator is shown; file-changing controls are disabled while it runs.
5. On success, use **Open folder** to open the output directory in Explorer.

The output uses the original stem with the new extension. Existing files or folders with that name are **refused**, including collisions appearing during conversion. There is no overwrite option in this phase; choose another destination. Originals remain unchanged and remain selected, so Copy Path continues to refer to the original after conversion. Successful Move File still changes the selected path to the moved file.

Conversion reads the actual image format and rejects extension/content mismatches, unsupported formats, incomplete decodes, and animated images. All eight EXIF orientations are applied before encoding. There is no resizing: width and height are retained except that 90°/270° orientation swaps the axes. PNG/WebP retain transparency; JPEG composites transparent and partially transparent pixels onto the selected background. JPEG quality is 92; WebP encoding quality is 100. Quality is currently fixed.

Output is encoded from fresh pixels, so source EXIF (including GPS), XMP, IPTC, comments, and the original orientation tag are not copied. Pixels are decoded/rendered as 8-bit sRGB; this is not an archival high-bit-depth/HDR workflow. Encoders may write ordinary format/color information. Metadata removal does not remove information visible in image pixels or filenames.

Completed bytes are written to a random `.filepilot-<id>.tmp` file in the chosen destination, flushed, and finalized with a same-directory no-overwrite move. Failures and cancellation remove the owned temporary file. If cleanup itself is denied, the error reports the temporary path. No existing destination is removed to make room.

**Cancel** is cooperative. Native decode, drawing, and encoding cannot be interrupted mid-call; the token is checked between those operations and during output copying, and before finalization. The window stays open until active work finishes. Cancellation arriving after finalization does not undo a successful output. A crash, forced process termination, power loss, or disconnected drive can leave a temporary file; the app does not scan for or delete files from previous runs.

## Resource limits and path safety

- Default limits: 64 MiB encoded input, 16 million pixels, and 16,384 pixels on either edge. Header information is checked before allocating full pixel buffers. Animated images are rejected. The service accepts lower limits for testing; the UI does not expose them.
- Codec work runs away from the UI thread. Native working memory can still reach hundreds of MiB near the limits; these are input/dimension guardrails, not a hard process memory quota or time limit. Separate app instances have separate limits.
- Only existing regular local files/folders on fixed, removable, or RAM drives are accepted. UNC/mapped-network paths, device paths, alternate data streams, traversal segments, reserved names, and reparse-point paths are rejected. This intentionally excludes symlinks, junctions, and many cloud placeholders.
- Output names are derived from the validated source leaf name, not user-entered relative paths. The source handle prevents ordinary concurrent writes/replacement while decoding.
- Validation does not defend against malicious filesystem changes between path checks. Do not use this prototype as a security boundary in adversarial shared folders.
- Locked files and permission failures produce inline errors. No elevation is requested to bypass them. Independent Windows cloud-sync/filter drivers may still act on local files; FilePilot itself makes no network requests.

Move File retains Phase 1's `File.Move(..., overwrite: false)` behavior. Across volumes Windows copies before deleting the source. If the source may remain after completion, FilePilot reports a partial result and does not retry deletion. A failed cross-volume move can leave a partial destination; inspect both paths before retrying. Real multi-volume fault testing remains outstanding.

## UI and keyboard

Light and dark themes are available; Settings can save the preferred theme. Tab/Shift+Tab navigate controls; standard control keys and underlined access keys operate them. Ctrl+Shift+C copies the absolute path. Alt+V starts conversion, Alt+A cancels conversion, and Alt+O opens the output folder when available. Native folder pickers support cancellation without starting an operation. On small desktops, the file/action area scrolls while feedback remains visible.

Tab/Shift+Tab navigate controls, including the popup settings and Exit buttons. System-theme synchronization, custom high-contrast handling, and a full screen-reader audit are not implemented. Monitor/DPI placement is implemented but has not been verified across every display configuration.

## Architecture and dependencies

```text
src/FilePilot.Core/
  FileActions, LocalPathPolicy, SelectedFile       Existing Phase 1 file services
  Conversion/IFileConverter                     Request/result contract
  Conversion/ImageFileConverter                 Header checks, decoding, orientation, encoding
  Conversion/AtomicOutput                       Temporary output and no-overwrite finalization
  Conversion/BatchConverter, FileRename          Sequential batches and safe single-file rename
  Documents/                                    DOCX preflight, PDF finalization and optional local engine
src/FilePilot.App/
  MainViewModel + MainViewModel.Conversion        Selection, actions, busy/cancellation state
  MainWindow, App, Themes                         WPF controls and native dialogs
  ExplorerIntegration                           Explicit HKCU registration commands
  Widget/                                       Shortcut, Explorer snapshot, monitor placement, tray lifecycle
  WidgetSettings, SettingsWindow                 Local preferences, folder favorites and optional engine
  MainViewModel.Documents                        Single-document export state and cancellation
tests/FilePilot.Tests/                           Core, registry, view-model, and WPF loading tests
notices/                                        Packaged third-party license notices
```

There is no MVVM framework or DI container. The converter is constructor-injectable for state/cancellation tests. Registry behavior is tested under isolated HKCU test roots, rather than installing a real Explorer menu in automated tests. Existing Phase 1 and Phase 2 file services are reused. WinForms is referenced only for the built-in NotifyIcon/context menu; the application and all action/settings windows remain WPF. WPF owns startup and DPI awareness through the manifest, so the WinForms-only manifest diagnostic WFO0003 is specifically suppressed. No extra NuGet package was added for Phase 3.

The sole new direct runtime package is **SkiaSharp 4.153.1**, a maintained MIT-licensed .NET wrapper for native Skia. Its NuGet dependencies supply native assets; publishing with `-r win-x64` selects the Windows architecture. No WPF-specific Skia view package is needed. SkiaSharp was chosen because currently maintained ImageSharp packages require build-time license setup; no older unpatched package or license-enforcement bypass is used. Keep native codecs updated. NuGet restore needs network access unless packages are cached; normal application use is offline.

Document conversion now has a separate `IDocumentConverter` / `IDocumentPdfEngine` contract, bounded DOCX preflight and optional LibreOffice adapter; the image format selector stays image-specific. See the Phase 5 document for the engine/deployment/cancellation policy and real LibreOffice acceptance results.

## Verification and limitations

Phase 1 was committed and pushed at `cab070dc111d215e4292eca3b5ac7e16a27942cb`. Phase 2 is committed as `926f35896292812e674c3cebbf59b6da93b50a4b` on `feature/phase-2-conversion`. Phase 2 and Phase 3 have been pushed to GitHub. Phase 3 is committed as `4661051325465ff212e26a2b42cd8de8bcc52515` on `feature/phase-3-widget`. Phase 4 packaging work is on `feature/phase-4-distribution`.

The current suite contains **189 automated tests**, including all original 28 Phase 1 tests. Coverage includes all six conversions, alpha preservation/blending, custom/default JPEG backgrounds, eight EXIF orientations and metadata checks, corrupted/truncated/mismatched inputs, animation rejection, resource limits, Unicode/spaced paths, source preservation, locks, unsafe destinations, existing/concurrent/late collisions, cleanup after simulated write failure and cancellation, UI busy-state recovery, actual WPF/theme loading, and isolated registry registration/ownership checks. Phase 8 adds high-contrast palette/template checks, primary-action focus, resident hide/reopen, simulated scaled layouts, DPI work-area calculations and explicit extended diagnostic arguments.

Phase 2 verification on 2026-10-22 (Windows 11, .NET SDK 10.0.402): Release build passed with zero warnings/errors; all 84 tests passed without skips; Windows x64 publish succeeded. NuGet vulnerability auditing (including transitive dependencies) reported no known vulnerable packages in the three projects on this date. Desktop checks verified launch with a Unicode/spaced PNG path, PNG-to-JPEG and PNG-to-WebP through the UI, JPEG dimensions with the independent WPF decoder, unchanged original SHA-256, output-format selection and keyboard conversion, light/dark rendering, the final compact layout, and collision refusal with an unchanged output hash. The real registration command succeeded and all four quoted HKCU command values were inspected. Unregistration was run and all four temporary menu keys were confirmed removed. No context-menu registration is left installed.

Explorer UI access timed out waiting for app approval, so an actual right-click launch was not verified. Full interaction with the destination folder picker and Open folder still requires manual acceptance testing. These are distinct from the passing registry, conversion-service, and view-model tests.

Not yet verified: Windows 10, ARM64/x86 deployment, broad codec fuzzing, real disk-full/power-loss/unplugged-drive cases, hostile ACL/reparse races, native cancellation under every codec workload, cross-volume moving, all monitor/DPI combinations, or comprehensive assistive-technology behavior. No production-readiness or hard memory-budget claim is made.

Phase 3 automated verification on 2026-10-23: Release build passed with zero warnings/errors; all 118 tests passed without skips; Windows x64 publish succeeded. Added coverage includes rename restrictions/locks/collisions/extensions, batch continuation/cancellation/bounds/source preservation, settings round trips and oversized-save refusal, shortcut parsing, stale-selection clearing, non-Explorer fallback, monitor clamping, and real popup HWND sizing in the WPF test. Windows 11 desktop checks on 2026-10-22 verified a spaced Unicode Explorer selection via Ctrl+Alt+F, two-file selection and keyboard batch conversion, both original SHA-256 hashes unchanged, Escape hide/reopen, configurable Ctrl+Alt+G, local settings persistence, light/dark rendering, and clearing the selection for the non-Explorer fallback. Test-only settings were restored afterwards.

Unverified Phase 3 desktop flows: full favorite-folder picker/add/remove/move interaction, rename through the popup, notification-icon menu interaction, multiple active Explorer tabs, shortcut conflicts with third-party applications, and all monitor/DPI combinations. Core rename, favorite settings persistence, and reused move services are covered by automated tests. Earlier single-file conversion/registry checks remain recorded above; actual Explorer right-click launch remains outstanding.

## Roadmap

1. Phase 1 — safe single-file Copy Path and Move File (completed).
2. Phase 2 — Explorer registration and safe image conversion (completed).
3. Phase 3 — floating widget, shortcut, batch conversion, rename, and favorite folders (implemented).
4. Phase 4 — portable packaging, startup/memory tooling, extracted-package runtime checks and resident lifecycle fix completed for the available Windows 11 host; Windows 10 is explicitly unverified.
5. Phase 5 — optional single-DOCX → PDF conversion verified with real LibreOffice 26.8.1.1 on Windows 11, source-property minimization, short local engine workspaces, cancellation/timeout/error handling and initial widget usability checks. See docs/phase-5-documents.md for evidence and limits.
6. Phase 6 — packaged per-user installation, guarded upgrade/rollback/uninstall, Unicode Start Menu shortcuts, version-scoped Explorer integration and widget usability/stability checks. See [Phase 6 details](docs/phase-6-usability.md). Installer signing and optional Windows 11 primary-menu integration remain later work.

7. Phase 7 — beta candidate metadata, Windows CI/package artifacts, real-renderer acceptance and documentation. See [beta readiness and evidence](docs/beta-readiness.md) and [WPF STA stabilization](docs/phase-7-sta-stabilization.md) for the failed gate, dispatcher correction and repeated lifecycle acceptance. Main integration is owner-approved subject to checks; public release still requires separate approval.

8. Phase 8 — primary-action keyboard focus, Windows high-contrast resources, readable selection/disabled controls, live-region notifications, rounded DPI layout and cached themes. Explicit diagnostics can exercise 100 popup and 10 Settings cycles. See [Phase 8 implementation and evidence](docs/phase-8-stabilization.md) and the [acceptance plan](docs/phase-8-preparation.md). Windows 10 and broad hardware/assistive-technology acceptance remain unverified; no release is approved.

9. Phase 9 — offline quick-start help, keyboard recovery of hidden conversions, real Explorer/picker/help/native-cancellation interaction checks, visible dark-mode help focus, routed modal acceptance and transient document cleanup-lock handling. Safely integrated into main at d346d50 after both required CI jobs passed, with 172 real-engine tests and no skips. See [Phase 9 readiness and remaining release gates](docs/phase-9-release-readiness.md).
10. Phase 10 — validated installation preview, optional trusted archive-checksum enforcement, cleanup/test-fixture stability and a packaged setup/upgrade/removal guide. Integrated into main at 84913cd after both required CI jobs passed; post-merge CI also passed. See [Phase 10 evidence and remaining gates](docs/phase-10-beta-distribution.md). No public release is approved.
11. Phase 11 — real-world beta acceptance on codex/phase-11-beta-acceptance: longer diagnostic runs, real widget/picker/rename/move checks, corrected rename-collision feedback and bounded DOCX visual review. See [Phase 11 coverage](docs/phase-11-beta-acceptance.md) and [current results and unresolved integration/memory merge blockers](docs/phase-11-acceptance-results.md).

Windows 10 and broad hardware/accessibility acceptance remain unverified release limitations, not inferred from Windows 11 test results.

## Manual acceptance checklist

Press **F1** or the **?** button for offline quick-start help and keyboard shortcuts. During conversion, the configured widget shortcut reopens the current operation and focuses Cancel without capturing a new Explorer selection. Help is unavailable during work so its dialog cannot block cancellation. One-shot popups have no resident shortcut; their help explains taskbar restoration instead.

Use disposable images and folders.

1. Register the published app; right-click a supported image and launch FilePilot from Show more options. Try a spaced Unicode path and multiple selection.
2. Convert each format to both other formats. Check dimensions, oriented pictures, transparent edges, and a custom JPEG background.
3. Verify original contents and timestamps. Retry into an occupied output path and ensure existing contents survive.
4. Choose a different destination, cancel the picker, cancel a sufficiently large conversion, and check for leftover `.filepilot-*.tmp` files.
5. Check corrupted/unsupported/oversized images and locked or unwritable destinations.
6. Check light/dark themes, keyboard controls, Open folder, small windows, and Copy Path/Move File regressions.
7. Unregister and verify the menu disappears without changing default file associations or other menu entries.
8. Launch the widget without arguments. Test Ctrl+Alt+F on single/batch selections, no selection, non-Explorer focus, different Explorer tabs, and a shortcut conflict. Confirm fallback clears old paths.
9. Save a different shortcut/theme and favorite folder. Reopen the app, use a favorite as output, confirm/cancel a favorite move, and try a missing favorite.
10. Rename disposable files with/without explicit extension changes. Check collisions/invalid names. Hide during a batch, cancel it, inspect results, then Exit and confirm the shortcut is released.

References: [Windows static shell verbs](https://learn.microsoft.com/en-us/windows/win32/shell/fa-verbs), [single-selection registration](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-employ-the-verb-selection-model), [SkiaSharp package](https://www.nuget.org/packages/SkiaSharp/4.153.1), [SKCodec API](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skcodec), [ImageSharp support/licensing changes](https://sixlabors.com/posts/security-support-and-license-enforcement/), [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey), [active shell views](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-queryactiveshellview).
