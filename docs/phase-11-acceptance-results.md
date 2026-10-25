# Phase 11 acceptance results — 2026-10-25

## Candidate and environment

This pass uses a development package based on 254e0804a89a93646657867b667d82647e724905, including the rename-feedback correction described below. Its manifest identifies SourceDirty=true; it is not a clean release candidate. The local ZIP SHA-256 is BF62828BE881E8E37C4EF93CF03328D0A93B3DE37F868F183409BED4E04543B3, with 27 inventoried payloads. Checksums identify this archive only.

The available desktop is Windows 11 Home Single Language x64, build 26200, one monitor at 120 DPI (125 percent). Verification used .NET SDK 10.0.402/runtime 10.0.13 and real LibreOffice 26.8.1.1. Windows 10 and physical transitions between monitors were unavailable. Neither simulated DPI tests nor hosted Windows Server CI certify those environments.

## Completed automated and interactive checks

Release build passed with zero warnings/errors. All 189 automated tests passed with real LibreOffice and no skips. The 24 package/script, 16 installer, 21 deployment lifecycle and 15 fresh-process desktop/memory checks passed. Independent PDF inspection and actionlint passed. An initial package attempt failed because NuGet vulnerability data was unavailable (NU1900); a subsequent ordinary restore and fresh package build succeeded. Auditing and warning policy were not disabled.

Disposable local fixtures exercised the actual widget and native dialogs:

- Ctrl+O opened a file whose path contained spaces and Unicode; Ctrl+Shift+C copied its exact absolute path.
- Quick Rename refused an occupied name without changing either file. A unique Unicode name preserved the extension and contents, and Copy Path followed the renamed file.
- Move File's native folder picker selected a Unicode destination. The move succeeded, Copy Path followed the destination, and the before/after SHA-256 matched.
- Keyboard traversal scrolled the rename action into view. Alt+R invoked Rename and Alt+X exited the idle widget.
- Settings kept its Save/Cancel footer accessible. Cancelling the favorite-folder picker left the list unchanged. Selecting a folder populated the unsaved list; Escape cancelled Settings. The saved preferences fingerprint remained unchanged.

The first rename collision correctly preserved data but incorrectly advised choosing another folder. Rename now gives action-specific feedback: choose another name. A regression test checks preserved source/collision contents, selection, Copy Path, busy-state recovery and a successful subsequent rename. The corrected message was also observed in the rebuilt widget.

The correction was committed and normally pushed as 842847154b0357e0e6abae5220f6bb593b09fe32. Both required jobs in [CI run 40185808589](https://github.com/amirzulfadhli/FilePilot/actions/runs/40185808589) passed for that exact commit. Downloaded reports confirm 186 passing tests plus the three expected real-engine skips in the build job, all 189 tests passing without skips in the real-engine job, 15 passing desktop repeats in each job, and successful independent two-page PDF inspection. The downloaded 27-payload package passed read-only validation, identifies that exact source revision with SourceDirty=false, and has SHA-256 D7761515CDA0237B1B0C39FFF03C4378BBFA6D2E7DC6BEE82BB353004006B220. This hosted archive is distinct from the earlier local measurement archive. CI does not exercise actual Explorer right-click visibility or the longer local workload.

These steps do not establish persisted favorite add/remove/move, tray-menu interaction, full keyboard traversal of every control, or screen-reader speech.

## DOCX fidelity and safety

The real-engine synthetic DOCX produced two A4 pages. Independent inspection and visual review of both rendered pages found the heading, italic text, bordered table, colored raster image, explicit page break and Chinese text readable and unclipped. Source author/title/subject/keyword sentinels were removed from PDF metadata. Creator/producer/date metadata may remain by the documented policy.

Real-engine tests covered original preservation, occupied-output refusal, cancellation, deadline expiry, error handling and workspace cleanup. This is bounded fixture acceptance, not a Word-reference comparison or proof of fidelity for representative complex documents, unusual fonts, pagination, fields or embedded objects. Such documents remain a release gate.

## Explorer integration — unresolved acceptance failure

Registration from a disposable installed executable succeeded. All five supported extensions had the expected owned HKCU verb, REG_SZ command, quoted Unicode/spaced executable path, quoted single file argument and Single selection model. The command was also visible through the merged HKCR view. The test shell and Explorer used the same user, were not elevated and had no package identity.

However, FilePilot did not appear in the actual Windows 11 **Show more options** menu for a selected supported PNG. Reopening the menu, refreshing the folder and delivering association-change notifications, including a synchronous flush, did not resolve it. A separate disposable all-file static-verb prototype was also absent; this does not demonstrate that changing FilePilot's registration location would fix the problem. The root cause remains undetermined. No product registration change is presented as a fix.

All five temporary FilePilot entries and the unchanged isolated prototype were removed after checking their commands/ownership, restoring their verified absent pre-test state. No default associations, system-wide keys or security settings were changed; Explorer was not restarted. Registry unit tests and command inspection remain passing checks, separate from this failed shell-visibility acceptance case.

Treat this as a blocking integration issue before merging Phase 11. Reproduce in a fresh interactive Windows session and investigate shell association enumeration/cache behavior without changing default applications or restarting the user's Explorer automatically. The documented widget shortcut and Ctrl+O remain available fallbacks; they do not make right-click launch verified.

## Extended stability

The user-requested workload completed 1,800 seconds of requested hidden idle per mode, popup then resident widget, followed by 100 popup and 10 synthetic Settings cycles per mode. Both modes recorded 180 samples and finished the workload, but the combined performance check **failed**: resident post-cycle private-memory growth exceeded the unchanged 32-MiB budget. The wrapper exited 1 and summary.json records Passed=false. A per-process Succeeded=true means its workload completed; it does not mean the wrapper's performance budgets passed.

| Mode | Actual hidden idle | First frame | Hidden private / sampled peak | Post-cycle private growth | Idle CPU |
| --- | --- | --- | --- | --- | --- |
| Popup | 1,802.397 s | 838 ms | 63.70 / 68.01 MiB | 29.875 MiB | 2,171.875 ms |
| Widget | 1,802.225 s | 1,118 ms | 65.65 / 66.53 MiB | **46.609375 MiB — failed** | 2,234.375 ms |

Both reports identify 120 DPI, no forced collection, and successful codec/source/collision checks. The resident shortcut registered. Unchanged-executable/assembly checks completed before budget evaluation. Startup and all sampled idle-private values passed their existing 5-second/200-MiB limits; popup post-cycle growth passed. No threshold was raised, no forced collection or working-set trim was introduced, and the failure was not retried away. Both diagnostic processes exited; saved preferences still matched their pre-test fingerprint.

Resident private memory increased from 68,837,376 to 117,710,848 bytes after the popup/Settings burst, while the managed snapshot increased from 3,610,560 to 6,168,816 bytes (about 2.44 MiB). Those snapshots narrow the investigation but cannot identify an allocation owner or establish a leak. The existing theme cache and main-window static-event unsubscription were inspected; no proven underlying defect was identified. Follow-up needs separate post-popup/post-Settings checkpoints and native/GUI-resource or heap profiling, with the same budget and no artificial collection, followed by repetition of the requested long workload after a justified fix. Do not treat the earlier passing short/default workloads or green CI as resolving this failure.

Local reports are retained under ignored artifacts/performance/20261025-181540-357de5fd. This synthetic workload does not establish an hours-long mixed-use session, cold startup, assistive-technology behavior or physical monitor transitions. Interactive Explorer investigation overlapped the first portion of popup idle; other local heavy test/build workloads were not run during measurement.

## Outcome and remaining gates

Phase 11 remains on its feature branch. Do not merge or start Phase 12 while the Explorer integration failure or long-run resident memory-growth failure is unresolved. Hosted checks for the final commit must be reviewed separately; successful CI cannot replace either failed acceptance gate. Main remains at 84913cd462b7b91330b8768b6bd12b3b5392af66; no post-merge check is claimed.

Other unverified release gates include Windows 10, physical multi-monitor/DPI transitions, live OS contrast/text scaling, screen-reader/magnifier acceptance, saved favorites/tray interaction, hours of mixed real use, representative DOCX/font fidelity, owner acceptance of an exact clean candidate, and the signing/distribution decision. There is no approved public GitHub Release. See the [acceptance protocol](beta-acceptance-record.md) for reproducible remaining checks.
