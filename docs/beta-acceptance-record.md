# Beta acceptance record

Copy this form outside the installation directory for each candidate/environment. Use disposable local files and keep the package and trusted checksum. This is a test protocol, not a completed acceptance report. Mark each case Passed, Failed or Not run, with evidence; do not infer compatibility from another Windows edition or simulated layout.

## Environment and provenance

Record date, source revision/dirty flag, ZIP SHA-256, FilePilot version, Windows edition/build, x64 runtime version, optional LibreOffice version, monitor count/resolutions/layout/scaling, text scaling, contrast theme, assistive technology/version and test duration. Redact personal paths. Use the [setup guide](beta-quick-start.md) to validate provenance and install into a new directory; do not bypass security policy for an unsigned candidate.

## Cases

| Case | Reproduction and expected behavior | Result / evidence |
| --- | --- | --- |
| First run | Start the portable/resident widget; select a spaced Unicode file in Explorer and invoke the configured shortcut. Confirm filename and primary focus. From a non-Explorer window, confirm stale selection clears and Ctrl+O works. | Not run |
| File actions | Copy a disposable absolute path; rename with extension preserved; reject a collision/invalid name; add/remove a favorite and move into it. Compare contents/hashes and existing conflicting files. | Not run |
| Native integration | Exit the widget; preview/install a new version with its trusted checksum, activate it and opt into HKCU context menus. Right-click a supported file (Show more options on Windows 11). Upgrade/rollback/uninstall while keeping sources, preferences and other versions. | Not run |
| Conversion | Use opaque/transparent/oriented images and a representative DOCX with known fonts/pages/images. Verify dimensions, background, layout, property policy, unchanged originals and occupied-output refusal. Hide/reopen and cancel a batch; completed outputs remain, no partial output/temp files remain. | Not run |
| Keyboard and feedback | Traverse every action/Settings/Help control with Tab/Shift+Tab. Check visible focus, labels, Enter/Space, picker cancellation, Escape/dropdown behavior, busy guards and Alt+X. With a screen reader, check announced names/state/status and cancellation feedback. | Not run |
| Contrast/text | Check light/dark, live Windows contrast changes and enlarged text/magnifier, then restore the original settings. Confirm focus, selected text, fixed footer, scrolling and success/error feedback remain visible. | Not run |
| Physical displays | Test 100/125/150/200-percent scaling across real displays, including left/above primary (negative coordinates). Invoke near every edge; move between displays; change primary/taskbar/work area and disconnect a secondary display. Confirm popup/dialog bounds, sizing, focus and recovery. | Not run |
| Windows editions | Repeat on Windows 11 and an appropriate Windows 10 edition with its supported runtime. Record each exact environment independently. No available machine means Not run, not Passed. | Not run |
| Extended stability | Run the matching opt-in diagnostics with a chosen bounded duration; retain actual idle/sample/CPU/cycle results. Also test hours-long real mixed actions/idle without profiling, checking startup, cancellation, responsiveness and resource trends. | Not run |
| Recovery | Try locked files, protected destinations, missing runtime/engine, an interrupted owned uninstall and a shortcut conflict. Expect actionable errors, preserved data and a usable picker/retained installation; never terminate another application to free a shortcut. | Not run |

For mutations, save before/after hashes outside the installation directory. Do not delete failed fixtures until their reported paths/state have been inspected. Restore any deliberately changed shortcut/theme/Explorer registration after testing. Automated registry tests use isolated roots; actual integration must be recorded separately.

## Outcome

List failures with exact reproduction, expected/actual result, source/OS/runtime and a minimal disposable fixture. Record unavailable tests and why. Do not upload private documents, credentials or full diagnostic directories. FilePilot has no automatic reporting; submitting a report is a user action. Record the owner's release decision separately: successful testing does not approve publishing a GitHub Release.
