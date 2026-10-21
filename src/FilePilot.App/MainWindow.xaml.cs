using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FilePilot.App;

public partial class MainWindow : Window
{
    private bool darkTheme;
    private MainViewModel Model => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(path => Clipboard.SetDataObject(path, true));
    }

    private void ChooseFile(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Choose a file for FilePilot", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) == true) Model.LoadFile(picker.FileName);
    }

    private async void MoveFile(object sender, RoutedEventArgs e)
    {
        if (!Model.CanMove) return;
        var picker = new OpenFolderDialog { Title = "Move file to this folder — existing files will not be replaced", Multiselect = false };
        if (picker.ShowDialog(this) == true) await Model.MoveAsync(picker.FolderName);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Do not terminate the process in the middle of an uncancellable native move.
        if (Model.IsBusy) e.Cancel = true;
        base.OnClosing(e);
    }

    private void ToggleTheme(object sender, RoutedEventArgs e)
    {
        darkTheme = !darkTheme;
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"Themes/{(darkTheme ? "Dark" : "Light")}.xaml", UriKind.Relative)
        };
        ((Button)sender).Content = darkTheme ? "_Light theme" : "_Dark theme";
    }
}
