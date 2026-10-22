# Phase 5 — first document conversion slice

Status: implemented on `feature/phase-5-documents`, with adapter/file-safety tests. **Actual LibreOffice rendering has not run on this machine** because no LibreOffice installation was found in the standard locations or PATH. The process fixture used in tests is synthetic, is never shipped, and does not render documents. This is not a completed document-rendering acceptance claim.

## Engine choice and setup

FilePilot keeps C#/.NET 10, WPF and its existing file/image services. DOCX layout needs a document renderer; extending the image codec would not provide Word layout semantics. This slice adds an optional, separately installed local LibreOffice process through an `IDocumentPdfEngine` adapter. No new runtime NuGet dependency, bundled office suite, cloud API, account, or background service is added.

LibreOffice documents [headless Windows CLI use through soffice.com](https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html) and [Writer PDF export](https://help.libreoffice.org/latest/en-US/text/shared/guide/pdf_params.html). Its [license information](https://www.libreoffice.org/licenses/) describes the MPL-based product licensing. The engine is not redistributed in FilePilot's ZIP; users retain their own LibreOffice installation and its notices. Check the engine's own supported OS/runtime requirements independently of FilePilot.

1. Install/maintain LibreOffice locally from a trusted source. FilePilot does not download or install it.
2. Open FilePilot Settings and choose `program/soffice.com` from that installation. Windows console/headless use deliberately requires `soffice.com`, not a shell command or PATH lookup. Clearing this optional field disables document export without affecting image/file actions.
3. Choose one ordinary `.docx` through Ctrl+O, the widget shortcut, or a quoted file-path launch. The existing Explorer context-menu registration remains image-only in this slice.
4. Review the destination folder and choose Export PDF (Alt+P). Existing outputs are refused, including late collisions. Cancel export requests cancellation; file actions stay disabled until the owned engine has stopped.
5. Open PDF folder and inspect layout, fonts, page breaks, links and metadata before sharing. The source remains selected and unchanged. DOCX batches, DOC/DOCM, password-protected/encrypted documents and other document formats are deferred.

## Boundaries and reliability

`DocxPdfConverter` validates/copies the source with a read-sharing-only handle, so ordinary writes/replacement are blocked during the job. It reads actual ZIP/XML content off the UI thread and rejects corrupted packages, unsafe/duplicate entry names, XML DTDs, macros/embedded objects, SVG/other unsupported media, external relationships (including ordinary external hyperlinks/templates), and fields that can load external/active content. This conservative first version accepts XML and embedded PNG/JPEG parts; some otherwise valid Word documents will be refused. It is a preflight policy, not a complete OOXML schema validator or a security sandbox.

Default limits: 32 MiB compressed source, 128 MiB actual expanded bytes, 2,048 entries, 8 MiB per XML part, 16 million pixels per embedded PNG/JPEG (64 million across the package, 16,384 pixels per edge, animations refused), 64 MiB completed PDF, and a 90-second engine deadline. These are guardrails, not hard native process memory/disk quotas. Keep the native engine patched and use trusted documents/engines. FilePilot makes no document-network requests and refuses explicit external content, but it does not impose an OS network sandbox on a separately installed engine.

Each job has a random, owned `.filepilot-doc-<id>` directory inside the chosen destination. Its source copy, output, and LibreOffice user profile are isolated from the original and the user's normal office profile. The generated profile requests disabled macro execution, active content and OLE automation. Enforcement/fidelity still needs real-engine acceptance testing; inspect the [engine configuration definitions](https://github.com/LibreOffice/core/blob/master/officecfg/registry/schema/org/openoffice/Office/Common.xcs) when upgrading LibreOffice.

The adapter uses `ProcessStartInfo.ArgumentList` without a shell, handles Unicode/spaces, drains both output streams with only bounded diagnostic text retained, and kills its owned process tree on cancellation/timeout. If the process cannot be confirmed stopped, its work directory is retained and reported instead of racing deletion. Otherwise owned job work is cleaned. Cleanup failures identify the retained location; if finalization already succeeded, check the output folder before retrying.

A successful engine result must contain a bounded PDF with `%PDF-` and `%%EOF` markers. These checks detect obvious empty/wrong/incomplete output; they do not validate PDF semantics or rendering fidelity. The completed bytes reuse `AtomicOutput` for flush and same-directory no-overwrite finalization. Nothing replaces the source or an existing output. Metadata scrubbing for document PDFs is not implemented/verified; the image converter's metadata policy does not imply a document policy. Review document/PDF metadata before sharing.

## Widget usability changes

- Ctrl+O opens the existing multi-file picker, with the same busy-state guard.
- Escape closes an open image-format or favorite-folder dropdown first; the next Escape dismisses the popup.
- Empty single-file batch details are hidden.
- Missing/removed document engines disable PDF export and explain the setup requirement; saved theme/shortcut/favorites still load.

## Validation and remaining work

Release build: zero warnings/errors. All **156 application tests** passed without skips (including the original file/image suite). New checks cover DOCX limits/content refusal, source content/timestamp preservation, early/late file and folder collisions, output markers/size, locks, cancellation and work cleanup, safe engine arguments, and UI/settings busy-state recovery. An isolated test-only executable exercises real process launch, noisy stdout/stderr, exit failure, timeout and cancellation. WPF tests load the document panel and settings and verify the Open command binding. No real LibreOffice rendering is implied by these tests. All 22 packaging/script checks passed; the Phase 5 ZIP validated with 12 payload files (including both offline setup/validation documents) plus its manifest and no test executable/office engine bundled. Two extracted-package startup/memory/image-codec smoke sessions (one per mode) stayed within the existing budgets.

Remaining: real LibreOffice conversion and visual PDF checks with representative text/tables/images/fonts, real profile/active-content settings validation, engine/version/OS acceptance (including Windows 10), manual native-picker/keyboard checks, PDF metadata policy, and broader format/media support. Desktop automation did not expose a targetable app window during this slice, so new interactive UI behavior has not been manually certified. Phase 5 remains in progress.
