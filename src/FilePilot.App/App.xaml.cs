using System.Windows;

namespace FilePilot.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        ((MainViewModel)window.DataContext).LoadArguments(e.Args);
    }
}
