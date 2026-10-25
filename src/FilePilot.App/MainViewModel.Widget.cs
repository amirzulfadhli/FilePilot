using System.IO;
using FilePilot.Core;
using FilePilot.Core.Conversion;

namespace FilePilot.App;

public sealed partial class MainViewModel
{
    private IReadOnlyList<SelectedFile> selection = [];
    private string renameText = "";
    private bool changeExtension;
    public bool IsBatch => selection.Count > 1;
    public string SelectionSummary => IsBatch ? $"{selection.Count} files selected" : FileName;
    public string SelectionPaths => string.Join(Environment.NewLine, selection.Select(f => f.FullPath));
    public string BatchDetails { get; private set; } = "";
    public string RenameText { get => renameText; set { if (!IsBusy) { renameText = value; OnPropertyChanged(); } } }
    public bool ChangeExtension
    {
        get => changeExtension;
        set { if (!IsBusy) { changeExtension = value; ResetRename(); OnPropertyChanged(); } }
    }
    public void LoadFiles(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        try
        {
            var list = paths.Take(BatchConverter.MaxFiles + 1).ToArray();
            if (list.Length is < 1 or > BatchConverter.MaxFiles) throw new ArgumentException("Choose 1–100 local files.");
            selection = list.Distinct(StringComparer.OrdinalIgnoreCase).Select(actions.Inspect).ToArray();
            selectedFile = selection[0];
            Status = IsBatch ? $"{selection.Count} files ready. Image conversion is sequential; DOCX export requires a single selection. Existing outputs are never replaced." : "Ready. Choose an action.";
        }
        catch (Exception ex) when (IsExpected(ex)) { ClearSelection(FriendlyError(ex)); return; }
        ResetConversion();
        RefreshFile();
    }
    public void ClearSelection(string message)
    {
        if (IsBusy) return;
        selectedFile = null;
        selection = [];
        ResetConversion();
        RefreshFile();
        Status = message;
    }
    public void SetStatus(string message) => Status = message;
    private void ResetRename()
    {
        renameText = selectedFile is null || IsBatch ? "" : changeExtension ? selectedFile.Name : Path.GetFileNameWithoutExtension(selectedFile.Name);
        OnPropertyChanged(nameof(RenameText));
    }
    public async Task RenameAsync()
    {
        if (!CanMove) return;
        var source = selectedFile!;
        var name = renameText;
        var includeExtension = changeExtension;
        IsBusy = true;
        try
        {
            var path = await Task.Run(() => FileRename.Rename(source.FullPath, name, includeExtension));
            selectedFile = source with { FullPath = path, Name = Path.GetFileName(path), Extension = Path.GetExtension(path) };
            selection = [selectedFile];
            ResetConversion();
            Status = "Success: file renamed. Copy Path now uses the new name.";
        }
        catch (FileCollisionException)
        { Status = "A file or folder with that name already exists. Nothing was renamed or overwritten. Choose another name."; }
        catch (Exception ex) when (IsExpected(ex)) { Status = FriendlyError(ex); }
        finally { IsBusy = false; ResetRename(); }
    }
}
