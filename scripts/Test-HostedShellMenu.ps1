#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ExecutablePath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# This probe changes real HKCU associations only on an ephemeral GitHub-hosted VM.
if (-not $IsWindows -or $env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted') {
    throw 'Shell acceptance is restricted to disposable GitHub-hosted Windows runners.'
}
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run this probe with pwsh -STA.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$executable = (Get-Item -LiteralPath $ExecutablePath).FullName
if (-not $executable.StartsWith((Join-Path $repository 'artifacts') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($executable) -ine 'FilePilot.exe') { throw 'Choose the FilePilot.exe published under this checkout artifacts directory.' }
$output = Join-Path $repository ('artifacts/shell-acceptance/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
$extensions = @('.png', '.jpg', '.jpeg', '.webp', '.docx')
$prototypePath = 'Software\Classes\*\shell\FilePilot.CloudControl'
$prototypeLabel = 'FilePilot cloud control'
$prototypeOwner = 'FilePilot.CloudAcceptance.' + [Guid]::NewGuid().ToString('N')
$expectedCommand = '"' + $executable + '" "%1"'
foreach ($extension in $extensions) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Software\Classes\SystemFileAssociations\$extension\shell\FilePilot")
    if ($key) { $key.Dispose(); throw 'Pre-existing FilePilot registration; preserving it and refusing the probe.' }
}
$key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($prototypePath)
if ($key) { $key.Dispose(); throw 'Pre-existing cloud control; preserving it and refusing the probe.' }
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class FilePilotCloudMenu
{
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr h, IntPtr b, [MarshalAs(UnmanagedType.LPWStr)] string n, ref uint eaten, out IntPtr id, ref uint attr);
        [PreserveSig] int EnumObjects(IntPtr h, uint flags, out IntPtr list);
        [PreserveSig] int BindToObject(IntPtr id, IntPtr b, ref Guid iid, out IntPtr result);
        [PreserveSig] int BindToStorage(IntPtr id, IntPtr b, ref Guid iid, out IntPtr result);
        [PreserveSig] int CompareIDs(IntPtr l, IntPtr a, IntPtr b);
        [PreserveSig] int CreateViewObject(IntPtr h, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetAttributesOf(uint count, IntPtr ids, ref uint attr);
        [PreserveSig] int GetUIObjectOf(IntPtr h, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] ids, ref Guid iid, IntPtr reserved, out IntPtr result);
    }
    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint index, uint first, uint last, uint flags);
    }
    [DllImport("ole32.dll")] static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")] static extern void OleUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr id, uint flags, out uint attr);
    [DllImport("shell32.dll")] static extern int SHBindToParent(IntPtr id, ref Guid iid, out IShellFolder parent, out IntPtr child);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int maximum, uint flags);

    public static string[] Read(string path)
    {
        Marshal.ThrowExceptionForHR(OleInitialize(IntPtr.Zero));
        IntPtr id = IntPtr.Zero, raw = IntPtr.Zero, menu = IntPtr.Zero;
        IShellFolder parent = null;
        IContextMenu contextMenu = null;
        try
        {
            uint attr;
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out id, 0, out attr));
            Guid folderId = typeof(IShellFolder).GUID;
            IntPtr child;
            Marshal.ThrowExceptionForHR(SHBindToParent(id, ref folderId, out parent, out child));
            Guid menuId = typeof(IContextMenu).GUID;
            Marshal.ThrowExceptionForHR(parent.GetUIObjectOf(IntPtr.Zero, 1, new[] { child }, ref menuId, IntPtr.Zero, out raw));
            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(raw);
            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) throw new InvalidOperationException("CreatePopupMenu failed.");
            Marshal.ThrowExceptionForHR(contextMenu.QueryContextMenu(menu, 0, 1, 0x7fff, 0));
            int count = GetMenuItemCount(menu);
            if (count < 0) throw new InvalidOperationException("GetMenuItemCount failed.");
            var labels = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                var text = new StringBuilder(1024);
                GetMenuString(menu, i, text, text.Capacity, 0x400);
                labels.Add(text.ToString().Replace("&", ""));
            }
            return labels.ToArray();
        }
        finally
        {
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            if (contextMenu != null) Marshal.ReleaseComObject(contextMenu);
            if (raw != IntPtr.Zero) Marshal.Release(raw);
            if (parent != null) Marshal.ReleaseComObject(parent);
            if (id != IntPtr.Zero) Marshal.FreeCoTaskMem(id);
            OleUninitialize();
        }
    }
}
'@
function Invoke-Registration([string]$Action) {
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.ArgumentList.Add($Action); $start.ArgumentList.Add('--quiet')
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(30000)) { $process.Kill($true); $process.WaitForExit(); throw 'Owned registration process timed out.' }
        if ($process.ExitCode -ne 0) { throw "Registration command failed: $Action, exit $($process.ExitCode)" }
    }
    finally { $process.Dispose() }
}
$registered = $false
$prototypeCreated = $false
$results = @()
try {
    # Preflight proved the product verb absent at every supported extension.
    $registered = $true
    Invoke-Registration '--register-context-menu'
    $prototype = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($prototypePath)
    try {
        $prototype.SetValue('', $prototypeLabel)
        $prototype.SetValue('FilePilotOwner', $prototypeOwner)
        $prototype.SetValue('MultiSelectModel', 'Single')
        $command = $prototype.CreateSubKey('command')
        try { $command.SetValue('', $expectedCommand, [Microsoft.Win32.RegistryValueKind]::String) } finally { $command.Dispose() }
        $prototypeCreated = $true
    }
    finally { $prototype.Dispose() }
    foreach ($extension in @($extensions) + @('.txt')) {
        $fixture = Join-Path $output ('selected 文 with spaces' + $extension)
        # Only association enumeration is performed; no handler or conversion is invoked.
        [IO.File]::WriteAllBytes($fixture, [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/1cAAAAASUVORK5CYII='))
        $labels = @([FilePilotCloudMenu]::Read($fixture))
        $results += [ordered]@{ Extension = $extension; Labels = $labels; FilePilotVisible = ($labels -contains 'FilePilot'); ControlVisible = ($labels -contains $prototypeLabel) }
    }
    $failed = @($results | Where-Object { $_.FilePilotVisible -ne ($_.Extension -in $extensions) -or -not $_.ControlVisible })
    $report = [ordered]@{ SourceRevision = $env:GITHUB_SHA; OS = [Runtime.InteropServices.RuntimeInformation]::OSDescription; NativeShellEnumeration = $true; ManualExplorerVerification = $false; Results = $results; Passed = ($failed.Count -eq 0) }
    [IO.File]::WriteAllText((Join-Path $output 'shell-menu.json'), ($report | ConvertTo-Json -Depth 6))
    $report | ConvertTo-Json -Depth 6 | Write-Host
    if ($failed.Count) { throw 'Native Shell menu acceptance failed; inspect the retained report. Registry presence is not visibility.' }
}
finally {
    if ($prototypeCreated) {
        $prototype = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($prototypePath)
        try {
            $command = $prototype.OpenSubKey('command')
            try {
                if ($prototype.GetValue('FilePilotOwner') -cne $prototypeOwner -or $command.GetValue('') -cne $expectedCommand) { throw 'Cloud control changed; preserving it.' }
            }
            finally { $command.Dispose() }
        }
        finally { $prototype.Dispose() }
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($prototypePath, $false)
    }
    if ($registered) { Invoke-Registration '--unregister-context-menu' }
}
