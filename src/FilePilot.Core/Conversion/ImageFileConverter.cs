using System.Globalization;
using SkiaSharp;

namespace FilePilot.Core.Conversion;

public sealed class ImageFileConverter : IFileConverter
{
    private readonly ImageConversionLimits limits;
    public ImageFileConverter(ImageConversionLimits? limits = null)
    {
        this.limits = limits ?? new();
        if (this.limits.MaxInputBytes <= 0 || this.limits.MaxPixels <= 0 || this.limits.MaxDimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits));
    }

    public IReadOnlyList<ImageFileFormat> GetOutputFormats(string sourcePath) => InputFormat(sourcePath) switch
    {
        ImageFileFormat.Png => [ImageFileFormat.Jpg, ImageFileFormat.WebP],
        ImageFileFormat.Jpg => [ImageFileFormat.Png, ImageFileFormat.WebP],
        ImageFileFormat.WebP => [ImageFileFormat.Png, ImageFileFormat.Jpg],
        _ => []
    };

    public Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(() => ConvertCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<ConversionResult> ConvertCoreAsync(ConversionRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var source = new FileActions().Inspect(request.SourcePath);
        if (!GetOutputFormats(source.FullPath).Contains(request.OutputFormat))
            throw new ImageConversionException("Choose one of the supported PNG, JPG, or WebP conversion directions.");
        var folder = LocalPathPolicy.Validate(request.DestinationFolder);
        if (!Directory.Exists(folder)) throw new ArgumentException("Choose an existing destination folder.");
        var extension = request.OutputFormat switch { ImageFileFormat.Png => ".png", ImageFileFormat.Jpg => ".jpg", _ => ".webp" };
        var destination = Path.Combine(folder, Path.GetFileNameWithoutExtension(source.Name) + extension);
        if (string.Equals(destination, source.FullPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The output cannot replace the original file.");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new FileCollisionException(destination);
        var background = request.OutputFormat == ImageFileFormat.Jpg ? ParseBackground(request.JpegBackground) : SKColors.Transparent;

        // Hold the same source handle through decode so other ordinary processes cannot replace/write it.
        using var input = new FileStream(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > limits.MaxInputBytes)
            throw new ImageConversionException($"Image exceeds the {limits.MaxInputBytes / (1024 * 1024)} MiB input limit.");
        using var managedStream = new SKManagedStream(input, disposeManagedStream: false);
        using var codec = SKCodec.Create(managedStream) ?? throw new ImageConversionException("This image is unsupported or corrupted.");
        var actualFormat = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Png => ImageFileFormat.Png,
            SKEncodedImageFormat.Jpeg => ImageFileFormat.Jpg,
            SKEncodedImageFormat.Webp => ImageFileFormat.WebP,
            _ => (ImageFileFormat?)null
        };
        if (actualFormat is null || actualFormat != InputFormat(source.FullPath))
            throw new ImageConversionException("The image content does not match its supported file extension.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > limits.MaxDimension || info.Height > limits.MaxDimension ||
            (long)info.Width * info.Height > limits.MaxPixels)
            throw new ImageConversionException($"Image exceeds the {limits.MaxPixels:N0}-pixel or {limits.MaxDimension:N0}-pixel edge limit.");
        if (codec.FrameCount > 1)
            throw new ImageConversionException("Animated images are not supported. Choose a single-frame image.");
        token.ThrowIfCancellationRequested();
        using var srgb = SKColorSpace.CreateSrgb();
        using var decoded = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
        if (decoded.GetPixels() == IntPtr.Zero) throw new ImageConversionException("Not enough memory to decode this image.");
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
            throw new ImageConversionException("The image is corrupted or incomplete; no output was created.");
        token.ThrowIfCancellationRequested();

        var origin = codec.EncodedOrigin;
        var swapsAxes = (int)origin >= 5 && (int)origin <= 8;
        var width = swapsAxes ? info.Height : info.Width;
        var height = swapsAxes ? info.Width : info.Height;
        using var rendered = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
        if (rendered.GetPixels() == IntPtr.Zero) throw new ImageConversionException("Not enough memory to orient this image.");
        using (var canvas = new SKCanvas(rendered))
        {
            canvas.Clear(background);
            canvas.SetMatrix(OrientationMatrix(origin, info.Width, info.Height));
            canvas.DrawBitmap(decoded, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            canvas.Flush();
        }
        token.ThrowIfCancellationRequested();
        // Encoding a new pixel image drops source EXIF/GPS/XMP/IPTC/comments and orientation tags.
        using var image = SKImage.FromBitmap(rendered);
        var outputFormat = request.OutputFormat switch
        {
            ImageFileFormat.Png => SKEncodedImageFormat.Png,
            ImageFileFormat.Jpg => SKEncodedImageFormat.Jpeg,
            _ => SKEncodedImageFormat.Webp
        };
        using var encoded = image.Encode(outputFormat, request.OutputFormat == ImageFileFormat.Jpg ? 92 : 100)
            ?? throw new ImageConversionException("The image encoder could not produce output.");
        token.ThrowIfCancellationRequested();
        await AtomicOutput.WriteAsync(destination, async (stream, cancellation) =>
        {
            using var encodedStream = encoded.AsStream();
            await encodedStream.CopyToAsync(stream, cancellation);
        }, token);
        return new ConversionResult(destination, width, height);
    }

    private static ImageFileFormat? InputFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => ImageFileFormat.Png,
        ".jpg" or ".jpeg" => ImageFileFormat.Jpg,
        ".webp" => ImageFileFormat.WebP,
        _ => null
    };

    private static SKColor ParseBackground(string value)
    {
        if (value is null || value.Length != 7 || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException("JPEG background must be a color such as #FFFFFF or #204060.");
        return new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }

    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int width, int height) => (int)origin switch
    {
        2 => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
        3 => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
        4 => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
        5 => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        6 => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
        7 => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
        8 => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity
    };
}
