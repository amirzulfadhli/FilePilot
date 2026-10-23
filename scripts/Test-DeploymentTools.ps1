#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath, [switch]$UseSourceTools)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) ('artifacts/deployment-tests-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$state = Join-Path $root 'state'; $shortcut = Join-Path $root 'FilePilot 文.lnk'
$a = Join-Path $root 'version A'; $b = Join-Path $root 'version B'
foreach ($folder in @($a, $b)) { & (Join-Path $PSScriptRoot 'Install-FilePilot.ps1') -PackagePath $PackagePath -InstallDirectory $folder | Out-Host }
$manager = Join-Path $a 'deployment/Manage-FilePilot.ps1'
if ($UseSourceTools) { $manager = Join-Path $PSScriptRoot 'Manage-FilePilot.ps1' }
$arguments = @{ StateDirectory = $state; ShortcutPath = $shortcut; Confirm = $false }
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
$script:passed = 0
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message }; $script:passed++ }
function Refused([scriptblock]$Operation) {
    $failed = $false
    try { & $Operation | Out-Null } catch { $failed = $true }
    Check $failed 'An unsafe deployment operation was accepted.'
}
& $manager -Action Activate -InstallDirectory $a @arguments | Out-Host
Check ([FilePilot.Deployment.Shortcut]::Read($shortcut)[0] -ieq (Join-Path $a 'FilePilot.exe')) 'Initial shortcut target is wrong.'
$stateLock = [IO.File]::Open((Join-Path $state 'state.json'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try { Refused { & $manager -Action Activate -InstallDirectory $b @arguments } } finally { $stateLock.Dispose() }
Check ([FilePilot.Deployment.Shortcut]::Read($shortcut)[0] -ieq (Join-Path $a 'FilePilot.exe')) 'Failed state commit did not restore the original shortcut.'
& $manager -Action Activate -InstallDirectory $b @arguments | Out-Host
$saved = Get-Content (Join-Path $state 'state.json') -Raw | ConvertFrom-Json
Check ($saved.Current -ieq $b -and $saved.Previous -ieq $a -and (Test-Path -LiteralPath (Join-Path $a 'FilePilot.exe'))) 'Upgrade did not retain the previous version.'
& $manager -Action Rollback @arguments | Out-Host
Check ([FilePilot.Deployment.Shortcut]::Read($shortcut)[0] -ieq (Join-Path $a 'FilePilot.exe')) 'Rollback did not restore the previous shortcut.'
& $manager -Action Uninstall -InstallDirectory $b @arguments | Out-Host
Check (-not (Test-Path -LiteralPath $b) -and (Test-Path -LiteralPath $shortcut)) 'Inactive removal affected the current installation.'
$userFile = Join-Path $a 'my-notes.txt'; [IO.File]::WriteAllText($userFile, 'Preserve me')
Refused { & $manager -Action Uninstall -InstallDirectory $a @arguments }
Check ([IO.File]::ReadAllText($userFile) -ceq 'Preserve me') 'Unexpected user file was changed.'
Remove-Item -LiteralPath $userFile
$extraFolder = Join-Path $a 'user folder'; [IO.Directory]::CreateDirectory($extraFolder) | Out-Null
try { Refused { & $manager -Action Uninstall -InstallDirectory $a @arguments } } finally { [IO.Directory]::Delete($extraFolder, $false) }
$readme = Join-Path $a 'README.md'; $original = [IO.File]::ReadAllBytes($readme)
try {
    [IO.File]::AppendAllText($readme, 'Changed by test')
    Refused { & $manager -Action Uninstall -InstallDirectory $a @arguments }
} finally { [IO.File]::WriteAllBytes($readme, $original) }
$attributes = [IO.File]::GetAttributes($readme)
try {
    [IO.File]::SetAttributes($readme, $attributes -bor [IO.FileAttributes]::ReadOnly)
    Refused { & $manager -Action Uninstall -InstallDirectory $a @arguments }
} finally { [IO.File]::SetAttributes($readme, $attributes) }
$locked = [IO.File]::Open((Join-Path $a 'FilePilot.Core.dll'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try { Refused { & $manager -Action Uninstall -InstallDirectory $a @arguments } } finally { $locked.Dispose() }
Check (Test-Path -LiteralPath $shortcut) 'Failed preflight changed the active shortcut.'
[FilePilot.Deployment.Shortcut]::Save($shortcut, (Join-Path $root 'other.exe'), '--widget', $root)
try { Refused { & $manager -Action Uninstall -InstallDirectory $a @arguments } }
finally { [FilePilot.Deployment.Shortcut]::Save($shortcut, (Join-Path $a 'FilePilot.exe'), '--widget', $a) }
$beforeState = [IO.File]::ReadAllBytes((Join-Path $state 'state.json'))
& $manager -Action Uninstall -InstallDirectory $a @arguments -WhatIf | Out-Host
Check ([Convert]::ToHexString($beforeState) -ceq [Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $state 'state.json')))) 'WhatIf changed deployment state.'
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $a 'FilePilot.exe')); $start.UseShellExecute = $false
foreach ($value in @('--diagnostics', (Join-Path $root 'running-report.json'), 'popup')) { $start.ArgumentList.Add($value) }
$ownedProcess = [Diagnostics.Process]::Start($start)
try {
    Refused { & $manager -Action Uninstall -InstallDirectory $a @arguments }
    Check (-not $ownedProcess.HasExited) 'Manager terminated the running application.'
    if (-not $ownedProcess.WaitForExit(30000)) { throw 'Owned diagnostic did not exit.' }
    Check ($ownedProcess.ExitCode -eq 0) 'Installed executable diagnostic failed.'
}
finally { if (-not $ownedProcess.HasExited) { $ownedProcess.Kill($true) }; $ownedProcess.Dispose() }
$preferences = Join-Path $root 'settings-sentinel.json'; [IO.File]::WriteAllText($preferences, 'User preferences')
& $manager -Action Uninstall -InstallDirectory $a @arguments | Out-Host
Check (-not (Test-Path -LiteralPath $a) -and -not (Test-Path -LiteralPath $shortcut)) 'Active uninstall did not remove its owned files/shortcut.'
Check ([IO.File]::ReadAllText($preferences) -ceq 'User preferences') 'Preferences were changed.'
$c = Join-Path $root 'interrupted version C'
& (Join-Path $PSScriptRoot 'Install-FilePilot.ps1') -PackagePath $PackagePath -InstallDirectory $c | Out-Host
$interrupted = Read-FilePilotJson (Join-Path $c 'filepilot-installation.json'); $interrupted.Removing = $true
Write-FilePilotJson (Join-Path $c 'filepilot-installation.json') $interrupted
# Simulate interruption after one owned payload was removed, including a missing app executable.
[IO.File]::Delete((Join-Path $c 'FilePilot.exe'))
if (-not $UseSourceTools) { $manager = Join-Path $c 'deployment/Manage-FilePilot.ps1' }
& $manager -Action Uninstall -InstallDirectory $c @arguments | Out-Host
Check (-not (Test-Path -LiteralPath $c)) 'Interrupted uninstall could not resume safely.'
[pscustomobject]@{ Passed = $script:passed; Failed = 0; Fixtures = $root }
