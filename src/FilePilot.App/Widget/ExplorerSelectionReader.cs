using System.Runtime.InteropServices;
using System.Text;

namespace FilePilot.App.Widget;

internal sealed record ExplorerSelection(string[] Paths, string? Error = null);

/// <summary>One bounded request per activation. No keyboard/clipboard simulation or polling.</summary>
internal sealed class ExplorerSelectionReader
{
    private int running;
    public async Task<ExplorerSelection> ReadAsync(nint foreground)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return new([], "Explorer is still responding. Use Choose files.");
        var completion = new TaskCompletionSource<ExplorerSelection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(Read(foreground)); }
            catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
            { completion.TrySetResult(new([], "Explorer selection is unavailable. Use Choose files.")); }
            finally { Interlocked.Exchange(ref running, 0); }
        }) { IsBackground = true, Name = "FilePilot Explorer selection" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException) { return new([], "Explorer did not respond in time. Use Choose files."); }
    }

    private static ExplorerSelection Read(nint foreground)
    {
        var className = new StringBuilder(256);
        NativeMethods.GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is not ("CabinetWClass" or "ExploreWClass"))
            return new([], "Select files in an Explorer folder, then press the shortcut. Or use Choose files.");
        var owned = new List<object>();
        T Own<T>(T value) where T : class { owned.Add(value); return value; }
        try
        {
            dynamic shell = Own(Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("13709620-C279-11CE-A49E-444553540000"))!)!);
            dynamic windows = Own((object)shell.Windows());
            var candidates = new Dictionary<nint, string[]>();
            int count = windows.Count;
            if (count > 100) return new([], "Too many Explorer windows. Use Choose files.");
            for (int i = 0; i < count; i++)
            {
                dynamic browser = Own((object)windows.Item(i));
                if ((nint)(long)browser.HWND != foreground) continue;
                var provider = (IServiceProvider)browser;
                var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                var iid = typeof(IShellBrowser).GUID;
                provider.QueryService(ref service, ref iid, out var browserObject);
                var shellBrowser = (IShellBrowser)Own(browserObject);
                shellBrowser.QueryActiveShellView(out var view);
                Own(view);
                view.GetWindow(out var viewWindow);
                // Hidden tabs are never candidates, and the view must belong to the foreground frame.
                if (!NativeMethods.IsWindowVisible(viewWindow) || NativeMethods.GetAncestor(viewWindow, 2) != foreground) continue;
                // SVGIO_BACKGROUND exposes IDispatch; asking directly for the dual IID fails on some Windows 11 views.
                var dispatchId = new Guid("00020400-0000-0000-C000-000000000046");
                view.GetItemObject(0, ref dispatchId, out var documentObject);
                dynamic document = Own(documentObject);
                dynamic items = Own((object)document.SelectedItems());
                int selectedCount = items.Count;
                if (selectedCount is < 1 or > 100) return new([], "Choose 1–100 regular local files, or use Choose files.");
                var paths = new List<string>();
                for (int n = 0; n < selectedCount; n++)
                {
                    dynamic item = Own((object)items.Item(n));
                    if (!(bool)item.IsFileSystem || (bool)item.IsFolder) return new([], "The selection includes folders or virtual items. Use Choose files.");
                    paths.Add((string)item.Path);
                }
                candidates[viewWindow] = paths.ToArray();
            }
            if (NativeMethods.GetForegroundWindow() != foreground || candidates.Count != 1)
                return new([], "The active Explorer selection could not be identified safely. Use Choose files.");
            return new(candidates.Single().Value);
        }
        finally
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (Marshal.IsComObject(owned[i])) Marshal.ReleaseComObject(owned[i]);
        }
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        void QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }
    // Native vtable order is intentional, including unused slots inherited from IOleWindow.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out nint hwnd);
        void ContextSensitiveHelp(int enter);
        void InsertMenusSB(nint menu, nint widths);
        void SetMenuSB(nint menu, nint oleMenu, nint active);
        void RemoveMenusSB(nint menu);
        void SetStatusTextSB(nint text);
        void EnableModelessSB(int enable);
        void TranslateAcceleratorSB(nint message, ushort id);
        void BrowseObject(nint pidl, uint flags);
        void GetViewStateStream(uint mode, out nint stream);
        void GetControlWindow(uint id, out nint hwnd);
        void SendControlMsg(uint id, uint message, nint wParam, nint lParam, out nint result);
        void QueryActiveShellView(out IShellView view);
    }
    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow(out nint hwnd);
        void ContextSensitiveHelp(int enter);
        void TranslateAccelerator(nint message);
        void EnableModeless(int enable);
        void UIActivate(uint state);
        void Refresh();
        void CreateViewWindow(nint previous, nint settings, nint browser, nint rect, out nint hwnd);
        void DestroyViewWindow();
        void GetCurrentInfo(nint settings);
        void AddPropertySheetPages(uint reserved, nint callback, nint parameter);
        void SaveViewState();
        void SelectItem(nint pidl, uint flags);
        void GetItemObject(uint item, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }
}
