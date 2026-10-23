using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;
using System.Runtime.InteropServices;
using FilePilot.Core;

namespace FilePilot.App;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly FileActions actions = new();
    private readonly Action<string> copyPath;
    private SelectedFile? selectedFile;
    private bool isBusy;
    private string status = "Launch FilePilot with one absolute file path, or choose a file.";

    public MainViewModel(Action<string> copyPath, FilePilot.Core.Conversion.IFileConverter? converter = null, FilePilot.Core.Documents.IDocumentConverter? documentConverter = null)
    {
        this.copyPath = copyPath;
        this.documentConverter = documentConverter;
        this.converter = converter ?? new FilePilot.Core.Conversion.ImageFileConverter();
        CopyPathCommand = new RelayCommand(CopyPath, () => selectedFile is not null && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RelayCommand CopyPathCommand { get; }
    public string FileName => selectedFile?.Name ?? "Choose a file to get started";
    public string FullPath => selectedFile?.FullPath ?? "No file selected";
    public string Details => selectedFile is null ? "Local files · choose one or more" : IsBatch ? $"{selection.Count} files · {selection.Sum(file => file.Length):N0} bytes total" :
        $"{(string.IsNullOrEmpty(selectedFile.Extension) ? "No extension" : selectedFile.Extension.ToUpperInvariant())}  ·  {selectedFile.Length:N0} bytes  ·  Modified {selectedFile.LastWriteTime:g}";
    public string Status { get => status; private set { status = value; OnPropertyChanged(); } }
    public bool CanMove => selectedFile is not null && !IsBatch && !IsBusy;
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
            RefreshConversionState();
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
            selection = [selectedFile];
            Status = "Ready. Choose an action. Existing files will not be overwritten.";
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            selectedFile = null;
            selection = [];
            Status = FriendlyError(ex);
        }
        ResetConversion();
        RefreshFile();
    }

    private void CopyPath()
    {
        try
        {
            // Revalidate in case the file has been removed since launch.
            copyPath(string.Join(Environment.NewLine, selection.Select(file => actions.Inspect(file.FullPath).FullPath)));
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
            selection = [selectedFile];
            ResetConversion();
            Status = result.SourceMayRemain
                ? "Partial result: destination created, but the source may still remain. Check both locations. FilePilot will not delete it automatically."
                : "Success: file moved. Copy Path now uses the new location.";
        }
        catch (Exception ex) when (IsExpected(ex)) { Status = FriendlyError(ex); }
        finally { IsBusy = false; }
    }

    private void RefreshFile()
    {
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(SelectionPaths));
        OnPropertyChanged(nameof(IsBatch));
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
        FilePilot.Core.Conversion.ImageConversionException => ex.Message,
        FilePilot.Core.Documents.DocumentConversionException => ex.Message,
        UnauthorizedAccessException => "Access denied. Choose a file and folder you have permission to use.",
        FileNotFoundException or DirectoryNotFoundException => "The file or folder no longer exists. Choose it again.",
        COMException => "The clipboard is busy or unavailable. Try Copy Path again.",
        IOException => $"The operation could not finish. The file may be locked or the drive unavailable. {ex.Message}",
        _ => ex.Message
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
