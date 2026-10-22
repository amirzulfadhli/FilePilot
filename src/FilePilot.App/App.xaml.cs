using System.Windows;

namespace FilePilot.App;

public partial class App : Application
{
    private Mutex? residentMutex;
    private Widget.WidgetHost? host;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 1 && e.Args[0] is "--register-context-menu" or "--unregister-context-menu")
        {
            try
            {
                var register = e.Args[0] == "--register-context-menu";
                if (register) ExplorerIntegration.Register(Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate FilePilot.exe."));
                else ExplorerIntegration.Unregister();
                MessageBox.Show(register
                    ? "FilePilot was added for PNG, JPG, JPEG, and WebP files for your Windows account. On Windows 11, use Show more options."
                    : "FilePilot's context-menu entries were removed for your Windows account.", "FilePilot", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0);
            }
            catch (Exception ex) when (MainViewModel.IsExpected(ex) || ex is InvalidOperationException)
            {
                MessageBox.Show($"Explorer integration could not finish. {ex.Message}", "FilePilot", MessageBoxButton.OK, MessageBoxImage.Error);
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
            residentMutex = new Mutex(false, @"Local\FilePilot.Widget." + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, out var created);
            if (!created) { resident = false; notice = "FilePilot is already running in the notification area. This popup can still choose files."; }
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
    protected override void OnExit(ExitEventArgs e)
    {
        host?.Dispose();
        residentMutex?.Dispose();
        base.OnExit(e);
    }
}
