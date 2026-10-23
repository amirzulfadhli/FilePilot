#requires -Version 7.2
# This is deliberately not shipped in the app or normal deployment tools.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'Engine installation is restricted to disposable GitHub-hosted Windows runners.' }
$root = Join-Path $env:RUNNER_TEMP ('filepilot-engine-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
function Get-TrustedInstaller([string]$Url, [string]$Name, [string]$Publisher, [string]$Hash = '') {
    $path = Join-Path $root $Name
    Invoke-WebRequest -Uri $Url -OutFile $path
    if ($Hash -and (Get-FileHash -LiteralPath $path).Hash -ine $Hash) { throw 'Document engine checksum mismatch.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch $Publisher) { throw 'Installer signature validation failed.' }
    return $path
}
$runtime = Get-TrustedInstaller 'https://aka.ms/vc14/vc_redist.x64.exe' 'vc_redist.x64.exe' 'Microsoft Corporation'
$process = Start-Process -FilePath $runtime -ArgumentList @('/install', '/quiet', '/norestart') -WindowStyle Hidden -PassThru -Wait
try { if ($process.ExitCode -notin @(0, 3010)) { throw "C++ runtime installation failed: $($process.ExitCode)" } } finally { $process.Dispose() }
$engine = Get-TrustedInstaller 'https://download.documentfoundation.org/libreoffice/stable/26.8.1/win/x86_64/LibreOffice_26.8.1_Win_x86-64.msi' 'LibreOffice.msi' 'The Document Foundation' 'C6298B10CFA1748BCBEDAD94C62CE771E240E5021BFD6D7A967CD571B04B5312'
$process = Start-Process -FilePath "$env:WINDIR/System32/msiexec.exe" -ArgumentList @('/i', ('"' + $engine + '"'), '/qn', '/norestart') -WindowStyle Hidden -PassThru -Wait
try { if ($process.ExitCode -notin @(0, 3010)) { throw "LibreOffice installation failed: $($process.ExitCode)" } } finally { $process.Dispose() }
$executable = Join-Path $env:ProgramFiles 'LibreOffice/program/soffice.com'
& $executable --version
if ($LASTEXITCODE -ne 0) { throw 'Installed LibreOffice could not initialize.' }
