using System.IO;
using FilePilot.App;
using FilePilot.Core.Conversion;
using Xunit;

namespace FilePilot.Tests;

public sealed class ConversionViewModelTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FilePilot-ui-" + Guid.NewGuid().ToString("N"));
    private readonly string source;
    public ConversionViewModelTests()
    {
        Directory.CreateDirectory(root);
        source = Path.Combine(root, "test image.png");
        File.WriteAllText(source, "The fake converter does not decode this fixture.");
    }

    [Fact]
    public async Task ConversionUsesSelectionsAndKeepsOriginalSelected()
    {
        var fake = new ControlledConverter();
        var model = new MainViewModel(_ => { }, fake);
        model.LoadFile(source);
        Assert.True(model.IsImageSelected);
        Assert.Equal(root, model.DestinationFolder);
        model.SelectedOutputFormat = ImageFileFormat.Jpg;
        model.JpegBackground = "#204060";
        var work = model.ConvertAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.CanMove);
        Assert.False(model.CanConvert);
        Assert.False(model.CopyPathCommand.CanExecute(null));
        Assert.False(model.CanChooseFile);
        Assert.True(model.CanCancelConversion);
        model.LoadFile("invalid");
        await model.ConvertAsync();
        Assert.Equal(1, fake.Calls);
        var expectedOutput = Path.Combine(root, "test image.jpg");
        fake.Completion.SetResult(new(expectedOutput, 32, 16));
        await work;
        Assert.Equal(source, model.FullPath);
        Assert.Equal(expectedOutput, model.LastOutputPath);
        Assert.True(model.CanOpenOutputFolder);
        Assert.False(model.IsBusy);
        Assert.Equal("#204060", fake.Request!.JpegBackground);
        Assert.StartsWith("Success:", model.Status);
        model.LoadFile(source);
        Assert.False(model.CanOpenOutputFolder);
    }

    [Fact]
    public async Task CancellationKeepsBusyUntilConverterStopsAndRestoresControls()
    {
        var fake = new ControlledConverter();
        var model = new MainViewModel(_ => { }, fake);
        model.LoadFile(source);
        var work = model.ConvertAsync();
        model.CancelConversion();
        Assert.True(fake.Token.IsCancellationRequested);
        Assert.True(model.IsBusy);
        Assert.False(model.CanCancelConversion);
        fake.Completion.SetCanceled(fake.Token);
        await work;
        Assert.False(model.IsBusy);
        Assert.True(model.CanConvert);
        Assert.False(model.CanOpenOutputFolder);
        Assert.Contains("cancelled", model.Status);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task ConversionFailureRestoresControlsAndReportsError()
    {
        var fake = new ControlledConverter();
        var model = new MainViewModel(_ => { }, fake);
        model.LoadFile(source);
        var work = model.ConvertAsync();
        fake.Completion.SetException(new ImageConversionException("Corrupted image"));
        await work;
        Assert.Equal("Corrupted image", model.Status);
        Assert.False(model.IsBusy);
        Assert.True(model.CanConvert);
        Assert.Null(model.LastOutputPath);
    }

    [Fact]
    public void NonImagesAndInvalidFilesDoNotEnableConversion()
    {
        var model = new MainViewModel(_ => { });
        var text = Path.Combine(root, "note.txt");
        File.WriteAllText(text, "text");
        model.LoadFile(text);
        Assert.False(model.IsImageSelected);
        Assert.False(model.CanConvert);
        Assert.True(model.CanMove);
        model.LoadFile(source);
        Assert.True(model.CanConvert);
        model.LoadFile(Path.Combine(root, "missing.png"));
        Assert.False(model.CanConvert);
    }

    private sealed class ControlledConverter : IFileConverter
    {
        public TaskCompletionSource<ConversionResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConversionRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public IReadOnlyList<ImageFileFormat> GetOutputFormats(string path) => [ImageFileFormat.Jpg, ImageFileFormat.WebP];
        public Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Request = request;
            Token = cancellationToken;
            return Completion.Task;
        }
    }

    public void Dispose() => Directory.Delete(root, true);
}
