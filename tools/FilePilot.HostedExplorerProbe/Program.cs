using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32;

internal static class Program
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".docx"];
    private static string stage = "guard";
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
        { Console.Error.WriteLine("This probe is restricted to disposable GitHub-hosted Windows VMs."); return 2; }
        if (args.Length != 2) { Console.Error.WriteLine("Expected application executable and a new report directory."); return 2; }
        string executable = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        string artifacts = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")!, "candidate", "artifacts")) + Path.DirectorySeparatorChar;
        if (!executable.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || !output.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(executable) != "FilePilot.exe" || !File.Exists(executable) || Directory.Exists(output))
        { Console.Error.WriteLine("Use a published FilePilot.exe and new output under candidate/artifacts."); return 2; }
        foreach (string extension in Extensions)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(VerbPath(extension));
            if (existing is not null) { Console.Error.WriteLine("Pre-existing registration; preserving it."); return 2; }
        }
        Directory.CreateDirectory(output);
        string folder = Path.Combine(output, "FilePilot Explorer " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string fixture = Path.Combine(folder, "selected 文 with spaces.png");
        File.WriteAllBytes(fixture, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/1cAAAAASUVORK5CYII="));
        byte[] original = File.ReadAllBytes(fixture);
        AutomationElement? explorer = null;
        Process? application = null;
        bool registered = false, passed = false, menuSeen = false, pathMatched = false, clipboardMatched = false, cleanup = false;
        DesktopObservation? desktop = null;
        object? uiObservation = null;
        string? error = null;
        try
        {
            Stage("verify hosted input desktop");
            desktop = ReadDesktop();
            Console.WriteLine(JsonSerializer.Serialize(desktop));
            RequireInputDesktop(desktop);
            var previous = ExplorerWindows().Select(w => w.Current.NativeWindowHandle).ToHashSet();
            Stage("open owned Explorer folder");
            using (var launched = Process.Start(new ProcessStartInfo("explorer.exe", "/n,/select,\"" + fixture + "\"") { UseShellExecute = true })) { }
            var ownedExplorer = WaitFor(() => ExplorerWindows().SingleOrDefault(w =>
                !w.Current.IsOffscreen && !previous.Contains(w.Current.NativeWindowHandle) && w.Current.Name.Contains(Path.GetFileName(folder), StringComparison.Ordinal)), "new Explorer folder");
            explorer = ownedExplorer;
            Stage("select fixture");
            var item = WaitFor(() => FindFixtureItem(ownedExplorer, fixture), "fixture list item");
            ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            item.SetFocus();
            nint explorerHandle = ownedExplorer.Current.NativeWindowHandle;
            SetForegroundWindow(explorerHandle);
            WaitFor(() => GetForegroundWindow() == explorerHandle ? new object() : null, "owned Explorer foreground");
            Stage("warm classic menu before registration");
            OpenClassicMenu(WaitFor(() => FindFixtureItem(ownedExplorer, fixture), "fixture list item"));
            if (MenuItem("FilePilot") is not null) throw new InvalidOperationException("Unexpected FilePilot command before registration.");
            SendSingleKey(0x1B);
            WaitFor(() => VisibleElement("Show more options") is null && MenuItem("Create shortcut") is null && MenuItem("Send to") is null ? new object() : null, "pre-registration menu dismissal");
            Stage("register into warmed Explorer session");
            registered = true;
            Registration(executable, "--register-context-menu");
            SetForegroundWindow(explorerHandle);
            WaitFor(() => GetForegroundWindow() == explorerHandle ? new object() : null, "owned Explorer foreground");
            Stage("open context menu after registration");
            OpenClassicMenu(WaitFor(() => FindFixtureItem(ownedExplorer, fixture), "fixture list item"));
            var menu = WaitFor(() => MenuItem("FilePilot"), "classic FilePilot menu item");
            menuSeen = true;
            var oldProcesses = Process.GetProcessesByName("FilePilot");
            var oldIds = oldProcesses.Select(p => p.Id).ToHashSet();
            foreach (var p in oldProcesses) p.Dispose();
            Stage("invoke FilePilot");
            Invoke(menu);
            var window = WaitFor(() =>
            {
                foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children,
                    new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "FilePilot"),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window))))
                {
                    if (!oldIds.Contains(w.Current.ProcessId)) return w;
                }
                return null;
            }, "new FilePilot window");
            application = Process.GetProcessById(window.Current.ProcessId);
            // Never close or terminate a process whose executable identity is uncertain.
            var image = new System.Text.StringBuilder(32768);
            int length = image.Capacity;
            if (!QueryFullProcessImageName(application.Handle, 0, image, ref length) ||
                !image.ToString().Equals(executable, StringComparison.OrdinalIgnoreCase))
            { application.Dispose(); application = null; throw new InvalidOperationException("Unexpected FilePilot executable identity."); }
            Stage("verify quoted selected-file argument");
            var pathBox = WaitFor(() => window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "Selected file path")), "selected path control");
            pathMatched = ((ValuePattern)pathBox.GetCurrentPattern(ValuePattern.Pattern)).Current.Value.Equals(fixture, StringComparison.OrdinalIgnoreCase);
            if (!pathMatched) throw new InvalidOperationException("Explorer did not pass the exact single Unicode/spaced file argument.");
            Stage("copy path");
            var copy = WaitFor(() => window.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "CopyPathButton")), "Copy Path");
            Invoke(copy);
            clipboardMatched = WaitFor(() => Clipboard.ContainsText() &&
                Clipboard.GetText().Equals(fixture, StringComparison.OrdinalIgnoreCase) ? new object() : null, "clipboard path") is not null;
            if (!File.ReadAllBytes(fixture).SequenceEqual(original)) throw new InvalidOperationException("The fixture changed.");
            passed = true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            if (explorer is not null)
            {
                try { uiObservation = ObserveUI(explorer); }
                catch (Exception observationError) { uiObservation = new { Error = observationError.GetType().Name }; }
            }
        }
        finally
        {
            try
            {
                if (application is not null)
                {
                    if (!application.HasExited) { application.CloseMainWindow(); if (!application.WaitForExit(5000)) { application.Kill(true); application.WaitForExit(5000); } }
                    application.Dispose();
                }
                if (explorer is not null && explorer.TryGetCurrentPattern(WindowPattern.Pattern, out var close))
                    ((WindowPattern)close).Close();
            }
            catch (Exception ex) { passed = false; error = (error ?? "") + " Owned-window cleanup: " + ex.Message; }
            try
            {
                if (registered) Registration(executable, "--unregister-context-menu");
                foreach (var extension in Extensions)
                {
                    using var key = Registry.CurrentUser.OpenSubKey(VerbPath(extension));
                    if (key is not null) throw new InvalidOperationException("An owned registration remains.");
                }
                cleanup = true;
            }
            catch (Exception ex) { passed = false; error = (error ?? "") + " Registration cleanup: " + ex.Message; }
        }
        var report = new
        {
            SourceRevision = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            OS = RuntimeInformation.OSDescription, HostArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            AutomatedExplorerInteraction = true, ManualVerification = false, Stage = stage,
            Desktop = desktop, UIObservation = uiObservation, VisibleMenuItemsRequired = true,
            MenuSeen = menuSeen, SelectedPathMatched = pathMatched, ClipboardMatched = clipboardMatched,
            OriginalPreserved = File.Exists(fixture) && File.ReadAllBytes(fixture).SequenceEqual(original), CleanupVerified = cleanup,
            Passed = passed && cleanup, Error = error
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "explorer-ui.json"), json);
        Console.WriteLine(json);
        return report.Passed ? 0 : 1;
    }
    private static void Stage(string value) { stage = value; Console.WriteLine("checkpoint: " + value); }
    private static AutomationElement? FindFixtureItem(AutomationElement explorer, string fixture) =>
        explorer.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
            .Cast<AutomationElement>().SingleOrDefault(e => !e.Current.IsOffscreen &&
                (e.Current.Name == Path.GetFileName(fixture) || e.Current.Name == Path.GetFileNameWithoutExtension(fixture)));
    private static AutomationElement? VisibleElement(string name) => AutomationElement.RootElement.FindFirst(TreeScope.Descendants,
        new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));
    private static void OpenClassicMenu(AutomationElement item)
    {
        RequireInputDesktop(ReadDesktop());
        if (item.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll)) ((ScrollItemPattern)scroll).ScrollIntoView();
        var bounds = item.Current.BoundingRectangle;
        if (item.Current.IsOffscreen || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("The owned fixture is not visibly clickable.");
        int x = (int)Math.Round(bounds.Left + bounds.Width / 2), y = (int)Math.Round(bounds.Top + bounds.Height / 2);
        if (!SetPhysicalCursorPos(x, y)) throw new InvalidOperationException("The hosted pointer could not reach the fixture.");
        Console.WriteLine($"checkpoint: right-click owned fixture at {x},{y}");
        Input[] clicks = [
            new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { X = 0, Y = 0, Data = 0, Flags = 0x0008, Time = 0, Extra = 0 } } },
            new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { X = 0, Y = 0, Data = 0, Flags = 0x0010, Time = 0, Extra = 0 } } }
        ];
        if (SendInput((uint)clicks.Length, clicks, Marshal.SizeOf<Input>()) != clicks.Length) throw new InvalidOperationException("Hosted right-click injection failed.");
        var menu = WaitFor(() => VisibleElement("Show more options") ?? MenuItem("Create shortcut") ?? MenuItem("Send to"), "context menu");
        if (menu.Current.Name == "Show more options") Invoke(menu);
        WaitFor(() => MenuItem("Create shortcut") ?? MenuItem("Send to"), "classic context menu");
    }
    private static AutomationElement? ClassicMenu() => AutomationElement.RootElement.FindFirst(TreeScope.Children,
        new AndCondition(new PropertyCondition(AutomationElement.ClassNameProperty, "#32768"),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));
    private static string VerbPath(string extension) => $@"Software\Classes\SystemFileAssociations\{extension}\shell\FilePilot";
    private static void Registration(string executable, string action)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(action); start.ArgumentList.Add("--quiet");
        using var p = Process.Start(start)!;
        if (!p.WaitForExit(30000)) { p.Kill(true); p.WaitForExit(5000); throw new TimeoutException("Owned registration process timed out."); }
        if (p.ExitCode != 0) throw new InvalidOperationException("Registration exit code " + p.ExitCode);
    }
    private static IEnumerable<AutomationElement> ExplorerWindows() =>
        AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ClassNameProperty, "CabinetWClass")).Cast<AutomationElement>();
    private static AutomationElement? MenuItem(string name) =>
        AutomationElement.RootElement.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
            new PropertyCondition(AutomationElement.NameProperty, name),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));
    private static void Invoke(AutomationElement element) =>
        ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    private static T WaitFor<T>(Func<T?> read, string label) where T : class
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            try { if (read() is { } value) return value; }
            catch (ElementNotAvailableException) { }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x800401D0)) { }
            Thread.Sleep(100); // Explicit hosted test only; normal FilePilot has no such polling.
        }
        throw new TimeoutException("Timed out waiting for " + label);
    }
    private sealed record DesktopObservation(string WindowStation, string ThreadDesktop, string InputDesktop, int InputError, bool UserInteractive, int SessionId, int InputStructureSize);
    private static DesktopObservation ReadDesktop()
    {
        nint input = OpenInputDesktop(0, false, 1);
        int error = input == 0 ? Marshal.GetLastWin32Error() : 0;
        try
        {
            using var process = Process.GetCurrentProcess();
            return new(ReadObjectName(GetProcessWindowStation()), ReadObjectName(GetThreadDesktop(GetCurrentThreadId())),
                input == 0 ? "unavailable" : ReadObjectName(input), error, Environment.UserInteractive, process.SessionId, Marshal.SizeOf<Input>());
        }
        finally { if (input != 0) CloseDesktop(input); }
    }
    private static string ReadObjectName(nint handle)
    {
        var value = new System.Text.StringBuilder(256);
        return GetUserObjectInformation(handle, 2, value, (uint)(value.Capacity * 2), out _) ? value.ToString() : "unavailable";
    }
    private static void RequireInputDesktop(DesktopObservation value)
    {
        if (!value.UserInteractive || !value.WindowStation.Equals("WinSta0", StringComparison.OrdinalIgnoreCase) || !value.InputDesktop.Equals("Default", StringComparison.OrdinalIgnoreCase) || !value.InputDesktop.Equals(value.ThreadDesktop, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hosted input desktop is unavailable; real Explorer interaction is unverified. No unlock or security change will be attempted.");
    }
    private static object ObserveUI(AutomationElement explorer)
    {
        var windows = new List<object>();
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition))
        {
            var info = window.Current;
            if (info.ProcessId != explorer.Current.ProcessId && info.ClassName != "#32768" &&
                !info.ClassName.Contains("Popup", StringComparison.OrdinalIgnoreCase)) continue;
            windows.Add(new { info.Name, info.ClassName, Type = info.ControlType.ProgrammaticName, info.IsOffscreen,
                Items = window.FindAll(TreeScope.Descendants, new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                    .Cast<AutomationElement>().Take(100).Select(e => new { e.Current.Name, e.Current.ClassName, Type = e.Current.ControlType.ProgrammaticName, e.Current.IsOffscreen }).ToArray() });
            if (windows.Count == 20) break;
        }
        return new { Windows = windows, Foreground = GetForegroundWindow().ToInt64() };
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetProcessWindowStation();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetUserObjectInformation(nint handle, int index, System.Text.StringBuilder value, uint size, out uint needed);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X; public int Y; public uint Data; public uint Flags; public uint Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key; public ushort Scan; public uint Flags; public uint Time; public nuint Extra; }
    private static void SendSingleKey(ushort key)
    {
        Input[] keys = [
            new() { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key, Scan = 0, Flags = 0, Time = 0, Extra = 0 } } },
            new() { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key, Scan = 0, Flags = 2, Time = 0, Extra = 0 } } }
        ];
        if (SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>()) != keys.Length) throw new InvalidOperationException("SendInput failed.");
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetPhysicalCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(nint process, uint flags, System.Text.StringBuilder name, ref int size);
}
