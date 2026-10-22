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
        var picker = new OpenFileDialog { Title = "Choose a file for FilePilot", CheckFileExists = true, Multiselect = true };
        if (ShowPicker(() => picker.ShowDialog(this)) == true) { Model.LoadFiles(picker.FileNames); ShowPopup(); }
    }

    private async void MoveFile(object sender, RoutedEventArgs e)
    {
        if (!Model.CanMove) return;
        var picker = new OpenFolderDialog { Title = "Move file to this folder — existing files will not be replaced", Multiselect = false };
        if (ShowPicker(() => picker.ShowDialog(this)) == true) await Model.MoveAsync(picker.FolderName);
    }

    private void ChooseDestination(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose conversion output folder", Multiselect = false, InitialDirectory = Model.DestinationFolder };
        if (ShowPicker(() => picker.ShowDialog(this)) == true) Model.SetDestinationFolder(picker.FolderName);
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
        if (Model.IsBusy || IsDialogOpen) e.Cancel = true;
        else if (resident && !exiting) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }

    private void ToggleTheme(object sender, RoutedEventArgs e)
    {
        darkTheme = !darkTheme;
        settings = settings with { DarkTheme = darkTheme };
        ApplyTheme();
    }
}
