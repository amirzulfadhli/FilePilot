using FilePilot.Core.Documents;

namespace FilePilot.App;

public sealed partial class MainViewModel
{
    private IDocumentConverter? documentConverter;
    private string documentEngineStatus = "Choose a trusted LibreOffice installation in Settings to enable PDF export.";
    public bool IsDocumentSelected => !IsBatch && selectedFile?.Extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) == true;
    public bool CanConvertDocument => IsDocumentSelected && documentConverter is not null && !IsBusy && !string.IsNullOrEmpty(destinationFolder);
    public string DocumentEngineStatus => documentEngineStatus;
    public void ConfigureDocumentEngine(string? executable)
    {
        if (IsBusy) return;
        documentConverter = null;
        documentEngineStatus = "Choose a trusted LibreOffice installation in Settings to enable PDF export.";
        if (!string.IsNullOrWhiteSpace(executable))
        {
            try
            {
                var path = LibreOfficePdfEngine.ValidateExecutable(executable);
                documentConverter = new DocxPdfConverter(new LibreOfficePdfEngine(path));
                documentEngineStatus = "Local LibreOffice engine configured. PDF layout can differ when fonts are unavailable.";
            }
            catch (Exception ex) when (IsExpected(ex)) { documentEngineStatus = "Document engine unavailable. " + FriendlyError(ex); }
        }
        OnPropertyChanged(nameof(DocumentEngineStatus));
        OnPropertyChanged(nameof(CanConvertDocument));
    }
    public async Task ConvertDocumentAsync()
    {
        if (!CanConvertDocument) return;
        var request = new DocumentConversionRequest(selectedFile!.FullPath, destinationFolder);
        var service = documentConverter!;
        using var cancellation = new CancellationTokenSource();
        conversionCancellation = cancellation;
        lastOutputPath = null;
        OnPropertyChanged(nameof(LastOutputPath));
        IsBusy = true;
        Status = "Exporting DOCX to PDF with the local engine… Original unchanged.";
        try
        {
            var result = await service.ConvertAsync(request, cancellation.Token);
            lastOutputPath = result.OutputPath;
            OnPropertyChanged(nameof(LastOutputPath));
            Status = $"Success: saved PDF to {result.OutputPath}. Original unchanged; check layout and fonts before sharing.";
        }
        catch (OperationCanceledException) { Status = "Document export cancelled. No completed output was finalized; original unchanged."; }
        catch (Exception ex) when (IsExpected(ex)) { Status = FriendlyError(ex); }
        finally { conversionCancellation = null; IsBusy = false; }
    }
}
