#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path, [ValidateRange(1, 10)][int]$Runs = 3)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Run extracted-package acceptance on Windows.' }
$archive = (Get-Item -LiteralPath $Path).FullName
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = Join-Path $repository ('artifacts/package-acceptance-' + [Guid]::NewGuid().ToString('N'))
# Hold a read-sharing-only handle across validation/extraction to prevent ordinary replacement.
$stream = [IO.File]::Open($archive, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    & (Join-Path $PSScriptRoot 'Test-Package.ps1') -Path $archive | Out-Host
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
    try { [IO.Compression.ZipFileExtensions]::ExtractToDirectory($zip, $destination) }
    finally { $zip.Dispose() }
}
finally { $stream.Dispose() }
$result = & (Join-Path $PSScriptRoot 'Measure-FilePilot.ps1') -ExecutablePath (Join-Path $destination 'FilePilot.exe') -Runs $Runs
[pscustomobject]@{ Passed = $result.Passed; ExtractedPackage = $destination; Summary = $result.Summary }
