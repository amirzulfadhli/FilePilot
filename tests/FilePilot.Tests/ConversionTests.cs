using System.IO;
using System.Text;
using FilePilot.Core;
using FilePilot.Core.Conversion;
using SkiaSharp;
using Xunit;

namespace FilePilot.Tests;

public sealed class ConversionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FilePilot-conversion-" + Guid.NewGuid().ToString("N"));
    private readonly string output;
    private readonly ImageFileConverter converter = new();

    public ConversionTests()
    {
        Directory.CreateDirectory(root);
        output = Path.Combine(root, "output 文 folder");
        Directory.CreateDirectory(output);
    }

    private string CreateImage(ImageFileFormat format, bool transparent = false, string? name = null)
    {
        var extension = format switch { ImageFileFormat.Png => ".png", ImageFileFormat.Jpg => ".jpg", _ => ".webp" };
        var path = Path.Combine(root, (name ?? "sample 文 file") + extension);
        using var bitmap = new SKBitmap(64, 32);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(transparent ? SKColors.Transparent : SKColors.Red);
            if (!transparent)
            {
                using var paint = new SKPaint { Color = SKColors.Green };
                canvas.DrawRect(32, 0, 32, 16, paint);
                paint.Color = SKColors.Blue;
                canvas.DrawRect(0, 16, 32, 16, paint);
                paint.Color = SKColors.Yellow;
                canvas.DrawRect(32, 16, 32, 16, paint);
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format switch
        {
            ImageFileFormat.Png => SKEncodedImageFormat.Png,
            ImageFileFormat.Jpg => SKEncodedImageFormat.Jpeg,
            _ => SKEncodedImageFormat.Webp
        }, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
        return path;
    }

    [Theory]
    [InlineData(ImageFileFormat.Png, ImageFileFormat.Jpg)]
    [InlineData(ImageFileFormat.Png, ImageFileFormat.WebP)]
    [InlineData(ImageFileFormat.Jpg, ImageFileFormat.Png)]
    [InlineData(ImageFileFormat.Jpg, ImageFileFormat.WebP)]
    [InlineData(ImageFileFormat.WebP, ImageFileFormat.Png)]
    [InlineData(ImageFileFormat.WebP, ImageFileFormat.Jpg)]
    public async Task ConvertsEveryDirectionPreservingDimensionsOriginalAndUnicodePaths(ImageFileFormat from, ImageFileFormat to)
    {
        var source = CreateImage(from);
        var original = File.ReadAllBytes(source);
        var timestamp = File.GetLastWriteTimeUtc(source);
        var result = await converter.ConvertAsync(new(source, output, to));
        using var decoded = SKBitmap.Decode(result.OutputPath);
        Assert.NotNull(decoded);
        Assert.Equal(64, decoded.Width);
        Assert.Equal(32, decoded.Height);
        using var codec = SKCodec.Create(result.OutputPath);
        Assert.Equal(to switch { ImageFileFormat.Png => SKEncodedImageFormat.Png, ImageFileFormat.Jpg => SKEncodedImageFormat.Jpeg, _ => SKEncodedImageFormat.Webp }, codec.EncodedFormat);
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(source));
        Assert.Single(Directory.GetFiles(output));
        Assert.Empty(Directory.GetFiles(output, ".filepilot-*.tmp"));
    }

    [Theory]
    [InlineData(ImageFileFormat.Png, ImageFileFormat.WebP)]
    [InlineData(ImageFileFormat.WebP, ImageFileFormat.Png)]
    public async Task PreservesTransparency(ImageFileFormat from, ImageFileFormat to)
    {
        var source = CreateImage(from, true);
        var result = await converter.ConvertAsync(new(source, output, to));
        using var image = SKBitmap.Decode(result.OutputPath);
        Assert.Equal(0, image.GetPixel(20, 10).Alpha);
    }

    [Theory]
    [InlineData(ImageFileFormat.Png, "#FFFFFF", 255, 255, 255)]
    [InlineData(ImageFileFormat.WebP, "#FFFFFF", 255, 255, 255)]
    [InlineData(ImageFileFormat.Png, "#204060", 32, 64, 96)]
    [InlineData(ImageFileFormat.WebP, "#204060", 32, 64, 96)]
    public async Task JpegFlattensOnConfiguredBackground(ImageFileFormat from, string background, int r, int g, int b)
    {
        var result = await converter.ConvertAsync(new(CreateImage(from, true), output, ImageFileFormat.Jpg, background));
        using var image = SKBitmap.Decode(result.OutputPath);
        var pixel = image.GetPixel(20, 10);
        Assert.Equal(255, pixel.Alpha);
        Assert.InRange(Math.Abs(pixel.Red - r), 0, 3);
        Assert.InRange(Math.Abs(pixel.Green - g), 0, 3);
        Assert.InRange(Math.Abs(pixel.Blue - b), 0, 3);
    }

    [Fact]
    public async Task JpegBlendsPartialAlphaOnDefaultWhite()
    {
        var source = Path.Combine(root, "half.png");
        using (var bitmap = new SKBitmap(8, 8))
        {
            bitmap.Erase(new SKColor(255, 0, 0, 128));
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(source);
            encoded.SaveTo(file);
        }
        var result = await converter.ConvertAsync(new(source, output, ImageFileFormat.Jpg));
        using var actual = SKBitmap.Decode(result.OutputPath);
        var pixel = actual.GetPixel(4, 4);
        Assert.InRange(pixel.Red, 252, 255);
        Assert.InRange(pixel.Green, 124, 130);
        Assert.InRange(pixel.Blue, 124, 130);
    }

    [Theory]
    [InlineData(1, "RGBY")]
    [InlineData(2, "GRYB")]
    [InlineData(3, "YBGR")]
    [InlineData(4, "BYRG")]
    [InlineData(5, "RBGY")]
    [InlineData(6, "BRYG")]
    [InlineData(7, "YGBR")]
    [InlineData(8, "GYRB")]
    public async Task AppliesEveryExifOrientationBeforeRemovingMetadata(int orientation, string corners)
    {
        var source = CreateImage(ImageFileFormat.Jpg);
        AddExif(source, (ushort)orientation);
        using (var inputCodec = SKCodec.Create(source)) Assert.Equal(orientation, (int)inputCodec.EncodedOrigin);
        var original = File.ReadAllBytes(source);
        var result = await converter.ConvertAsync(new(source, output, ImageFileFormat.Png));
        using var image = SKBitmap.Decode(result.OutputPath);
        Assert.Equal(orientation >= 5 ? 32 : 64, image.Width);
        Assert.Equal(orientation >= 5 ? 64 : 32, image.Height);
        var points = new[] { (1, 1), (3, 1), (1, 3), (3, 3) };
        var colors = new Dictionary<char, SKColor> { ['R'] = SKColors.Red, ['G'] = SKColors.Green, ['B'] = SKColors.Blue, ['Y'] = SKColors.Yellow };
        for (var i = 0; i < 4; i++)
        {
            var actual = image.GetPixel(image.Width * points[i].Item1 / 4, image.Height * points[i].Item2 / 4);
            var expected = colors[corners[i]];
            Assert.InRange(Math.Abs(actual.Red - expected.Red), 0, 8);
            Assert.InRange(Math.Abs(actual.Green - expected.Green), 0, 8);
            Assert.InRange(Math.Abs(actual.Blue - expected.Blue), 0, 8);
        }
        Assert.DoesNotContain("private_location", Encoding.Latin1.GetString(File.ReadAllBytes(result.OutputPath)));
        using var outputCodec = SKCodec.Create(result.OutputPath);
        Assert.Equal(SKEncodedOrigin.TopLeft, outputCodec.EncodedOrigin);
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Fact]
    public async Task WebpOutputAlsoDropsExifMetadata()
    {
        var source = CreateImage(ImageFileFormat.Jpg);
        AddExif(source, 6);
        var result = await converter.ConvertAsync(new(source, output, ImageFileFormat.WebP));
        Assert.DoesNotContain("private_location", Encoding.Latin1.GetString(File.ReadAllBytes(result.OutputPath)));
        using var codec = SKCodec.Create(result.OutputPath);
        Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
        Assert.Equal(32, codec.Info.Width);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".webp")]
    [InlineData(".txt")]
    [InlineData(".gif")]
    public async Task RejectsCorruptOrUnsupportedFiles(string extension)
    {
        var source = Path.Combine(root, "invalid" + extension);
        File.WriteAllText(source, "Not an image");
        await Assert.ThrowsAsync<ImageConversionException>(() => converter.ConvertAsync(new(source, output, ImageFileFormat.Png)));
        Assert.Empty(Directory.GetFiles(output));
        Assert.Equal("Not an image", File.ReadAllText(source));
    }

    [Fact]
    public async Task RejectsContentExtensionMismatchAndTruncatedData()
    {
        var source = CreateImage(ImageFileFormat.Png);
        var disguised = Path.ChangeExtension(source, ".jpg");
        File.Copy(source, disguised);
        await Assert.ThrowsAsync<ImageConversionException>(() => converter.ConvertAsync(new(disguised, output, ImageFileFormat.Png)));
        var jpg = CreateImage(ImageFileFormat.Jpg, name: "truncated");
        var bytes = File.ReadAllBytes(jpg);
        File.WriteAllBytes(jpg, bytes[..(bytes.Length / 2)]);
        await Assert.ThrowsAsync<ImageConversionException>(() => converter.ConvertAsync(new(jpg, output, ImageFileFormat.Png)));
        Assert.Empty(Directory.GetFiles(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollisionNeverChangesExistingFileOrDirectory(bool directory)
    {
        var source = CreateImage(ImageFileFormat.Png);
        var target = Path.Combine(output, Path.GetFileNameWithoutExtension(source) + ".jpg");
        if (directory) Directory.CreateDirectory(target); else File.WriteAllText(target, "Do not overwrite");
        await Assert.ThrowsAsync<FileCollisionException>(() => converter.ConvertAsync(new(source, output, ImageFileFormat.Jpg)));
        if (!directory) Assert.Equal("Do not overwrite", File.ReadAllText(target));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFiles(output, ".filepilot-*.tmp"));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("C:\\folder\\..\\output")]
    [InlineData("\\\\server\\share")]
    [InlineData("\\\\?\\C:\\output")]
    [InlineData("C:\\output:stream")]
    public async Task RejectsUnsafeOutputFolders(string folder)
    {
        var source = CreateImage(ImageFileFormat.Png);
        await Assert.ThrowsAsync<ArgumentException>(() => converter.ConvertAsync(new(source, folder, ImageFileFormat.Jpg)));
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public async Task RejectsMissingOrFileDestination()
    {
        var source = CreateImage(ImageFileFormat.Png);
        await Assert.ThrowsAnyAsync<IOException>(() => converter.ConvertAsync(new(source, Path.Combine(root, "missing"), ImageFileFormat.Jpg)));
        await Assert.ThrowsAsync<ArgumentException>(() => converter.ConvertAsync(new(source, source, ImageFileFormat.Jpg)));
    }

    [Fact]
    public async Task LimitsInputBytesPixelsAndDimensionsBeforeDecode()
    {
        var source = CreateImage(ImageFileFormat.Png);
        foreach (var limits in new[] { new ImageConversionLimits(MaxInputBytes: 1), new ImageConversionLimits(MaxPixels: 10), new ImageConversionLimits(MaxDimension: 10) })
            await Assert.ThrowsAsync<ImageConversionException>(() => new ImageFileConverter(limits).ConvertAsync(new(source, output, ImageFileFormat.Jpg)));
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public async Task RejectsBadBackgroundAndSameFormat()
    {
        var source = CreateImage(ImageFileFormat.Png);
        await Assert.ThrowsAsync<ArgumentException>(() => converter.ConvertAsync(new(source, output, ImageFileFormat.Jpg, "#GGFFFF")));
        await Assert.ThrowsAsync<ImageConversionException>(() => converter.ConvertAsync(new(source, output, ImageFileFormat.Png)));
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public async Task PreCanceledConversionDoesNotCreateOutput()
    {
        var source = CreateImage(ImageFileFormat.Png);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => converter.ConvertAsync(new(source, output, ImageFileFormat.Jpg), new CancellationToken(true)));
        Assert.Empty(Directory.GetFiles(output));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task LockedSourceIsPreserved()
    {
        var source = CreateImage(ImageFileFormat.Png);
        using var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
        await Assert.ThrowsAnyAsync<IOException>(() => converter.ConvertAsync(new(source, output, ImageFileFormat.Jpg)));
        Assert.Empty(Directory.GetFiles(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleansPartiallyWrittenTemporaryFileAfterFailureOrCancellation(bool cancel)
    {
        var destination = Path.Combine(output, "result.png");
        using var cts = new CancellationTokenSource();
        var exception = await Record.ExceptionAsync(() => AtomicOutput.WriteAsync(destination, async (stream, token) =>
        {
            await stream.WriteAsync(new byte[] { 1, 2, 3 }, token);
            Assert.Single(Directory.GetFiles(output, ".filepilot-*.tmp"));
            if (cancel) cts.Cancel(); else throw new IOException("Simulated full disk");
        }, cts.Token));
        Assert.NotNull(exception);
        if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(exception); else Assert.IsType<IOException>(exception);
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public async Task CollisionAppearingDuringWriteIsNotOverwrittenAndTempIsRemoved()
    {
        var destination = Path.Combine(output, "result.png");
        await Assert.ThrowsAsync<FileCollisionException>(() => AtomicOutput.WriteAsync(destination, async (stream, token) =>
        {
            await stream.WriteAsync(new byte[] { 1, 2, 3 }, token);
            File.WriteAllText(destination, "Racing file must survive");
        }, CancellationToken.None));
        Assert.Equal("Racing file must survive", File.ReadAllText(destination));
        Assert.Single(Directory.GetFiles(output));
    }

    [Fact]
    public async Task AnimatedWebpIsRejectedInsteadOfSilentlyDroppingFrames()
    {
        var source = CreateImage(ImageFileFormat.WebP);
        var still = File.ReadAllBytes(source);
        using var frameChunks = new MemoryStream();
        for (var offset = 12; offset + 8 <= still.Length;)
        {
            var length = BitConverter.ToInt32(still, offset + 4);
            var total = 8 + length + (length & 1);
            var type = Encoding.ASCII.GetString(still, offset, 4);
            if (type is "VP8 " or "VP8L" or "ALPH") frameChunks.Write(still, offset, total);
            offset += total;
        }
        using var body = new MemoryStream();
        body.Write(Encoding.ASCII.GetBytes("WEBP"));
        WriteChunk(body, "VP8X", new byte[] { 2, 0, 0, 0, 63, 0, 0, 31, 0, 0 });
        WriteChunk(body, "ANIM", new byte[6]);
        using var frame = new MemoryStream();
        frame.Write(new byte[] { 0, 0, 0, 0, 0, 0, 63, 0, 0, 31, 0, 0, 100, 0, 0, 2 });
        frame.Write(frameChunks.ToArray());
        WriteChunk(body, "ANMF", frame.ToArray());
        WriteChunk(body, "ANMF", frame.ToArray());
        using (var file = File.Create(source))
        {
            file.Write(Encoding.ASCII.GetBytes("RIFF"));
            file.Write(BitConverter.GetBytes((int)body.Length));
            file.Write(body.ToArray());
        }
        using (var codec = SKCodec.Create(source)) Assert.Equal(2, codec.FrameCount);
        var error = await Assert.ThrowsAsync<ImageConversionException>(() => converter.ConvertAsync(new(source, output, ImageFileFormat.Png)));
        Assert.Contains("Animated", error.Message);
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public async Task ConcurrentConversionsCannotOverwriteEachOther()
    {
        var source = CreateImage(ImageFileFormat.Png);
        var request = new ConversionRequest(source, output, ImageFileFormat.Jpg);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Record.ExceptionAsync(() => converter.ConvertAsync(request))));
        Assert.Single(outcomes, error => error is null);
        Assert.Single(outcomes, error => error is FileCollisionException);
        Assert.Single(Directory.GetFiles(output));
        using var decoded = SKBitmap.Decode(Directory.GetFiles(output)[0]);
        Assert.Equal(64, decoded.Width);
        Assert.True(File.Exists(source));
    }

    private static void WriteChunk(Stream stream, string type, byte[] bytes)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(bytes.Length));
        stream.Write(bytes);
        if ((bytes.Length & 1) != 0) stream.WriteByte(0);
    }
    private static void AddExif(string path, ushort orientation)
    {
        var original = File.ReadAllBytes(path);
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.ASCII, true))
        {
            writer.Write(new byte[] { 69, 120, 105, 102, 0, 0 });
            writer.Write(new byte[] { 73, 73, 42, 0, 8, 0, 0, 0 });
            writer.Write((ushort)2);
            writer.Write((ushort)0x112); writer.Write((ushort)3); writer.Write(1u); writer.Write(orientation); writer.Write((ushort)0);
            var description = Encoding.ASCII.GetBytes("private_location\0");
            writer.Write((ushort)0x10E); writer.Write((ushort)2); writer.Write((uint)description.Length); writer.Write(38u);
            writer.Write(0u); writer.Write(description);
        }
        var exif = payload.ToArray();
        using var file = File.Create(path);
        file.Write(original.AsSpan(0, 2));
        file.Write(new byte[] { 255, 225, (byte)((exif.Length + 2) >> 8), (byte)(exif.Length + 2) });
        file.Write(exif); file.Write(original.AsSpan(2));
    }

    public void Dispose() => Directory.Delete(root, true);
}
