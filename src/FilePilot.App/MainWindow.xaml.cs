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

    private void ChooseDestination(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose conversion output folder", Multiselect = false, InitialDirectory = Model.DestinationFolder };
        if (picker.ShowDialog(this) == true) Model.SetDestinationFolder(picker.FolderName);
    }

    private async void ConvertImage(object sender, RoutedEventArgs e) => await Model.ConvertAsync();
    private void CancelConversion(object sender, RoutedEventArgs e) => Model.CancelConversion();

    private void OpenOutputFolder(object sender, RoutedEventArgs e)
    {
        if (!Model.CanOpenOutputFolder) return;
        try
        {
            var folder = FilePilot.Core.LocalPathPolicy.Validate(System.IO.Path.GetDirectoryName(Model.LastOutputPath)!);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (MainViewModel.IsExpected(ex) || ex is System.ComponentModel.Win32Exception)
        { Model.ReportUiError(ex); }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        // Keep the process alive while a move or native codec operation is running.
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
