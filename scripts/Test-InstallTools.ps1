#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root = Join-Path $repository ('artifacts/install-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$installer = Join-Path $PSScriptRoot 'Install-FilePilot.ps1'
$package = (Get-Item -LiteralPath $PackagePath).FullName
$destination = Join-Path $root 'FilePilot 文 installed folder'
$result = & $installer -PackagePath $package -InstallDirectory $destination
if (-not $result.Installed -or -not (Test-Path -LiteralPath $result.Executable)) { throw 'Install did not produce its executable.' }
$receipt = Get-Content -LiteralPath (Join-Path $destination 'filepilot-installation.json') -Raw | ConvertFrom-Json
if ($receipt.PackageSHA256 -ne (Get-FileHash -LiteralPath $package).Hash) { throw 'Installation receipt hash mismatch.' }
# Compare every extracted payload with the already validated manifest.
$manifest = Get-Content -LiteralPath (Join-Path $destination 'package-manifest.json') -Raw | ConvertFrom-Json
foreach ($file in $manifest.Files) {
    if ((Get-FileHash -LiteralPath (Join-Path $destination $file.Path)).Hash -ne $file.Sha256) { throw "Installed payload mismatch: $($file.Path)" }
}
# Initial installation exercises the source tool; refusal/preview cases exercise the delivered copy.
$installer = Join-Path $destination 'deployment/Install-FilePilot.ps1'
$passed = 1
function Assert-Refused([string]$Target, [string]$Archive = $package, [switch]$Preview) {
    $refused = $false
    $arguments = @{ PackagePath = $Archive; InstallDirectory = $Target; WhatIf = $Preview.IsPresent }
    try { & $installer @arguments | Out-Null }
    catch { $refused = $true }
    if (-not $refused) { throw "Unsafe install was accepted: $Target" }
    $script:passed++
}
$sentinel = Join-Path $destination 'keep.txt'
[IO.File]::WriteAllText($sentinel, 'Preserve existing work')
Assert-Refused $destination
if ([IO.File]::ReadAllText($sentinel) -cne 'Preserve existing work') { throw 'An existing installation was modified.' }
Assert-Refused 'relative-folder'
Assert-Refused (Join-Path $root '../traversal')
Assert-Refused '\\server\share\FilePilot'
Assert-Refused ($destination + ':stream')
Assert-Refused (Join-Path $root 'CON')
$corrupt = Join-Path $root 'corrupt.zip'
[IO.File]::WriteAllText($corrupt, 'Not a package')
$refusedDirectory = Join-Path $root 'invalid-package'
Assert-Refused $refusedDirectory $corrupt
if (Test-Path -LiteralPath $refusedDirectory) { throw 'Invalid archive created an installation directory.' }
$previewParent = Join-Path $root 'preview parent'
$previewDestination = Join-Path $previewParent 'version 文'
$preview = & $installer -PackagePath $package -InstallDirectory $previewDestination -WhatIf
if ($preview.Installed -or -not $preview.Preview -or $preview.Directory -ine $previewDestination -or
    $preview.SourceRevision -cne $manifest.SourceRevision -or $preview.SourceDirty -ne $manifest.SourceDirty -or
    (Test-Path -LiteralPath $previewParent)) { throw 'Preview changed the filesystem or returned incorrect provenance.' }
$passed++
Assert-Refused $destination -Preview
if ([IO.File]::ReadAllText($sentinel) -cne 'Preserve existing work') { throw 'Preview modified an existing installation.' }
Assert-Refused $refusedDirectory $corrupt -Preview
if (Test-Path -LiteralPath $refusedDirectory) { throw 'Preview created a directory for an invalid archive.' }
[pscustomobject]@{ Passed = $passed; Failed = 0; InstalledExecutable = $result.Executable; Fixtures = $root }
