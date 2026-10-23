using System.Windows;

namespace FilePilot.App;

public partial class App : Application
{
    private readonly Action<StartupEventArgs>? startupAction;
    private Widget.ResidentInstance? residentInstance;
    private Widget.WidgetHost? host;
    public App() { }
    // Keep a real, pumped WPF Application in window tests without launching another
    // widget or reading the user's saved preferences inside testhost.
    internal App(Action<StartupEventArgs> startupAction) => this.startupAction = startupAction;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (startupAction is not null) { startupAction(e); return; }
        if (e.Args.Length > 0 && e.Args[0] == "--diagnostics") { RunDiagnostics(e.Args); return; }
        if (e.Args.Length is 1 or 2 && e.Args[0] is "--register-context-menu" or "--unregister-context-menu" && (e.Args.Length == 1 || e.Args[1] == "--quiet"))
        {
            try
            {
                var register = e.Args[0] == "--register-context-menu";
                if (register) ExplorerIntegration.Register(Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate FilePilot.exe."));
                else ExplorerIntegration.Unregister();
                if (e.Args.Length == 1) MessageBox.Show(register
                    ? "FilePilot was added for PNG, JPG, JPEG, WebP and DOCX files for your Windows account. On Windows 11, use Show more options."
                    : "Context-menu entries pointing to this FilePilot executable were removed for your Windows account. Newer installations are preserved.", "FilePilot", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0);
            }
            catch (Exception ex) when (MainViewModel.IsExpected(ex) || ex is InvalidOperationException)
            {
                if (e.Args.Length == 1) MessageBox.Show($"Explorer integration could not finish. {ex.Message}", "FilePilot", MessageBoxButton.OK, MessageBoxImage.Error);
                else Console.Error.WriteLine($"Explorer integration could not finish. {ex.Message}");
                Shutdown(1);
            }
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        var model = (MainViewModel)window.DataContext;
        bool resident = e.Args.Length == 0 || e.Args.SequenceEqual(new[] { "--widget" });
        string? notice = null;
        var settings = new WidgetSettings();
        try { settings = SettingsStore.Default.Load(); }
        catch (Exception ex) when (MainViewModel.IsExpected(ex) || ex is System.Text.Json.JsonException)
        { notice = "Settings could not be loaded; defaults are active. " + ex.Message; }
        if (resident)
        {
            residentInstance = Widget.ResidentInstance.TryStart(@"Local\FilePilot.Widget." + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value);
            if (residentInstance is null) { resident = false; notice = "FilePilot is already running in the notification area. This popup can still choose files."; }
        }
        window.Configure(resident, settings);
        if (resident)
        {
            host = new Widget.WidgetHost(window);
            window.Host = host;
            try { host.SetShortcut(settings.Shortcut); }
            catch (System.ComponentModel.Win32Exception ex) { notice = ex.Message; }
            model.SetStatus($"Ready. Select files in Explorer and press {settings.Shortcut}. Escape hides this popup; use the notification icon to reopen or exit.");
        }
        else model.LoadArguments(e.Args);
        if (notice is not null) model.SetStatus(notice);
        window.ShowPopup();
    }
    private async void RunDiagnostics(string[] args)
    {
        Diagnostics.DiagnosticOptions? options = null;
        try
        {
            options = Diagnostics.DiagnosticOptions.Parse(args);
            var window = new MainWindow();
            MainWindow = window;
            // Isolate diagnostics from saved preferences and the ordinary resident-instance mutex.
            window.Configure(options.Widget, new WidgetSettings());
            window.IsEnabled = false; // The synthetic run must not start user file operations.
            bool? hotkeyRegistered = null;
            if (options.Widget)
            {
                host = new Widget.WidgetHost(window);
                window.Host = host;
                try { host.SetShortcut("Ctrl+Alt+F"); hotkeyRegistered = true; }
                catch (System.ComponentModel.Win32Exception) { hotkeyRegistered = false; }
            }
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.ContentRendered += (_, _) => rendered.TrySetResult();
            window.ShowPopup();
            await Diagnostics.DiagnosticRunner.RunAsync(window, options, rendered.Task, hotkeyRegistered);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            // Diagnostic mode is unattended: failures use a nonzero exit/report, never a blocking dialog.
            if (options is not null)
            {
                try { Diagnostics.DiagnosticOptions.WriteReport(options.ReportPath, new { SchemaVersion = 1, Succeeded = false, Error = ex.GetType().Name + ": " + ex.Message }); }
                catch (Exception reportError) when (MainViewModel.IsExpected(reportError)) { }
            }
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        host?.Dispose();
        residentInstance?.Dispose();
        base.OnExit(e);
    }
}
