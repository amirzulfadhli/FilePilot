# Phase 7 WPF STA stabilization

## Investigation and evidence

[Run 39936011763](https://github.com/amirzulfadhli/FilePilot/actions/runs/39936011763), commit c22e91a, failed `WindowTests.WindowAndBothThemeDictionariesLoadOnStaThread` at its 20-second `Thread.Join` assertion. The other 161 tests passed, including the real LibreOffice tests. The independent PDF inspection step was consequently skipped. That log contains the waiting caller's stack only: it cannot identify the exact operation executing on the STA thread at the deadline.

The diagnostic-only commit 0d7de72 retained the original harness and added elapsed-time/thread checkpoints around application construction, App.xaml loading, window construction, selection loading, each theme load/measure/arrange, native popup operations, Settings layout and cleanup. Window lifecycle events and dispatcher shutdown state are recorded too. Test output is retained in TRX; a timeout includes the last BEGIN/END checkpoint in its assertion message.

The exact historical timeout did not recur in the local instrumented full suite, five fresh-process repetitions with DOTNET_PROCESSOR_COUNT=2, or [diagnostic CI run 39937748286](https://github.com/amirzulfadhli/FilePilot/actions/runs/39937748286). The CI window test took 8.86 seconds on a four-processor runner: the STA delegate entered at 2.61 seconds, MainWindow construction took about 3.10 seconds, and theme loads took less than a few milliseconds. Scheduling/cold initialization consumed much more of the deadline than theme loading. These timings show contention sensitivity; they do not prove what blocked the original run.

The confirmed harness defect was its incomplete WPF lifecycle. It created a bare STA thread, called UI operations synchronously, then called Application.Shutdown and exited without running a dispatcher loop. All five legacy repetitions and diagnostic CI finished with shutdown started=false/finished=false, without ContentRendered or Application.Exit. Thus a passing legacy test did not establish rendered-frame or dispatcher-cleanup success. WPF queues application startup and shutdown on its dispatcher; see the [WPF threading model](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model) and [Application implementation](https://source.dot.net/PresentationFramework/System/Windows/Application.cs.html). The missing historical STA stack remains a diagnostic limitation, not a claimed reproduction or a proven product deadlock.

## Correction

- Run the real Application and App.xaml resources on a dedicated STA with Application.Run. Execute the asynchronous window checks on its DispatcherSynchronizationContext and yield to process queued work.
- Await ContentRendered for the real popup and Settings HWNDs, preserving the existing themes, height binding, minimized recovery, document panel and Settings footer assertions. Request application shutdown after cleanup and require the dispatcher to finish before the STA exits.
- Use a small internal startup delegate on App for test isolation. The ordinary public constructor keeps normal startup. Tests load the actual compiled App.xaml on the actual App type, while avoiding production startup's user preferences, second popup, tray and global shortcut. Packaged diagnostics exercise the ordinary constructor separately.
- Give this desktop lifecycle test an exclusive xUnit collection, preventing its cold UI initialization from competing with the suite's native conversion work. Other collections keep their existing parallelism. This uses xUnit's [collection scheduling](https://github.com/xunit/xunit/discussions/2237).
- Keep the original **20-second** limit. No test is skipped, retried into success, or reduced to layout-only verification. Exceptions still fail the test, and window cleanup runs in finally blocks.
- Run Test-WpfReliability.ps1 five times in fresh testhost processes in each CI job. Every iteration must execute and pass exactly one affected test; any failure stops the script. A new process respects WPF's one-Application-per-process rule. CI retains each checkpoint-bearing TRX, including on failure.

## Reproduction and verification

Build Release first. Use fresh report directories; the script refuses existing destinations, paths outside artifacts and reparse-point ancestors.

```powershell
dotnet build FilePilot.slnx -c Release
dotnet test FilePilot.slnx -c Release --no-build --no-restore
pwsh -NoProfile -File scripts/Test-WpfReliability.ps1 -Runs 10 -TestResultsDirectory artifacts/wpf-local-check
```

For real-renderer coverage, set FILEPILOT_LIBREOFFICE to a trusted local program/soffice.com before the full test run, as described in phase-5-documents.md. Without it, three renderer tests explicitly skip. To inspect a single run, use `dotnet test tests/FilePilot.Tests/FilePilot.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~WindowTests --logger 'console;verbosity=detailed'`. A failed BEGIN without END identifies the pending operation; the named ContentRendered wait distinguishes showing a window from completing its first frame.

The corrected local Release build passed with zero warnings/errors and all 162 tests passed with real LibreOffice enabled. Ten additional fresh-process repetitions with DOTNET_PROCESSOR_COUNT=2 passed (maximum 2.28 seconds); every report contains popup/Settings rendering and completed dispatcher shutdown. The processor-count override constrains .NET scheduling, not physical CPU allocation or host load. Independent real-PDF pagination/text/Unicode/image/property inspection and Actionlint passed. All 24 package/script, 8 installer and 21 packaged deployment checks passed. Extracted-package popup/widget smoke runs passed at 1.41/1.87 seconds to first frame, 63.4/66.6 MiB hidden private memory, and less than 3 MiB private growth after 20 popup cycles. These two synthetic sessions do not certify cold-cache performance or long-duration stability. Require both final hosted jobs, including all their WPF repetitions, to pass for the corrected source commit before main integration. Earlier green diagnostic CI does not certify the corrected commit.

Local evidence is under ignored artifacts/wpf-diagnosis, artifacts/results-phase7-wpf-fixed and artifacts/document-acceptance/wpf-fixed. Reports, engine binaries and generated documents are never source-control inputs. Windows 10 remains explicitly unverified; Windows 11 and Windows Server CI results do not extend that claim. Broad accessibility/DPI coverage, long-duration stress and public release approval remain in beta-readiness.md.
