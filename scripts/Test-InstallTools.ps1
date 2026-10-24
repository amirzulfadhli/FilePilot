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
$archiveHash = (Get-FileHash -LiteralPath $package).Hash
$destination = Join-Path $root 'FilePilot 文 installed folder'
$result = & $installer -PackagePath $package -InstallDirectory $destination -ExpectedSHA256 $archiveHash.ToLowerInvariant()
if (-not $result.Installed -or -not (Test-Path -LiteralPath $result.Executable)) { throw 'Install did not produce its executable.' }
$receipt = Get-Content -LiteralPath (Join-Path $destination 'filepilot-installation.json') -Raw | ConvertFrom-Json
if ($receipt.PackageSHA256 -cne $archiveHash -or $result.PackageSHA256 -cne $archiveHash) { throw 'Installation receipt/result hash mismatch.' }
# Compare every extracted payload with the already validated manifest.
$manifest = Get-Content -LiteralPath (Join-Path $destination 'package-manifest.json') -Raw | ConvertFrom-Json
foreach ($file in $manifest.Files) {
    if ((Get-FileHash -LiteralPath (Join-Path $destination $file.Path)).Hash -ne $file.Sha256) { throw "Installed payload mismatch: $($file.Path)" }
}
# Initial installation exercises the source tool; refusal/preview cases exercise the delivered copy.
$installer = Join-Path $destination 'deployment/Install-FilePilot.ps1'
$passed = 1
function Assert-Refused([string]$Target, [string]$Archive = $package, [switch]$Preview, [string]$ExpectedHash, [string]$ErrorText) {
    $refused = $false
    $arguments = @{ PackagePath = $Archive; InstallDirectory = $Target; WhatIf = $Preview.IsPresent }
    if ($PSBoundParameters.ContainsKey('ExpectedHash')) { $arguments.ExpectedSHA256 = $ExpectedHash }
    try { & $installer @arguments | Out-Null }
    catch { if ($ErrorText -and -not $_.Exception.Message.Contains($ErrorText)) { throw }; $refused = $true }
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
$preview = & $installer -PackagePath $package -InstallDirectory $previewDestination -ExpectedSHA256 $archiveHash.ToLowerInvariant() -WhatIf
if ($preview.Installed -or -not $preview.Preview -or $preview.Directory -ine $previewDestination -or
    $preview.SourceRevision -cne $manifest.SourceRevision -or $preview.SourceDirty -ne $manifest.SourceDirty -or $preview.PackageSHA256 -cne $archiveHash -or
    (Test-Path -LiteralPath $previewParent)) { throw 'Preview changed the filesystem or returned incorrect provenance.' }
$passed++
Assert-Refused $destination -Preview
if ([IO.File]::ReadAllText($sentinel) -cne 'Preserve existing work') { throw 'Preview modified an existing installation.' }
Assert-Refused $refusedDirectory $corrupt -Preview
if (Test-Path -LiteralPath $refusedDirectory) { throw 'Preview created a directory for an invalid archive.' }
$wrongHash = $(if ($archiveHash[0] -eq '0') { '1' } else { '0' }) + $archiveHash.Substring(1)
foreach ($previewOnly in @($false, $true)) {
    $target = Join-Path $root ('mismatch-parent-' + $previewOnly + '/version')
    Assert-Refused $target -ExpectedHash $wrongHash -Preview:$previewOnly -ErrorText 'SHA-256 does not match'
    if (Test-Path -LiteralPath (Split-Path $target)) { throw 'Checksum mismatch created a destination parent.' }
}
foreach ($badHash in @('bad', ($archiveHash + "`n"))) {
    $target = Join-Path $root 'malformed-hash'
    Assert-Refused $target -ExpectedHash $badHash
    if (Test-Path -LiteralPath $target) { throw 'Malformed checksum created a destination.' }
}
$boundDestination = Join-Path $root 'checksum-bound delivered install'
$bound = & $installer $package $boundDestination -ExpectedSHA256 $archiveHash
$boundReceipt = Get-Content -LiteralPath (Join-Path $boundDestination 'filepilot-installation.json') -Raw | ConvertFrom-Json
if (-not $bound.Installed -or $bound.PackageSHA256 -cne $archiveHash -or $boundReceipt.PackageSHA256 -cne $archiveHash) { throw 'Delivered checksum-bound installation failed.' }
$passed++
[pscustomobject]@{ Passed = $passed; Failed = 0; InstalledExecutable = $result.Executable; Fixtures = $root }
