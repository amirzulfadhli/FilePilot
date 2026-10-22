namespace FilePilot.Core.Documents;

public sealed record DocumentConversionRequest(string SourcePath, string DestinationFolder);
public sealed record DocumentConversionResult(string OutputPath);
public sealed record DocumentConversionLimits(long MaxInputBytes = 32 * 1024 * 1024, long MaxExpandedBytes = 128 * 1024 * 1024,
    int MaxEntries = 2048, long MaxOutputBytes = 64 * 1024 * 1024);
public interface IDocumentConverter
{
    Task<DocumentConversionResult> ConvertAsync(DocumentConversionRequest request, CancellationToken cancellationToken = default);
}
public interface IDocumentPdfEngine
{
    Task ExportAsync(string inputPath, string outputFolder, string profileFolder, CancellationToken cancellationToken);
}
public class DocumentConversionException(string message, Exception? inner = null) : IOException(message, inner);
public sealed class DocumentEngineException(string message, bool mayStillBeRunning = false, Exception? inner = null)
    : DocumentConversionException(message, inner)
{
    public bool MayStillBeRunning { get; } = mayStillBeRunning;
}
