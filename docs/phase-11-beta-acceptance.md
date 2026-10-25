# Phase 11 — real-world beta acceptance

The [2026-10-25 acceptance results](phase-11-acceptance-results.md) record the subsequent 189-test verification, interactive checks, unresolved Explorer-menu visibility failure and failed resident memory-growth budget after the requested hour-long workload. These failures block integration; the initial passing short-run results below are retained as dated evidence rather than relabeled as final acceptance.

Phase 10 was integrated on 2026-10-25 by fast-forwarding main from d346d500f4287e0a554105f67468cda8945d08e1 to 84913cd462b7b91330b8768b6bd12b3b5392af66 after both jobs in [run 40123696277](https://github.com/amirzulfadhli/FilePilot/actions/runs/40123696277) passed. Both jobs in the [post-merge main run 40124247627](https://github.com/amirzulfadhli/FilePilot/actions/runs/40124247627) also passed. No force push, history replacement or release publication was used.

Downloaded Phase 10 branch reports confirm 173 real-engine tests passed without skips, both sets of 15 desktop/memory checks passed, and independent PDF inspection succeeded. Build/package, 24 package/script, 16 installer, 21 lifecycle and extracted-widget checks passed. The clean branch archive has 25 payloads, source 84913cd462b7b91330b8768b6bd12b3b5392af66, SourceDirty=false and SHA-256 6861C5AC0F0BF5D8BCE95F99F39032140E551E922732C521B6306EF2603CCFE3. That checksum identifies that artifact only, not a later Phase 11 or separately packaged main build.

## First acceptance slice

The existing explicit diagnostic runner accepts `--extended --idle-seconds 30..3600`. The matching measurement script exposes `-Extended -IdleSeconds`; supplying a duration without Extended is rejected before creating output. Normal and ordinary diagnostic launches retain their behavior: 3 seconds hidden idle/20 popup cycles for the default diagnostic workload, and 30 seconds/100 popups plus 10 Settings cycles for Extended without a duration. Normal use never instantiates the runner.

During explicit diagnostics only, memory is sampled at intervals up to ten seconds. Reports retain requested and actual idle duration, sample timestamps/counters, idle CPU, popup/Settings counts and unchanged codec/source/collision checks. The 200-MiB idle-private budget applies to every sampled idle value; the existing 5-second startup and 32-MiB popup-growth budgets remain unchanged. No forced collection, working-set trimming, normal-use timer or background polling is added. Default wrapper deadlines remain 30/90 seconds; only an explicitly requested longer run receives its requested duration plus 60 seconds for startup/cycles/finalization.

```powershell
# After Build-Package.ps1 has passed; choose the matching new published executable.
pwsh -NoProfile -File scripts/Measure-FilePilot.ps1 -ExecutablePath artifacts/packages/my-build/publish/FilePilot.exe -Runs 1 -Extended -IdleSeconds 300
```

This runs popup and widget separately: at least five minutes of requested hidden idle per mode, then 100 popup and 10 synthetic Settings cycles each. It does not save user preferences or operate on user documents. Reports/fixtures are local under artifacts/performance. Older default diagnostic reports remain readable by the wrapper; longer intervals require the updated executable. Do not change binaries during measurement. A successful ten-minute combined workload does not establish an hours-long session, multi-monitor transitions or assistive-technology acceptance.

Fifteen new argument cases cover both modes, supported bounds and malformed/unbounded durations. [Beta acceptance record](beta-acceptance-record.md) provides reproducible steps and an environment/evidence form. CI runs both required jobs on this branch, retaining the current action pins, read-only permissions, deadlines and explicit short-lived artifacts. No release job is introduced. Local verification is recorded below; exact-commit hosted checks remain required before any integration.

## Local verification — 2026-10-25

Release build passed with zero warnings/errors. All 188 tests passed with real LibreOffice and no skips, including all original file-operation/conversion cases and fifteen new diagnostic cases. All 24 package/script, 16 installer, 21 packaged lifecycle and 15 fresh-process desktop/memory checks passed. Independent synthetic-PDF inspection and actionlint passed. The measurement script rejected IdleSeconds without Extended before accessing an executable/output path. All relative documentation links resolved.

The updated wrapper passed default measurements with both the earlier Phase 10 executable/report format and the new extracted Phase 11 package. The latter's popup/widget first frames were 814/900 ms, hidden private memory 68.3/69.3 MiB and growth after 20 cycles 2.15/1.79 MiB. Existing budgets were unchanged.

Both longer workloads completed, with 30 samples, 100 popup cycles and 10 synthetic Settings cycles per mode:

| Mode | Actual hidden idle | First frame | Hidden private / sampled peak | Post-cycle private growth | Idle CPU |
| --- | --- | --- | --- | --- | --- |
| Popup | 300.423 s | 768 ms | 66.1 / 68.70 MiB | 18.59 MiB | 359.4 ms |
| Widget | 300.435 s | 1,011 ms | 63.7 / 66.23 MiB | 22.35 MiB | 578.1 ms |

All budgets and codec/source/collision checks passed; both reports identify 120 DPI and ForcedGarbageCollection=false. CPU includes the explicit diagnostic sampling workload. Reports remain local under ignored artifacts/performance/20261025-032649-ffc37966. The package contains 27 payload files and is a dirty development build based on 84913cd; these measurements are not a clean release-candidate checksum or cold-start guarantee. No new manual Explorer, screen-reader, live-contrast, multi-monitor or hours-long acceptance pass is claimed. Phase 11's initial slice is prepared separately for review, not merged into main.

## Available coverage and unavailable checks

The available local desktop is Windows 11 x64, one monitor at 120 DPI (125 percent), with work area 0,0..1920,1020. The retained Phase 10 native bounds test placed its HWND at 1320,220..1920,1020 within that area. Hosted Windows Server CI used 96 DPI and a 1024x720 work area; this does not certify Windows 10. Simulated 100/125/150/200-percent and negative-coordinate/small-work-area tests remain useful but do not substitute for physical monitor transitions.

Windows 10 remains explicitly unverified by prior agreement. No Windows 10 VM or second physical display is available. Live OS contrast switching, screen-reader speech, text scaling/magnifier combinations, hours-long idle/mixed-work sessions, broader Explorer right-click/favorites/rename/tray interactions and representative complex DOCX/font fidelity remain unverified for this candidate. Earlier dated Phase 9 real Explorer/picker/help/native-batch-cancellation evidence is retained in its own document, not relabeled as a new Phase 11 manual pass.

Remaining release blockers: owner acceptance of an exact candidate, representative document/UI/hardware/accessibility coverage, decision on unsigned distribution/signing, and separate public-release approval. Graphical setup, Apps & Features registration, ARM64/x86 and automatic updates remain outside the current package. Phase 11 remains separate from main for review.
