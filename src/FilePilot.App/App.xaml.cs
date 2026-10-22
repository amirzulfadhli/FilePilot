using System.Windows;

namespace FilePilot.App;

public partial class App : Application
{
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
        window.Show();
        ((MainViewModel)window.DataContext).LoadArguments(e.Args);
    }
}
