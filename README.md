# FilePilot

A local Windows 10/11 file-action utility built with C# / .NET 10 and WPF. Phase 1 provides file details, Copy Path, and safe Move File. Phase 2 adds per-user Explorer integration and PNG/JPEG/WebP conversion. No background service, cloud API, accounts, database, telemetry, or administrator privileges are required by the application.

## Build and run

Install the .NET 10 SDK with Windows Desktop support. From the repository directory in PowerShell:

```powershell
dotnet restore FilePilot.slnx
dotnet build FilePilot.slnx -c Release
dotnet test FilePilot.slnx -c Release

dotnet run --project src/FilePilot.App -c Release -- "C:\Files\photo with spaces.png"
```

Pass exactly one absolute local file path, including quotes around paths with spaces. Unicode names are supported. With no argument, choose a file in the app. Unsupported image extensions can still use Copy Path and Move File.

Create a Windows x64 distribution folder:

```powershell
dotnet publish src/FilePilot.App -c Release -r win-x64 --self-contained false -o artifacts/publish
& .\artifacts\publish\FilePilot.exe "C:\Files\photo.png"
```

Distribute the **whole folder**, including SkiaSharp's native library and the `notices` directory. This build requires the .NET 10 Desktop Runtime. Use `--self-contained true` for a larger distribution that bundles the runtime. There is no installer or code signing yet. Do not register an executable in a temporary location that will later be deleted.

## Explorer context menu

Registration is explicit and per user. Run the published executable from its final location:

```powershell
& .\artifacts\publish\FilePilot.exe --register-context-menu
& .\artifacts\publish\FilePilot.exe --unregister-context-menu
```

Each command shows a success or failure dialog and exits (0 for success, 1 for failure). To wait for its result in a script:

```powershell
$process = Start-Process .\artifacts\publish\FilePilot.exe -ArgumentList '--register-context-menu' -Wait -PassThru
$process.ExitCode
```

Right-click a `.png`, `.jpg`, `.jpeg`, or `.webp` file and choose **FilePilot**. On Windows 11, use **Show more options**. This is a classic static shell verb; no COM extension or primary-menu integration is installed.

Only these per-user locations are written:

```text
HKCU\Software\Classes\SystemFileAssociations\<extension>\shell\FilePilot
HKCU\Software\Classes\SystemFileAssociations\<extension>\shell\FilePilot\command
```

The command is `"C:\Full Path\FilePilot.exe" "%1"`: the executable and the one selected path are separately quoted. `MultiSelectModel=Single` requests single selection; the menu should be absent for multiple selections. The application also rejects multiple arguments. Selection of multiple files, folders, network items, and shell virtual items is not supported.

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

Light and dark themes are available for the current session. Tab/Shift+Tab navigate controls; standard control keys and underlined access keys operate them. Ctrl+Shift+C copies the absolute path. Alt+V starts conversion, Alt+A cancels conversion, and Alt+O opens the output folder when available. Native folder pickers support cancellation without starting an operation. On small desktops, the file/action area scrolls while feedback remains visible.

The theme, JPEG background, and destination choices are not persisted. System-theme synchronization, custom high-contrast handling, multi-monitor cursor positioning, and a full screen-reader audit are not implemented.

## Architecture and dependencies

```text
src/FilePilot.Core/
  FileActions, LocalPathPolicy, SelectedFile       Existing Phase 1 file services
  Conversion/IFileConverter                     Request/result contract
  Conversion/ImageFileConverter                 Header checks, decoding, orientation, encoding
  Conversion/AtomicOutput                       Temporary output and no-overwrite finalization
src/FilePilot.App/
  MainViewModel + MainViewModel.Conversion        Selection, actions, busy/cancellation state
  MainWindow, App, Themes                         WPF controls and native dialogs
  ExplorerIntegration                           Explicit HKCU registration commands
tests/FilePilot.Tests/                           Core, registry, view-model, and WPF loading tests
notices/                                        Packaged third-party license notices
```

There is no MVVM framework or DI container. The converter is constructor-injectable for state/cancellation tests. Registry behavior is tested under isolated HKCU test roots, rather than installing a real Explorer menu in automated tests. Existing Phase 1 behavior is reused.

The sole new direct runtime package is **SkiaSharp 4.153.1**, a maintained MIT-licensed .NET wrapper for native Skia. Its NuGet dependencies supply native assets; publishing with `-r win-x64` selects the Windows architecture. No WPF-specific Skia view package is needed. SkiaSharp was chosen because currently maintained ImageSharp packages require build-time license setup; no older unpatched package or license-enforcement bypass is used. Keep native codecs updated. NuGet restore needs network access unless packages are cached; normal application use is offline.

A future local DOCX-to-PDF engine can be added separately from the view. Its format contract and engine deployment/cancellation requirements should be designed when that feature is undertaken; Phase 2's format selector is deliberately image-specific.

## Verification and limitations

Phase 1 was committed and pushed at `cab070dc111d215e4292eca3b5ac7e16a27942cb`. Phase 2 is recorded on `feature/phase-2-conversion`; its Release build and 84-test suite passed before committing.

The current suite contains **84 automated tests**, including all original 28 Phase 1 tests. Coverage includes all six conversions, alpha preservation/blending, custom/default JPEG backgrounds, eight EXIF orientations and metadata checks, corrupted/truncated/mismatched inputs, animation rejection, resource limits, Unicode/spaced paths, source preservation, locks, unsafe destinations, existing/concurrent/late collisions, cleanup after simulated write failure and cancellation, UI busy-state recovery, actual WPF/theme loading, and isolated registry registration/ownership checks.

Final verification on 2026-10-22 (Windows 11, .NET SDK 10.0.402): Release build passed with zero warnings/errors; all 84 tests passed without skips; Windows x64 publish succeeded. NuGet vulnerability auditing (including transitive dependencies) reported no known vulnerable packages in the three projects on this date. Desktop checks verified launch with a Unicode/spaced PNG path, PNG-to-JPEG and PNG-to-WebP through the UI, JPEG dimensions with the independent WPF decoder, unchanged original SHA-256, output-format selection and keyboard conversion, light/dark rendering, the final compact layout, and collision refusal with an unchanged output hash. The real registration command succeeded and all four quoted HKCU command values were inspected. Unregistration was run and all four temporary menu keys were confirmed removed. No context-menu registration is left installed.

Explorer UI access timed out waiting for app approval, so an actual right-click launch was not verified. Full interaction with the destination folder picker and Open folder still requires manual acceptance testing. These are distinct from the passing registry, conversion-service, and view-model tests.

Not yet verified: Windows 10, ARM64/x86 deployment, broad codec fuzzing, real disk-full/power-loss/unplugged-drive cases, hostile ACL/reparse races, native cancellation under every codec workload, cross-volume moving, all monitor/DPI combinations, or comprehensive assistive-technology behavior. No production-readiness or hard memory-budget claim is made.

Still deferred: quick rename, favorite destinations, batch conversion, document conversion, installer/signing, Windows 11 primary-menu integration, and startup/memory benchmarks.

## Manual acceptance checklist

Use disposable images and folders.

1. Register the published app; right-click a supported image and launch FilePilot from Show more options. Try a spaced Unicode path and multiple selection.
2. Convert each format to both other formats. Check dimensions, oriented pictures, transparent edges, and a custom JPEG background.
3. Verify original contents and timestamps. Retry into an occupied output path and ensure existing contents survive.
4. Choose a different destination, cancel the picker, cancel a sufficiently large conversion, and check for leftover `.filepilot-*.tmp` files.
5. Check corrupted/unsupported/oversized images and locked or unwritable destinations.
6. Check light/dark themes, keyboard controls, Open folder, small windows, and Copy Path/Move File regressions.
7. Unregister and verify the menu disappears without changing default file associations or other menu entries.

References: [Windows static shell verbs](https://learn.microsoft.com/en-us/windows/win32/shell/fa-verbs), [single-selection registration](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-employ-the-verb-selection-model), [SkiaSharp package](https://www.nuget.org/packages/SkiaSharp/4.153.1), [SKCodec API](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skcodec), [ImageSharp support/licensing changes](https://sixlabors.com/posts/security-support-and-license-enforcement/).
