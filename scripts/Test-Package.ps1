#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-PackagePath([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.Length -gt 1024 -or $Name.Contains('\') -or
        $Name.StartsWith('/') -or $Name.Contains(':') -or $Name.IndexOfAny([char[]](0..31)) -ge 0) {
        throw "Unsafe package path: $Name"
    }
    foreach ($segment in $Name.Split('/')) {
        if ($segment -in @('', '.', '..') -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or $segment.Length -gt 255 -or
            $segment -match '(?i)^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(\.|$)' -or
            $segment.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { throw "Unsafe package path: $Name" }
    }
    if ($Name -match '(?i)(^|/)(bin|obj|\.vs|artifacts)(/|$)|(^|/)(\.env[^/]*|secrets\.json)$|\.(pdb|user|suo|pfx|p12|pem|key|nupkg|snupkg)$') {
        throw "Development output or sensitive file is not allowed in a package: $Name"
    }
}

$fullPath = [IO.Path]::GetFullPath($Path)
if ((Get-Item -LiteralPath $fullPath).Length -gt 512MB) { throw 'Package exceeds the 512 MiB archive limit.' }
$zip = [IO.Compression.ZipFile]::OpenRead($fullPath)
try {
    if ($zip.Entries.Count -gt 1024) { throw 'Package has too many entries.' }
    $entries = [Collections.Generic.Dictionary[string, IO.Compression.ZipArchiveEntry]]::new([StringComparer]::OrdinalIgnoreCase)
    [long]$expandedBytes = 0
    foreach ($entry in $zip.Entries) {
        Assert-PackagePath $entry.FullName
        if (-not $entries.TryAdd($entry.FullName, $entry)) { throw "Duplicate package path: $($entry.FullName)" }
        # Reject Unix symlink entries. This tool validates without extracting anything.
        if ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'Symbolic links are not allowed in a package.' }
        $expandedBytes += $entry.Length
        if ($expandedBytes -gt 1GB) { throw 'Package exceeds the 1 GiB expanded limit.' }
    }
    $manifestName = 'package-manifest.json'
    if (-not $entries.ContainsKey($manifestName)) { throw 'Package manifest is missing.' }
    if ($entries[$manifestName].Length -gt 2MB) { throw 'Package manifest exceeds the size limit.' }
    $reader = [IO.StreamReader]::new($entries[$manifestName].Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable }
    finally { $reader.Dispose() }
    if ($manifest['SchemaVersion'] -ne 1 -or $manifest['Runtime'] -ne 'win-x64' -or
        $manifest['SourceRevision'] -isnot [string] -or $manifest['SourceRevision'] -notmatch '^[a-fA-F0-9]{40,64}$' -or
        $manifest['SourceDirty'] -isnot [bool] -or $manifest['SelfContained'] -isnot [bool] -or $manifest['SelfContained'] -ne $false -or
        $manifest['Files'] -isnot [array] -or $manifest['Files'].Count -eq 0) { throw 'Unsupported or invalid package manifest.' }
    $listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $manifest['Files']) {
        if ($file -isnot [Collections.IDictionary] -or $file['Path'] -isnot [string] -or
            $file['Sha256'] -isnot [string] -or $file['Sha256'] -notmatch '^[a-fA-F0-9]{64}$' -or
            ($file['Length'] -isnot [long] -and $file['Length'] -isnot [int]) -or $file['Length'] -lt 0) { throw 'Invalid package file record.' }
        $name = $file['Path']
        Assert-PackagePath $name
        if ($name -ieq $manifestName -or -not $listed.Add($name)) { throw "Duplicate manifest path: $name" }
        if (-not $entries.ContainsKey($name) -or $entries[$name].Length -ne $file['Length']) { throw "Missing file or length mismatch: $name" }
        $stream = $entries[$name].Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($hash -cne $file['Sha256'].ToUpperInvariant()) { throw "Hash mismatch: $name" }
    }
    if ($entries.Count -ne $listed.Count + 1) { throw 'Package contains an unlisted file.' }
    foreach ($required in @('FilePilot.exe', 'FilePilot.dll', 'FilePilot.Core.dll', 'FilePilot.deps.json',
        'FilePilot.runtimeconfig.json', 'SkiaSharp.dll', 'libSkiaSharp.dll', 'README.md',
        'notices/SkiaSharp-LICENSE.txt', 'notices/SkiaSharp-NATIVE-NOTICES.txt')) {
        if (-not $listed.Contains($required)) { throw "Required runtime/documentation file is missing: $required" }
    }
    [pscustomobject]@{ Path = $fullPath; Runtime = $manifest['Runtime']; FileCount = $listed.Count; ExpandedBytes = $expandedBytes; Valid = $true }
}
finally { $zip.Dispose() }
