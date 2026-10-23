# Shared by the source and packaged deployment tools. No service or background work.
Set-StrictMode -Version Latest
if (-not ('FilePilot.Deployment.Shortcut' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
namespace FilePilot.Deployment {
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] internal class ShellLink { }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, IntPtr data, uint flags);
        void GetIDList(out IntPtr list); void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short hotkey); void SetHotkey(short hotkey);
        void GetShowCmd(out int command); void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
    public static class Shortcut {
        public static void Save(string file, string target, string arguments, string directory) {
            object instance = new ShellLink();
            try {
                var link = (IShellLinkW)instance;
                link.SetPath(target); link.SetArguments(arguments); link.SetWorkingDirectory(directory);
                link.SetDescription("FilePilot quick file actions");
                ((IPersistFile)instance).Save(file, true);
            } finally { Marshal.FinalReleaseComObject(instance); }
        }
        public static string[] Read(string file) {
            object instance = new ShellLink();
            try {
                ((IPersistFile)instance).Load(file, 0);
                var link = (IShellLinkW)instance;
                var target = new StringBuilder(32768); var arguments = new StringBuilder(32768);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4); link.GetArguments(arguments, arguments.Capacity);
                return new [] { target.ToString(), arguments.ToString() };
            } finally { Marshal.FinalReleaseComObject(instance); }
        }
    }
}
'@
}
function Resolve-FilePilotLocalPath([string]$Path) {
    if ($Path -notmatch '^[A-Za-z]:[\\/]' -or $Path.Substring(2).Contains(':')) { throw 'Choose an absolute local drive-letter path.' }
    foreach ($part in $Path.Substring(3).Split([char[]]'\/', [StringSplitOptions]::RemoveEmptyEntries)) {
        if ($part -in @('.', '..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part.Length -gt 255 -or
            $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
            $part -match '(?i)^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(\.|$)') { throw 'Unsafe local path component.' }
    }
    $full = [IO.Path]::GetFullPath($Path)
    if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($full)).DriveType -notin @([IO.DriveType]::Fixed, [IO.DriveType]::Removable, [IO.DriveType]::Ram)) { throw 'Network paths are not supported.' }
    for ($ancestor = $full; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Offline))) { throw 'Reparse points and offline paths are not supported.' }
    }
    return $full
}
function Read-FilePilotJson([string]$Path, [long]$Maximum = 2MB) {
    $null = Resolve-FilePilotLocalPath $Path
    if ((Get-Item -LiteralPath $Path).Length -gt $Maximum) { throw "Metadata exceeds size limit: $Path" }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
}
function Write-FilePilotJson([string]$Path, [hashtable]$Value) {
    $null = Resolve-FilePilotLocalPath $Path
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write([Text.Encoding]::UTF8.GetBytes(($Value | ConvertTo-Json -Depth 8))); $stream.Flush($true) } finally { $stream.Dispose() }
        [IO.File]::Move($temporary, $Path, $true)
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}
function Get-FilePilotInstallation([string]$Directory, [switch]$AllowRemoving) {
    $directory = Resolve-FilePilotLocalPath $Directory
    if ($directory -eq [IO.Path]::GetPathRoot($directory)) { throw 'A drive root is not an installation.' }
    $manifest = Read-FilePilotJson (Join-Path $directory 'package-manifest.json')
    $receipt = Read-FilePilotJson (Join-Path $directory 'filepilot-installation.json') 8192
    if ($manifest.SchemaVersion -ne 1 -or $manifest.Runtime -ne 'win-x64' -or $manifest.SourceRevision -notmatch '^[a-fA-F0-9]{40,64}$' -or
        $manifest.SourceDirty -isnot [bool] -or $receipt.SchemaVersion -ne 1 -or $receipt.SourceRevision -cne $manifest.SourceRevision -or
        $receipt.SourceDirty -ne $manifest.SourceDirty -or $receipt.PackageSHA256 -notmatch '^[a-fA-F0-9]{64}$' -or
        @($manifest.Files).Count -gt 1024 -or @($manifest.Files).Count -eq 0) { throw 'Installation metadata is invalid.' }
    $removing = $receipt.ContainsKey('Removing') -and $receipt.Removing -eq $true
    if ($removing -and -not $AllowRemoving) { throw 'This installation is being removed. Finish uninstall before activating it.' }
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $payload = [Collections.Generic.List[string]]::new()
    $checks = @{}
    [long]$expanded = 0
    $null = $allowed.Add('package-manifest.json'); $null = $allowed.Add('filepilot-installation.json')
    foreach ($file in $manifest.Files) {
        if ($file.Path -isnot [string] -or $file.Path.Contains('\') -or $file.Path.StartsWith('/') -or $file.Path.Contains(':') -or
            $file.Sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $file.Length -isnot [long] -and $file.Length -isnot [int] -or $file.Length -lt 0 -or -not $allowed.Add($file.Path)) { throw 'Unsafe or duplicate installed payload metadata.' }
        $expanded += $file.Length
        if ($expanded -gt 1GB) { throw 'Installed payload exceeds the size limit.' }
        $full = Resolve-FilePilotLocalPath (Join-Path $directory $file.Path)
        if (-not $full.StartsWith($directory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload escapes installation directory.' }
        $parent = [IO.Path]::GetDirectoryName($file.Path.Replace('/', '\'))
        while ($parent) { $null = $directories.Add($parent.Replace('\', '/')); $parent = [IO.Path]::GetDirectoryName($parent) }
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            if ((Get-Item -LiteralPath $full).Length -ne $file.Length -or (Get-FileHash -LiteralPath $full).Hash -ine $file.Sha256) { throw "Modified installation file preserved: $full" }
            $payload.Add($full)
            $checks[$full] = $file
        }
        elseif (-not ($removing -and $AllowRemoving)) { throw "Installed file is missing: $full" }
    }
    foreach ($required in @('FilePilot.exe', 'FilePilot.dll', 'deployment/Manage-FilePilot.ps1')) {
        if (-not $allowed.Contains($required)) { throw 'Managed deployment requires a current FilePilot package with deployment tools.' }
    }
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($directory)
    $actualDirectories = [Collections.Generic.List[string]]::new()
    while ($pending.Count) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            $null = Resolve-FilePilotLocalPath $item.FullName
            $relative = [IO.Path]::GetRelativePath($directory, $item.FullName).Replace('\', '/')
            if ($item.PSIsContainer) {
                if (-not $directories.Contains($relative)) { throw "Unexpected folder preserved: $($item.FullName)" }
                $pending.Push($item.FullName); $actualDirectories.Add($item.FullName)
            }
            elseif (-not $allowed.Contains($relative)) { throw "Unexpected file preserved: $($item.FullName)" }
        }
    }
    return @{ Directory = $directory; Manifest = $manifest; Receipt = $receipt; Payload = $payload.ToArray(); Checks = $checks; Directories = $actualDirectories.ToArray(); Removing = $removing }
}
function Assert-FilePilotStopped([string[]]$Directories) {
    foreach ($process in Get-Process -Name FilePilot -ErrorAction SilentlyContinue) {
        try {
            $path = $process.Path
            if ($path -and ($Directories | Where-Object { $path.StartsWith($_ + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) })) { throw 'Exit FilePilot from its notification icon before switching versions or uninstalling. No process was stopped.' }
        }
        finally { $process.Dispose() }
    }
}
function Invoke-FilePilotMenu([string]$Directory, [bool]$Register) {
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $Directory 'FilePilot.exe'))
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.ArgumentList.Add($(if ($Register) { '--register-context-menu' } else { '--unregister-context-menu' })); $start.ArgumentList.Add('--quiet')
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(15000)) { $process.Kill($true); throw 'The owned Explorer integration command timed out.' }
        if ($process.ExitCode -ne 0) { throw 'Explorer integration failed. Version files were retained; resolve the menu conflict before retrying.' }
    }
    finally { $process.Dispose() }
}
