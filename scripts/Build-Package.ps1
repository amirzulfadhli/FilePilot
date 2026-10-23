#requires -Version 7.2
[CmdletBinding()]
param([string]$OutputDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Build FilePilot packages on Windows with the .NET 10 SDK.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repository 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot ('packages/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
}
$output = [IO.Path]::GetFullPath($OutputDirectory, $repository)
if (-not $output.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be a new directory inside this repository''s artifacts folder.'
}
if (Test-Path -LiteralPath $output) { throw 'OutputDirectory already exists. Choose a new directory; existing output is never replaced.' }
$ancestor = [IO.Path]::GetDirectoryName($output)
while ($ancestor) {
    if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Output paths through symbolic links or junctions are not supported.'
    }
    $ancestor = [IO.Path]::GetDirectoryName($ancestor)
}
[IO.Directory]::CreateDirectory($output) | Out-Null
$publish = Join-Path $output 'publish'
Push-Location $repository
try {
    & (Join-Path $PSScriptRoot 'Test-PackageTools.ps1') | Out-Host
    & dotnet build FilePilot.slnx -c Release --verbosity minimal | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed. No package was finalized.' }
    & dotnet test FilePilot.slnx -c Release --no-build --no-restore --verbosity minimal | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed. No package was finalized.' }
    & dotnet publish src/FilePilot.App -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o $publish --verbosity minimal | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed. No package was finalized.' }
    Copy-Item -LiteralPath (Join-Path $repository 'README.md') -Destination (Join-Path $publish 'README.md')
    $deployment = [IO.Directory]::CreateDirectory((Join-Path $publish 'deployment')).FullName
    foreach ($tool in @('Install-FilePilot.ps1', 'Manage-FilePilot.ps1', 'Deployment.Common.ps1', 'Test-Package.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $tool) -Destination (Join-Path $deployment $tool)
    }
    $revision = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record the source revision.' }
    $status = @(& git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record source working-tree state.' }
    $files = @(Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object Extension -ne '.pdb' | Sort-Object FullName)
    $records = @($files | ForEach-Object {
        if ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Published symbolic links are not supported.' }
        [ordered]@{ Path = [IO.Path]::GetRelativePath($publish, $_.FullName).Replace('\', '/'); Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $manifest = [ordered]@{
        SchemaVersion = 1; Runtime = 'win-x64'; SelfContained = $false
        SourceRevision = $revision; SourceDirty = ($status.Count -gt 0); CreatedUtc = [DateTime]::UtcNow.ToString('O'); Files = $records
    }
    $manifestPath = Join-Path $publish 'package-manifest.json'
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    $temporaryZip = Join-Path $output 'FilePilot-win-x64.zip.tmp'
    $zipFile = [IO.File]::Open($temporaryZip, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $zip = [IO.Compression.ZipArchive]::new($zipFile, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in @($files) + @(Get-Item -LiteralPath $manifestPath)) {
                $name = [IO.Path]::GetRelativePath($publish, $file.FullName).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $zipFile.Dispose() }
    & (Join-Path $PSScriptRoot 'Test-Package.ps1') -Path $temporaryZip | Out-Host
    $archivePath = Join-Path $output 'FilePilot-win-x64.zip'
    [IO.File]::Move($temporaryZip, $archivePath, $false)
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    [IO.File]::WriteAllText($archivePath + '.sha256', "$hash  FilePilot-win-x64.zip`n")
    [pscustomobject]@{ Archive = $archivePath; Sha256 = $hash; Bytes = (Get-Item -LiteralPath $archivePath).Length; SourceRevision = $revision; SourceDirty = ($status.Count -gt 0) }
}
finally { Pop-Location }
