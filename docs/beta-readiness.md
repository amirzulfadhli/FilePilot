# Beta readiness — 0.1.0-beta.1 candidate

The version identifies a candidate build; it does not approve distribution. The project owner approved integrating completed feature branches into main on 2026-10-23, after passing checks. Release tags and public release publication still require separate approval. CI retains short-lived test artifacts only. There is no deployment/release publishing job or write-scoped repository token.

## CI and evidence

FilePilot CI runs on GitHub-hosted Windows Server 2025 for pushes to main/feature/codex branches and pull requests. Official checkout/setup-dotnet/upload-artifact actions are pinned to reviewed commit SHAs, repository permission is contents:read, checkout credentials are not persisted, and candidate ZIP/checksum/TRX artifacts expire after seven days. No user credentials or cloud document services are used. CI uploads only the explicit report/archive paths, not the whole artifacts directory.

The build job runs the existing Release/package pipeline (including package/script checks), then installer and upgrade/rollback/uninstall checks against the resulting ZIP. It requires a clean checkout and records its commit/hash manifest. Without an engine, three real-LibreOffice tests are explicitly skipped; a green build job alone is not document-rendering evidence. Windows Server runner success does not certify Windows 10 or all Windows 11 configurations.

Real LibreOffice acceptance runs on pull requests and pushes to main, the Phase 7 beta-validation branch, codex/phase-8-stabilization, codex/phase-9-release-readiness and codex/phase-10-beta-distribution. It can also be requested through workflow_dispatch(real_documents=true). Other feature/codex branches still receive the build/package checks. It downloads the fixed official LibreOffice 26.8.1 MSI, verifies SHA-256 and publisher signature, and installs it plus Microsoft's signed x64 C++ runtime only on a disposable GitHub-hosted runner. The setup script refuses ordinary/local/self-hosted environments and is not included in FilePilot's deployment payload. It runs all tests with the real engine enabled. The app still neither downloads nor bundles LibreOffice.

The real-engine test validates export/source preservation/collisions, native-renderer cancellation/exit, deadline/malformed input and cleanup. The document job also uses pinned development-only Python tools to parse the synthetic PDF independently, check A4 pagination/text/Unicode/image/source-property policy and render page PNGs. It retains those synthetic page images and the inspection JSON for review. Neither a CI TRX nor a parsed fixture certifies arbitrary Word layout: visual fidelity with representative documents remains an owner gate. Reproduce inspection with scripts/Inspect-DocumentPdf.py and the Phase 5 guide. GitHub Actions logs/run results are the authority for whether hosted jobs actually ran; workflow source presence is not a passing CI claim.

## Local preflight

```powershell
pwsh -NoProfile -File scripts/Build-Package.ps1 -OutputDirectory artifacts/packages/beta-check -TestResultsDirectory artifacts/beta-results
pwsh -NoProfile -File scripts/Test-InstallTools.ps1 -PackagePath artifacts/packages/beta-check/FilePilot-win-x64.zip
pwsh -NoProfile -File scripts/Test-DeploymentTools.ps1 -PackagePath artifacts/packages/beta-check/FilePilot-win-x64.zip
pwsh -NoProfile -File scripts/Test-ExtractedPackage.ps1 -Path artifacts/packages/beta-check/FilePilot-win-x64.zip -Runs 3
```

Use new result/output directories. Set FILEPILOT_LIBREOFFICE to a trusted program/soffice.com and FILEPILOT_DOCUMENT_TEST_OUTPUT to an ignored artifact directory for real-renderer tests. Review staged source, secret/artifact exclusions, clean source revision and ZIP hashes before committing. Never force-push, silently overwrite user files, or weaken Windows execution policy to run setup.

## Owner acceptance before a beta release

- [x] Approve main integration (2026-10-23); completed feature branches must pass checks and retain their history.
- [ ] Approve a public release separately; review distribution terms and signing plan.
- [ ] Review successful build/deployment and real-renderer CI runs for the intended commit.
- [ ] Validate candidate ZIP/checksum, notices, candidate version, setup/rollback/uninstall instructions and source provenance.
- [ ] Inspect representative real DOCX PDFs visually, including installed fonts, tables/images, page breaks and property policy.
- [ ] Test actual Explorer right-click launch, picker cancellation, favorites/rename/tray, shortcut conflicts and stale-selection fallback.
- [ ] Complete Windows 10 testing on a supported .NET edition or approve/document a narrower supported OS list. It remains explicitly unverified here.
- [ ] Review multi-monitor/DPI, small work areas, keyboard navigation, screen-reader/high-contrast and longer-duration memory/CPU behavior.
- [ ] Decide whether unsigned portable/PowerShell setup is acceptable for the beta; graphical setup, signing, Apps & Features entry, ARM64/x86 and automatic upgrades are not implemented.

## Reporting problems

Include the version shown in Settings, package/source revision, Windows edition/build, runtime/engine version, action, expected/actual result and reproduction steps with disposable files. Redact personal paths and document contents. Do not upload credentials, private documents or whole diagnostic directories. FilePilot has no telemetry, background reporting or automatic crash upload; issue submission is a user action.

Local Phase 7 checks: Release build had zero warnings/errors; all 162 tests passed with the real engine enabled and TRX output was retained. All 24 packaging checks, 8 installer checks and 21 packaged lifecycle checks passed. Actionlint 1.7.12 accepted the workflow. The CI-only engine setup script refused the local workspace as intended, before installation. File metadata reports 0.1.0-beta.1, and the updated ready feedback was observed in the popup. A later Settings capture failed with "no screenshot targets found"; the new version footer was not manually certified by that attempt. The Phase 6 fixed-footer/focus/Escape and light/dark interactive checks remain recorded separately. No saved preferences were changed by these checks.
The pinned CI PDF inspection tool versions also passed the local synthetic-PDF checks. [Hosted run 39892774960](https://github.com/amirzulfadhli/FilePilot/actions/runs/39892774960) passed both jobs for commit 8b4c1d6f3fd250cac6501b43f7845830f09f0606. The real-engine report contains 162 passed tests with no skips; independent PDF inspection passed and both rendered synthetic pages were visually reviewed. The clean hosted package also passed extracted-package startup/memory smoke checks on this Windows 11 host. These are fixture/smoke results, not certification of arbitrary documents, extended sessions or other Windows editions.

Phase 7 implementation is complete. The unchecked items above remain release acceptance gates and carry forward into Phase 8 stabilization. They do not authorize publishing a release. Before main integration, rerun local build/package/deployment checks and require successful hosted jobs for the final Phase 7 commit; do not infer success from the earlier run alone.

Final Phase 7 local preflight (2026-10-23): Release build passed with zero warnings/errors, all 162 tests passed with the real engine, and 24 package/script, 8 installer and 21 packaged deployment lifecycle checks passed again. The packaged Windows 11 popup and Settings window were opened interactively: the 0.1.0-beta.1 footer and fixed Save/Cancel controls were visible, shortcut text was selected on entry, and Escape returned to the popup without saving. This successful retry supersedes the earlier failed capture for the version footer only; it does not complete the broader accessibility or Explorer acceptance checklist.

Subsequent run 39936011763 failed the WPF STA test despite local success; main integration was stopped. [STA stabilization](phase-7-sta-stabilization.md) records the investigation, confirmed dispatcher-lifecycle defect, scheduling evidence, corrected harness, repeat checks and limits of the historical diagnosis. Each CI job now adds five fresh-process WPF repetitions and retains their TRX checkpoints. Require green checks for the corrected commit before merging; prior local or hosted passes do not override a failed gate.

[Run 39942231530](https://github.com/amirzulfadhli/FilePilot/actions/runs/39942231530) passed both jobs for dd6ec8d139eab16ba8886abb49965469f533006d: all 163 real-engine tests passed without skips and both jobs passed 15 separate-process desktop/memory checks. Reports and the clean candidate ZIP were downloaded and validated. The owner-approved main integration then fast-forwarded from cab070d to dd6ec8d, including all completed feature branch heads without rewriting history. [Phase 8 preparation](phase-8-preparation.md) carries forward the unverified release gates and historical diagnostic uncertainties. The expanded real-renderer trigger policy above is introduced on the Phase 8 branch; the integrated Phase 7 workflow runs real documents automatically on its beta branch, with manual dispatch available on main.

Phase 8 implementation and available-host acceptance are recorded in [widget stabilization](phase-8-stabilization.md). The suite now has 169 tests. Contrast resources, primary-action focus, live-region events and scaled layout checks are implemented; OS contrast switching and screen-reader speech remain manual release gates. CI additionally checks extracted-package startup, private memory and native compatibility. Main integration continues to require both jobs green for the exact candidate commit.
