using FilePilot.Core;
using FilePilot.App;
using System.IO;
using Xunit;

namespace FilePilot.Tests;

public sealed class FileActionsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FilePilot-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FileActions actions = new();

    public FileActionsTests() => Directory.CreateDirectory(root);

    private string CreateFile(string name = "hello world.txt", string content = "FilePilot test data")
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void InspectReportsMetadataWithoutChangingFile()
    {
        var path = CreateFile();
        var file = actions.Inspect(path);
        Assert.Equal(path, file.FullPath);
        Assert.Equal("hello world.txt", file.Name);
        Assert.Equal(".txt", file.Extension);
        Assert.Equal(new FileInfo(path).Length, file.Length);
        Assert.Equal("FilePilot test data", File.ReadAllText(path));
    }

    [Fact]
    public void CopyPathUsesAbsolutePathIncludingSpacesAndUnicode()
    {
        var path = CreateFile("résumé 文档.txt");
        string? copied = null;
        var model = new MainViewModel(value => copied = value);
        model.LoadArguments([path]);
        model.CopyPathCommand.Execute(null);
        Assert.Equal(path, copied);
        Assert.StartsWith("Success:", model.Status);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void InvalidSelectionDisablesActions()
    {
        var model = new MainViewModel(_ => throw new InvalidOperationException());
        model.LoadFile(Path.Combine(root, "missing.txt"));
        Assert.False(model.CopyPathCommand.CanExecute(null));
        Assert.Contains("no longer exists", model.Status);
    }

    [Fact]
    public void RejectsMultipleArgumentsAndDirectories()
    {
        var model = new MainViewModel(_ => { });
        model.LoadArguments(["one", "two"]);
        Assert.False(model.CopyPathCommand.CanExecute(null));
        Assert.Contains("one file", model.Status);
        Assert.Throws<ArgumentException>(() => actions.Inspect(root));
    }

    [Theory]
    [InlineData("relative.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData("\\\\?\\C:\\file.txt")]
    [InlineData("C:\\folder\\..\\file.txt")]
    [InlineData("C:\\file.txt:stream")]
    [InlineData("C:\\NUL.txt")]
    [InlineData("C:\\trailing.\\file.txt")]
    [InlineData("")]
    public void RejectsUnsafePaths(string path) => Assert.Throws<ArgumentException>(() => actions.Inspect(path));

    public void Dispose() => Directory.Delete(root, true);
}
