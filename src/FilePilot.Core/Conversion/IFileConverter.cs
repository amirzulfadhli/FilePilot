namespace FilePilot.Core.Conversion;

public enum ImageFileFormat { Png, Jpg, WebP }
public sealed record ConversionRequest(string SourcePath, string DestinationFolder,
    ImageFileFormat OutputFormat, string JpegBackground = "#FFFFFF");
public sealed record ConversionResult(string OutputPath, int Width, int Height);
public sealed record ImageConversionLimits(long MaxInputBytes = 64 * 1024 * 1024,
    long MaxPixels = 16_000_000, int MaxDimension = 16_384);

// Image-specific formats; documents use the independent IDocumentConverter contract.
public interface IFileConverter
{
    IReadOnlyList<ImageFileFormat> GetOutputFormats(string sourcePath);
    Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default);
}

public sealed class ImageConversionException(string message, Exception? inner = null) : IOException(message, inner);
