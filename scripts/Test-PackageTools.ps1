#requires -Version 7.2
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root = Join-Path $repository ('artifacts/package-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$required = @('FilePilot.exe', 'FilePilot.dll', 'FilePilot.Core.dll', 'FilePilot.deps.json',
    'FilePilot.runtimeconfig.json', 'SkiaSharp.dll', 'libSkiaSharp.dll', 'README.md',
    'notices/SkiaSharp-LICENSE.txt', 'notices/SkiaSharp-NATIVE-NOTICES.txt')
$script:passed = 0
foreach ($source in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
    $tokens = $null
    $syntaxErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($source.FullName, [ref]$tokens, [ref]$syntaxErrors) | Out-Null
    if ($syntaxErrors.Count) { throw "PowerShell syntax errors in $($source.Name): $($syntaxErrors.Message -join '; ')" }
}
$script:passed++

function New-FixtureZip {
    param([string]$Extra, [string]$Duplicate, [string]$Corrupt, [string]$Omit, [string]$RecordPath,
        [switch]$BadLength, [switch]$NoManifest, [switch]$BadSchema, [switch]$BadJson, [switch]$BadRevision)
    $path = Join-Path $root ([Guid]::NewGuid().ToString('N') + '.zip')
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $records = @()
        $names = @($required | Where-Object { $_ -ne $Omit })
        foreach ($name in $names) {
            $bytes = [Text.Encoding]::UTF8.GetBytes("fixture: $name")
            $entry = $zip.CreateEntry($name)
            $stream = $entry.Open()
            try {
                $contents = if ($name -eq $Corrupt) { [Text.Encoding]::UTF8.GetBytes(('x' * $bytes.Length)) } else { $bytes }
                $stream.Write($contents, 0, $contents.Length)
            }
            finally { $stream.Dispose() }
            $records += [ordered]@{ Path = $name; Length = [long]$bytes.Length; Sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) }
        }
        if ($Extra) { $zip.CreateEntry($Extra) | Out-Null }
        if ($Duplicate) { $zip.CreateEntry($Duplicate) | Out-Null }
        if ($RecordPath) { $records[0]['Path'] = $RecordPath }
        if ($BadLength) { $records[0]['Length']++ }
        if (-not $NoManifest) {
            $schema = if ($BadSchema) { 99 } else { 1 }
            $revision = if ($BadRevision) { 'bad' } else { '0' * 40 }
            $manifest = @{ SourceRevision = $revision; SourceDirty = $false; SchemaVersion = $schema; Runtime = 'win-x64'; SelfContained = $false; Files = $records }
            $text = if ($BadJson) { '{' } else { $manifest | ConvertTo-Json -Depth 5 }
            $writer = [IO.StreamWriter]::new($zip.CreateEntry('package-manifest.json').Open())
            try { $writer.Write($text) }
            finally { $writer.Dispose() }
        }
    }
    finally { $zip.Dispose() }
    return $path
}

function Assert-Rejected([string]$Archive, [string]$Pattern) {
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Test-Package.ps1') -Path $Archive | Out-Null }
    catch {
        if ($_.Exception.Message -notlike "*$Pattern*") { throw "Unexpected rejection: $($_.Exception.Message) (expected $Pattern)" }
        $rejected = $true
    }
    if (-not $rejected) { throw "Validator accepted invalid archive: $Archive" }
    $script:passed++
}

$result = & (Join-Path $PSScriptRoot 'Test-Package.ps1') -Path (New-FixtureZip)
if (-not $result.Valid -or $result.FileCount -ne $required.Count) { throw 'Valid fixture was rejected.' }
$script:passed++
Assert-Rejected (New-FixtureZip -Corrupt 'FilePilot.dll') 'Hash mismatch'
Assert-Rejected (New-FixtureZip -BadLength) 'length mismatch'
Assert-Rejected (New-FixtureZip -Omit 'libSkiaSharp.dll') 'Required runtime'
Assert-Rejected (New-FixtureZip -Omit 'notices/SkiaSharp-LICENSE.txt') 'Required runtime'
Assert-Rejected (New-FixtureZip -NoManifest) 'manifest is missing'
Assert-Rejected (New-FixtureZip -BadSchema) 'invalid package manifest'
Assert-Rejected (New-FixtureZip -BadRevision) 'invalid package manifest'
Assert-Rejected (New-FixtureZip -BadJson) 'JSON'
Assert-Rejected (New-FixtureZip -Extra 'unlisted.txt') 'unlisted file'
Assert-Rejected (New-FixtureZip -Extra 'debug.pdb') 'not allowed'
Assert-Rejected (New-FixtureZip -Extra '.env') 'not allowed'
Assert-Rejected (New-FixtureZip -Extra '../outside.txt') 'Unsafe package path'
Assert-Rejected (New-FixtureZip -Extra '/absolute.txt') 'Unsafe package path'
Assert-Rejected (New-FixtureZip -Extra 'C:/absolute.txt') 'Unsafe package path'
Assert-Rejected (New-FixtureZip -Extra 'folder\bad.txt') 'Unsafe package path'
Assert-Rejected (New-FixtureZip -Extra 'folder/CON.txt') 'Unsafe package path'
Assert-Rejected (New-FixtureZip -Duplicate 'filepilot.DLL') 'Duplicate package path'
Assert-Rejected (New-FixtureZip -RecordPath '../bad.dll') 'Unsafe package path'

if ($IsWindows) {
    $marker = Join-Path $root 'keep.txt'
    [IO.File]::WriteAllText($marker, 'existing output')
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Build-Package.ps1') -OutputDirectory $root | Out-Null }
    catch { if ($_.Exception.Message -notlike '*already exists*') { throw }; $rejected = $true }
    if (-not $rejected -or [IO.File]::ReadAllText($marker) -ne 'existing output') { throw 'Existing package directory was not preserved.' }
    $script:passed++
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Build-Package.ps1') -OutputDirectory $repository | Out-Null }
    catch { if ($_.Exception.Message -notlike '*inside this repository*') { throw }; $rejected = $true }
    if (-not $rejected) { throw 'Builder accepted an output path outside artifacts.' }
    $script:passed++
}
[pscustomobject]@{ Passed = $script:passed; Failed = 0; Fixtures = $root }
