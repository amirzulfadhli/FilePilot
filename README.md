# FilePilot

A small, local Windows file-action utility built with C# / .NET 10 and WPF. **Phase 1 only:** launch with one file, inspect its details, copy its absolute path, and move it to an existing folder. No service, accounts, network calls, telemetry, or administrator privileges are needed by the app.

## Feasibility and architecture

The proposed stack is a good fit. WPF supplies native Windows windows, keyboard support, clipboard access, and an `OpenFolderDialog` without extra runtime packages. Normal file operations use .NET APIs. Keep the process short-lived: one window per invocation, exiting when it closes.

```text
FilePilot.slnx
src/
  FilePilot.Core/       Path policy, metadata, safe file actions; no WPF dependency
  FilePilot.App/        WPF views, themes, small view model, OS dialogs/clipboard
tests/
  FilePilot.Tests/      xUnit integration tests using isolated temporary files
```

The view model owns selection and user-facing state. Code-behind handles Windows-specific UI (dialogs, clipboard, theme selection). The core library owns validation and file operations. This avoids an MVVM framework, DI container, and background service. Tests are the only third-party dependencies.

## Setup, build, and run

Requires Windows 10/11 and the .NET 10 SDK with WPF / Windows Desktop support. Normal execution requires only the .NET 10 **Desktop** Runtime. Use PowerShell in this repository:

```powershell
dotnet restore FilePilot.slnx
dotnet build FilePilot.slnx -c Release
dotnet test FilePilot.slnx -c Release

dotnet run --project src/FilePilot.App -c Release -- "C:\Users\you\Pictures\photo.png"
```

Quote paths with spaces. Pass exactly one absolute local path. With no argument, the app opens with actions disabled and a Choose file button. Missing, unsupported, or multiple paths produce inline guidance. Files do not need an image extension for Phase 1.

To create a framework-dependent executable folder:

```powershell
dotnet publish src/FilePilot.App -c Release -r win-x64 --self-contained false -o artifacts/publish
& .\artifacts\publish\FilePilot.exe "C:\Files\example.txt"
```

Distribute the whole publish folder, not just its executable. A self-contained build is possible with `--self-contained true`, at the cost of a larger download. There is no installer or registry modification in Phase 1.

## Using Phase 1

- **Copy Path** copies the full, unquoted path. Shortcut: Ctrl+Shift+C; Alt+C also activates the button.
- **Move File** opens the Windows folder picker. Selecting a folder commits the move. Cancellation does nothing. A collision refuses the move; choose another folder.
- File details and subsequent Copy Path operations follow the new location after a successful move.
- **Choose file** changes the selection. **Dark theme / Light theme** switches the palette for the current session.
- Tab / Shift+Tab, Enter / Space, and button access keys provide keyboard navigation.

## Safety model

- Only existing regular files and destination folders on fixed, removable, or RAM drives are accepted. UNC paths, mapped network drives, device paths, alternate data streams, traversal segments, reserved names, and paths through reparse points are rejected. This intentionally excludes junctions, symbolic links, and many cloud placeholders.
- Destination names are derived solely from the source filename. No free-text destination filename is accepted.
- Moves use `File.Move(source, destination, overwrite: false)`. The OS enforces no replacement even if a destination appears after validation. Directory-name collisions are also rejected. Moving to the same folder is refused.
- .NET delegates cross-volume moves to Windows copy/delete behavior: the source is removed only after copying succeeds. If the source remains, FilePilot reports a partial result, retains both files, and selects the destination. It never retries source deletion automatically.
- Windows may leave a partial destination after a failed cross-volume copy (for example, an unplugged drive). FilePilot does not automatically remove it. Inspect both locations before retrying; collisions continue to block replacement.
- Move runs on a worker thread; file-changing actions and window closing are disabled until it completes. There is no cancellation after starting a move.
- Permission and locked-file failures are reported inline. The app never requests elevation to get around them.
- Validation is a guardrail against mistaken paths, not a security boundary against another process maliciously replacing filesystem entries between checks. Cloud sync software and filesystem filters may still run independently of FilePilot. Do not use this prototype for adversarial shared directories.

## Manual acceptance check

Use disposable files, not your originals. Create a source folder and two destination folders.

1. Launch with a filename containing spaces and Unicode. Verify name, extension, byte count, and modification time.
2. Activate Copy Path; paste into a text editor and compare with the full path.
3. Move to an empty destination. Verify contents, disappearance from the source, updated displayed path, and Copy Path at the new location.
4. Place a different file of the same name in the next destination. Attempt the move; verify both contents remain unchanged.
5. Cancel the folder picker; verify nothing changes. Choose the current folder; verify a clear refusal.
6. Open a file with an exclusive lock and attempt the move. Verify an error and preservation of the source. Retry after releasing the lock.
7. Check no argument, missing file, a directory, multiple arguments, and an unsupported network path.
8. Check both themes, keyboard navigation, long paths, display scaling, and a narrow window.
9. Before release, exercise a real second local volume, disk-full/unplugged-drive failures, restricted ACLs, and Windows 10/11 separately.

Automated tests use isolated temporary files and delete only their own fixtures. They do not modify the real clipboard. UI smoke testing is separate from automated tests.

## Incremental roadmap

1. **Phase 1 (this repository):** path launch, file details, Copy Path, safe Move File, basic themes, tests.
2. **Explorer integration:** opt-in per-user `HKCU\Software\Classes\*\shell\FilePilot` registration with a quoted executable and `"%1"`, plus uninstall support. Initially use Windows 11's Show more options menu. Add cursor-adjacent placement with per-monitor DPI and work-area clamping. Keep installation out of ordinary startup.
3. **Conversion:** introduce `IFileConverter` with supported input/output formats and an asynchronous conversion operation. Select a maintained image library after reviewing its current license, deployment size, decoder limits, and security maintenance. Do not install it before this stage. Implement one PNG-to-JPG path first, then the other requested pairs. Use signature checks, dimensions/pixel limits, explicit JPEG background compositing, an output picker, temporary output, and no-overwrite finalization. Preserve originals; require explicit confirmation for any future replacement workflow. Test transparency, malformed input, mismatched extensions, collisions, and output integrity.
4. **Rename and favorite folders:** validated leaf names, extension preservation, collisions, case-only rename handling, and a small local JSON settings file for favorite destinations/theme. No database.
5. **Packaging:** signing, installer/uninstaller, real Windows 10/11 and multi-volume testing, accessibility and startup/memory measurements.

DOCX-to-PDF should be another `IFileConverter` implementation backed by a separately selected local engine. Keep process invocation, engine availability, timeout, and temporary-file cleanup behind that implementation; do not assume Microsoft Office is installed. An engine such as LibreOffice would add a substantial deployment and maintenance dependency, so choose it in a later phase.

## Verification on this machine

On 2026-10-21, Release build and all 28 automated tests passed on Windows 11 with .NET SDK 10.0.402. The tests include loading the actual WPF window and both theme dictionaries. An interactive smoke check confirmed launch with a quoted file path, displayed metadata, the Copy Path shortcut and exact clipboard text, theme switching, and opening the native folder picker. The automation helper could not reliably target the native picker controls, so an end-to-end move through that picker is still a manual acceptance item. Core moves and view-model updates were exercised by automated tests. Cross-volume moves, ACL-denied filesystem operations, power loss/disk-full cases, Windows 10, high contrast, and varied monitor DPI were not tested.

## Current limitations

Single file and one window at a time. No Explorer registration, conversion, rename, favorites, persisted settings, cursor positioning, automatic system-theme synchronization, installer, or background agent. Light theme is the default. No explicit overwrite option is offered. Startup time and memory have not been benchmarked. Cross-volume behavior depends on Windows and needs dedicated hardware/fault testing before release. No claim is made that Phase 1 is production-ready or that conversion/rename tests exist before those features are implemented.

References: [WPF native dialogs](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-open-common-system-dialog-box), [File.Move behavior](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-10.0).

