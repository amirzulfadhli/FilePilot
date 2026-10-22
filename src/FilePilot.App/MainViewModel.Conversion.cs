using System.IO;
using FilePilot.Core;
using FilePilot.Core.Conversion;

namespace FilePilot.App;

public sealed partial class MainViewModel
{
    private readonly IFileConverter converter;
    private CancellationTokenSource? conversionCancellation;
    private ImageFileFormat? outputFormat;
    private string destinationFolder = "";
    private string jpegBackground = "#FFFFFF";
    private string? lastOutputPath;

    public IReadOnlyList<ImageFileFormat> OutputFormats { get; private set; } = [];
    public double WindowHeight => IsImageSelected ? 640 : 420;
    public bool IsImageSelected => OutputFormats.Count > 0;
    public bool CanConvert => IsImageSelected && outputFormat.HasValue && !IsBusy && !string.IsNullOrEmpty(destinationFolder);
    public bool CanCancelConversion => conversionCancellation is { IsCancellationRequested: false };
    public bool CanOpenOutputFolder => lastOutputPath is not null && !IsBusy;
    public bool IsJpegOutput => outputFormat == ImageFileFormat.Jpg;
    public string? LastOutputPath => lastOutputPath;
    public string DestinationFolder => destinationFolder;
    public ImageFileFormat? SelectedOutputFormat
    {
        get => outputFormat;
        set
        {
            if (IsBusy) return;
            outputFormat = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsJpegOutput));
            OnPropertyChanged(nameof(CanConvert));
        }
    }
    public string JpegBackground
    {
        get => jpegBackground;
        set { if (!IsBusy) { jpegBackground = value; OnPropertyChanged(); } }
    }

    private void ResetConversion()
    {
        OutputFormats = selection.SelectMany(file => converter.GetOutputFormats(file.FullPath)).Distinct().ToArray();
        ResetRename();
        BatchDetails = "";
        OnPropertyChanged(nameof(BatchDetails));
        outputFormat = OutputFormats.Count > 0 ? OutputFormats[0] : null;
        destinationFolder = selectedFile is null ? "" : Path.GetDirectoryName(selectedFile.FullPath)!;
        lastOutputPath = null;
        OnPropertyChanged(nameof(OutputFormats));
        OnPropertyChanged(nameof(IsImageSelected));
        OnPropertyChanged(nameof(WindowHeight));
        OnPropertyChanged(nameof(SelectedOutputFormat));
        OnPropertyChanged(nameof(IsJpegOutput));
        OnPropertyChanged(nameof(DestinationFolder));
        OnPropertyChanged(nameof(LastOutputPath));
        RefreshConversionState();
    }

    public void SetDestinationFolder(string path)
    {
        if (IsBusy) return;
        try
        {
            var folder = LocalPathPolicy.Validate(path);
            if (!Directory.Exists(folder)) throw new ArgumentException("Choose an existing destination folder.");
            destinationFolder = folder;
            OnPropertyChanged(nameof(DestinationFolder));
            OnPropertyChanged(nameof(CanConvert));
        }
        catch (Exception ex) when (IsExpected(ex)) { Status = FriendlyError(ex); }
    }

    public async Task ConvertAsync()
    {
        if (!CanConvert) return;
        var request = new ConversionRequest(selectedFile!.FullPath, destinationFolder, outputFormat!.Value, jpegBackground);
        using var cancellation = new CancellationTokenSource();
        conversionCancellation = cancellation;
        lastOutputPath = null;
        OnPropertyChanged(nameof(LastOutputPath));
        IsBusy = true;
        Status = "Converting image… Your original file will stay unchanged.";
        try
        {
            if (IsBatch)
            {
                var progress = new Progress<string>(message => { if (IsBusy && ReferenceEquals(conversionCancellation, cancellation)) Status = message; });
                var batch = await new BatchConverter(converter).ConvertAsync(selection.Select(f => f.FullPath).ToArray(),
                    destinationFolder, outputFormat!.Value, jpegBackground, progress, cancellation.Token);
                lastOutputPath = batch.Items.LastOrDefault(item => item.OutputPath is not null)?.OutputPath;
                BatchDetails = string.Join(Environment.NewLine, batch.Items.Select(item => $"{Path.GetFileName(item.SourcePath)}: {item.Message}"));
                OnPropertyChanged(nameof(BatchDetails));
                OnPropertyChanged(nameof(LastOutputPath));
                var saved = batch.Items.Count(item => item.OutputPath is not null);
                Status = $"{(batch.Cancelled ? "Cancelled" : "Finished")}: {saved} saved, {batch.Items.Count - saved} skipped/failed, {selection.Count - batch.Items.Count} not processed. Originals unchanged. See batch details.";
                return;
            }
            var result = await converter.ConvertAsync(request, cancellation.Token);
            lastOutputPath = result.OutputPath;
            OnPropertyChanged(nameof(LastOutputPath));
            Status = $"Success: saved {result.Width} × {result.Height} image to {result.OutputPath}. Original unchanged.";
        }
        catch (OperationCanceledException) { Status = "Conversion cancelled. No completed output was saved; original unchanged."; }
        catch (Exception ex) when (IsExpected(ex)) { Status = FriendlyError(ex); }
        finally
        {
            conversionCancellation = null;
            IsBusy = false;
        }
    }

    public void CancelConversion()
    {
        if (!CanCancelConversion) return;
        conversionCancellation!.Cancel();
        Status = "Cancelling… Waiting for the current codec operation to finish.";
        RefreshConversionState();
    }

    public void ReportUiError(Exception error) => Status = FriendlyError(error);

    private void RefreshConversionState()
    {
        OnPropertyChanged(nameof(CanConvert));
        OnPropertyChanged(nameof(CanCancelConversion));
        OnPropertyChanged(nameof(CanOpenOutputFolder));
    }
}
