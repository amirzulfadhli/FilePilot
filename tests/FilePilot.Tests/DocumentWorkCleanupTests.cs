using System.IO;
using FilePilot.Core.Documents;
using Xunit;

namespace FilePilot.Tests;

public sealed class DocumentWorkCleanupTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "FilePilot-cleanup-" + Guid.NewGuid().ToString("N"))).FullName;

    private string NewJob() => Directory.CreateDirectory(Path.Combine(root, ".filepilot-doc-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public async Task SharingViolationCanRecoverAfterTheHandleIsReleased()
    {
        var job = NewJob();
        var path = Path.Combine(job, "input.docx");
        File.WriteAllText(path, "Owned temporary copy");
        using var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
        // Reproduce the failed one-shot cleanup using a real Windows sharing handle.
        var failure = Assert.Throws<IOException>(() => Directory.Delete(job, true));
        Assert.Contains(failure.HResult & 0xFFFF, new[] { 32, 33 });
        var cleanup = DocumentWorkCleanup.DeleteAsync(root, job);
        held.Dispose();
        await cleanup;
        Assert.False(Directory.Exists(job));
    }

    [Fact]
    public async Task PersistentLockFailsAndRetainsTheOwnedWork()
    {
        var job = NewJob();
        var path = Path.Combine(job, "input.docx");
        File.WriteAllText(path, "Retain for inspection");
        using var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => DocumentWorkCleanup.DeleteAsync(root, job).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(Directory.Exists(job));
        held.Dispose();
        Assert.Equal("Retain for inspection", File.ReadAllText(path));
    }

    [Fact]
    public async Task UnownedAndOutsideDirectoriesAreNeverRemoved()
    {
        var ordinary = Directory.CreateDirectory(Path.Combine(root, "keep")).FullName;
        var sentinel = Path.Combine(ordinary, "keep.txt");
        File.WriteAllText(sentinel, "Unrelated work");
        await Assert.ThrowsAsync<ArgumentException>(() => DocumentWorkCleanup.DeleteAsync(root, ordinary));
        var job = NewJob();
        await Assert.ThrowsAsync<ArgumentException>(() => DocumentWorkCleanup.DeleteAsync(ordinary, job));
        Assert.Equal("Unrelated work", File.ReadAllText(sentinel));
        Assert.True(Directory.Exists(job));
    }

    public void Dispose() => Directory.Delete(root, true);
}
