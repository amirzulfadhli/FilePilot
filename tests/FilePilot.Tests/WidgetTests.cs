using System.IO;
using FilePilot.App;
using FilePilot.App.Widget;
using FilePilot.Core;
using FilePilot.Core.Conversion;
using SkiaSharp;
using Xunit;

namespace FilePilot.Tests;

public sealed class WidgetTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FilePilot-widget-" + Guid.NewGuid().ToString("N"));
    public WidgetTests() => Directory.CreateDirectory(root);
    private string FileAt(string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "original data");
        return path;
    }
    private string ImageAt(string name)
    {
        var path = Path.Combine(root, name);
        using var bitmap = new SKBitmap(12, 8);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        data.SaveTo(output);
        return path;
    }
    [Fact]
    public void RenamePreservesExtensionUnicodeAndContent()
    {
        var source = FileAt("source.txt");
        var result = FileRename.Rename(source, "文 new name");
        Assert.Equal(Path.Combine(root, "文 new name.txt"), result);
        Assert.Equal("original data", File.ReadAllText(result));
        Assert.False(File.Exists(source));
    }
    [Fact]
    public void RenameChangesExtensionOnlyWhenExplicit()
    {
        var result = FileRename.Rename(FileAt("source.txt"), "new.md", true);
        Assert.EndsWith("new.md", result);
        var retained = FileRename.Rename(result, "another.log");
        Assert.EndsWith("another.log.md", retained);
    }
    [Theory]
    [InlineData("../bad")][InlineData("..")][InlineData("CON")][InlineData("nul.txt")]
    [InlineData("COM¹")][InlineData("trailing.")][InlineData("trailing ")][InlineData("C:\\bad")]
    [InlineData("bad:name")][InlineData("")][InlineData("LPT2")][InlineData("folder\\name")]
    public void RenameRejectsUnsafeNamesWithoutChangingOriginal(string name)
    {
        var source = FileAt("source.txt");
        Assert.Throws<ArgumentException>(() => FileRename.Rename(source, name));
        Assert.Equal("original data", File.ReadAllText(source));
    }
    [Fact]
    public void RenameRefusesCollisionsAndLocks()
    {
        var source = FileAt("source.txt");
        var existing = FileAt("existing.txt");
        Assert.Throws<FileCollisionException>(() => FileRename.Rename(source, "existing"));
        Directory.CreateDirectory(Path.Combine(root, "folder.txt"));
        Assert.Throws<FileCollisionException>(() => FileRename.Rename(source, "folder"));
        using (File.Open(source, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => FileRename.Rename(source, "locked"));
        Assert.Equal("original data", File.ReadAllText(source));
        Assert.Equal("original data", File.ReadAllText(existing));
        Assert.False(File.Exists(Path.Combine(root, "locked.txt")));
    }
    [Fact]
    public async Task RenameUpdatesViewModelAndCopyPath()
    {
        string? copied = null;
        var model = new MainViewModel(value => copied = value);
        model.LoadFile(FileAt("source.txt"));
        Assert.Equal("source", model.RenameText);
        model.RenameText = "new";
        await model.RenameAsync();
        model.CopyPathCommand.Execute(null);
        Assert.Equal(Path.Combine(root, "new.txt"), copied);
        Assert.Equal("new.txt", model.FileName);
        Assert.StartsWith("Success", model.Status);
    }
    [Fact]
    public async Task RenameCollisionPreservesSelectionAndAllowsChoosingAnotherName()
    {
        var source = FileAt("source.txt");
        var occupied = Path.Combine(root, "occupied.txt");
        File.WriteAllText(occupied, "collision sentinel");
        string? copied = null;
        var model = new MainViewModel(path => copied = path);
        model.LoadFile(source);
        model.RenameText = "occupied";
        await model.RenameAsync();
        Assert.Contains("Choose another name", model.Status);
        Assert.DoesNotContain("Choose another folder", model.Status);
        Assert.False(model.IsBusy);
        Assert.True(model.CanMove);
        Assert.Equal("original data", File.ReadAllText(source));
        Assert.Equal("collision sentinel", File.ReadAllText(occupied));
        model.CopyPathCommand.Execute(null);
        Assert.Equal(source, copied);
        model.RenameText = "recovered 文";
        await model.RenameAsync();
        model.CopyPathCommand.Execute(null);
        Assert.Equal(Path.Combine(root, "recovered 文.txt"), copied);
        Assert.Equal("original data", File.ReadAllText(copied!));
        Assert.False(File.Exists(source));
        Assert.Equal("collision sentinel", File.ReadAllText(occupied));
    }
    [Fact]
    public void MixedInvalidSelectionClearsOldSelectionAndNeverActsOnSubset()
    {
        var model = new MainViewModel(_ => throw new InvalidOperationException());
        var source = FileAt("source.txt");
        model.LoadFile(source);
        model.LoadFiles([source, root]);
        Assert.False(model.CanMove);
        Assert.False(model.CopyPathCommand.CanExecute(null));
        Assert.Empty(model.SelectionPaths);
        model.LoadFiles([source]);
        model.ClearSelection("Selection unavailable");
        Assert.False(model.CanConvert);
        Assert.False(model.CopyPathCommand.CanExecute(null));
    }
    [Fact]
    public void BatchCopyIncludesAllPathsAndDisablesSingleFileActions()
    {
        string? copied = null;
        var model = new MainViewModel(value => copied = value);
        var first = FileAt("a 文.txt"); var second = FileAt("b.txt");
        model.LoadFiles([first, second, first]);
        Assert.True(model.IsBatch);
        Assert.False(model.CanMove);
        model.CopyPathCommand.Execute(null);
        Assert.Equal(first + Environment.NewLine + second, copied);
    }
    [Fact]
    public async Task BatchContinuesAfterCollisionAndCorruptionPreservingSources()
    {
        var a = ImageAt("a.png"); var b = ImageAt("b 文.png"); var broken = FileAt("broken.png");
        var existing = FileAt("a.jpg"); var unsupported = FileAt("text.txt");
        var original = File.ReadAllBytes(b);
        var batch = await new BatchConverter(new ImageFileConverter()).ConvertAsync([a, broken, b, unsupported], root, ImageFileFormat.Jpg, "#FFFFFF");
        Assert.False(batch.Cancelled);
        Assert.Equal(4, batch.Items.Count);
        Assert.Single(batch.Items, i => i.OutputPath is not null);
        Assert.Equal("original data", File.ReadAllText(existing));
        Assert.Equal(original, File.ReadAllBytes(b));
        Assert.All(new[] { a, b, broken, unsupported }, p => Assert.True(File.Exists(p)));
        Assert.Empty(Directory.GetFiles(root, ".filepilot-*.tmp"));
    }
    [Fact]
    public async Task BatchViewModelReportsResultsWithoutReplacingSelection()
    {
        var a = ImageAt("a.png"); var b = ImageAt("b.png");
        var model = new MainViewModel(_ => { });
        model.LoadFiles([a, b]);
        model.SelectedOutputFormat = ImageFileFormat.Jpg;
        await model.ConvertAsync();
        Assert.Contains("2 saved", model.Status);
        Assert.Contains("a.png", model.BatchDetails);
        Assert.Contains("b.png", model.BatchDetails);
        Assert.Equal(a, model.FullPath);
        Assert.True(model.CanOpenOutputFolder);
        Assert.False(model.IsBusy);
    }
    [Fact]
    public async Task BatchCancellationKeepsCompletedFilesAndStopsFurtherWork()
    {
        using var cancellation = new CancellationTokenSource();
        var converter = new CancellingConverter(cancellation);
        var batch = await new BatchConverter(converter).ConvertAsync(["a.png", "b.png"], root, ImageFileFormat.Jpg, "#FFFFFF", cancellationToken: cancellation.Token);
        Assert.True(batch.Cancelled);
        Assert.Single(batch.Items);
        Assert.Equal("completed.jpg", batch.Items[0].OutputPath);
        Assert.Equal(1, converter.Calls);
    }
    [Fact]
    public async Task BatchBoundsAndPreCancellationDoNotCallConverter()
    {
        using var cancellation = new CancellationTokenSource();
        var converter = new CancellingConverter(cancellation);
        var service = new BatchConverter(converter);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ConvertAsync(Enumerable.Repeat("a.png", 101).ToArray(), root, ImageFileFormat.Jpg, "#FFFFFF"));
        cancellation.Cancel();
        Assert.True((await service.ConvertAsync(["a.png"], root, ImageFileFormat.Jpg, "#FFFFFF", cancellationToken: cancellation.Token)).Cancelled);
        Assert.Equal(0, converter.Calls);
    }
    [Fact]
    public void FavoriteSettingsRoundTripAndFailedSaveKeepsPreviousSettings()
    {
        var path = Path.Combine(root, "settings.json");
        var store = new SettingsStore(path);
        Assert.Equal("Ctrl+Alt+F", store.Load().Shortcut);
        var settings = new WidgetSettings("Alt+Shift+F8", [root], true);
        store.Save(settings);
        var actual = store.Load();
        Assert.Equal(settings.Shortcut, actual.Shortcut);
        Assert.Equal(settings.FavoriteFolders, actual.FavoriteFolders);
        Assert.True(actual.DarkTheme);
        Assert.Throws<ArgumentException>(() => store.Save(new("F12")));
        Assert.Equal(settings.Shortcut, store.Load().Shortcut);
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        File.WriteAllText(path, new string('x', 32769));
        Assert.Throws<IOException>(() => store.Load());
    }
    [Fact]
    public void OversizedSerializedFavoritesCannotReplaceReadableSettings()
    {
        var store = new SettingsStore(Path.Combine(root, "settings.json"));
        store.Save(new());
        var folders = Enumerable.Repeat(new string('文', 1000), 20).ToArray();
        Assert.Throws<ArgumentException>(() => store.Save(new("Ctrl+Alt+F", folders)));
        Assert.Equal("Ctrl+Alt+F", store.Load().Shortcut);
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }
    [Theory]
    [InlineData("F")][InlineData("Shift+F")][InlineData("Ctrl+F12")][InlineData("Win+F")]
    [InlineData("Ctrl+Ctrl+F")][InlineData("Ctrl+")][InlineData("")]
    public void RejectsUnsafeOrInvalidShortcut(string text) => Assert.Throws<ArgumentException>(() => HotkeyGesture.Parse(text));
    [Fact]
    public void ShortcutParsesModifiersAndKeys()
    {
        Assert.Equal(new HotkeyGesture(3, 70), HotkeyGesture.Parse("ctrl + alt + f"));
        Assert.Equal(new HotkeyGesture(5, 119), HotkeyGesture.Parse("Alt+Shift+F8"));
    }
    [Fact]
    public void PopupClampsToWorkAreaIncludingNegativeMonitorCoordinates()
    {
        Assert.Equal((-480, 440), PopupPlacement.Clamp(-10, 1070, 480, 640, -1920, 0, 0, 1080));
        Assert.Equal((0, 0), PopupPlacement.Clamp(-50, -30, 480, 640, 0, 0, 400, 300));
    }
    [Theory]
    [InlineData(96, 1920, 1080)]
    [InlineData(120, 1536, 864)]
    [InlineData(144, 1280, 720)]
    [InlineData(192, 960, 540)]
    public void WorkAreaSizeUsesMonitorDpi(uint dpi, double width, double height)
    {
        var size = PopupPlacement.WorkAreaSize(1920, 1080, dpi);
        Assert.Equal(width, size.Width);
        Assert.Equal(height, size.Height);
        var fractional = PopupPlacement.WorkAreaSize(1919, 1079, dpi);
        Assert.True(fractional.Width * dpi / 96d <= 1919);
        Assert.True(fractional.Height * dpi / 96d <= 1079);
    }
    [Fact]
    public async Task NonExplorerCaptureProvidesPickerFallback()
    {
        var result = await new ExplorerSelectionReader().ReadAsync(0);
        Assert.Empty(result.Paths);
        Assert.Contains("Choose files", result.Error);
    }
    private sealed class CancellingConverter(CancellationTokenSource cancellation) : IFileConverter
    {
        public int Calls { get; private set; }
        public IReadOnlyList<ImageFileFormat> GetOutputFormats(string path) => [ImageFileFormat.Jpg];
        public Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellation.Cancel();
            return Task.FromResult(new ConversionResult("completed.jpg", 12, 8));
        }
    }
    public void Dispose() => Directory.Delete(root, true);
}
