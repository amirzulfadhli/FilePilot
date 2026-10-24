# Beta setup quick start

0.1.0-beta.1 is an unreleased candidate. Public release publication requires owner approval. These instructions describe the current x64 framework-dependent package; they do not announce a download or approve a beta release.

## Requirements and package checks

Use Windows x64 with the [.NET 10 Windows Desktop Runtime, x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). The SDK is only needed to build from source. Deployment scripts require PowerShell 7.2 or later (`pwsh`); the portable app does not require PowerShell. Windows 11 has local acceptance coverage. Windows 10 remains explicitly unverified; see the OS limitations in README.md.

Obtain the intended ZIP and its expected SHA-256 through a trusted maintainer channel. A checksum shipped beside an untrusted archive does not authenticate its publisher. Compare the expected value before running anything:

```powershell
Get-FileHash -LiteralPath 'C:\Downloads\FilePilot-win-x64.zip' -Algorithm SHA256
```

Extract the whole ZIP into a new local folder you own. Keep the native libraries, notices, docs and deployment folder together. From that folder in PowerShell 7, validate its archive inventory and inspect provenance:

```powershell
& .\deployment\Test-Package.ps1 -Path 'C:\Downloads\FilePilot-win-x64.zip'
Get-Content -LiteralPath .\package-manifest.json -Raw | ConvertFrom-Json |
    Select-Object SourceRevision, SourceDirty, Runtime, SelfContained
```

For an approved candidate, require the intended source revision, `SourceDirty=False` and successful build/deployment plus real-document CI for that revision. Validation checks integrity and inventory, not publisher identity. If Windows or an organization blocks this unsigned candidate, stop and ask the maintainer or administrator; these instructions do not bypass execution policy or security warnings.

## Try the portable widget

Double-click FilePilot.exe, or run `& .\FilePilot.exe --widget`. No autostart entry is created. In Explorer, select an ordinary local file and press Ctrl+Alt+F (configurable in Settings). If selection cannot be detected, choose files with Ctrl+O. Press F1 for offline help. Escape hides the resident widget; Alt+X exits it when idle. Exit before moving its folder or changing versions.

Conversions preserve originals and refuse occupied output names. Native image batches run sequentially; cancellation retains any completed outputs. DOCX export separately requires a trusted local LibreOffice installation configured in Settings. FilePilot does not download or bundle that engine. Use disposable files for first-run checks.

## Optional per-user installation

From the unpacked candidate, preview a new destination, then install it. The preview validates the package, path and installed runtime but creates no installation folders or receipt. It does not predict future disk space, permissions or file-lock changes. A preview reports `Installed=False`; check that the real installation reports `Installed=True` before activation.

```powershell
$archive = 'C:\Downloads\FilePilot-win-x64.zip'
$destination = Join-Path $env:LOCALAPPDATA 'Programs/FilePilot/beta-check-1'
& .\deployment\Install-FilePilot.ps1 -PackagePath $archive -InstallDirectory $destination -WhatIf
$installed = & .\deployment\Install-FilePilot.ps1 -PackagePath $archive -InstallDirectory $destination
$installed | Format-List
```

Choose a fresh directory for each version. Existing directories are refused. A failed partial installation is retained and reported; do not recursively delete a directory containing your own files. Installation alone neither starts FilePilot nor registers Explorer integration.

Exit all running FilePilot instances before activation. This creates the per-user Start Menu shortcut and records managed version state:

```powershell
$manager = Join-Path $installed.Directory 'deployment/Manage-FilePilot.ps1'
& $manager -Action Activate -InstallDirectory $installed.Directory -WhatIf
& $manager -Action Activate -InstallDirectory $installed.Directory
```

Add `-RegisterContextMenu` to activation only if wanted. It writes HKCU classic single-file Explorer verbs; Windows 11 exposes them through Show more options. It does not restart Explorer. Reopen the widget from Start after activation. Normal actions require no administrator privileges.

## Upgrade, roll back or remove

Install a later trusted candidate into another new folder. Exit the current widget, then activate the new version using its packaged manager. The previous directory and settings are retained. `& $manager -Action Rollback` switches to the retained previous version; it does not undo file actions or delete the new version.

To remove one managed installation, exit FilePilot and preview first:

```powershell
& $manager -Action Uninstall -InstallDirectory $installed.Directory -WhatIf
& $manager -Action Uninstall -InstallDirectory $installed.Directory
```

Removal refuses changed/unexpected payloads and running instances, preserves preferences and other versions, and supports retrying an interrupted owned removal. Keep manager tools from another unpacked/retained package available for recovery. See [deployment behavior and limitations](phase-6-usability.md) for custom state paths and interrupted operations. Portable folders without an installation receipt are not managed installations; exit the app before relocating them.

## Feedback and remaining gates

Use [beta readiness](beta-readiness.md) for owner acceptance and the current test limits. Include the source revision, Windows/runtime/engine versions and disposable reproduction steps when reporting an issue. Redact personal paths and document contents. No telemetry or automatic crash upload is present.

Windows 10, multiple physical monitors, live contrast switching, screen-reader speech, hours-long sessions and representative complex DOCX layout remain unverified. Graphical setup, code signing, Apps & Features registration, ARM64/x86 packages and automatic updates remain unimplemented.
