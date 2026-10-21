using FilePilot.Core;
using FilePilot.App;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace FilePilot.Tests;

public sealed class MoveTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FilePilot-moves-" + Guid.NewGuid().ToString("N"));
    private readonly FileActions actions = new();
    private readonly string source;
    private readonly string destination;

    public MoveTests()
    {
        Directory.CreateDirectory(root);
        source = Path.Combine(root, "sample 文档.txt");
        destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(destination);
        File.WriteAllText(source, "Original contents, preserved byte for byte.");
    }

    [Fact]
    public void MovePreservesContentAndModificationTime()
    {
        var bytes = File.ReadAllBytes(source);
        var modified = File.GetLastWriteTimeUtc(source);
        var result = actions.Move(source, destination);
        Assert.False(result.SourceMayRemain);
        Assert.False(File.Exists(source));
        Assert.Equal(bytes, File.ReadAllBytes(result.DestinationPath));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(result.DestinationPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionNeverOverwritesEitherFile(bool upperCaseName)
    {
        var target = Path.Combine(destination, upperCaseName ? Path.GetFileName(source).ToUpperInvariant() : Path.GetFileName(source));
        File.WriteAllText(target, "Existing destination contents");
        Assert.Throws<FileCollisionException>(() => actions.Move(source, destination));
        Assert.Equal("Original contents, preserved byte for byte.", File.ReadAllText(source));
        Assert.Equal("Existing destination contents", File.ReadAllText(target));
    }

    [Fact]
    public void DirectoryAtDestinationIsAlsoACollision()
    {
        Directory.CreateDirectory(Path.Combine(destination, Path.GetFileName(source)));
        Assert.Throws<FileCollisionException>(() => actions.Move(source, destination));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void MovingToCurrentFolderIsRefused()
    {
        Assert.Throws<ArgumentException>(() => actions.Move(source, root));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void MissingDestinationDoesNotRemoveSource()
    {
        Assert.ThrowsAny<IOException>(() => actions.Move(source, Path.Combine(root, "missing")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void FileCannotBeUsedAsDestinationFolder()
    {
        Assert.Throws<ArgumentException>(() => actions.Move(source, source));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void DestinationTraversalIsRejected()
    {
        Assert.Throws<ArgumentException>(() => actions.Move(source, Path.Combine(destination, "..")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void LockedSourceFailsWithoutCreatingDestination()
    {
        using var locked = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.ThrowsAny<IOException>(() => actions.Move(source, destination));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFileSystemEntries(destination));
    }

    [Fact]
    public void MissingSourceDoesNotCreateDestination()
    {
        File.Delete(source);
        Assert.ThrowsAny<IOException>(() => actions.Move(source, destination));
        Assert.Empty(Directory.GetFileSystemEntries(destination));
    }

    [Fact]
    public async Task ViewModelFollowsMovedFileAndCopiesNewPath()
    {
        string? copied = null;
        var model = new MainViewModel(value => copied = value);
        model.LoadFile(source);
        await model.MoveAsync(destination);
        Assert.False(model.IsBusy);
        Assert.True(model.CanMove);
        Assert.StartsWith("Success:", model.Status);
        Assert.Equal(Path.Combine(destination, Path.GetFileName(source)), model.FullPath);
        model.CopyPathCommand.Execute(null);
        Assert.Equal(model.FullPath, copied);
    }

    [Fact]
    public async Task FailedMoveKeepsSelectionAndReenablesActions()
    {
        var model = new MainViewModel(_ => { });
        model.LoadFile(source);
        File.WriteAllText(Path.Combine(destination, Path.GetFileName(source)), "keep me");
        await model.MoveAsync(destination);
        Assert.Equal(source, model.FullPath);
        Assert.False(model.IsBusy);
        Assert.True(model.CopyPathCommand.CanExecute(null));
        Assert.True(model.CanMove);
        Assert.Contains("Nothing was overwritten", model.Status);
    }

    [Fact]
    public void ClipboardFailureIsReportedWithoutChangingFile()
    {
        var model = new MainViewModel(_ => throw new COMException("Busy"));
        model.LoadFile(source);
        model.CopyPathCommand.Execute(null);
        Assert.Contains("clipboard is busy", model.Status);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void RemovedFileIsNotCopiedFromStaleSelection()
    {
        string? copied = null;
        var model = new MainViewModel(value => copied = value);
        model.LoadFile(source);
        File.Delete(source);
        model.CopyPathCommand.Execute(null);
        Assert.Null(copied);
        Assert.Contains("no longer exists", model.Status);
    }

    [Fact]
    public void PermissionErrorsAreReportedWithoutElevation()
    {
        var model = new MainViewModel(_ => throw new UnauthorizedAccessException());
        model.LoadFile(source);
        model.CopyPathCommand.Execute(null);
        Assert.Contains("Access denied", model.Status);
        Assert.True(File.Exists(source));
    }

    public void Dispose() => Directory.Delete(root, true);
}
