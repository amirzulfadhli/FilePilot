using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using FilePilot.App;
using Xunit;
using Xunit.Abstractions;

namespace FilePilot.Tests;

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
            try
            {
                trace.Record($"STA entry: apartment={Thread.CurrentThread.GetApartmentState()}, dispatcherExists={Dispatcher.FromThread(Thread.CurrentThread) is not null}, context={SynchronizationContext.Current?.GetType().Name ?? "none"}");
                FilePilot.App.App application = null!;
                trace.Run("Application constructor", () => application = new FilePilot.App.App());
                application.Dispatcher.ShutdownStarted += (_, _) => trace.Record("event: dispatcher ShutdownStarted");
                application.Dispatcher.ShutdownFinished += (_, _) => trace.Record("event: dispatcher ShutdownFinished");
                application.Exit += (_, _) => trace.Record("event: Application.Exit");
                trace.Run("Application.InitializeComponent", application.InitializeComponent);
                MainWindow window = null!;
                trace.Run("MainWindow constructor", () => window = new MainWindow());
                window.SourceInitialized += (_, _) => trace.Record("event: SourceInitialized");
                window.Loaded += (_, _) => trace.Record("event: Loaded");
                window.ContentRendered += (_, _) => trace.Record("event: ContentRendered");
                window.StateChanged += (_, _) => trace.Record($"event: StateChanged {window.WindowState}");
                var fixture = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FilePilot-layout-" + Guid.NewGuid().ToString("N") + ".png");
                System.IO.File.WriteAllText(fixture, "Metadata-only selection for layout test.");
                try { trace.Run("load image selection", () => ((MainViewModel)window.DataContext).LoadFile(fixture)); }
                finally { System.IO.File.Delete(fixture); }
                foreach (var theme in new[] { "Light", "Dark" })
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
                Assert.IsType<MainViewModel>(window.DataContext);
                Assert.Single(window.InputBindings);
                Assert.Single(window.CommandBindings);
                Assert.Equal(System.Windows.Input.ApplicationCommands.Open, ((System.Windows.Input.CommandBinding)window.CommandBindings[0]).Command);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                // Exercise the native HWND/DPI path: layout-only tests cannot catch height replacement.
                trace.Run("image ShowPopup", () => window.ShowPopup());
                Assert.True(window.IsVisible);
                Assert.Equal(640, window.Height);
                trace.Run("minimize", () => window.WindowState = WindowState.Minimized);
                trace.Run("restore ShowPopup", () => window.ShowPopup());
                Assert.Equal(WindowState.Normal, window.WindowState);
                ((MainViewModel)window.DataContext).ClearSelection("Choose files");
                trace.Run("empty ShowPopup", () => window.ShowPopup());
                Assert.Equal(420, window.Height);
                var docFixture = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FilePilot-doc-layout-" + Guid.NewGuid().ToString("N") + ".docx");
                System.IO.File.WriteAllText(docFixture, "Metadata-only document fixture");
                try
                {
                    var model = (MainViewModel)window.DataContext;
                    model.LoadFile(docFixture);
                    Assert.True(model.IsDocumentSelected);
                    Assert.False(model.CanConvertDocument);
                    trace.Run("document ShowPopup", () => window.ShowPopup());
                    Assert.Equal(640, window.Height);
                }
                finally { System.IO.File.Delete(docFixture); }
                SettingsWindow settings = null!;
                trace.Run("Settings constructor", () => settings = new SettingsWindow(new(), _ => { }));
                trace.Run("Settings Measure", () => settings.Measure(new Size(460, 520)));
                trace.Run("Settings Arrange", () => settings.Arrange(new Rect(0, 0, 460, 520)));
                var settingsLayout = Assert.IsType<System.Windows.Controls.DockPanel>(settings.Content);
                var settingsFooter = Assert.IsType<System.Windows.Controls.StackPanel>(settingsLayout.Children[0]);
                Assert.Equal(System.Windows.Controls.Dock.Bottom, System.Windows.Controls.DockPanel.GetDock(settingsFooter));
                var settingsActions = Assert.IsType<System.Windows.Controls.DockPanel>(settingsFooter.Children[1]);
                Assert.Contains(settingsActions.Children.Cast<System.Windows.UIElement>(), child => child is System.Windows.Controls.Button { IsCancel: true });
                Assert.Contains(settingsActions.Children.Cast<System.Windows.UIElement>(), child => child is System.Windows.Controls.Button { IsDefault: true });
                trace.Run("Settings.Close", settings.Close);
                trace.Run("MainWindow.Close", window.Close);
                trace.Run("Application.Shutdown", application.Shutdown);
                trace.Record($"dispatcher shutdown started={application.Dispatcher.HasShutdownStarted}, finished={application.Dispatcher.HasShutdownFinished}");
            }
            catch (Exception ex) { failure = ex; trace.Record("exception: " + ex); }
            finally { trace.Record("STA delegate exited"); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = "FilePilot WPF acceptance";
        thread.Start();
        var exited = thread.Join(TimeSpan.FromSeconds(20));
        trace.Record($"join: exited={exited}, threadState={thread.ThreadState}");
        output.WriteLine(trace.ToString());
        Assert.True(exited, "WPF window loading timed out.\n" + trace);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
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
