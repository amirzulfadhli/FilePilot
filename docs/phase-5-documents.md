# Phase 5 — first document conversion slice

Status: Phase 5 acceptance completed on the available Windows 11 host using real LibreOffice 26.8.1.1. Windows 10 remains explicitly unverified. This covers the conservative, single-DOCX feature boundary below, not arbitrary Word documents or pixel-identical Microsoft Word layout.

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

Each job has a random, owned `.filepilot-doc-<id>` directory in the validated local TEMP folder. Native LibreOffice profile files failed under deeply nested destinations during real testing; the short engine workspace fixes this while destination-side AtomicOutput still provides safe finalization. TEMP roots longer than 120 characters are refused with guidance. Its source copy, output, and LibreOffice user profile are isolated from the original and the user's normal office profile. The generated profile requests disabled macro execution, active content and OLE automation. The settings are now written to the actual `profile/user/registrymodifications.xcu` location and survived a real export. This does not certify resistance to all active-content exploits; preflight still refuses such documents. Inspect the [engine configuration definitions](https://github.com/LibreOffice/core/blob/master/officecfg/registry/schema/org/openoffice/Office/Common.xcs) when upgrading LibreOffice.

The adapter uses `ProcessStartInfo.ArgumentList` without a shell, handles Unicode/spaces, drains both output streams with only bounded diagnostic text retained, and kills its owned process tree on cancellation/timeout. If the process cannot be confirmed stopped, its work directory is retained and reported instead of racing deletion. Otherwise owned job work is cleaned. Cleanup failures identify the retained location; if finalization already succeeded, check the output folder before retrying.

A successful engine result must contain a bounded PDF with `%PDF-` and `%%EOF` markers. These checks detect obvious empty/wrong/incomplete output; they do not validate PDF semantics or rendering fidelity. The completed bytes reuse `AtomicOutput` for flush and same-directory no-overwrite finalization. Nothing replaces the source or an existing output. Before export, FilePilot removes docProps parts (core, extended, custom properties and thumbnails) and their package references from its owned DOCX copy. The isolated profile requests removal of personal user, note, revision and printer information. Real PDF inspection confirmed no source author, title, subject, keywords or custom-property sentinels. PDF Creator/Producer and a new export CreationDate remain. This is property minimization, not anonymization: visible text, images, comments/revisions and embedded image data may identify people. Inspect sensitive outputs before sharing.

## Widget usability changes

- Ctrl+O opens the existing multi-file picker, with the same busy-state guard.
- Escape closes an open image-format or favorite-folder dropdown first; the next Escape dismisses the popup.
- Empty single-file batch details are hidden.
- Missing/removed document engines disable PDF export and explain the setup requirement; saved theme/shortcut/favorites still load.

## Validation on 23 October 2026

Release build: zero warnings/errors. **161 tests passed, zero skipped**, with FILEPILOT_LIBREOFFICE configured. This includes 158 regular tests and three opt-in real-renderer tests, plus all earlier file/image/widget regressions. All 22 packaging/script checks passed. NuGet reported no known vulnerable direct/transitive packages from its current feed.

Real engine: LibreOffice 26.8.1.1 (bf6ef2662401c7d6dce671ab4deb26ee0af3a7c1), Windows x64, Windows 11 build 26200. The official MSI's SHA-256 was C6298B10CFA1748BCBEDAD94C62CE771E240E5021BFD6D7A967CD571B04B5312, matching its official mirror listing, with a valid The Document Foundation signature. It was administratively extracted under ignored artifacts, not installed system-wide or bundled with FilePilot. Its missing C++ runtime was supplied app-locally from Microsoft's signed v14.51.36247 x64 redistributable. This verifies that test setup; users should install/repair LibreOffice and its supported prerequisites normally. No system runtime DLLs were replaced.

The synthetic, generated DOCX contains styled Latin text, a bordered table, a PNG, Chinese text, A4 margins and an explicit page break. The final PDF was parsed with pypdf and rendered with PDFium; both pages were visually inspected. Two A4 pages, text, Unicode, image and source-property removal checks passed. Fonts/layout looked intact for this fixture; no comparison with Microsoft Word was available, and this is not broad Word-fidelity certification. Creator=Writer, Producer=LibreOffice 26.8.1.1 and a new CreationDate remain.

Real tests exercised source SHA-256/timestamp preservation, repeated-output collision refusal, cancellation after soffice.bin appeared, confirmed native renderer exit, owned-work cleanup, a 1 ms deadline, and LibreOffice's response to malformed input. The regular suite covers preflight corruption/active content, limits, permissions/locks, invalid/missing/oversized output, late collisions, diagnostic bounds, initialization failure, and inability to stop an engine. Failed exploratory runs exposed the profile-location, native-runtime diagnostics and long-profile-path issues fixed in this change; only the final passing run counts as acceptance.

To reproduce (Windows, trusted LibreOffice already available):

```powershell
$env:FILEPILOT_LIBREOFFICE = 'C:\Program Files\LibreOffice\program\soffice.com'
$env:FILEPILOT_DOCUMENT_TEST_OUTPUT = Join-Path $PWD 'artifacts/document-acceptance/my-run'
New-Item -ItemType Directory $env:FILEPILOT_DOCUMENT_TEST_OUTPUT
# Use a new output directory for each acceptance run.
dotnet build FilePilot.slnx -c Release
dotnet test FilePilot.slnx -c Release --no-build --no-restore
# Development-only Python packages: pypdf, pypdfium2, Pillow (not app dependencies).
python scripts/Inspect-DocumentPdf.py $env:FILEPILOT_DOCUMENT_TEST_OUTPUT
if ($LASTEXITCODE -ne 0) { throw 'PDF inspection failed' }
```

Without FILEPILOT_LIBREOFFICE the three real-engine tests are explicitly skipped; synthetic process tests cannot replace them. Generated DOCX/PDF/page PNG/profile reports are acceptance artifacts, never shipped. Inspect the rendered PNGs after the assertions.

Remaining release limits: Windows 10 (explicitly unverified by user agreement), other engine versions/architectures, arbitrary fonts/complex Word layouts, full accessibility/high contrast and multi-monitor/DPI acceptance, hostile native-engine security, native process memory/disk quotas, DOCX batches and broader media/formats. Keep the separately installed engine patched. Phase 6 follows with settings/widget usability, performance and per-user Windows deployment work.

Interactive Windows 11 checks also ran: real widget DOCX export, visible busy indicator and disabled conflicting actions, success feedback with original still selected, missing-engine export disabled, Ctrl+O/native-picker cancellation, light/dark theme rendering and Settings keyboard submission. The temporary engine preference used for UI testing was restored afterward. Settings Save required scrolling and Escape did not dismiss Settings; these usability findings are queued for Phase 6. Open-folder invocation was exercised; Explorer's resulting path is recorded only if observed below.
Explorer exposed the document-ui output folder after Open PDF folder; the launch was observed successfully.
