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
        var clock = new CleanupClock();
        var delays = 0;
        Task CountDelay(TimeSpan interval)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(50), interval);
            delays++; return Task.CompletedTask;
        }
        await Assert.ThrowsAsync<IOException>(() => DocumentWorkCleanup.DeleteAsync(root, job, clock, CountDelay));
        Assert.Equal(20, delays);
        Assert.True(Directory.Exists(job));
        held.Dispose();
        Assert.Equal("Retain for inspection", File.ReadAllText(path));
    }

    [Fact]
    public async Task DelayedContinuationDoesNotRestartTheRetryBudget()
    {
        var job = NewJob();
        var path = Path.Combine(job, "input.docx");
        File.WriteAllText(path, "Retain under a delayed continuation");
        using var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var clock = new CleanupClock();
        var delays = 0;
        Task ResumeLate(TimeSpan interval)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(50), interval);
            delays++; clock.Advance(TimeSpan.FromSeconds(2)); return Task.CompletedTask;
        }
        await Assert.ThrowsAsync<IOException>(() => DocumentWorkCleanup.DeleteAsync(root, job, clock, ResumeLate));
        Assert.Equal(1, delays);
        Assert.True(File.Exists(path));
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

    private sealed class CleanupClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        internal void Advance(TimeSpan interval) => ticks += interval.Ticks;
    }
}
