using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using FilePilot.App;
using Xunit;
using Xunit.Abstractions;

namespace FilePilot.Tests;

[CollectionDefinition("WPF desktop", DisableParallelization = true)]
public sealed class WpfDesktopCollection;

[Collection("WPF desktop")]
public sealed class WindowTests(ITestOutputHelper output)
{
    [Fact]
    public void WindowAndBothThemeDictionariesLoadOnStaThread()
    {
        Exception? failure = null;
        var trace = new Checkpoints();
        trace.Record($"host: processors={Environment.ProcessorCount}, apartment={Thread.CurrentThread.GetApartmentState()}, context={SynchronizationContext.Current?.GetType().Name ?? "none"}");
        var thread = new Thread(() =>
        {
            FilePilot.App.App? application = null;
            try
            {
                trace.Record("STA delegate entered");
                trace.Run("inspect STA context", () => trace.Record($"apartment={Thread.CurrentThread.GetApartmentState()}, dispatcherExists={Dispatcher.FromThread(Thread.CurrentThread) is not null}, context={SynchronizationContext.Current?.GetType().Name ?? "none"}"));
                trace.Run("initialize STA Dispatcher", () => { _ = Dispatcher.CurrentDispatcher; });
                bool started = false;
                trace.Run("Application constructor", () => application = new FilePilot.App.App(_ =>
                { started = true; trace.Record("event: isolated Application.OnStartup"); }));
                Assert.NotNull(application);
                application.Dispatcher.ShutdownStarted += (_, _) => trace.Record("event: dispatcher ShutdownStarted");
                application.Dispatcher.ShutdownFinished += (_, _) => trace.Record("event: dispatcher ShutdownFinished");
                application.Exit += (_, _) => trace.Record("event: Application.Exit");
                trace.Run("Application.InitializeComponent", application.InitializeComponent);
                Assert.Equal(ShutdownMode.OnMainWindowClose, application.ShutdownMode);
                application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                application.DispatcherUnhandledException += (_, e) =>
                {
                    failure ??= e.Exception;
                    trace.Record("dispatcher exception: " + e.Exception);
                    e.Handled = true;
                    application.Shutdown();
                };
                // The callback and every await continuation run on the pumped STA dispatcher.
                application.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        Assert.True(started);
                        Assert.IsType<DispatcherSynchronizationContext>(SynchronizationContext.Current);
                        await ExerciseWindowsAsync(trace);
                    }
                    catch (Exception ex) { failure = ex; trace.Record("exception: " + ex); }
                    finally { trace.Run("Application.Shutdown", application.Shutdown); }
                }), DispatcherPriority.Normal);
                trace.Run("Application.Run (dispatcher loop)", () => application.Run());
                Assert.True(application.Dispatcher.HasShutdownFinished);
                trace.Record("verified: dispatcher shutdown finished");
            }
            catch (Exception ex) { failure = ex; trace.Record("exception: " + ex); }
            finally
            {
                if (application is not null && !application.Dispatcher.HasShutdownFinished)
                    trace.Run("cleanup dispatcher", application.Dispatcher.InvokeShutdown);
                trace.Record("STA delegate exited");
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = "FilePilot WPF acceptance";
        trace.Run("STA Thread.Start", thread.Start);
        var exited = thread.Join(TimeSpan.FromSeconds(20));
        trace.Record($"join: exited={exited}, threadState={thread.ThreadState}");
        output.WriteLine(trace.ToString());
        Assert.True(exited, "WPF window loading timed out.\n" + trace);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static async Task ExerciseWindowsAsync(Checkpoints trace)
    {
        MainWindow window = null!;
        trace.Run("MainWindow constructor", () => window = new MainWindow());
        SettingsWindow? settings = null;
        try
        {
            window.SourceInitialized += (_, _) => trace.Record("event: SourceInitialized");
            window.Loaded += (_, _) => trace.Record("event: Loaded");
            window.StateChanged += (_, _) => trace.Record($"event: StateChanged {window.WindowState}");
            var fixture = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FilePilot-layout-" + Guid.NewGuid().ToString("N") + ".png");
            System.IO.File.WriteAllText(fixture, "Metadata-only selection for layout test.");
            try { trace.Run("load image selection", () => ((MainViewModel)window.DataContext).LoadFile(fixture)); }
            finally { System.IO.File.Delete(fixture); }
            foreach (var theme in new[] { "Light", "Dark", "HighContrast" })
            {
                ResourceDictionary resources = null!;
                trace.Run($"load {theme} resources", () => resources = new ResourceDictionary
                { Source = new Uri($"/FilePilot;component/Themes/{theme}.xaml", UriKind.Relative) });
                Assert.True(resources.Contains("TextBrush"));
                window.Resources.MergedDictionaries.Clear();
                window.Resources.MergedDictionaries.Add(resources);
                trace.Run($"{theme} Measure", () => window.Measure(new Size(480, 640)));
                trace.Run($"{theme} Arrange", () => window.Arrange(new Rect(0, 0, 480, 640)));
            }
            window.Resources.MergedDictionaries.Clear();
            trace.Run("theme cache, contrast pairs and templates", () => VerifyThemes(window));
            window.Configure(false, new());
            var dismiss = (Button)window.FindName("DismissButton");
            Assert.Equal("Close popup", AutomationProperties.GetName(dismiss));
            Assert.IsType<MainViewModel>(window.DataContext);
            Assert.Equal(2, window.InputBindings.Count);
            Assert.Contains(window.InputBindings.Cast<InputBinding>(), binding => binding is KeyBinding { Key: Key.F1 } && binding.Command == ApplicationCommands.Help);
            Assert.Equal(2, window.CommandBindings.Count);
            Assert.Equal(System.Windows.Input.ApplicationCommands.Open, ((System.Windows.Input.CommandBinding)window.CommandBindings[0]).Command);
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            // Exercise the native HWND/DPI path: layout-only tests cannot catch height replacement.
            await ShowAndRenderAsync(window, () => window.ShowPopup(), "image ShowPopup", trace);
            Assert.True(window.IsVisible);
            Assert.Equal(640, window.Height);
            Assert.Same(window.FindName("CopyPathButton"), FocusManager.GetFocusedElement(window));
            trace.Run("minimize", () => window.WindowState = WindowState.Minimized);
            trace.Run("restore ShowPopup", () => window.ShowPopup());
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(WindowState.Normal, window.WindowState);
            ((MainViewModel)window.DataContext).ClearSelection("Choose files");
            trace.Run("empty ShowPopup", () => window.ShowPopup());
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(420, window.Height);
            Assert.Same(window.FindName("ChooseFilesButton"), FocusManager.GetFocusedElement(window));
            var status = (TextBlock)window.FindName("StatusText");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            var peer = UIElementAutomationPeer.CreatePeerForElement(status);
            Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
            trace.Run("resident hide and primary focus restore", () =>
            {
                window.Configure(true, new());
                Assert.Equal("Hide popup", AutomationProperties.GetName(dismiss));
                window.Close();
                Assert.False(window.IsVisible);
                window.ShowPopup();
                Assert.Same(window.FindName("ChooseFilesButton"), FocusManager.GetFocusedElement(window));
                window.Configure(false, new());
            });
            var docFixture = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FilePilot-doc-layout-" + Guid.NewGuid().ToString("N") + ".docx");
            System.IO.File.WriteAllText(docFixture, "Metadata-only document fixture");
            try
            {
                var model = (MainViewModel)window.DataContext;
                model.LoadFile(docFixture);
                Assert.True(model.IsDocumentSelected);
                Assert.False(model.CanConvertDocument);
                trace.Run("document ShowPopup", () => window.ShowPopup());
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(640, window.Height);
            }
            finally { System.IO.File.Delete(docFixture); }
            trace.Run("Settings constructor", () => settings = new SettingsWindow(new(), _ => { }));
            Assert.NotNull(settings);
            trace.Run("Settings Measure", () => settings.Measure(new Size(460, 520)));
            trace.Run("Settings Arrange", () => settings.Arrange(new Rect(0, 0, 460, 520)));
            settings.Owner = window;
            await ShowAndRenderAsync(settings, settings.Show, "Settings.Show", trace);
            var settingsLayout = Assert.IsType<System.Windows.Controls.DockPanel>(settings.Content);
            var settingsFooter = Assert.IsType<System.Windows.Controls.StackPanel>(settingsLayout.Children[0]);
            Assert.Equal(System.Windows.Controls.Dock.Bottom, System.Windows.Controls.DockPanel.GetDock(settingsFooter));
            var settingsActions = Assert.IsType<System.Windows.Controls.DockPanel>(settingsFooter.Children[1]);
            Assert.Contains(settingsActions.Children.Cast<System.Windows.UIElement>(), child => child is System.Windows.Controls.Button { IsCancel: true });
            Assert.Contains(settingsActions.Children.Cast<System.Windows.UIElement>(), child => child is System.Windows.Controls.Button { IsDefault: true });
            await VerifyAvailableMonitorsAsync(window, trace);
            trace.Run("scaled work-area layout", () => VerifyScaledLayout(window, settings));
            trace.Run("Settings.Close after layout checks", settings.Close);
            settings = null;
            await VerifyBusyShortcutAsync(window, trace);
            var help = new HelpWindow("Ctrl+Alt+F", true) { Owner = window };
            try
            {
                await ShowAndRenderAsync(help, help.Show, "Help.Show", trace);
                var layout = Assert.IsType<DockPanel>(help.Content);
                var close = Assert.IsType<Button>(layout.Children[0]);
                Assert.True(close.IsCancel);
                Assert.True(close.IsDefault);
                Assert.Equal(Dock.Bottom, DockPanel.GetDock(close));
                var scroll = Assert.IsType<ScrollViewer>(layout.Children[1]);
                Assert.Same(scroll, FocusManager.GetFocusedElement(help));
                Assert.Same(Application.Current.FindResource("HelpFocusVisual"), scroll.FocusVisualStyle);
                var paragraphs = ((StackPanel)scroll.Content).Children.Cast<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Contains(paragraphs, text => text.Contains("Ctrl+Alt+F"));
                help.MaxWidth = 320; help.MaxHeight = 384; help.UpdateLayout();
                var bounds = close.TransformToAncestor(layout).TransformBounds(new Rect(close.RenderSize));
                Assert.True(bounds.Bottom <= layout.ActualHeight + 1);
                Assert.True(ApplicationCommands.Help.CanExecute(null, window));
            }
            finally { trace.Run("Help.Close", help.Close); }
            VerifyRoutedHelp(window, trace);
        }
        finally
        {
            if (settings is not null) trace.Run("Settings.Close", settings.Close);
            trace.Run("MainWindow.Close", window.Close);
        }
    }

    private static void VerifyRoutedHelp(MainWindow window, Checkpoints trace)
    {
        Exception? modalFailure = null;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            HelpWindow? dialog = null;
            try
            {
                dialog = Assert.Single(window.OwnedWindows.OfType<HelpWindow>());
                Assert.True(dialog.IsVisible);
                Assert.True(window.IsDialogOpen);
                // ShowDialog disables the native owner, not its IsEnabled dependency property.
                Assert.False(IsWindowEnabled(new System.Windows.Interop.WindowInteropHelper(window).Handle));
                Assert.False(ApplicationCommands.Help.CanExecute(null, window));
                trace.Record("verified: routed help modal owns focus and blocks reentry");
                window.ExitWidget();
                Assert.True(window.IsVisible);
            }
            catch (Exception ex) { modalFailure = ex; }
            finally { dialog?.Close(); }
        }), DispatcherPriority.ApplicationIdle);
        trace.Run("routed Help.Execute / ShowDialog", () => ApplicationCommands.Help.Execute(null, window));
        if (modalFailure is not null) ExceptionDispatchInfo.Capture(modalFailure).Throw();
        Assert.False(window.IsDialogOpen);
        Assert.True(window.IsEnabled);
        Assert.True(IsWindowEnabled(new System.Windows.Interop.WindowInteropHelper(window).Handle));
        Assert.True(ApplicationCommands.Help.CanExecute(null, window));
    }

    private static async Task VerifyBusyShortcutAsync(MainWindow window, Checkpoints trace)
    {
        var originalModel = window.DataContext;
        var converter = new BlockingConverter();
        var model = new MainViewModel(_ => { }, converter);
        var source = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FilePilot-busy-" + Guid.NewGuid().ToString("N") + ".png");
        System.IO.File.WriteAllText(source, "Unchanged lifecycle fixture; the injected converter does not decode it.");
        Task? conversion = null;
        try
        {
            window.DataContext = model;
            model.LoadFile(source);
            Assert.True(model.CanConvert, model.Status);
            conversion = model.ConvertAsync();
            trace.Record("BEGIN await injected converter start");
            await converter.Started.Task;
            trace.Record("END await injected converter start");
            Assert.True(model.IsBusy);
            Assert.False(ApplicationCommands.Help.CanExecute(null, window));
            Assert.False(((Button)window.FindName("HelpButton")).IsEnabled);
            window.Hide();
            using var host = new FilePilot.App.Widget.WidgetHost(window);
            trace.Run("busy shortcut reopens without Explorer capture", host.ActivateFromShortcut);
            Assert.True(window.IsVisible);
            Assert.Equal(source, model.FullPath);
            Assert.True(model.CanCancelConversion);
            Assert.Same(window.FindName("CancelImageButton"), FocusManager.GetFocusedElement(window));
            model.CancelConversion();
            trace.Record("BEGIN await busy shortcut cancellation");
            await conversion;
            trace.Record("END await busy shortcut cancellation");
            Assert.False(model.IsBusy);
            Assert.Null(model.LastOutputPath);
            Assert.Contains("cancelled", model.Status);
            Assert.True(System.IO.File.Exists(source));
        }
        finally
        {
            model.CancelConversion();
            if (conversion is not null) await conversion;
            window.DataContext = originalModel;
            System.IO.File.Delete(source);
        }
    }

    private sealed class BlockingConverter : FilePilot.Core.Conversion.IFileConverter
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<FilePilot.Core.Conversion.ImageFileFormat> GetOutputFormats(string path) => [FilePilot.Core.Conversion.ImageFileFormat.Jpg];
        public async Task<FilePilot.Core.Conversion.ConversionResult> ConvertAsync(FilePilot.Core.Conversion.ConversionRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The lifecycle test must cancel its converter.");
        }
    }

    private static async Task VerifyAvailableMonitorsAsync(MainWindow window, Checkpoints trace)
    {
        var monitors = new List<nint>();
        Assert.True(EnumDisplayMonitors(0, 0, (monitor, _, _, _) => { monitors.Add(monitor); return true; }, 0));
        Assert.NotEmpty(monitors);
        foreach (var monitor in monitors)
        {
            var info = new FilePilot.App.Widget.NativeMethods.MonitorInfo
            { Size = Marshal.SizeOf<FilePilot.App.Widget.NativeMethods.MonitorInfo>() };
            Assert.True(FilePilot.App.Widget.NativeMethods.GetMonitorInfo(monitor, ref info));
            var work = info.Work;
            trace.Run($"monitor work area {work.Left},{work.Top}..{work.Right},{work.Bottom}", () =>
                window.ShowPopup(new() { X = work.Right - 1, Y = work.Bottom - 1 }));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Assert.True(GetWindowRect(hwnd, out var bounds));
            var dpi = FilePilot.App.Widget.NativeMethods.GetDpiForWindow(hwnd);
            trace.Record($"monitor result: DPI={dpi}, HWND={bounds.Left},{bounds.Top}..{bounds.Right},{bounds.Bottom}");
            Assert.True(bounds.Left >= work.Left && bounds.Top >= work.Top &&
                bounds.Right <= work.Right + 1 && bounds.Bottom <= work.Bottom + 1,
                $"Popup escaped monitor work area at {dpi} DPI.");
        }
    }

    private delegate bool MonitorCallback(nint monitor, nint hdc, nint rectangle, nint data);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out FilePilot.App.Widget.NativeMethods.Rect rectangle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint hwnd);

    private static void VerifyThemes(MainWindow window)
    {
        var resources = Application.Current.Resources;
        var original = resources.MergedDictionaries[0];
        try
        {
            var themes = new ThemeResources(resources);
            themes.Apply(false, false);
            var light = resources.MergedDictionaries[0];
            VerifyFocusColor(window);
            themes.Apply(true, false);
            var dark = resources.MergedDictionaries[0];
            VerifyFocusColor(window);
            var expander = (Expander)window.FindName("RenameExpander");
            expander.Measure(new Size(300, 120));
            expander.Arrange(new Rect(0, 0, 300, 120));
            Assert.Equal(((SolidColorBrush)window.FindResource("TextBrush")).Color,
                ((SolidColorBrush)expander.Foreground).Color);
            Assert.Equal(((SolidColorBrush)window.FindResource("TextBrush")).Color,
                ((SolidColorBrush)VisualChildren<TextBlock>(expander).Single(text => text.Text == "Quick rename").Foreground).Color);
            themes.Apply(true, true);
            var contrast = resources.MergedDictionaries[0];
            VerifyFocusColor(window);
            Assert.Equal(SystemColors.WindowColor, ((SolidColorBrush)window.FindResource("WindowBrush")).Color);
            Assert.Equal(SystemColors.WindowTextColor, ((SolidColorBrush)window.FindResource("TextBrush")).Color);
            Assert.Equal(SystemColors.HighlightColor, ((SolidColorBrush)window.FindResource("HoverBrush")).Color);
            Assert.Equal(SystemColors.HighlightTextColor, ((SolidColorBrush)window.FindResource("HoverTextBrush")).Color);
            // Test the actual implicit item template, including the paired selected text brush.
            var item = new ComboBoxItem { Content = "PNG", IsSelected = true };
            item.ApplyTemplate();
            item.Measure(new Size(180, 40));
            item.Arrange(new Rect(0, 0, 180, 40));
            Assert.Equal(SystemColors.HighlightTextColor, ((SolidColorBrush)item.Foreground).Color);
            Assert.Equal(SystemColors.HighlightColor, ((SolidColorBrush)((Border)item.Template.FindName("ItemBorder", item)).Background).Color);
            Assert.Equal(SystemColors.HighlightTextColor, ((SolidColorBrush)VisualChildren<TextBlock>(item).Single().Foreground).Color);
            var copy = (Button)window.FindName("CopyPathButton");
            copy.IsEnabled = false;
            copy.ApplyTemplate();
            Assert.Equal(SystemColors.GrayTextColor, ((SolidColorBrush)copy.Foreground).Color);
            copy.ClearValue(UIElement.IsEnabledProperty);
            for (int i = 0; i < 100; i++)
            {
                themes.Apply(false, false);
                Assert.Same(light, resources.MergedDictionaries[0]);
                themes.Apply(true, false);
                Assert.Same(dark, resources.MergedDictionaries[0]);
                themes.Apply(false, true);
                Assert.Same(contrast, resources.MergedDictionaries[0]);
            }
            themes.Apply(false, false);
            Assert.Same(light, resources.MergedDictionaries[0]);
        }
        finally { resources.MergedDictionaries[0] = original; }
    }

    private static void VerifyFocusColor(MainWindow window)
    {
        var focus = new Control { Style = (Style)window.FindResource("HelpFocusVisual") };
        focus.ApplyTemplate();
        var outline = Assert.IsType<Border>(VisualTreeHelper.GetChild(focus, 0));
        Assert.Equal(((SolidColorBrush)window.FindResource("AccentBrush")).Color,
            ((SolidColorBrush)outline.BorderBrush).Color);
        Assert.True(outline.BorderThickness.Left > 0);
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void VerifyScaledLayout(MainWindow window, SettingsWindow settings)
    {
        // Simulated work-area constraints, not a claim of physical monitor DPI switching.
        foreach (int physicalWidth in new[] { 960, 640 })
        foreach (uint dpi in new uint[] { 96, 120, 144, 192 })
        {
            var available = FilePilot.App.Widget.PopupPlacement.WorkAreaSize(physicalWidth, 768, dpi);
            foreach (var target in new Window[] { window, settings })
            {
                target.MaxWidth = available.Width;
                target.MaxHeight = available.Height;
                target.UpdateLayout();
                var root = (FrameworkElement)target.Content;
                Assert.True(root.ActualWidth > 0 && root.ActualHeight > 0);
                var action = target == window ? (FrameworkElement)window.FindName("ChooseFilesButton")
                    : ((DockPanel)settings.Content).Children[0] as FrameworkElement;
                Assert.NotNull(action);
                var bounds = action.TransformToAncestor(root).TransformBounds(new Rect(action.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= root.ActualHeight + 1, $"{target.Title} at {dpi} DPI: footer {bounds}, root {root.RenderSize}");
                Assert.True(bounds.Left >= 0 && bounds.Right <= root.ActualWidth + 1);
                Assert.True(target.UseLayoutRounding);
            }
        }
    }

    private static async Task ShowAndRenderAsync(Window window, Action show, string operation, Checkpoints trace)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRendered(object? sender, EventArgs e) { trace.Record("event: " + operation + " ContentRendered"); rendered.TrySetResult(); }
        window.ContentRendered += OnRendered;
        try
        {
            trace.Run(operation, show);
            trace.Record("BEGIN await " + operation + " ContentRendered");
            await rendered.Task;
            trace.Record("END await " + operation + " ContentRendered");
        }
        finally { window.ContentRendered -= OnRendered; }
    }

    private sealed class Checkpoints
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly ConcurrentQueue<string> entries = new();
        internal void Record(string message) => entries.Enqueue($"{clock.Elapsed.TotalMilliseconds,9:F1} ms [thread {Environment.CurrentManagedThreadId}] {message}");
        internal void Run(string operation, Action action)
        {
            Record("BEGIN " + operation);
            action();
            Record("END " + operation);
        }
        public override string ToString() => string.Join(Environment.NewLine, entries);
    }
}
