using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Runtime.InteropServices;
using FilePilot.Core;

namespace FilePilot.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly FileActions actions = new();
    private readonly Action<string> copyPath;
    private SelectedFile? selectedFile;
    private bool isBusy;
    private string status = "Launch FilePilot with one absolute file path, or choose a file.";

    public MainViewModel(Action<string> copyPath)
    {
        this.copyPath = copyPath;
        CopyPathCommand = new RelayCommand(CopyPath, () => selectedFile is not null && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RelayCommand CopyPathCommand { get; }
    public string FileName => selectedFile?.Name ?? "Choose a file to get started";
    public string FullPath => selectedFile?.FullPath ?? "No file selected";
    public string Details => selectedFile is null ? "Single-file actions · local and offline" :
        $"{(string.IsNullOrEmpty(selectedFile.Extension) ? "No extension" : selectedFile.Extension.ToUpperInvariant())}  ·  {selectedFile.Length:N0} bytes  ·  Modified {selectedFile.LastWriteTime:g}";
    public string Status { get => status; private set { status = value; OnPropertyChanged(); } }
    public bool CanMove => selectedFile is not null && !IsBusy;
    public bool CanChooseFile => !IsBusy;
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanChooseFile));
            RefreshFile();
        }
    }

    public void LoadArguments(string[] args)
    {
        if (args.Length == 1) LoadFile(args[0]);
        else if (args.Length > 1) Status = "Select one file at a time. Put paths containing spaces in double quotes.";
    }

    public void LoadFile(string path)
    {
        if (IsBusy) return;
        try
        {
            selectedFile = actions.Inspect(path);
            Status = "Ready. Move File moves this file when you confirm a destination folder.";
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            selectedFile = null;
            Status = FriendlyError(ex);
        }
        RefreshFile();
    }

    private void CopyPath()
    {
        try
        {
            // Revalidate in case the file has been removed since launch.
            copyPath(actions.Inspect(selectedFile!.FullPath).FullPath);
            Status = "Success: absolute file path copied to the clipboard.";
        }
        catch (Exception ex) when (IsExpected(ex)) { Status = FriendlyError(ex); }
    }

    public async Task MoveAsync(string destinationFolder)
    {
        if (!CanMove) return;
        var source = selectedFile!;
        IsBusy = true;
        Status = "Moving file… Please leave FilePilot open until this finishes.";
        try
        {
            var result = await Task.Run(() => actions.Move(source.FullPath, destinationFolder));
            // Preserve known metadata without a second I/O call that could obscure success.
            selectedFile = source with { FullPath = result.DestinationPath };
            Status = result.SourceMayRemain
                ? "Partial result: destination created, but the source may still remain. Check both locations. FilePilot will not delete it automatically."
                : "Success: file moved. Copy Path now uses the new location.";
        }
        catch (Exception ex) when (IsExpected(ex)) { Status = FriendlyError(ex); }
        finally { IsBusy = false; }
    }

    private void RefreshFile()
    {
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(CanMove));
        CopyPathCommand.Refresh();
    }

    internal static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or System.Security.SecurityException or COMException;

    internal static string FriendlyError(Exception ex) => ex switch
    {
        FileCollisionException => ex.Message,
        UnauthorizedAccessException => "Access denied. Choose a file and folder you have permission to use.",
        FileNotFoundException or DirectoryNotFoundException => "The file or folder no longer exists. Choose it again.",
        COMException => "The clipboard is busy or unavailable. Try Copy Path again.",
        IOException => $"The operation could not finish. The file may be locked or the drive unavailable. {ex.Message}",
        _ => ex.Message
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
