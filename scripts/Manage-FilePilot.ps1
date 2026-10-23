#requires -Version 7.2
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][ValidateSet('Activate', 'Rollback', 'Uninstall')][string]$Action,
    [string]$InstallDirectory,
    [string]$StateDirectory = (Join-Path $env:LOCALAPPDATA 'FilePilot/Deployment'),
    [string]$ShortcutPath = (Join-Path ([Environment]::GetFolderPath('Programs')) 'FilePilot.lnk'),
    [switch]$RegisterContextMenu
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Manage FilePilot on Windows.' }
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
$stateRoot = Resolve-FilePilotLocalPath $StateDirectory
$shortcut = Resolve-FilePilotLocalPath $ShortcutPath
if ([IO.Path]::GetExtension($shortcut) -ine '.lnk') { throw 'Choose a .lnk shortcut path.' }
$statePath = Join-Path $stateRoot 'state.json'
$state = @{ Owner = 'FilePilot.Deployment.v1'; Current = $null; Previous = $null; Shortcut = $shortcut; ContextMenu = $false }
if (Test-Path -LiteralPath $statePath) {
    $state = Read-FilePilotJson $statePath 8192
    if ($state.Owner -cne 'FilePilot.Deployment.v1' -or $state.Shortcut -ine $shortcut -or $state.ContextMenu -isnot [bool]) { throw 'Unrelated or incompatible deployment state. No installation was changed.' }
    foreach ($value in @($state.Current, $state.Previous) | Where-Object { $_ }) { $null = Resolve-FilePilotLocalPath $value }
}
if ($Action -eq 'Rollback') {
    if (-not $state.Previous) { throw 'There is no retained previous version to roll back to.' }
    $InstallDirectory = $state.Previous
}
if (-not $InstallDirectory) { throw 'Specify the installation directory.' }
$target = Get-FilePilotInstallation $InstallDirectory -AllowRemoving:($Action -eq 'Uninstall')
$directory = $target.Directory
foreach ($installation in @($directory, $state.Current) | Where-Object { $_ }) {
    if ($stateRoot -ieq $installation -or $stateRoot.StartsWith($installation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $shortcut.StartsWith($installation + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep deployment state and shortcuts outside version directories.' }
}
Assert-FilePilotStopped @($directory, $state.Current | Where-Object { $_ })
$oldShortcutBytes = $null
if (Test-Path -LiteralPath $shortcut) {
    if ((Get-Item -LiteralPath $shortcut).Length -gt 64KB) { throw 'Unrelated shortcut preserved.' }
    $existing = [FilePilot.Deployment.Shortcut]::Read($shortcut)
    if (-not $state.Current -or $existing[0] -ine (Join-Path $state.Current 'FilePilot.exe') -or $existing[1] -cne '--widget') { throw 'Unrelated or changed Start Menu shortcut preserved.' }
    $oldShortcutBytes = [IO.File]::ReadAllBytes($shortcut)
}
if (-not $PSCmdlet.ShouldProcess($directory, $Action + ' FilePilot (settings and other versions are preserved)')) { return }
[IO.Directory]::CreateDirectory($stateRoot) | Out-Null
$lock = [IO.File]::Open((Join-Path $stateRoot 'manage.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    # Re-read under the lock; do not apply a snapshot if another manager changed it.
    if (Test-Path -LiteralPath $statePath) {
        $latest = Read-FilePilotJson $statePath 8192
        if (($latest | ConvertTo-Json -Compress) -cne ($state | ConvertTo-Json -Compress)) { throw 'Deployment state changed; retry.' }
    }
    if ($Action -in @('Activate', 'Rollback')) {
        if ($directory -ine $state.Current -and $state.Current) { $null = Get-FilePilotInstallation $state.Current }
        if ($directory -ieq $state.Current) {
            if ($RegisterContextMenu -and -not $state.ContextMenu) {
                Invoke-FilePilotMenu $directory $true
                $state.ContextMenu = $true
                try { Write-FilePilotJson $statePath $state }
                catch { Invoke-FilePilotMenu $directory $false; throw }
            }
            [pscustomobject]@{ Action = $Action; Current = $directory; Changed = [bool]$RegisterContextMenu }; return
        }
        $parent = [IO.Path]::GetDirectoryName($shortcut)
        $null = Resolve-FilePilotLocalPath $parent
        [IO.Directory]::CreateDirectory($parent) | Out-Null
        $temporary = Join-Path $parent ('FilePilot-' + [Guid]::NewGuid().ToString('N') + '.lnk')
        $menuChanged = $false
        try {
            [FilePilot.Deployment.Shortcut]::Save($temporary, (Join-Path $directory 'FilePilot.exe'), '--widget', $directory)
            $menu = $RegisterContextMenu -or $state.ContextMenu
            if ($menu) { Invoke-FilePilotMenu $directory $true; $menuChanged = $true }
            [IO.File]::Move($temporary, $shortcut, $true)
            $updated = @{ Owner = $state.Owner; Current = $directory; Previous = $state.Current; Shortcut = $shortcut; ContextMenu = [bool]$menu }
            try { Write-FilePilotJson $statePath $updated }
            catch {
                if ($oldShortcutBytes) { [IO.File]::WriteAllBytes($shortcut, $oldShortcutBytes) } else { Remove-Item -LiteralPath $shortcut }
                throw
            }
        }
        catch {
            if ($menuChanged) {
                if ($state.ContextMenu -and $state.Current) { Invoke-FilePilotMenu $state.Current $true }
                else { Invoke-FilePilotMenu $directory $false }
            }
            throw
        }
        finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
        [pscustomobject]@{ Action = $Action; Current = $directory; Previous = $state.Current; Shortcut = $shortcut; Changed = $true }
    }
    else {
        # Check every owned file can be written/deleted before changing shortcuts or deleting anything.
        $handles = [Collections.Generic.List[IO.FileStream]]::new()
        try {
            foreach ($file in @($target.Payload) + @((Join-Path $directory 'package-manifest.json'), (Join-Path $directory 'filepilot-installation.json'))) {
                $probe = [IO.File]::Open($file, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read -bor [IO.FileShare]::Delete)
                $probe.Dispose()
            }
            if (-not $target.Removing) { Invoke-FilePilotMenu $directory $false }
            foreach ($file in $target.Payload) { $handles.Add([IO.File]::Open($file, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read -bor [IO.FileShare]::Delete)) }
            foreach ($handle in $handles) {
                $expected = $target.Checks[$handle.Name]
                if ($handle.Length -ne $expected.Length -or [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($handle)) -ine $expected.Sha256) { throw 'An installed file changed during uninstall preflight. No payload was deleted.' }
            }
            $target.Receipt.Removing = $true
            Write-FilePilotJson (Join-Path $directory 'filepilot-installation.json') $target.Receipt
            if ($state.Current -ieq $directory) {
                if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
                $state.Current = $null; $state.ContextMenu = $false
            }
            if ($state.Previous -ieq $directory) { $state.Previous = $null }
            Write-FilePilotJson $statePath $state
            foreach ($file in $target.Payload) {
                # Each resolved file was checked to be inside the explicitly selected installation.
                if (-not $file.StartsWith($directory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Uninstall containment check failed.' }
                [IO.File]::Delete($file)
            }
        }
        finally { foreach ($handle in $handles) { $handle.Dispose() } }
        foreach ($folder in $target.Directories | Sort-Object Length -Descending) { [IO.Directory]::Delete($folder, $false) }
        [IO.File]::Delete((Join-Path $directory 'filepilot-installation.json'))
        [IO.File]::Delete((Join-Path $directory 'package-manifest.json'))
        [IO.Directory]::Delete($directory, $false)
        [pscustomobject]@{ Action = 'Uninstall'; Removed = $directory; SettingsPreserved = $true }
    }
}
finally { $lock.Dispose() }
