using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FilePilot.App.Widget;

internal sealed class WidgetHost : IDisposable
{
    private readonly MainWindow window;
    private readonly HwndSource source;
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly ExplorerSelectionReader selection = new();
    private int hotkeyId;
    private bool activating;
    private HotkeyGesture? current;

    internal WidgetHost(MainWindow window)
    {
        this.window = window;
        source = new HwndSource(new HwndSourceParameters("FilePilot shortcut listener") { Width = 0, Height = 0, WindowStyle = 0 });
        source.AddHook(Hook);
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open FilePilot", null, (_, _) => window.Dispatcher.Invoke(() => window.ShowPopup()));
        menu.Items.Add("Settings", null, (_, _) => window.Dispatcher.Invoke(() => { window.ShowPopup(); window.ShowSettings(); }));
        menu.Items.Add("Exit FilePilot", null, (_, _) => window.Dispatcher.Invoke(window.ExitWidget));
        tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "FilePilot — quick file actions", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => window.Dispatcher.Invoke(() => window.ShowPopup());
    }
    internal void SetShortcut(string text)
    {
        var gesture = HotkeyGesture.Parse(text);
        if (gesture == current) return;
        int next = hotkeyId == 1 ? 2 : 1;
        if (!NativeMethods.RegisterHotKey(source.Handle, next, gesture.Modifiers | 0x4000, gesture.Key))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "That shortcut is unavailable. Choose another in Settings.");
        if (hotkeyId != 0) NativeMethods.UnregisterHotKey(source.Handle, hotkeyId);
        hotkeyId = next;
        current = gesture;
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && (int)wParam == hotkeyId)
        {
            handled = true;
            ActivateFromShortcut();
        }
        return 0;
    }
    internal void ActivateFromShortcut()
    {
        if (activating || window.IsDialogOpen) return;
        // Reopen the active operation without replacing its files or touching Explorer COM.
        if (window.IsWorking) window.ShowPopup();
        else ActivateFromExplorer();
    }
    private async void ActivateFromExplorer()
    {
        activating = true;
        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetCursorPos(out var cursor);
        try
        {
            // Keep focus in Explorer until its selection has been captured.
            var result = await selection.ReadAsync(foreground);
            if (window.IsWorking || window.IsDialogOpen) return;
            // Never resurrect a previous selection after a failed capture.
            var model = (MainViewModel)window.DataContext;
            if (result.Error is not null) model.ClearSelection(result.Error);
            else model.LoadFiles(result.Paths);
            window.ShowPopup(cursor);
        }
        finally { activating = false; }
    }
    public void Dispose()
    {
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        if (hotkeyId != 0) NativeMethods.UnregisterHotKey(source.Handle, hotkeyId);
        source.RemoveHook(Hook);
        source.Dispose();
    }
}
