#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA ('Programs/FilePilot/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)))
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') { throw 'This package installer requires Windows x64.' }
if ($InstallDirectory -notmatch '^[A-Za-z]:[\\/]' -or $InstallDirectory.Substring(2).Contains(':')) { throw 'Choose a new absolute local drive-letter installation directory.' }
$parts = $InstallDirectory.Substring(3).Split([char[]]'\/', [StringSplitOptions]::RemoveEmptyEntries)
foreach ($part in $parts) {
    if ($part -in @('.', '..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part.Length -gt 255 -or
        $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
        $part -match '(?i)^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(\.|$)') { throw 'Unsafe installation path component.' }
}
$destination = [IO.Path]::GetFullPath($InstallDirectory)
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($destination))
if ($drive.DriveType -notin @([IO.DriveType]::Fixed, [IO.DriveType]::Removable, [IO.DriveType]::Ram)) { throw 'Network installation paths are not supported.' }
for ($current = $destination; $current; $current = [IO.Path]::GetDirectoryName($current)) {
    if (Test-Path -LiteralPath $current) {
        $attributes = (Get-Item -LiteralPath $current -Force).Attributes
        if ($attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Offline)) { throw 'Installation paths cannot contain reparse points or offline files.' }
    }
}
if (Test-Path -LiteralPath $destination) { throw 'Installation directory already exists. Choose a new directory; existing installations are never overwritten.' }
$archive = (Get-Item -LiteralPath $PackagePath).FullName
# Prevent ordinary archive replacement between validation and extraction.
$stream = [IO.File]::Open($archive, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    & (Join-Path $PSScriptRoot 'Test-Package.ps1') -Path $archive | Out-Host
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        if (@($zip.Entries | Where-Object FullName -IEQ 'filepilot-installation.json').Count) { throw 'Package contains the reserved installation receipt name.' }
        $entry = $zip.GetEntry('package-manifest.json')
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if (-not $manifest.SelfContained) {
            $dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
            if (-not $dotnet) { throw 'Install the .NET 10 Windows Desktop Runtime (x64), then retry.' }
            # A 32-bit host can list a Desktop Runtime that cannot run this x64 package.
            $hostStream = [IO.File]::OpenRead($dotnet.Source)
            $hostReader = [IO.BinaryReader]::new($hostStream)
            try {
                $hostStream.Position = 0x3C
                $peOffset = $hostReader.ReadInt32()
                if ($peOffset -lt 0 -or $peOffset -gt $hostStream.Length - 6) { throw 'The selected dotnet host is not a valid executable.' }
                $hostStream.Position = $peOffset
                if ($hostReader.ReadUInt32() -ne 0x4550 -or $hostReader.ReadUInt16() -ne 0x8664) { throw 'Use an x64 dotnet host with the .NET 10 Windows Desktop Runtime.' }
            }
            finally { $hostReader.Dispose() }
            $runtimes = & $dotnet.Source --list-runtimes
            if ($LASTEXITCODE -ne 0 -or -not ($runtimes -match '^Microsoft\.WindowsDesktop\.App 10\.0\.\d+ ')) { throw 'Install the .NET 10 Windows Desktop Runtime (x64), then retry.' }
        }
        # This operation only creates a fresh tree. Failed/partial installs are retained for inspection.
        [IO.Directory]::CreateDirectory($destination) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToDirectory($zip, $destination)
        $stream.Position = 0
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
        $receipt = @{ SchemaVersion = 1; SourceRevision = $manifest.SourceRevision; SourceDirty = $manifest.SourceDirty; PackageSHA256 = $hash; InstalledUtc = [DateTime]::UtcNow.ToString('O') }
        $receiptFile = [IO.File]::Open((Join-Path $destination 'filepilot-installation.json'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $bytes = [Text.Encoding]::UTF8.GetBytes(($receipt | ConvertTo-Json)); $receiptFile.Write($bytes); $receiptFile.Flush($true) } finally { $receiptFile.Dispose() }
    }
    finally { $zip.Dispose() }
}
finally { $stream.Dispose() }
[pscustomobject]@{ Installed = $true; Directory = $destination; Executable = (Join-Path $destination 'FilePilot.exe'); SourceRevision = $manifest.SourceRevision; SourceDirty = $manifest.SourceDirty }
