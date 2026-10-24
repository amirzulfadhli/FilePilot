using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FilePilot.App.Widget;

internal static class PopupPlacement
{
    internal static Size WorkAreaSize(int width, int height, uint dpi)
    {
        double scale = Math.Max(96, dpi) / 96d;
        // Whole DIPs avoid rounding the last physical pixel outside a fractional-DPI work area.
        return new Size(Math.Max(200, Math.Floor(width / scale)), Math.Max(120, Math.Floor(height / scale)));
    }
    internal static (int X, int Y) Clamp(int x, int y, int width, int height, int left, int top, int right, int bottom) =>
        (Math.Clamp(x, left, Math.Max(left, right - width)), Math.Clamp(y, top, Math.Max(top, bottom - height)));

    internal static void Place(Window window, NativeMethods.Point? position = null)
    {
        window.GetBindingExpression(Window.HeightProperty)?.UpdateTarget();
        NativeMethods.GetCursorPos(out var cursor);
        if (position.HasValue) cursor = position.Value;
        var monitor = NativeMethods.MonitorFromPoint(cursor, 2);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        // Move the HWND onto the target monitor first so its per-monitor DPI can update.
        NativeMethods.SetWindowPos(hwnd, 0, cursor.X, cursor.Y, 0, 0, 0x0015);
        double scale = Math.Max(96, NativeMethods.GetDpiForWindow(hwnd)) / 96d;
        var available = WorkAreaSize(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top, NativeMethods.GetDpiForWindow(hwnd));
        window.MaxHeight = available.Height;
        window.MaxWidth = available.Width;
        if (window.DataContext is MainViewModel model) window.SetCurrentValue(Window.HeightProperty, model.WindowHeight);
        window.UpdateLayout();
        int width = (int)Math.Ceiling(Math.Min(window.Width, window.MaxWidth) * scale);
        int height = (int)Math.Ceiling(Math.Min(window.Height, window.MaxHeight) * scale);
        var point = Clamp(cursor.X + 12, cursor.Y + 12, width, height, info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
        // WPF owns size/bindings. Native resizing here would replace the bound Height.
        NativeMethods.SetWindowPos(hwnd, 0, point.X, point.Y, 0, 0, 0x0015);
    }
}
